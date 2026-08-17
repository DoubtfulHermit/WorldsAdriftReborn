using Improbable.Corelibrary.Transforms;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game
{
    internal readonly record struct ShipPartWakeBundle(
        ShipDomainComponentUpdate Root,
        IReadOnlyList<ShipDomainComponentUpdate> Members);

    /// <summary>
    /// Keeps every bolted ship part FOLLOWING the moving hull by re-publishing its
    /// 190602 TransformState on a heartbeat below the client's one-second sleep.
    ///
    /// WHY IT EXISTS. A bolted part is seeded hull-relative (parent = Parent(hullId,
    /// "~")), so its follow-visualizer CAN track the hull - but that visualizer sleeps
    /// one second after its last transform change and only wakes on the part's own
    /// TransformState.PropertyUpdated (see <see cref="ShipPartMotionPolicy"/>). We move
    /// the hull with a 1130 control point and never touch the parts' 190602, so the
    /// parts sleep and park while the hull flies. This service is the missing wake: a
    /// value UPDATE (never a re-seed - that re-triggers the OnDisable->Clear destroy the
    /// seed-stability fix already closed) carrying the SAME hull-relative transform,
    /// sent both on every hull move AND on a standalone heartbeat so a single manual
    /// nudge and any idle period still hold the parts awake. It mirrors the shipped
    /// worker's RelativeParentTransformUpdater, which does this continuously.
    ///
    /// The decisions - which parts, what local offset, what cadence, what timestamp -
    /// are the pure <see cref="WorldEntityRegistry.BoltedParts"/>,
    /// <see cref="BoltedPartTransform"/> and <see cref="ShipPartMotionPolicy"/>; this
    /// class is only the wiring: resolve the hull, loop the parts, send.
    /// </summary>
    internal sealed class ShipPartMotionService
    {
        private readonly IClock _clock;
        private readonly CadenceTimer _cadence;

        /// <summary>
        /// The monotonic wake counter, SHARED across every wake source (nudge, ferry,
        /// heartbeat) so the parts' synthetic timeline only ever advances. Static and
        /// unlocked on purpose: the server is a single poll loop, exactly like
        /// EntityIdAllocator and the registries.
        /// </summary>
        private static long _sample;
        private static readonly Dictionary<long, FixedPointPosition> StaticLocalOffsets = new();

        public ShipPartMotionService(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _cadence = new CadenceTimer(TimeSpan.FromSeconds(ShipPartMotionPolicy.HeartbeatIntervalSeconds));
        }

        /// <summary>
        /// The heartbeat, one call per main-loop turn. Cheap when idle (one Stopwatch
        /// compare); when due and a hull exists it wakes every part. This is what keeps
        /// the parts following after a SINGLE nudge and during quiet stretches, when no
        /// hull move is publishing anything of its own.
        /// </summary>
        public void Tick()
        {
            if (!_cadence.Due(_clock.Elapsed))
            {
                return;
            }
            if (!ShipPublisher.TryResolveShip(out long hullEntityId, out _))
            {
                return;
            }
            if (WorldsAdriftRebornGameServer.Flight.IsFlightDomainActive(hullEntityId))
            {
                return;
            }
            if (WorldsAdriftRebornGameServer.ShipFerry.OwnsMotionFor(hullEntityId))
            {
                return;
            }
            PublishWake(hullEntityId);
        }

        /// <summary>
        /// Wakes every registered bolted part: for each, re-publish its 190602 as a
        /// value update carrying its hull-relative offset and the Parent(hullId, "~"),
        /// with the next monotonic stamp. Called on every hull move (nudge, ferry) for
        /// immediacy and by <see cref="Tick"/> for continuity. Returns the number of
        /// parts that reached at least one client.
        ///
        /// Skips a part whose entity id is not bound yet (its AddEntityOp has not run) -
        /// it will be woken on a later heartbeat once it is in the world. A part no peer
        /// has checked out yet simply gets dropped client-side, exactly as the hull's
        /// own control points do while a client is still loading.
        /// </summary>
        public static int PublishWake(long hullEntityId)
        {
            // Active piloted/unmanned-cruising domains publish their own root+member
            // frame in ShipFlightService. A second heartbeat would create an
            // independently sequenced member stream and can use an older generation.
            if (WorldsAdriftRebornGameServer.Flight.IsFlightDomainActive(hullEntityId))
            {
                return 0;
            }
            if (WorldsAdriftRebornGameServer.ShipFerry.OwnsMotionFor(hullEntityId))
            {
                return 0;
            }
            WorldEntity? hull = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(hullEntityId);
            if (hull == null)
            {
                return 0;
            }
            FixedPointPosition hullPos = hull.Position;

            ShipPartWakeBundle bundle = BuildWakeBundle(
                hullEntityId, hullPos,
                WorldsAdriftRebornGameServer.WorldEntities.RotationSeedFor(hullEntityId));
            ShipDomainDeliveryResult delivered = ShipPublisher.BroadcastDomainMotion(
                hullEntityId, hullPos,
                WorldsAdriftRebornGameServer.Flight.DomainGenerationFor(hullEntityId),
                bundle.Root, rootAuxiliary: null, bundle.Members);
            return delivered.MemberDeliveries;
        }

        /// <summary>
        /// Builds the root-first wake portion of a domain frame without sending it.
        /// Ferry/nudge publishers combine this with their 1130 root point so hull and
        /// members share one relevance decision and one replication sequence.
        /// </summary>
        public static ShipPartWakeBundle BuildWakeBundle(
            long hullEntityId, FixedPointPosition hullPos, uint packedRotation)
        {
            long sample = ++_sample;
            float stamp = ShipPartMotionPolicy.StampFor(sample, ShipPartMotionPolicy.HeartbeatIntervalSeconds);
            var rootWake = ShipPartTransform.BuildParentlessWakeUpdate(
                hullPos,
                new Improbable.Corelibrary.Math.Quaternion32(packedRotation),
                ShipPartMotionPolicy.ParentStampFor(sample, ShipPartMotionPolicy.HeartbeatIntervalSeconds));
            var members = new List<ShipDomainComponentUpdate>();
            foreach (WorldEntity part in WorldsAdriftRebornGameServer.WorldEntities.BoltedParts())
            {
                // A part seeded as a REAL Unity child of the hull (the deck) is dragged
                // along by the hull's transform through the Unity hierarchy and needs no
                // wake. Worse, re-sending its parent field here every heartbeat would
                // re-fire the client's ParentUpdated and churn an unparent+reparent
                // (rigidbody destroyed and re-added) twice a second. Only "~" followers
                // are woken.
                if (BoltedPartTransform.IsUnityChild(part.Key))
                {
                    continue;
                }

                long? partEntityId = WorldsAdriftRebornGameServer.WorldEntities.BoundEntityIdFor(part.Key);
                if (!partEntityId.HasValue)
                {
                    continue;
                }

                if (!StaticLocalOffsets.TryGetValue(partEntityId.Value, out FixedPointPosition localOffset))
                {
                    // Known static parts derive their offset from the key's placement
                    // function, which is pose-independent even if the first wake occurs
                    // after the hull registry has already been relocated. The fallback
                    // preserves forward compatibility for a future unrecognised key.
                    localOffset = BoltedPartTransform.LocalOffsetFor(part.Key, hullPos)
                        ?? BoltedPartTransform.LocalOffset(part.Position, hullPos);
                    StaticLocalOffsets.Add(partEntityId.Value, localOffset);
                }
                TransformState.Update wake = ShipPartTransform.BuildWakeUpdate(
                    localOffset, hullEntityId, BoltedPartTransform.HierarchyKeyFor(part.Key), stamp);
                members.Add(new ShipDomainComponentUpdate(
                    partEntityId.Value, ShipPartMotionPolicy.TransformStateComponentId, wake));
            }

            return new ShipPartWakeBundle(
                new ShipDomainComponentUpdate(
                    hullEntityId, ShipPartMotionPolicy.TransformStateComponentId, rootWake),
                members);
        }
    }
}

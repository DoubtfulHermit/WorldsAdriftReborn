using Improbable.Corelibrary.Transforms;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// ISLAND FAUNA ON THE WIRE. The impure half of
    /// <see cref="IslandFaunaPolicy"/>/<see cref="IslandFaunaRegistry"/>: it seeds
    /// the planned creatures at boot, streams each one to the peers close enough to
    /// care, and pushes the pose the registry says is due.
    ///
    /// A CREATURE IS NOT A WORLD REGISTRATION, and that decision is what shapes
    /// this whole file. It is stated in <see cref="IslandFaunaPolicy.FirstFaunaEntityId"/>
    /// and it is not a preference: <see cref="IslandFaunaRegistry.Add"/> REFUSES any
    /// id below the fauna band, while a <c>WorldEntityRegistry</c> registration is
    /// numbered by <c>EntityIdAllocator</c> counting up from 1. The two id schemes
    /// are mutually exclusive, so fauna cannot go through the registry - and
    /// therefore cannot ride <c>ResourceInterestService</c>, whose entire input is
    /// the registration list. Adding a "fauna-" prefix to
    /// <c>ResourceInterestPolicy.IsStreamedResourceKey</c> would be dead code: a
    /// creature has no registration key for it to match. The per-peer checkout below
    /// is the price of the disjoint id band, and it is deliberately the same shape
    /// as <see cref="ResourceInterestService"/>'s so the two behave alike.
    ///
    /// WIRE SHAPE, per creature, per peer (the multiplayer-safety contract):
    /// <list type="bullet">
    /// <item>OUT, once per checkout: an AssetLoadRequest one cadence before an
    ///   AddEntity, to peers inside <c>Interest.RadiusMetres</c> of the creature's
    ///   LIVE position, whose island terrain is already checked out, and which can
    ///   receive RemoveEntity. Nothing else is seeded; the client asks for the
    ///   components its own prefab wants over SEND_COMPONENT_INTEREST.</item>
    /// <item>OUT, 4/s while checked out: one 190602 TransformState carrying the
    ///   complete absolute position. 190602 is UNRELIABLE by
    ///   <c>MirrorSendPolicy.RelayReliabilityFor</c> and this stream supersedes -
    ///   every update is the whole pose - so a loss costs one frame of smoothness.</item>
    /// <item>OUT, once: RemoveEntity on channel 5 past the unload radius.</item>
    /// <item>IN: nothing. No client sends anything about a creature; there is no
    ///   update handler, and there is nothing to interact with yet.</item>
    /// </list>
    ///
    /// THE WORST CASE, stated so it can be checked rather than trusted. The registry
    /// caps the world at <see cref="IslandFaunaPolicy.DefaultMaxConcurrent"/> (24)
    /// live creatures and pushes each at <see cref="IslandFaunaRegistry.DefaultPoseInterval"/>
    /// (250 ms), so a peer that could somehow see EVERY creature at once receives
    /// 24 x 4 = 96 fauna transform updates a second - under a fifth of one 20 Hz
    /// avatar relay. In practice a peer sees one island's population, so the real
    /// figure is a handful a second. Raising <see cref="BudgetEnv"/> raises that
    /// ceiling proportionally; it is an operator decision, and the boot line says
    /// what the world would have wanted.
    ///
    /// ONLY PEERS THAT CAN RECEIVE RemoveEntity ARE EVER SHOWN A CREATURE. Channel 5
    /// is a negotiated capability, and a peer that lacks it could never unload the
    /// animal again. That is the same guard <c>FallingLogService</c> uses, and for
    /// the same reason: it is what lets this feature exist without a litter story.
    /// </summary>
    internal sealed class IslandFaunaService
    {
        /// <summary>The opt-in gate. See <see cref="IslandFaunaPolicy.EnabledEnvVar"/>.</summary>
        internal const string EnableEnv = IslandFaunaPolicy.EnabledEnvVar;

        /// <summary>
        /// How many creatures may be live world-wide. 0 is a second kill switch.
        /// Named like <c>WAREBORN_TREE_FALL_MAX</c> because it does the same job.
        /// </summary>
        internal const string BudgetEnv = "WAREBORN_ISLAND_FAUNA_MAX";

        private const uint TransformStateComponentId = 190602;
        private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(120);
        private const int MaxQueuedPerPeer = 128;

        private sealed class PeerState
        {
            public readonly HashSet<long> Loaded = new();
            public readonly Queue<ResourceStreamAction> Pending = new();
            public TimeSpan NextReconcile;
            public TimeSpan NextSend;
            public TimeSpan ContinuousAfter;
            public long AssetRequestedFor;
            public bool RemoveSupported;
            public bool ConnectPlanComplete;
        }

        private readonly IClock _clock;
        private readonly bool _enabled;
        private readonly IslandFaunaRegistry _registry;
        private readonly Dictionary<long, FaunaPlacement> _planned = new();
        private readonly Dictionary<ENetPeerHandle, PeerState> _peers = new();
        private Func<ENetPeerHandle, IslandId, bool>? _terrainReady;
        private long _sample;

        internal IslandFaunaService(IClock clock)
            : this(clock,
                IslandFaunaPolicy.EnabledFrom(Environment.GetEnvironmentVariable(EnableEnv)),
                IslandFaunaPolicy.ParseBudget(Environment.GetEnvironmentVariable(BudgetEnv)))
        {
        }

        internal IslandFaunaService(IClock clock, bool enabled, int? maxConcurrent)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _enabled = enabled;
            _registry = new IslandFaunaRegistry(clock,
                IslandFaunaMovement.WorldPoseAt, maxConcurrent);
        }

        /// <summary>Whether island fauna is switched on.</summary>
        internal bool Enabled => _enabled;

        /// <summary>How many creatures are live. Zero whenever the feature is off.</summary>
        internal int Count => _registry.Count;

        /// <summary>
        /// Takes the world's creatures live, once, at boot.
        ///
        /// The population is derived rather than persisted (see
        /// <see cref="IslandFaunaPolicy.PopulationFor"/>), so this is a pure
        /// function of the selected island set and runs before any peer can connect.
        /// It reports the DEMAND alongside the seeded count: at release-world scale
        /// those two numbers disagree by a lot, and an operator has to be told that
        /// rather than left to notice empty islands.
        /// </summary>
        internal void Seed(IReadOnlyList<ReleaseIslandRecord> islands)
        {
            if (!_enabled)
            {
                return;
            }
            if (islands == null)
            {
                throw new ArgumentNullException(nameof(islands));
            }

            int demand = IslandFaunaPlan.Demand(islands);
            IReadOnlyList<FaunaPlacement> plan =
                IslandFaunaPlan.Build(islands, _registry.MaxConcurrent);

            foreach (FaunaPlacement placement in plan)
            {
                if (!_registry.Add(placement.Creature, placement.Island, placement.Envelope))
                {
                    // The plan is built against the registry's own budget, so this
                    // is unreachable rather than routine - say so instead of letting
                    // an island quietly lose an animal.
                    Console.WriteLine("[island-fauna] the registry refused planned creature "
                        + placement.Creature.EntityId + " on " + placement.Creature.IslandId
                        + "; it will not exist this run.");
                    continue;
                }
                _planned[placement.Creature.EntityId] = placement;
            }

            Console.WriteLine("[island-fauna] ON: seeded " + _registry.Count + " creature(s) across "
                + IslandFaunaPlan.IslandCount(plan) + " of " + islands.Count + " island(s); the world"
                + " wanted " + demand + " and the world-wide budget is " + _registry.MaxConcurrent
                + " (" + BudgetEnv + "). Pose cadence "
                + _registry.PoseInterval.TotalMilliseconds.ToString("0") + " ms, so the worst case a"
                + " single peer can receive is " + WorstCaseUpdatesPerSecond().ToString("0")
                + " fauna transform update(s) a second.");

            // NAME the populated islands. "8 of 46" tells an operator the budget bit;
            // it does not tell a player where to go to see the feature at all, and a
            // feature nobody can find is indistinguishable from one that is broken.
            List<string> populated = new List<string>();
            foreach (FaunaPlacement placement in plan)
            {
                string id = placement.Creature.IslandId.ToString();
                if (!populated.Contains(id)) populated.Add(id);
            }
            if (populated.Count > 0)
            {
                Console.WriteLine("[island-fauna] populated island(s): "
                    + string.Join(", ", populated) + ".");
            }

            if (demand > _registry.MaxConcurrent)
            {
                Console.WriteLine("[island-fauna] " + (demand - _registry.Count)
                    + " planned creature(s) were dropped for budget: the remaining islands carry NO"
                    + " fauna. Raise " + BudgetEnv + " to cover more of the world, and expect the"
                    + " worst-case update rate above to rise in proportion.");
            }
        }

        /// <summary>
        /// Binds the terrain lifecycle, so no creature can be added to a peer that
        /// does not yet hold its island. Unbound is the fail-open legacy path, which
        /// is what a Haven-only world without terrain interest needs.
        /// </summary>
        internal void AttachTerrainReadiness(Func<ENetPeerHandle, IslandId, bool> terrainReady) =>
            _terrainReady = terrainReady ?? throw new ArgumentNullException(nameof(terrainReady));

        /// <summary>
        /// Hands lifecycle over from the connect spawn plan. A creature is never in
        /// that plan, but a joiner instantiating the world on its main thread must
        /// not also be handed wildlife: this is the same settle window the resource
        /// stream waits out, for the same OOM reason.
        /// </summary>
        internal void NoteConnectPlanComplete(ENetPeerHandle peer)
        {
            if (!_enabled) return;
            PeerState state = StateFor(peer);
            if (state.ConnectPlanComplete) return;
            state.ConnectPlanComplete = true;
            state.ContinuousAfter = _clock.Elapsed
                + WorldsAdriftRebornGameServer.ResourceInterest.SettleDelay;
            state.NextReconcile = state.ContinuousAfter;
        }

        /// <summary>Whether an entity id names one of this server's creatures.</summary>
        internal bool IsFauna(long entityId) => _enabled && _registry.IsFauna(entityId);

        /// <summary>
        /// Where a creature is RIGHT NOW, or null for anything that is not one.
        /// <c>ComponentsSerializer</c>'s 190602 branch asks this: a creature is not
        /// in the world registry, so <c>TransformSeedFor</c> would hand it the
        /// PLAYER SPAWN and a checking-out peer would find a manta on its head.
        /// </summary>
        internal FixedPointPosition? PositionOf(long entityId) =>
            _enabled ? _registry.PositionOf(entityId) : null;

        /// <summary>
        /// What a creature is, or null for anything that is not one. The 1182/4322
        /// species branches ask this; nothing else knows a creature exists.
        /// </summary>
        internal FaunaSpecies? SpeciesOf(long entityId) =>
            _enabled && _planned.TryGetValue(entityId, out FaunaPlacement placement)
                ? placement.Creature.Species : (FaunaSpecies?)null;

        /// <summary>
        /// Guards component interest against the cross-channel unload race. Channel 5
        /// RemoveEntity and channel 2 interest are independent, so a request may
        /// arrive after the creature was unloaded; re-seeding it would leave native
        /// components on an entity the client no longer holds. Non-fauna ids and the
        /// disabled feature both fail open.
        /// </summary>
        internal bool MayServe(ENetPeerHandle peer, long entityId) =>
            !IsFauna(entityId)
            || (_peers.TryGetValue(peer, out PeerState? state) && state.Loaded.Contains(entityId));

        /// <summary>
        /// One call per main-loop turn: per-peer checkout, then every due pose.
        /// Cheap when the feature is off (one bool) and when nothing is due (an
        /// empty-dictionary walk that allocates nothing).
        /// </summary>
        internal void Tick()
        {
            if (!_enabled)
            {
                return;
            }

            TickCheckout();
            TickPoses();
        }

        internal void Forget(ENetPeerHandle peer) => _peers.Remove(peer);

        /// <summary>
        /// The most fauna transform updates one peer could receive per second if it
        /// somehow held every live creature at once. Reported at boot so the sender
        /// budget is a stated number rather than a claim.
        /// </summary>
        private double WorstCaseUpdatesPerSecond() =>
            _registry.Count / _registry.PoseInterval.TotalSeconds;

        private void TickCheckout()
        {
            TimeSpan now = _clock.Elapsed;
            foreach ((ENetPeerHandle peer, PeerState state) in _peers.ToArray())
            {
                if (!state.ConnectPlanComplete || now < state.ContinuousAfter) continue;

                if (now >= state.NextReconcile)
                {
                    state.NextReconcile = now + ReconcileInterval;
                    Reconcile(peer, state);
                }
                if (now < state.NextSend || state.Pending.Count == 0) continue;
                state.NextSend = now + SendInterval;
                Execute(peer, state);
            }
        }

        /// <summary>
        /// Rebuilds this peer's pending work against the creatures' LIVE positions.
        ///
        /// The reconciliation itself is <c>ResourceInterestPolicy.Reconcile</c> - the
        /// same pure geometry, hysteresis and nearest-first ordering the resource
        /// stream uses. Fauna deliberately does not get its own copy of that
        /// arithmetic; the only thing different about a creature is that its
        /// position is asked for every time instead of read off a registration.
        /// </summary>
        private void Reconcile(ENetPeerHandle peer, PeerState state)
        {
            FixedPointPosition center = WorldsAdriftRebornGameServer.ResourceInterest.CenterFor(peer);
            List<(long Id, FixedPointPosition Position)> live =
                new List<(long, FixedPointPosition)>(_registry.Count);
            foreach (long entityId in _registry.Live)
            {
                FixedPointPosition? position = _registry.PositionOf(entityId);
                if (position.HasValue) live.Add((entityId, position.Value));
            }

            ResourceInterestPolicy.ReplacePending(
                state.Pending,
                ResourceInterestPolicy.Reconcile(
                    center, live, state.Loaded, Interest.RadiusMetres,
                    // A peer with no channel 5 can never unload, so it must never be
                    // told to: an infinite unload radius means "retain what you have".
                    state.RemoveSupported
                        ? WorldsAdriftRebornGameServer.ResourceInterest.UnloadRadiusMetres
                        : double.PositiveInfinity),
                MaxQueuedPerPeer);
        }

        private void Execute(ENetPeerHandle peer, PeerState state)
        {
            ResourceStreamAction action = state.Pending.Peek();

            // Revalidate at the last boundary. Nothing else adds a creature to a
            // peer, but a reconcile can queue work that a previous send already
            // satisfied, and duplicate AddEntity corrupts the client's entity map.
            if (!ResourceInterestPolicy.ShouldExecute(
                    state.ConnectPlanComplete, action, state.Loaded))
            {
                state.Pending.Dequeue();
                if (state.AssetRequestedFor == action.EntityId) state.AssetRequestedFor = 0;
                return;
            }

            if (action.Kind == ResourceStreamActionKind.Remove)
            {
                state.Pending.Dequeue();
                if (SendOPHelper.SendRemoveEntityOP(peer, action.EntityId))
                {
                    state.Loaded.Remove(action.EntityId);
                    PeerCheckoutCleanup.RemoveEntity(peer, action.EntityId);
                }
                else
                {
                    // Channel 5 was not negotiated after all. Retain the checkout so
                    // a later re-add cannot produce a second, inert copy.
                    state.RemoveSupported = false;
                }
                return;
            }

            if (!_planned.TryGetValue(action.EntityId, out FaunaPlacement placement))
            {
                state.Pending.Dequeue();
                return;
            }

            // A creature must not outrun its island: a peer that does not hold the
            // terrain has not loaded that part of the world at all. Drop rather than
            // park, so removals and other islands keep progressing; the next
            // reconcile requeues it once the terrain lands.
            if (_terrainReady != null && !_terrainReady(peer, placement.Creature.IslandId))
            {
                state.Pending.Dequeue();
                if (state.AssetRequestedFor == action.EntityId) state.AssetRequestedFor = 0;
                return;
            }

            string prefab = IslandFaunaPolicy.PrefabNameFor(placement.Creature.Species);
            if (state.AssetRequestedFor != action.EntityId)
            {
                // "notNeeded?" is the assetTYPE every other caller passes; the
                // context is separately the same literal via IslandCatalog.
                SendOPHelper.SendAssetLoadRequestOP(peer, "notNeeded?",
                    prefab, IslandCatalog.DefaultTerrainAssetContext);
                state.AssetRequestedFor = action.EntityId;
                return; // a full cadence for the asset callback before AddEntity
            }

            state.Pending.Dequeue();
            state.AssetRequestedFor = 0;
            if (SendOPHelper.SendAddEntityOP(peer, action.EntityId, prefab,
                    IslandCatalog.DefaultTerrainAssetContext))
            {
                state.Loaded.Add(action.EntityId);
                WorldsAdriftRebornGameServer.SentEntities.MarkSent(peer, action.EntityId);
                Console.WriteLine("[island-fauna] added " + placement.Creature.Species + " "
                    + action.EntityId + " on " + placement.Creature.IslandId + " to "
                    + peer.DangerousGetHandle() + ".");
            }
        }

        /// <summary>
        /// Pushes every pose the registry says is due, to the peers holding that
        /// creature's 190602.
        ///
        /// Sent to each peer DIRECTLY, never through <c>RelayToOtherPlayers</c> -
        /// that method re-addresses an update to the SENDER's own avatar, so a
        /// creature's pose routed through it would teleport whoever received it. The
        /// same trap is documented on the falling-log pose push and on the nugget
        /// depletion sink; it has already cost this project a debugging round.
        /// </summary>
        private void TickPoses()
        {
            // NOBODY IS WATCHING, SO NOTHING IS SENT - and nothing is even computed.
            // The registry is silent when no pose is DUE, but with a full world the
            // due set is 24 creatures four times a second forever, whether or not a
            // single player is connected. Asking first whether any peer holds any
            // creature keeps an empty server's fauna cost at one integer compare per
            // loop turn. Skipping does not desynchronise anything: a pose is a
            // closed-form function of absolute elapsed time, so the first push after
            // somebody arrives is exactly where the creature would have been.
            if (!AnyPeerHoldsFauna())
            {
                return;
            }

            IReadOnlyList<FaunaPose> poses = _registry.DuePoses();
            if (poses.Count == 0)
            {
                return;
            }

            // ONE sample index per turn, shared by every creature moving in it -
            // exactly what ShipPartMotionService and FallingLogService do. Per-
            // creature increments would make each animal's stamps climb at a rate
            // unrelated to the interval they are actually sent at, and the client's
            // interpolator plays back on the stamps.
            long sample = ++_sample;
            float stamp = ShipPartMotionPolicy.StampFor(sample, _registry.PoseInterval.TotalSeconds);

            foreach (FaunaPose pose in poses)
            {
                foreach (ENetPeerHandle peer in ConnectedPeers())
                {
                    if (!TryGetStoredRef(peer, pose.EntityId, TransformStateComponentId,
                            out ulong refId))
                    {
                        continue;
                    }

                    TransformState.Update update = ShipPartTransform.BuildParentlessWakeUpdate(
                        pose.Position,
                        new Improbable.Corelibrary.Math.Quaternion32(
                            Multiplayer.Placement.Quaternion32Packing.Identity),
                        stamp);

                    // Keep this peer's stored 190602 in step with what it has just
                    // been told, so a re-serve cannot resurrect the seed pose.
                    if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(refId)
                        is TransformState.Data stored)
                    {
                        update.ApplyTo(stored);
                    }

                    SendOPHelper.SendComponentUpdateOp(peer, pose.EntityId,
                        new List<uint> { TransformStateComponentId },
                        new List<object> { update });
                }
            }
        }

        private bool AnyPeerHoldsFauna()
        {
            foreach (PeerState state in _peers.Values)
            {
                if (state.Loaded.Count > 0) return true;
            }
            return false;
        }

        private PeerState StateFor(ENetPeerHandle peer)
        {
            if (_peers.TryGetValue(peer, out PeerState? state)) return state;

            state = new PeerState
            {
                // RemoveEntity is channel 5, and a peer that cannot receive it would
                // keep every creature it ever saw for the rest of its session.
                RemoveSupported = EnetLayer.ENet_PeerChannelCount(peer)
                    > (int)EnetLayer.ENetChannel.REMOVE_ENTITY_OP,
            };
            _peers[peer] = state;
            if (!state.RemoveSupported)
            {
                Console.WriteLine("[island-fauna] peer " + peer.DangerousGetHandle()
                    + " cannot receive RemoveEntity; it will retain every creature it is shown.");
            }
            return state;
        }

        private static bool TryGetStoredRef(ENetPeerHandle peer, long entityId, uint componentId,
            out ulong refId)
        {
            refId = 0;
            return GameState.Instance.ComponentMap.TryGetValue(peer,
                       out Dictionary<long, Dictionary<uint, ulong>>? byEntity)
                && byEntity.TryGetValue(entityId, out Dictionary<uint, ulong>? byComponent)
                && byComponent.TryGetValue(componentId, out refId);
        }

        private static IEnumerable<ENetPeerHandle> ConnectedPeers() =>
            PeerManager.Instance.playerState.Keys.ToList();
    }
}

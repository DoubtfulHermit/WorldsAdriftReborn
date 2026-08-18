using Improbable.Corelibrary.Transforms;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// THE SKY WHALE ON THE WIRE. The impure half of <see cref="SkyWhalePolicy"/>,
    /// <see cref="SkyWhaleCircuit"/> and <see cref="SkyWhaleInterestPolicy"/>: it
    /// seeds one whale per region at boot, streams it to the peers close enough to
    /// care, pushes its pose, and moves its invisible caller ahead of it.
    ///
    /// WIRE SHAPE, per peer (the multiplayer-safety contract):
    /// <list type="bullet">
    /// <item>OUT, at most once per lap: an AssetLoadRequest one cadence before an
    ///   AddEntity naming <see cref="SkyWhalePolicy.PrefabName"/>, to a peer within
    ///   <see cref="SkyWhalePolicy.DefaultLoadRadiusMetres"/> of the ANIMAL that can
    ///   receive RemoveEntity. Nothing else is seeded; the client asks for the
    ///   components its own prefab wants over SEND_COMPONENT_INTEREST, and of those
    ///   exactly ONE matters (190602) - the rest are the inherited ship-part stack,
    ///   which this server already serves or already declares known-absent for every
    ///   loose part in the world.</item>
    /// <item>OUT, 2/s while checked out: one 190602 TransformState carrying the
    ///   complete absolute pose. 190602 is UNRELIABLE by
    ///   <c>MirrorSendPolicy.RelayReliabilityFor</c> and this stream supersedes -
    ///   every update is the whole pose - so a loss costs one frame of smoothness.</item>
    /// <item>OUT, once per lap: RemoveEntity on channel 5 once the animal leaves the
    ///   unload radius.</item>
    /// <item>OUT, once per call (every
    ///   <see cref="SkyWhalePolicy.CallIntervalSeconds"/>): a RemoveEntity and a
    ///   fresh AddEntity for the CALLER, to peers within
    ///   <see cref="SkyWhalePolicy.DefaultCallRadiusMetres"/>. The caller carries NO
    ///   pose stream at all.</item>
    /// <item>IN: nothing. No client sends anything about a whale; there is no
    ///   update handler and nothing to interact with.</item>
    /// </list>
    ///
    /// THE WORST CASE, stated so it can be checked rather than trusted. A peer holds
    /// at most <see cref="SkyWhalePolicy.DefaultPerPeerWhales"/> (one) whale at
    /// <see cref="SkyWhalePolicy.DefaultPoseInterval"/> (500 ms), so this feature
    /// adds TWO transform updates a second to one peer's wire, whatever the world's
    /// region count. It is a SEPARATE service from
    /// <see cref="IslandFaunaService"/> with a separate registry and a separate id
    /// band, so it consumes no fauna slot and the fauna ceiling of 24 x 4 = 96
    /// stands unchanged. Ninety-eight is the new total, still under a fifth of one
    /// 20 Hz avatar relay.
    ///
    /// THE CALLER IS MOVED BY REMOVING AND RE-ADDING IT, and that is forced rather
    /// than chosen. <c>BigCallVisualiser.OnCoordsUpdated</c> assigns its transform
    /// ONLY when the new coordinates are within ONE METRE of where it already is
    /// (RECOVERED), so the entity cannot be slid along behind the whale - there is
    /// no update that would move it. What CAN be relied on is the other half of the
    /// same decompile: the generated reader's event <c>add</c> is
    /// <c>{ ComponentUpdated.Add(...); value(Data.playAudio); }</c>, so subscribing
    /// fires the handler IMMEDIATELY with the seeded value. Seeding 4347 with
    /// <c>playAudio = true</c> therefore makes the call sound the moment the
    /// visualiser enables, with no follow-up update and no ordering race, and
    /// <c>BigCallVisualiser</c>'s own <c>Job.Delay</c> replays it once more after a
    /// RECOVERED 15 s. A CALL IS A CHECKOUT. Calling again from a new place is
    /// therefore a remove and an add, which is exactly the machinery already here.
    ///
    /// A WHALE IS NOT TERRAIN-GATED, unlike a creature. <see cref="IslandFaunaService"/>
    /// refuses to add a manta to a peer that does not hold the manta's island,
    /// because a manta orbits a rock that has to be there first. A whale flies
    /// between islands in open sky; gating it on any island's terrain would withhold
    /// it for the whole of the transit, which is most of the animal's life.
    ///
    /// ONLY PEERS THAT CAN RECEIVE RemoveEntity ARE EVER SHOWN ONE. Channel 5 is a
    /// negotiated capability, and a peer that lacks it could never unload a
    /// 19,821-vertex prefab again. Same guard, same reason, as the fauna and the
    /// falling logs.
    /// </summary>
    internal sealed class SkyWhaleService
    {
        /// <summary>The opt-in gate. See <see cref="SkyWhalePolicy.EnabledEnvVar"/>.</summary>
        internal const string EnableEnv = SkyWhalePolicy.EnabledEnvVar;

        /// <summary>How near the ANIMAL a peer must be to be shown it.</summary>
        internal const string RadiusEnv = SkyWhalePolicy.LoadRadiusEnvVar;

        /// <summary>How near a CALL a peer must be to hear it. The "before you see it" knob.</summary>
        internal const string CallRadiusEnv = SkyWhalePolicy.CallRadiusEnvVar;

        private const uint TransformStateComponentId = 190602;

        /// <summary>4347 BigCallState. RECOVERED; see the type remarks.</summary>
        internal const uint BigCallStateComponentId = 4347;

        private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(120);
        private const int MaxQueuedPerPeer = 8;

        private sealed class PeerState
        {
            /// <summary>Whale entity ids this peer holds. At most one, by budget.</summary>
            public readonly HashSet<long> Loaded = new();
            public readonly Queue<ResourceStreamAction> Pending = new();
            public TimeSpan NextReconcile;
            public TimeSpan NextSend;
            public TimeSpan ContinuousAfter;
            public long AssetRequestedFor;
            public bool RemoveSupported;
            public bool ConnectPlanComplete;

            /// <summary>The caller entity this peer currently holds, or 0.</summary>
            public long CallEntityId;

            /// <summary>Which call it is showing. Compared against the clock's own index.</summary>
            public long CallIndex = long.MinValue;

            /// <summary>Whether the caller's asset has been requested but not yet added.</summary>
            public bool CallAssetRequested;
        }

        private readonly IClock _clock;
        private readonly bool _enabled;
        private readonly double _loadRadius;
        private readonly double _unloadRadius;
        private readonly double _callRadius;
        private readonly TimeSpan _poseInterval;
        private readonly Dictionary<long, SkyWhalePlacement> _whales = new();
        private readonly Dictionary<long, SkyWhalePlacement> _byCallEntity = new();
        private readonly Dictionary<ENetPeerHandle, PeerState> _peers = new();
        private TimeSpan _nextPoseAt;
        private long _sample;
        private int _regionsConsidered;

        internal SkyWhaleService(IClock clock)
            : this(clock,
                SkyWhalePolicy.EnabledFrom(Environment.GetEnvironmentVariable(EnableEnv)),
                SkyWhalePolicy.RadiusFrom(Environment.GetEnvironmentVariable(RadiusEnv),
                    SkyWhalePolicy.DefaultLoadRadiusMetres),
                SkyWhalePolicy.RadiusFrom(Environment.GetEnvironmentVariable(CallRadiusEnv),
                    SkyWhalePolicy.DefaultCallRadiusMetres))
        {
        }

        internal SkyWhaleService(IClock clock, bool enabled,
            double? loadRadius = null, double? callRadius = null, TimeSpan? poseInterval = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _enabled = enabled;
            _loadRadius = loadRadius ?? SkyWhalePolicy.DefaultLoadRadiusMetres;
            _unloadRadius = SkyWhalePolicy.UnloadRadiusFor(_loadRadius);
            _callRadius = callRadius ?? SkyWhalePolicy.DefaultCallRadiusMetres;
            _poseInterval = poseInterval ?? SkyWhalePolicy.DefaultPoseInterval;
            if (_poseInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(poseInterval),
                    "a non-positive pose interval would push a transform per main-loop turn");
            }
        }

        /// <summary>Whether the sky whale is switched on.</summary>
        internal bool Enabled => _enabled;

        /// <summary>How many whales exist. Zero whenever the feature is off.</summary>
        internal int Count => _whales.Count;

        /// <summary>The parsed visual radius this boot decides with, for telemetry.</summary>
        internal double LoadRadiusMetres => _loadRadius;

        /// <summary>The parsed call radius this boot decides with, for telemetry.</summary>
        internal double CallRadiusMetres => _callRadius;

        /// <summary>
        /// The operator console's and the public map's view of the world's whales:
        /// which regions carry one, where each one's current call is coming from,
        /// and AT WHAT CLOCK.
        ///
        /// The clock is the point, exactly as it is in
        /// <see cref="IslandFaunaService.Telemetry"/>. Every pose this service
        /// sends is a closed form of the same <c>_clock.Elapsed</c> reported here,
        /// so a console holding this number and the region's circuit places the
        /// animal exactly where this server has it - without anybody streaming a
        /// position three times a minute and calling the result live.
        ///
        /// READ-ONLY and allocating only the region list. It is called from the
        /// same authoritative poll thread that owns every field it touches, on the
        /// stats writer's few-second cadence, so it needs no lock.
        /// </summary>
        internal SkyWhaleRuntimeStat Telemetry()
        {
            if (!_enabled)
            {
                return SkyWhaleRuntimeStat.Off;
            }

            List<SkyWhaleRegionStat> regions = new List<SkyWhaleRegionStat>(_whales.Count);
            foreach (SkyWhalePlacement placement in _whales.Values)
            {
                SkyWhaleCall call = CurrentCall(placement);
                regions.Add(new SkyWhaleRegionStat(
                    placement.Whale.Region.Value,
                    placement.Whale.EntityId,
                    placement.Whale.CallEntityId,
                    call.Index,
                    call.Position.MetresX, call.Position.MetresY, call.Position.MetresZ));
            }
            // Sorted by region id so the file diffs readably and the console's
            // order does not depend on dictionary iteration.
            regions.Sort((left, right) =>
                string.CompareOrdinal(left.RegionId, right.RegionId));

            return new SkyWhaleRuntimeStat(
                enabled: true,
                clockSeconds: _clock.Elapsed.TotalSeconds,
                loadRadiusMetres: _loadRadius,
                callRadiusMetres: _callRadius,
                poseIntervalMs: (int)Math.Round(_poseInterval.TotalMilliseconds),
                callIntervalSeconds: SkyWhalePolicy.CallIntervalSeconds,
                regions: regions);
        }

        /// <summary>How many whales this peer holds, for the interest stats snapshot.</summary>
        internal int CheckedOutFor(ENetPeerHandle peer) =>
            _peers.TryGetValue(peer, out PeerState? state) ? state.Loaded.Count : 0;

        /// <summary>
        /// Takes the world's whales live, once, at boot.
        ///
        /// A pure function of the selected island set - see
        /// <see cref="SkyWhalePlan.Build"/> - so it runs before any peer can connect
        /// and nothing about it is persisted. It reports the regions it could NOT
        /// give a whale to as well as the ones it could: a region silently missing
        /// its animal is indistinguishable from the feature being broken.
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

            _regionsConsidered = SkyWhalePlan.RegionCount(islands);
            foreach (SkyWhalePlacement placement in SkyWhalePlan.Build(islands))
            {
                _whales[placement.Whale.EntityId] = placement;
                _byCallEntity[placement.Whale.CallEntityId] = placement;
                Console.WriteLine("[sky-whale] " + placement.Whale.Region.Value + ": whale "
                    + placement.Whale.EntityId + " + caller " + placement.Whale.CallEntityId
                    + " on a " + placement.Circuit.Waypoints.Count + "-island circuit of "
                    + placement.Circuit.LengthMetres.ToString("0") + " m ("
                    + (placement.Circuit.CircuitSeconds / 60.0).ToString("0.0")
                    + " min a lap at " + SkyWhalePolicy.MetresPerSecond.ToString("0")
                    + " m/s average), starting at lap fraction "
                    + placement.Circuit.PhaseFraction.ToString("0.000") + ".");
            }

            Console.WriteLine("[sky-whale] ON (" + EnableEnv + "): " + _whales.Count
                + " whale(s) across " + _regionsConsidered + " region(s). PROVENANCE: the"
                + " prefab, its size, its single required component (190602) and BigCall's"
                + " semantics are RECOVERED from the shipped client; the path, speed,"
                + " altitude, cadence, radii and call interval are WAREBORN TUNING - retail's"
                + " whale behaviour was cut and its five Play_SkyWhale_* events ship in no"
                + " bank.");
            Console.WriteLine("[sky-whale] interest is keyed on the ANIMAL at "
                + _loadRadius.ToString("0") + " m load / " + _unloadRadius.ToString("0")
                + " m unload (" + RadiusEnv + "), capped at "
                + SkyWhalePolicy.DefaultPerPeerWhales + " whale per peer. At a "
                + _poseInterval.TotalMilliseconds.ToString("0")
                + " ms pose cadence the worst case ONE peer can receive is "
                + SkyWhalePolicy.WorstCaseUpdatesPerSecond(
                    SkyWhalePolicy.DefaultPerPeerWhales, _poseInterval).ToString("0")
                + " whale transform update(s) a second - a SEPARATE budget from island"
                + " fauna's 96, which is untouched.");
            Console.WriteLine("[sky-whale] the caller is checked out at "
                + _callRadius.ToString("0") + " m (" + CallRadiusEnv + ") and the animal at "
                + _loadRadius.ToString("0") + " m, so a call always arrives from a whale"
                + " that cannot be seen yet - about "
                + ((_callRadius - _loadRadius) / SkyWhalePolicy.MetresPerSecond).ToString("0")
                + " s of warning on a head-on approach. A call is a CHECKOUT, every "
                + SkyWhalePolicy.CallIntervalSeconds.ToString("0")
                + " s, and carries no pose stream at all.");

            if (_whales.Count < _regionsConsidered)
            {
                Console.WriteLine("[sky-whale] " + (_regionsConsidered - _whales.Count)
                    + " region(s) carry NO whale: a closed circuit needs at least "
                    + SkyWhalePolicy.MinimumIslandsPerRegion
                    + " islands and theirs have fewer.");
            }
        }

        /// <summary>Says so when the feature is on but the world gave it nothing to do.</summary>
        internal void WarnIfEmpty()
        {
            if (!_enabled || _whales.Count > 0)
            {
                return;
            }
            Console.WriteLine("[sky-whale] ON but nothing was seeded: the whale needs the"
                + " release-world rollout (" + ReleaseWorldRolloutPolicy.EnvVar + "), because a"
                + " region is a MapFile cell and only release islands have one.");
        }

        /// <summary>Whether an entity id names this server's whale or its caller.</summary>
        internal bool IsSkyWhale(long entityId) =>
            _enabled && (_whales.ContainsKey(entityId) || _byCallEntity.ContainsKey(entityId));

        /// <summary>
        /// Where a whale or its caller is RIGHT NOW, or null for anything else.
        /// <c>ComponentsSerializer</c>'s 190602 branch asks this for the same reason
        /// it asks <see cref="IslandFaunaService.PositionOf"/>: neither is in the
        /// world registry, so <c>TransformSeedFor</c> would hand it the PLAYER
        /// SPAWN and a checking-out peer would find a 173 m animal on its head.
        ///
        /// THE CALLER'S POSITION IS ITS CALL STATION, not the whale's live position.
        /// That is the whole point of the caller: the sound comes from where the
        /// animal WAS when it called, which is somewhere the player cannot see yet.
        /// </summary>
        internal FixedPointPosition? PositionOf(long entityId)
        {
            if (!_enabled) return null;
            if (_whales.TryGetValue(entityId, out SkyWhalePlacement whale))
            {
                return SkyWhaleMotion.WorldPositionAt(whale.Circuit, _clock.Elapsed.TotalSeconds);
            }
            if (_byCallEntity.TryGetValue(entityId, out SkyWhalePlacement caller))
            {
                return CurrentCall(caller).Position;
            }
            return null;
        }

        /// <summary>
        /// The caller's 4347 seed, or null for anything that is not a caller.
        ///
        /// <c>playAudio</c> is TRUE and that is deliberate: the generated reader
        /// fires its handler immediately on subscription with the seeded value
        /// (RECOVERED), so a true seed IS the call. <c>coords</c> is the same
        /// station 190602 was seeded with, so <c>OnCoordsUpdated</c>'s one-metre
        /// test compares a position against itself and passes.
        /// <c>nextCallTimecode</c> is left EMPTY: retail's timecode came from GSim's
        /// clock, this server has no such clock, and nothing in the shipped client
        /// reads the field - an invented value would be a claim for no benefit.
        /// </summary>
        internal Bossa.Travellers.Creatures.Special.BigCallState.Data? CallDataOf(long entityId)
        {
            if (!_enabled || !_byCallEntity.TryGetValue(entityId, out SkyWhalePlacement caller))
            {
                return null;
            }
            FixedPointPosition station = CurrentCall(caller).Position;
            return new Bossa.Travellers.Creatures.Special.BigCallState.Data(
                default(Improbable.Collections.Option<long>),
                true,
                new Improbable.Math.Coordinates(
                    station.MetresX, station.MetresY, station.MetresZ));
        }

        /// <summary>
        /// Guards component interest against the cross-channel unload race, exactly
        /// as <see cref="IslandFaunaService.MayServe"/> does: channel 5 RemoveEntity
        /// and channel 2 interest are independent, so a request may arrive after the
        /// entity was unloaded, and re-seeding it would leave native components on
        /// an entity the client no longer holds.
        /// </summary>
        internal bool MayServe(ENetPeerHandle peer, long entityId)
        {
            if (!IsSkyWhale(entityId)) return true;
            if (!_peers.TryGetValue(peer, out PeerState? state)) return false;
            return state.Loaded.Contains(entityId) || state.CallEntityId == entityId;
        }

        /// <summary>
        /// Hands lifecycle over from the connect spawn plan. Same settle window the
        /// resource and fauna streams wait out, for the same OOM reason: a joiner
        /// instantiating the world on its main thread must not also be handed a
        /// 19,821-vertex animal.
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

        internal void Forget(ENetPeerHandle peer) => _peers.Remove(peer);

        /// <summary>
        /// One call per main-loop turn: per-peer checkout, the caller's lifecycle,
        /// then the pose if one is due. Cheap when the feature is off (one bool) and
        /// when nothing is due (an empty-dictionary walk that allocates nothing).
        /// </summary>
        internal void Tick()
        {
            if (!_enabled || _whales.Count == 0)
            {
                return;
            }

            TickCheckout();
            TickPoses();
        }

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
                if (now < state.NextSend) continue;
                state.NextSend = now + SendInterval;

                // The animal first, then the caller, and one op each per send tick -
                // the same paced drip the resource and fauna streams use, so a
                // joiner is never handed several entities in one frame.
                if (state.Pending.Count > 0)
                {
                    Execute(peer, state);
                }
                else
                {
                    ExecuteCall(peer, state);
                }
            }
        }

        /// <summary>
        /// Rebuilds this peer's pending work from where the WHALES are.
        ///
        /// Keyed on the animal, not on an island; see
        /// <see cref="SkyWhaleInterestPolicy"/> for why that is the right rule here
        /// and was the wrong one for the mantas.
        /// </summary>
        private void Reconcile(ENetPeerHandle peer, PeerState state)
        {
            FixedPointPosition centre = WorldsAdriftRebornGameServer.ResourceInterest.CenterFor(peer);
            double now = _clock.Elapsed.TotalSeconds;

            List<SkyWhaleCandidate> candidates = new List<SkyWhaleCandidate>(_whales.Count);
            foreach ((long entityId, SkyWhalePlacement placement) in _whales)
            {
                candidates.Add(new SkyWhaleCandidate(entityId, SkyWhaleMotion.DistanceSquared(
                    centre, SkyWhaleMotion.WorldPositionAt(placement.Circuit, now))));
            }

            IReadOnlyList<long> desired = SkyWhaleInterestPolicy.Admit(
                candidates, state.Loaded, _loadRadius,
                // A peer with no channel 5 can never unload, so it must never be
                // told to: an infinite unload radius means "retain what you have".
                state.RemoveSupported ? _unloadRadius : double.PositiveInfinity,
                SkyWhalePolicy.DefaultPerPeerWhales);

            ResourceInterestPolicy.ReplacePending(
                state.Pending,
                SkyWhaleInterestPolicy.Reconcile(desired, state.Loaded),
                MaxQueuedPerPeer);
        }

        private void Execute(ENetPeerHandle peer, PeerState state)
        {
            ResourceStreamAction action = state.Pending.Peek();

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
                    Console.WriteLine("[sky-whale] removed whale " + action.EntityId
                        + " from " + peer.DangerousGetHandle() + ".");
                }
                else
                {
                    // Channel 5 was not negotiated after all. Retain the checkout so
                    // a later re-add cannot produce a second, inert copy.
                    state.RemoveSupported = false;
                }
                return;
            }

            if (!_whales.TryGetValue(action.EntityId, out SkyWhalePlacement placement))
            {
                state.Pending.Dequeue();
                return;
            }

            if (state.AssetRequestedFor != action.EntityId)
            {
                SendOPHelper.SendAssetLoadRequestOP(peer, "notNeeded?",
                    SkyWhalePolicy.PrefabName, IslandCatalog.DefaultTerrainAssetContext);
                state.AssetRequestedFor = action.EntityId;
                return; // a full cadence for the asset callback before AddEntity
            }

            state.Pending.Dequeue();
            state.AssetRequestedFor = 0;
            if (SendOPHelper.SendAddEntityOP(peer, action.EntityId,
                    SkyWhalePolicy.PrefabName, IslandCatalog.DefaultTerrainAssetContext))
            {
                state.Loaded.Add(action.EntityId);
                WorldsAdriftRebornGameServer.SentEntities.MarkSent(peer, action.EntityId);
                Console.WriteLine("[sky-whale] added whale " + action.EntityId + " ("
                    + placement.Whale.Region.Value + ") to " + peer.DangerousGetHandle() + ".");
            }
        }

        /// <summary>
        /// THE CALL, as a lifecycle rather than as an update.
        ///
        /// One op per send tick, in the only order the RECOVERED client semantics
        /// allow: a stale caller is REMOVED, then the current one is ADDED, and the
        /// add is what makes the sound. There is no third step, because there is no
        /// update that could move it - see the type remarks on the one-metre rule.
        /// </summary>
        private void ExecuteCall(ENetPeerHandle peer, PeerState state)
        {
            (long entityId, long index) = DesiredCallFor(peer, state);

            bool stale = state.CallEntityId != 0
                && (state.CallEntityId != entityId || state.CallIndex != index);
            if (stale)
            {
                if (!state.RemoveSupported)
                {
                    // It can never be taken back, so it must never be replaced: a
                    // second AddEntity for the same id would corrupt the client's
                    // entity map. This peer keeps the one call it was given.
                    return;
                }
                if (SendOPHelper.SendRemoveEntityOP(peer, state.CallEntityId))
                {
                    PeerCheckoutCleanup.RemoveEntity(peer, state.CallEntityId);
                    state.CallEntityId = 0;
                    state.CallAssetRequested = false;
                }
                else
                {
                    state.RemoveSupported = false;
                }
                return;
            }

            if (state.CallEntityId != 0 || entityId == 0)
            {
                return;
            }

            if (!state.CallAssetRequested)
            {
                SendOPHelper.SendAssetLoadRequestOP(peer, "notNeeded?",
                    SkyWhalePolicy.CallPrefabName, IslandCatalog.DefaultTerrainAssetContext);
                state.CallAssetRequested = true;
                return; // a full cadence for the asset callback before AddEntity
            }

            if (SendOPHelper.SendAddEntityOP(peer, entityId,
                    SkyWhalePolicy.CallPrefabName, IslandCatalog.DefaultTerrainAssetContext))
            {
                state.CallEntityId = entityId;
                state.CallIndex = index;
                state.CallAssetRequested = false;
                WorldsAdriftRebornGameServer.SentEntities.MarkSent(peer, entityId);
                Console.WriteLine("[sky-whale] call " + index + " from " + entityId
                    + " to " + peer.DangerousGetHandle() + ".");
            }
        }

        /// <summary>
        /// Which call this peer should be hearing: the nearest whale's current call,
        /// if its STATION is inside the call radius. Zero for "none".
        ///
        /// The station rather than the animal, deliberately. A call is a fixed place
        /// in the world for its whole two-minute window; a peer flying away from a
        /// call it can no longer plausibly hear should stop holding it, and a peer
        /// flying toward one should pick it up - both of which are questions about
        /// where the SOUND is, not about where the whale has got to since.
        /// </summary>
        private (long EntityId, long Index) DesiredCallFor(ENetPeerHandle peer, PeerState state)
        {
            if (_callRadius <= 0.0) return (0L, 0L);

            FixedPointPosition centre = WorldsAdriftRebornGameServer.ResourceInterest.CenterFor(peer);
            double best = _callRadius * _callRadius;
            long bestEntity = 0L;
            long bestIndex = 0L;
            foreach (SkyWhalePlacement placement in _whales.Values)
            {
                SkyWhaleCall call = CurrentCall(placement);
                double distance = SkyWhaleMotion.DistanceSquared(centre, call.Position);
                if (distance > best) continue;
                best = distance;
                bestEntity = placement.Whale.CallEntityId;
                bestIndex = call.Index;
            }
            return (bestEntity, bestIndex);
        }

        /// <summary>Which call one whale is on right now. A pure step function of the clock.</summary>
        private SkyWhaleCall CurrentCall(SkyWhalePlacement placement) =>
            SkyWhaleMotion.CallAt(placement.Circuit, _clock.Elapsed.TotalSeconds);

        /// <summary>
        /// Pushes the pose of every whale somebody is holding.
        ///
        /// NOBODY IS WATCHING, SO NOTHING IS SENT - and nothing is even computed.
        /// Skipping does not desynchronise anything: a pose is a closed form of
        /// absolute elapsed time, so the first push after somebody arrives is
        /// exactly where the animal would have been.
        ///
        /// Sent to each peer DIRECTLY, never through <c>RelayToOtherPlayers</c> -
        /// that method re-addresses an update to the SENDER's own avatar, so a
        /// whale's pose routed through it would teleport whoever received it. The
        /// same trap is documented on the fauna pose push, the falling-log pose
        /// push and the nugget depletion sink.
        /// </summary>
        private void TickPoses()
        {
            TimeSpan now = _clock.Elapsed;
            if (now < _nextPoseAt)
            {
                return;
            }
            _nextPoseAt = now + _poseInterval;

            // ONE sample index per turn, shared by every whale moving in it -
            // exactly what ShipPartMotionService, FallingLogService and
            // IslandFaunaService do. Per-entity increments would make each animal's
            // stamps climb at a rate unrelated to the interval they are sent at, and
            // the client's interpolator plays back on the stamps.
            long sample = ++_sample;
            float stamp = ShipPartMotionPolicy.StampFor(sample, _poseInterval.TotalSeconds);

            foreach ((ENetPeerHandle peer, PeerState state) in _peers)
            {
                foreach (long entityId in state.Loaded)
                {
                    if (!_whales.TryGetValue(entityId, out SkyWhalePlacement placement)
                        || !TryGetStoredRef(peer, entityId, TransformStateComponentId,
                            out ulong refId))
                    {
                        continue;
                    }

                    FaunaTransform pose = SkyWhaleMotion.WorldTransformAt(
                        placement.Circuit, now.TotalSeconds);

                    // THE ROTATION RIDES THIS SAME UPDATE. 190602 already carries a
                    // localRotation, so the heading costs no extra packet, no extra
                    // component and no extra send. Sending the identity sentinel
                    // instead - as the fauna path once did - is not neutral: the
                    // client applies position and rotation TOGETHER whenever the
                    // position moved, so identity would re-slam a 173 m animal to
                    // "nose along world +Z" twice a second regardless of travel.
                    TransformState.Update update = ShipPartTransform.BuildParentlessWakeUpdate(
                        pose.Position,
                        new Improbable.Corelibrary.Math.Quaternion32(
                            Multiplayer.Placement.Quaternion32Packing.Encode(
                                pose.Rotation.W, pose.Rotation.X,
                                pose.Rotation.Y, pose.Rotation.Z)),
                        stamp);

                    // Keep this peer's stored 190602 in step with what it has just
                    // been told, so a re-serve cannot resurrect the seed pose.
                    if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(refId)
                        is TransformState.Data stored)
                    {
                        update.ApplyTo(stored);
                    }

                    SendOPHelper.SendComponentUpdateOp(peer, entityId,
                        new List<uint> { TransformStateComponentId },
                        new List<object> { update });
                }
            }
        }

        private PeerState StateFor(ENetPeerHandle peer)
        {
            if (_peers.TryGetValue(peer, out PeerState? state)) return state;

            state = new PeerState
            {
                RemoveSupported = EnetLayer.ENet_PeerChannelCount(peer)
                    > (int)EnetLayer.ENetChannel.REMOVE_ENTITY_OP,
            };
            _peers[peer] = state;
            if (!state.RemoveSupported)
            {
                Console.WriteLine("[sky-whale] peer " + peer.DangerousGetHandle()
                    + " cannot receive RemoveEntity; it will retain the first whale and the"
                    + " first call it is shown, and be shown no others.");
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
    }
}

using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Components;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// Continuous per-peer checkout for optional island terrain. Haven is never
    /// owned by this service: it remains the unconditional load-bearing connect
    /// terrain. Registered optional terrain is paced through an exact correlated
    /// asset acknowledgement before AddEntity, with a bounded retry/fallback.
    /// </summary>
    internal sealed class IslandTerrainInterestService : IDisposable
    {
        private sealed class AssetFlight
        {
            public TerrainStreamAction Action;
            public TimeSpan RequestedAt;
            public TimeSpan LastRequestAt;
            public bool Acknowledged;

            /// <summary>Re-sends of this one request. Telemetry only.</summary>
            public int RetryCount;

            /// <summary>Whether this flight's ack-timeout fallback was already recorded.</summary>
            public bool FallbackNoted;
        }

        private sealed class PeerState
        {
            public readonly Queue<TerrainStreamAction> Pending = new();
            public FixedPointPosition Position = SpawnPolicy.PlayerSpawnPosition;
            public IslandId? ConfirmedGround;
            public AssetFlight? Asset;
            public TimeSpan NextReconcile;
            public TimeSpan NextSend;
            public TimeSpan ContinuousAfter;
            public bool ConnectPlanComplete;
            public bool RemoveSupported;
            public bool CorrelatedAckObserved;

            /// <summary>
            /// A process-local ordinal. It exists so a peer that has not spawned a
            /// player entity yet still has a stable telemetry row; no ENet handle
            /// or pointer is ever exported in its place.
            /// </summary>
            public int Slot;

            /// <summary>
            /// The island whose queued removal is currently blocked on its
            /// resources draining. Observation only: it records the answer the
            /// drain gate already gave inside <see cref="Execute"/>, so telemetry
            /// never has to ask that question itself (asking would mutate).
            /// </summary>
            public IslandId? DrainWaitingIsland;

            /// <summary>Islands whose last lifecycle step failed for this peer.</summary>
            public readonly HashSet<IslandId> Failed = new();

            /// <summary>
            /// The last destination readiness reported, and for which island, so
            /// the event ring records a CHANGE rather than every poll of a
            /// deferred teleport.
            /// </summary>
            public TerrainDestinationStatus? LastDestinationStatus;
            public IslandId? LastDestinationIsland;
        }

        /// <summary>
        /// Why one island is or is not stream-managed this boot. Recorded for
        /// EVERY registered island, including the ones that failed a prerequisite,
        /// so the console can say "not managed because there is no extracted
        /// envelope" instead of silently omitting the row.
        /// </summary>
        private readonly struct IslandCandidacy
        {
            public IslandCandidacy(IslandDefinition island, long entityId, bool registered,
                bool locallyOwned, IslandTerrainEnvelope? envelope, bool unconditional,
                bool managed)
            {
                Island = island;
                EntityId = entityId;
                Registered = registered;
                LocallyOwned = locallyOwned;
                Envelope = envelope;
                Unconditional = unconditional;
                Managed = managed;
            }

            public IslandDefinition Island { get; }
            public long EntityId { get; }
            public bool Registered { get; }
            public bool LocallyOwned { get; }
            public IslandTerrainEnvelope? Envelope { get; }
            public bool Unconditional { get; }
            public bool Managed { get; }
        }

        private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(120);
        private const int MaxQueuedPerPeer = 32;
        private const string AssetType = "notNeeded?";

        private readonly IClock _clock;
        private readonly WorldEntityRegistry _registry;
        private readonly IslandRegistry _islands;
        private readonly Dictionary<ENetPeerHandle, PeerState> _peers = new();
        private readonly IslandTerrainPeerLedger<ENetPeerHandle> _ledger = new();
        private readonly Dictionary<long, TerrainStreamCandidate> _candidates = new();
        private readonly Dictionary<IslandId, long> _entityByIsland = new();
        private readonly List<IslandCandidacy> _candidacy = new();
        private readonly TerrainEventLog _events = new();
        private readonly Func<ENetPeerHandle, IslandId, bool> _prepareAndCheckResourcesDrained;
        private readonly bool _resourceDrainWired;
        private readonly TimeSpan _settleDelay;
        private readonly IDisposable? _assetAckSubscription;
        private int _nextSlot;

        internal bool Enabled { get; }

        /// <summary>
        /// Whether the environment ASKED for terrain checkout. Distinct from
        /// <see cref="Enabled"/> on purpose: the resource-interest prerequisite can
        /// hold the feature back, and an operator who set the variable must be able
        /// to see that it was read and then refused, not merely "off".
        /// </summary>
        internal bool Requested { get; }
        internal double LoadRadiusMetres { get; }
        internal double UnloadRadiusMetres { get; }
        internal TimeSpan AssetAckTimeout { get; }

        internal IslandTerrainInterestService(
            IClock clock,
            WorldEntityRegistry registry,
            IslandRegistry islands,
            WorldDirectory directory,
            Func<ENetPeerHandle, IslandId, bool>? prepareAndCheckResourcesDrained = null,
            Func<long, bool>? isLocallyOwned = null,
            bool? enabled = null,
            TimeSpan? settleDelay = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _islands = islands ?? throw new ArgumentNullException(nameof(islands));
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            _resourceDrainWired = prepareAndCheckResourcesDrained != null;
            _prepareAndCheckResourcesDrained = prepareAndCheckResourcesDrained ?? ((_, _) => true);
            isLocallyOwned ??= _ => true;
            _settleDelay = settleDelay ?? ResourceInterestPolicy.SettleDelayFrom(
                Environment.GetEnvironmentVariable(ResourceInterestPolicy.SettleDelayEnvVar));

            Requested = IslandTerrainInterestPolicy.EnabledFrom(
                Environment.GetEnvironmentVariable(IslandTerrainInterestPolicy.EnabledEnvVar));
            Enabled = enabled ?? Requested;
            LoadRadiusMetres = IslandTerrainInterestPolicy.LoadRadiusFrom(
                Environment.GetEnvironmentVariable(IslandTerrainInterestPolicy.LoadRadiusEnvVar));
            UnloadRadiusMetres = IslandTerrainInterestPolicy.UnloadRadiusFrom(
                Environment.GetEnvironmentVariable(IslandTerrainInterestPolicy.UnloadRadiusEnvVar),
                LoadRadiusMetres);
            AssetAckTimeout = IslandTerrainInterestPolicy.AssetAckTimeoutFrom(
                Environment.GetEnvironmentVariable(IslandTerrainInterestPolicy.AssetAckTimeoutEnvVar));

            // Registration truth is recorded for every island whether or not the
            // feature is on. These are pure reads of the directory and registry, so
            // a disabled server behaves exactly as before while still being able to
            // tell the operator WHY an island is not managed.
            foreach (IslandDefinition island in islands.All)
            {
                bool unconditional = island.Id == IslandCatalog.HavenId;
                IslandTerrainEnvelope? envelope = IslandTerrainEnvelopes.ByIsland(island.Id);
                WorldDirectoryEntry? entry = directory.ByEntityKey(island.WorldEntityKey);
                long? entityId = registry.BoundEntityIdFor(island.WorldEntityKey);
                bool registered = entry != null && entry.IslandId == island.Id && entityId != null;
                bool locallyOwned = entityId != null && isLocallyOwned(entityId.Value);
                bool managed = !unconditional && envelope != null && registered && locallyOwned;
                _candidacy.Add(new IslandCandidacy(island, entityId ?? 0, registered,
                    locallyOwned, envelope, unconditional, managed));
            }

            if (!Enabled) return;
            _assetAckSubscription = AssetLoadedAckRouter.Subscribe(ack =>
                NoteAssetLoadedAck(ack.PeerId, ack.AssetType, ack.Name, ack.Context));
            foreach (IslandCandidacy candidacy in _candidacy)
            {
                if (!candidacy.Managed) continue;
                var candidate = new TerrainStreamCandidate(
                    candidacy.EntityId, candidacy.Island, candidacy.Envelope!.Value);
                _candidates.Add(candidacy.EntityId, candidate);
                _entityByIsland.Add(candidacy.Island.Id, candidacy.EntityId);
            }
        }

        internal void NotePeerConnected(ENetPeerHandle peer)
        {
            if (Enabled) StateFor(peer);
        }

        internal void NoteLoaded(ENetPeerHandle peer, long entityId)
        {
            if (!Enabled || !_candidates.ContainsKey(entityId)) return;
            PeerState state = StateFor(peer);
            _ledger.NoteLoaded(peer, entityId);
            if (state.Asset?.Action.EntityId == entityId) state.Asset = null;
        }

        internal void NoteConnectPlanComplete(ENetPeerHandle peer)
        {
            if (!Enabled) return;
            PeerState state = StateFor(peer);
            // The server observes the final spawn-plan sentinel on every poll.
            // Arm continuous interest exactly once; resetting ContinuousAfter
            // here would perpetually move the settle boundary into the future.
            if (!IslandTerrainInterestPolicy.ShouldArmContinuous(
                    state.ConnectPlanComplete)) return;
            state.ConnectPlanComplete = true;
            state.ContinuousAfter = _clock.Elapsed + _settleDelay;
            state.NextReconcile = state.ContinuousAfter;
        }

        /// <summary>Movement changes proximity only; it never invents a ground island.</summary>
        internal void ObserveGlobalPosition(ENetPeerHandle peer, FixedPointPosition position)
        {
            if (!Enabled) return;
            PeerState state = StateFor(peer);
            state.Position = position;
            state.NextReconcile = TimeSpan.Zero;
        }

        /// <summary>1073 relative-to is authoritative evidence of current terrain.</summary>
        internal void ObserveRelativeTo(ENetPeerHandle peer, long terrainEntityId)
        {
            if (!Enabled) return;
            PeerState state = StateFor(peer);
            WorldEntity? relative = _registry.ByEntityId(terrainEntityId);
            IslandDefinition? island = _islands.ByWorldEntityKey(relative?.Key);
            // An explicit ship/deck/object relative-to is also useful evidence: the
            // player is no longer grounded on the old island. A missing sparse field
            // is not evidence and never reaches this method.
            state.ConfirmedGround = island?.Id;
            if (island != null) _ledger.ClearDestination(peer);
            state.NextReconcile = TimeSpan.Zero;
        }

        internal TerrainDestinationStatus RequestDestination(ENetPeerHandle peer, IslandId islandId)
        {
            if (!Enabled) return TerrainDestinationStatus.Disabled;
            if (islandId == IslandCatalog.HavenId) return TerrainDestinationStatus.Ready;
            PeerState state = StateFor(peer);
            state.NextReconcile = TimeSpan.Zero;
            bool waiting = _entityByIsland.TryGetValue(islandId, out long entityId)
                && state.Asset?.Action.EntityId == entityId;
            TerrainDestinationStatus status = _ledger.RequestDestination(
                peer, islandId, IslandCatalog.HavenId, _entityByIsland, Enabled, waiting);
            // Deferred teleports re-ask every poll; only a CHANGE is an event, so a
            // long wait cannot flush the bounded ring with one repeated fact.
            if (state.LastDestinationStatus != status || state.LastDestinationIsland != islandId)
            {
                state.LastDestinationStatus = status;
                state.LastDestinationIsland = islandId;
                _events.Record(_clock.Elapsed, EventKindFor(status), islandId, state.Slot,
                    status == TerrainDestinationStatus.Ready);
            }
            return status;
        }

        private static TerrainEventKind EventKindFor(TerrainDestinationStatus status) => status switch
        {
            TerrainDestinationStatus.Ready => TerrainEventKind.TeleportReady,
            TerrainDestinationStatus.Queued => TerrainEventKind.TeleportWaiting,
            TerrainDestinationStatus.WaitingForAsset => TerrainEventKind.TeleportWaiting,
            _ => TerrainEventKind.TeleportRefused,
        };

        internal bool IsTerrainReady(ENetPeerHandle peer, IslandId islandId)
        {
            if (islandId == IslandCatalog.HavenId) return true;
            return Enabled && _entityByIsland.TryGetValue(islandId, out long entityId)
                && _peers.ContainsKey(peer)
                && _ledger.IsLoaded(peer, entityId)
                && WorldsAdriftRebornGameServer.SentEntities.WasSent(peer, entityId);
        }

        internal bool MayServe(ENetPeerHandle peer, long terrainEntityId) =>
            !Enabled || !_candidates.ContainsKey(terrainEntityId)
            || WorldsAdriftRebornGameServer.SentEntities.WasSent(peer, terrainEntityId);

        internal void NoteAssetLoadedAck(ulong peerId, string assetType, string name, string context)
        {
            if (!Enabled) return;
            ENetPeerHandle? peer = PeerIdentity.Instance.Resolve(new IntPtr((long)peerId));
            if (peer == null || !_peers.TryGetValue(peer, out PeerState? state)) return;
            // A marked v1 response is the protocol capability proof. Merely having
            // channel 5 is insufficient: legacy clients negotiate it in some builds
            // but return only the opaque eight-byte acknowledgement, so they retain
            // every terrain checkout for the life of the session.
            state.CorrelatedAckObserved = true;
            if (state.Asset == null) return;
            WorldEntity? entity = _registry.ByEntityId(state.Asset.Action.EntityId);
            if (entity != null && IslandTerrainInterestPolicy.ExactAssetAck(
                    PeerIdentity.IdOf(peer), AssetType, entity.AssetName, entity.AssetContext,
                    peerId, assetType, name, context))
            {
                state.Asset.Acknowledged = true;
                state.NextSend = TimeSpan.Zero;
                _events.Record(_clock.Elapsed, TerrainEventKind.AssetAcknowledged,
                    state.Asset.Action.IslandId, state.Slot, success: true);
            }
        }

        internal void Tick()
        {
            if (!Enabled) return;
            TimeSpan now = _clock.Elapsed;
            foreach ((ENetPeerHandle peer, PeerState state) in _peers.ToArray())
            {
                if (!state.ConnectPlanComplete || now < state.ContinuousAfter) continue;
                if (now >= state.NextReconcile)
                {
                    state.NextReconcile = now + ReconcileInterval;
                    Reconcile(peer, state);
                }
                if (now >= state.NextSend && state.Pending.Count > 0)
                {
                    state.NextSend = now + SendInterval;
                    Execute(peer, state, now);
                }
            }
        }

        /// <summary>
        /// An immutable operator-facing copy of the whole terrain lifecycle.
        ///
        /// This is a READ. It allocates no entity id, sends nothing, schedules
        /// nothing and asks no gate that would mutate state: every fact it reports
        /// was already decided by <see cref="Tick"/> on the single authoritative
        /// poll loop, and is copied out here. Call it from that same loop.
        ///
        /// <paramref name="resourceNodeCount"/> and
        /// <paramref name="checkedOutResourceCount"/> are optional read-only
        /// island lookups; when they are absent the counts are reported as -1
        /// (unknown) rather than as zero, which would read as "drained".
        /// </summary>
        internal TerrainRuntimeStat Snapshot(
            Func<IslandId, int>? resourceNodeCount = null,
            Func<IslandId, int>? checkedOutResourceCount = null,
            Func<ENetPeerHandle, long>? playerEntityIdOf = null)
        {
            playerEntityIdOf ??= DefaultPlayerEntityIdOf;
            TimeSpan now = _clock.Elapsed;
            KeyValuePair<ENetPeerHandle, PeerState>[] peers =
                _peers.OrderBy(pair => pair.Value.Slot).ToArray();

            Dictionary<IslandId, int[]> islandCounts = new();
            foreach (IslandCandidacy candidacy in _candidacy)
                islandCounts[candidacy.Island.Id] =
                    new int[TerrainTelemetryLabels.AllStates.Count];

            Dictionary<int, long> entityBySlot = new();
            List<TerrainPlayerStat> players = new(peers.Length);
            foreach ((ENetPeerHandle peer, PeerState state) in peers)
            {
                long playerEntityId = playerEntityIdOf(peer);
                entityBySlot[state.Slot] = playerEntityId;

                TerrainStreamAction[] pending = state.Pending.ToArray();
                bool mayRemove = IslandTerrainInterestPolicy.MayRemove(
                    state.RemoveSupported, state.CorrelatedAckObserved);

                List<TerrainPeerIslandStat> cells = new(_candidacy.Count);
                foreach (IslandCandidacy candidacy in _candidacy)
                {
                    if (!candidacy.Managed) continue;
                    long entityId = candidacy.EntityId;
                    bool assetInFlight = state.Asset != null
                        && state.Asset.Action.EntityId == entityId;
                    TerrainCheckoutState cell = IslandTerrainStatePolicy.CellState(
                        loaded: _ledger.IsLoaded(peer, entityId),
                        mayRemove: mayRemove,
                        pendingAdd: pending.Any(a => a.EntityId == entityId
                            && a.Kind == TerrainStreamActionKind.Add),
                        pendingRemove: pending.Any(a => a.EntityId == entityId
                            && a.Kind == TerrainStreamActionKind.Remove),
                        drainWaiting: state.DrainWaitingIsland == candidacy.Island.Id,
                        assetInFlight: assetInFlight,
                        assetAcknowledged: assetInFlight && state.Asset!.Acknowledged,
                        failed: state.Failed.Contains(candidacy.Island.Id));
                    cells.Add(new TerrainPeerIslandStat(candidacy.Island.Id.Value, cell));
                    islandCounts[candidacy.Island.Id][(int)cell]++;
                }

                TerrainPendingActionKind pendingAction = TerrainPendingActionKind.None;
                string? pendingIslandId = null;
                if (pending.Length > 0)
                {
                    TerrainStreamAction head = pending[0];
                    pendingIslandId = head.IslandId.Value;
                    pendingAction = head.Kind == TerrainStreamActionKind.Add
                        ? TerrainPendingActionKind.Load
                        : state.DrainWaitingIsland == head.IslandId
                            ? TerrainPendingActionKind.ResourceDrain
                            : TerrainPendingActionKind.Remove;
                }

                TerrainAssetFlightStat? asset = null;
                if (state.Asset != null)
                {
                    WorldEntity? flightEntity = _registry.ByEntityId(state.Asset.Action.EntityId);
                    asset = new TerrainAssetFlightStat(
                        state.Asset.Action.IslandId.Value,
                        flightEntity?.AssetName ?? string.Empty,
                        Milliseconds(now - state.Asset.RequestedAt),
                        Milliseconds(now - state.Asset.LastRequestAt),
                        state.Asset.RetryCount,
                        state.Asset.Acknowledged,
                        IslandTerrainInterestPolicy.AssetFallbackDue(
                            state.Asset.RequestedAt, now, AssetAckTimeout));
                }

                players.Add(new TerrainPlayerStat(
                    playerEntityId,
                    state.Slot,
                    state.Position.MetresX, state.Position.MetresY, state.Position.MetresZ,
                    state.ConfirmedGround?.Value,
                    _ledger.RequestedDestination(peer)?.Value,
                    pendingAction,
                    pendingIslandId,
                    asset,
                    state.CorrelatedAckObserved,
                    state.RemoveSupported,
                    state.ConnectPlanComplete,
                    state.ConnectPlanComplete && now < state.ContinuousAfter,
                    cells));
            }

            List<TerrainIslandStat> islands = new(_candidacy.Count);
            foreach (IslandCandidacy candidacy in _candidacy)
            {
                int[] counts = islandCounts[candidacy.Island.Id];
                IslandTerrainEnvelope envelope = candidacy.Envelope ?? default;
                islands.Add(new TerrainIslandStat(
                    candidacy.Island.Id.Value,
                    candidacy.Island.DisplayName,
                    candidacy.EntityId,
                    candidacy.Registered,
                    candidacy.LocallyOwned,
                    candidacy.Envelope != null,
                    candidacy.Managed,
                    candidacy.Unconditional,
                    envelope.MinX, envelope.MinY, envelope.MinZ,
                    envelope.MaxX, envelope.MaxY, envelope.MaxZ,
                    counts[(int)TerrainCheckoutState.Ready],
                    counts[(int)TerrainCheckoutState.Requesting]
                        + counts[(int)TerrainCheckoutState.WaitingAck],
                    counts[(int)TerrainCheckoutState.Draining],
                    counts[(int)TerrainCheckoutState.Unloading],
                    counts[(int)TerrainCheckoutState.RetainedLegacy],
                    counts[(int)TerrainCheckoutState.Error],
                    resourceNodeCount?.Invoke(candidacy.Island.Id) ?? -1,
                    checkedOutResourceCount?.Invoke(candidacy.Island.Id) ?? -1,
                    _resourceDrainWired));
            }

            return new TerrainRuntimeStat(
                Requested, Enabled,
                LoadRadiusMetres, UnloadRadiusMetres,
                (long)AssetAckTimeout.TotalMilliseconds,
                (long)_settleDelay.TotalMilliseconds,
                _candidates.Count,
                _peers.Count,
                players,
                islands,
                _events.Snapshot(now, slot =>
                    entityBySlot.TryGetValue(slot, out long entityId) ? entityId : 0));
        }

        private static long Milliseconds(TimeSpan span) =>
            (long)Math.Max(0, span.TotalMilliseconds);

        /// <summary>
        /// The player entity a peer controls, or 0 when it has not spawned one yet.
        /// The peer handle itself never leaves this class.
        /// </summary>
        private static long DefaultPlayerEntityIdOf(ENetPeerHandle peer) =>
            WorldsAdriftRebornGameServer.Players.EntityOf(PeerIdentity.IdOf(peer)) ?? 0;

        internal void Forget(ENetPeerHandle peer)
        {
            _peers.Remove(peer);
            _ledger.Forget(peer);
        }

        public void Dispose()
        {
            _assetAckSubscription?.Dispose();
            _peers.Clear();
            _ledger.Clear();
            _events.Clear();
        }

        private void Reconcile(ENetPeerHandle peer, PeerState state)
        {
            // The global ledgers are authoritative. Connect-plan and other checkout
            // paths may have completed while our paced queue was waiting.
            foreach (long entityId in _candidates.Keys)
            {
                if (WorldsAdriftRebornGameServer.SentEntities.WasSent(peer, entityId))
                    _ledger.NoteLoaded(peer, entityId);
                else
                    _ledger.NoteRemoved(peer, entityId);
            }

            TerrainStreamAction[] desired = IslandTerrainInterestPolicy.Reconcile(
                state.Position, _candidates.Values, _ledger.LoadedFor(peer),
                state.ConfirmedGround, _ledger.RequestedDestination(peer),
                LoadRadiusMetres, UnloadRadiusMetres,
                islandId => IslandTerrainInterestPolicy.MayRemove(
                        state.RemoveSupported, state.CorrelatedAckObserved)
                    && _prepareAndCheckResourcesDrained(peer, islandId))
                .Take(MaxQueuedPerPeer).ToArray();

            TerrainStreamAction? flightAction = state.Asset?.Action;
            bool retainFlight = flightAction != null && desired.Contains(flightAction.Value);
            if (!retainFlight) state.Asset = null;
            state.Pending.Clear();
            foreach (TerrainStreamAction action in desired) state.Pending.Enqueue(action);

            // Telemetry must not outlive the work it describes: a drain wait or a
            // failed step for an island this peer no longer has queued is history,
            // and history lives in the event ring, not in the live matrix.
            if (state.DrainWaitingIsland != null && !desired.Any(a =>
                    a.Kind == TerrainStreamActionKind.Remove
                    && a.IslandId == state.DrainWaitingIsland.Value))
                state.DrainWaitingIsland = null;
            state.Failed.RemoveWhere(island => !desired.Any(a => a.IslandId == island));
        }

        /// <summary>
        /// Records that this peer's queued removal is held back by its island's
        /// resources. Only a transition is an event; the gate itself is polled.
        /// </summary>
        private void NoteDrainWaiting(PeerState state, IslandId islandId, TimeSpan now)
        {
            if (state.DrainWaitingIsland == islandId) return;
            state.DrainWaitingIsland = islandId;
            _events.Record(now, TerrainEventKind.DrainWaiting, islandId, state.Slot,
                success: false);
        }

        private void Execute(ENetPeerHandle peer, PeerState state, TimeSpan now)
        {
            TerrainStreamAction action = state.Pending.Peek();
            bool sent = WorldsAdriftRebornGameServer.SentEntities.WasSent(peer, action.EntityId);
            if (action.Kind == TerrainStreamActionKind.Add && sent)
            {
                _ledger.NoteLoaded(peer, action.EntityId);
                CompleteHead(state);
                return;
            }
            if (action.Kind == TerrainStreamActionKind.Remove && !sent)
            {
                _ledger.NoteRemoved(peer, action.EntityId);
                CompleteHead(state);
                return;
            }

            if (action.Kind == TerrainStreamActionKind.Remove)
            {
                if (!IslandTerrainInterestPolicy.MayRemove(
                        state.RemoveSupported, state.CorrelatedAckObserved)) return;
                if (!_prepareAndCheckResourcesDrained(peer, action.IslandId))
                {
                    // The gate is polled every send tick; record the transition
                    // into waiting, not each repetition of it.
                    NoteDrainWaiting(state, action.IslandId, now);
                    return;
                }
                state.DrainWaitingIsland = null;
                if (SendOPHelper.SendRemoveEntityOP(peer, action.EntityId))
                {
                    PeerCheckoutCleanup.RemoveEntity(peer, action.EntityId);
                    _ledger.NoteRemoved(peer, action.EntityId);
                    state.Failed.Remove(action.IslandId);
                    _events.Record(now, TerrainEventKind.RemoveSucceeded, action.IslandId,
                        state.Slot, success: true);
                    CompleteHead(state);
                    Console.WriteLine("[terrain-interest] removed " + action.IslandId
                        + " terrain " + action.EntityId + " from peer "
                        + peer.DangerousGetHandle() + ".");
                }
                else
                {
                    state.RemoveSupported = false;
                    state.Pending.Clear();
                    state.Asset = null;
                    state.DrainWaitingIsland = null;
                    _events.Record(now, TerrainEventKind.RemoveFailed, action.IslandId,
                        state.Slot, success: false);
                    Console.WriteLine("[terrain-interest] peer cannot receive RemoveEntity;"
                        + " retaining visited terrain for compatibility.");
                }
                return;
            }

            WorldEntity? entity = _registry.ByEntityId(action.EntityId);
            if (entity == null)
            {
                CompleteHead(state);
                return;
            }

            if (state.Asset == null)
            {
                if (SendOPHelper.SendAssetLoadRequestOP(peer, AssetType,
                        entity.AssetName, entity.AssetContext))
                {
                    state.Asset = new AssetFlight
                    {
                        Action = action,
                        RequestedAt = now,
                        LastRequestAt = now,
                    };
                    _events.Record(now, TerrainEventKind.Requested, action.IslandId,
                        state.Slot, success: true);
                }
                return;
            }

            bool fallback = IslandTerrainInterestPolicy.AssetFallbackDue(
                state.Asset.RequestedAt, now, AssetAckTimeout);
            if (!state.Asset.Acknowledged && !fallback)
            {
                if (IslandTerrainInterestPolicy.AssetRetryDue(state.Asset.LastRequestAt, now)
                    && SendOPHelper.SendAssetLoadRequestOP(peer, AssetType,
                        entity.AssetName, entity.AssetContext))
                {
                    state.Asset.LastRequestAt = now;
                    state.Asset.RetryCount++;
                    _events.Record(now, TerrainEventKind.AssetRetried, action.IslandId,
                        state.Slot, success: true);
                }
                return;
            }

            // One fallback event per flight, not one per send tick: the ring is a
            // 64-entry diagnostic window, and a repeated fact must not evict the
            // history that explains it.
            if (fallback && !state.Asset.Acknowledged && !state.Asset.FallbackNoted)
            {
                state.Asset.FallbackNoted = true;
                _events.Record(now, TerrainEventKind.AssetFallback, action.IslandId,
                    state.Slot, success: false);
            }

            if (!CheckoutTerrain(peer, action.EntityId, entity))
            {
                if (state.Failed.Add(action.IslandId))
                    _events.Record(now, TerrainEventKind.AddFailed, action.IslandId,
                        state.Slot, success: false);
            }
            else
            {
                state.Failed.Remove(action.IslandId);
                _events.Record(now, TerrainEventKind.AddSucceeded, action.IslandId,
                    state.Slot, success: true);
                if (fallback && !state.CorrelatedAckObserved)
                {
                    // Exact correlation was never demonstrated. The entity may be
                    // added through the client's synchronous rescue, but it must not
                    // later be removed/re-added: that path is only safe for patched
                    // clients which proved the v1 lifecycle protocol.
                    state.RemoveSupported = false;
                }
                _ledger.NoteLoaded(peer, action.EntityId);
                CompleteHead(state);
                Console.WriteLine("[terrain-interest] added " + action.IslandId
                    + " terrain " + action.EntityId + " to peer "
                    + peer.DangerousGetHandle() + (fallback ? " after bounded ack fallback." : "."));
            }
        }

        private static bool CheckoutTerrain(ENetPeerHandle peer, long entityId, WorldEntity entity)
        {
            if (!SendOPHelper.SendAddEntityOP(peer, entityId, entity.AssetName, entity.AssetContext))
                return false;
            WorldsAdriftRebornGameServer.SentEntities.MarkSent(peer, entityId);
            if (entity.SeedComponents.Count == 0) return true;
            var seeds = entity.SeedComponents
                .Select(id => new Structs.Structs.InterestOverride(id, 1)).ToList();
            var served = new List<uint>();
            if (!SendOPHelper.SendAddComponentOp(peer, entityId, seeds, true, served))
                return false;
            WorldsAdriftRebornGameServer.ServedComponents.MarkServed(peer, entityId, served);
            return true;
        }

        private static void CompleteHead(PeerState state)
        {
            if (state.Pending.Count > 0) state.Pending.Dequeue();
            state.Asset = null;
            state.DrainWaitingIsland = null;
            state.NextReconcile = TimeSpan.Zero;
        }

        private PeerState StateFor(ENetPeerHandle peer)
        {
            if (_peers.TryGetValue(peer, out PeerState? state)) return state;
            state = new PeerState
            {
                RemoveSupported = EnetLayer.ENet_PeerChannelCount(peer)
                    > (int)EnetLayer.ENetChannel.REMOVE_ENTITY_OP,
                Slot = ++_nextSlot,
            };
            _peers.Add(peer, state);
            _ledger.NotePeer(peer);
            return state;
        }
    }
}

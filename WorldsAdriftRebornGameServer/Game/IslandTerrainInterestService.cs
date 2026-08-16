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
        private readonly Func<ENetPeerHandle, IslandId, bool> _prepareAndCheckResourcesDrained;
        private readonly TimeSpan _settleDelay;
        private readonly IDisposable? _assetAckSubscription;

        internal bool Enabled { get; }
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
            _prepareAndCheckResourcesDrained = prepareAndCheckResourcesDrained ?? ((_, _) => true);
            isLocallyOwned ??= _ => true;
            _settleDelay = settleDelay ?? ResourceInterestPolicy.SettleDelayFrom(
                Environment.GetEnvironmentVariable(ResourceInterestPolicy.SettleDelayEnvVar));

            Enabled = enabled ?? IslandTerrainInterestPolicy.EnabledFrom(
                Environment.GetEnvironmentVariable(IslandTerrainInterestPolicy.EnabledEnvVar));
            LoadRadiusMetres = IslandTerrainInterestPolicy.LoadRadiusFrom(
                Environment.GetEnvironmentVariable(IslandTerrainInterestPolicy.LoadRadiusEnvVar));
            UnloadRadiusMetres = IslandTerrainInterestPolicy.UnloadRadiusFrom(
                Environment.GetEnvironmentVariable(IslandTerrainInterestPolicy.UnloadRadiusEnvVar),
                LoadRadiusMetres);
            AssetAckTimeout = IslandTerrainInterestPolicy.AssetAckTimeoutFrom(
                Environment.GetEnvironmentVariable(IslandTerrainInterestPolicy.AssetAckTimeoutEnvVar));

            if (!Enabled) return;
            _assetAckSubscription = AssetLoadedAckRouter.Subscribe(ack =>
                NoteAssetLoadedAck(ack.PeerId, ack.AssetType, ack.Name, ack.Context));
            foreach (IslandDefinition island in islands.All)
            {
                if (island.Id == IslandCatalog.HavenId) continue;
                IslandTerrainEnvelope? envelope = IslandTerrainEnvelopes.ByIsland(island.Id);
                WorldDirectoryEntry? entry = directory.ByEntityKey(island.WorldEntityKey);
                long? entityId = registry.BoundEntityIdFor(island.WorldEntityKey);
                if (envelope == null || entry == null || entry.IslandId != island.Id
                    || entityId == null || !isLocallyOwned(entityId.Value))
                    continue;
                var candidate = new TerrainStreamCandidate(entityId.Value, island, envelope.Value);
                _candidates.Add(entityId.Value, candidate);
                _entityByIsland.Add(island.Id, entityId.Value);
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
            return _ledger.RequestDestination(peer, islandId, IslandCatalog.HavenId,
                _entityByIsland, Enabled, waiting);
        }

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
                        state.RemoveSupported, state.CorrelatedAckObserved)
                    || !_prepareAndCheckResourcesDrained(peer, action.IslandId)) return;
                if (SendOPHelper.SendRemoveEntityOP(peer, action.EntityId))
                {
                    PeerCheckoutCleanup.RemoveEntity(peer, action.EntityId);
                    _ledger.NoteRemoved(peer, action.EntityId);
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
                    state.Asset.LastRequestAt = now;
                return;
            }

            if (CheckoutTerrain(peer, action.EntityId, entity))
            {
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
            state.NextReconcile = TimeSpan.Zero;
        }

        private PeerState StateFor(ENetPeerHandle peer)
        {
            if (_peers.TryGetValue(peer, out PeerState? state)) return state;
            state = new PeerState
            {
                RemoveSupported = EnetLayer.ENet_PeerChannelCount(peer)
                    > (int)EnetLayer.ENetChannel.REMOVE_ENTITY_OP,
            };
            _peers.Add(peer, state);
            _ledger.NotePeer(peer);
            return state;
        }
    }
}

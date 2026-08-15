using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>Movement-driven, per-peer checkout for static island resources.</summary>
    internal sealed class ResourceInterestService
    {
        private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(120);
        private const int MaxQueuedPerPeer = 512;

        private sealed class PeerState
        {
            public readonly HashSet<long> Loaded = new();
            public readonly Queue<ResourceStreamAction> Pending = new();
            public FixedPointPosition Center = SpawnPolicy.PlayerSpawnPosition;
            public IslandId ActiveIsland = IslandCatalog.HavenId;
            public TimeSpan NextReconcile;
            public TimeSpan NextSend;
            public long AssetRequestedFor;
            public bool RemoveSupported;
            public bool ConnectPlanComplete;
            public TimeSpan ContinuousAfter;
        }

        private readonly IClock _clock;
        private readonly WorldEntityRegistry _registry;
        private readonly IslandRegistry _islands = IslandRegistry.CreateDefault();
        private readonly RegionRegistry? _regions;
        private RegionInterestQuery? _interestQuery;
        private readonly Dictionary<ENetPeerHandle, PeerState> _peers = new();
        private readonly Dictionary<long, WorldEntity> _resources = new();
        private readonly Dictionary<string, long> _resourceIdsByKey = new(StringComparer.Ordinal);
        private readonly Dictionary<long, IslandId> _resourceIslands = new();

        public ResourceInterestService(IClock clock, WorldEntityRegistry registry)
        {
            _clock = clock;
            _registry = registry;
            // Fail-open compatibility: with interest disabled, do not even bind
            // resource ids or construct routing topology early; the old spawn plan
            // retains both its allocation order and its previous failure boundary.
            if (!Interest.Enabled) return;
            _regions = RegionRegistry.CreateDefault(_islands);
            _interestQuery = new RegionInterestQuery(
                WorldDirectory.Build(registry, _islands, _regions));
            foreach (WorldEntity entity in registry.Registrations.Where(e => ResourceInterestPolicy.IsStreamedResourceKey(e.Key)))
            {
                long entityId = registry.EntityIdFor(entity);
                _resources[entityId] = entity;
                _resourceIdsByKey[entity.Key] = entityId;
                _resourceIslands[entityId] = IslandResourceInterestPolicy.ClosestIsland(
                    entity.Position, _islands.All);
            }
        }

        public bool Enabled => Interest.Enabled && _resources.Count > 0;
        public double UnloadRadiusMetres { get; } = ResourceInterestPolicy.UnloadRadiusFrom(
            Environment.GetEnvironmentVariable(ResourceInterestPolicy.UnloadRadiusEnvVar), Interest.RadiusMetres);
        public TimeSpan SettleDelay { get; } = ResourceInterestPolicy.SettleDelayFrom(
            Environment.GetEnvironmentVariable(ResourceInterestPolicy.SettleDelayEnvVar));

        /// <summary>
        /// Adds a resource registered after the boot snapshot (handshake/fallback
        /// deposits and their shards) to continuous interest. Returns true when the
        /// caller must NOT broadcast it: the next current-position reconcile will
        /// queue it only for eligible peers. Disabled interest and non-resource/global
        /// registrations return false, preserving their immediate broadcast path.
        /// </summary>
        public bool RegisterRuntime(long entityId, WorldEntity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (ResourceInterestPolicy.IsStreamedResourceKey(entity.Key))
            {
                LocalDomainOwnership.MoveToIsland(
                    WorldsAdriftRebornGameServer.DomainHost, entityId, entity.Position);
            }
            if (!ResourceInterestPolicy.StreamsRuntimeRegistration(entity.Key, Interest.Enabled))
            {
                return false;
            }

            _resources[entityId] = entity;
            _resourceIdsByKey[entity.Key] = entityId;
            _resourceIslands[entityId] = IslandResourceInterestPolicy.ClosestIsland(
                entity.Position, _islands.All);
            RegionDefinition region = _regions!.ByIsland(_resourceIslands[entityId])
                ?? throw new InvalidOperationException(
                    "runtime resource island '" + _resourceIslands[entityId]
                    + "' has no region owner");
            _interestQuery!.Register(entity, region.Id);
            // Force every known peer to reconcile on the next Tick, without
            // manufacturing a position for peers whose first 1073 has not arrived.
            foreach (PeerState state in _peers.Values) state.NextReconcile = TimeSpan.Zero;
            return true;
        }

        /// <summary>
        /// Replaces the constructor-time topology snapshot with the canonical
        /// post-restore directory. This is called before peers can connect, after
        /// mounted parts and restored registrations have their final ownership.
        /// Runtime registrations made later extend the query through RegisterRuntime.
        /// </summary>
        public void AttachDirectory(WorldDirectory directory)
        {
            if (!Interest.Enabled) return;
            _interestQuery = new RegionInterestQuery(
                directory ?? throw new ArgumentNullException(nameof(directory)));
        }

        public void NoteLoaded(ENetPeerHandle peer, long entityId)
        {
            if (Enabled && _resources.ContainsKey(entityId)) StateFor(peer).Loaded.Add(entityId);
        }

        /// <summary>
        /// Hands resource lifecycle ownership from the immutable connect plan to
        /// continuous movement interest. Until this seam fires, Tick deliberately
        /// sends no dynamic AddEntity operations; NoteLoaded still records every
        /// resource the plan checks out so the first reconcile starts from truth.
        /// </summary>
        public void NoteConnectPlanComplete(ENetPeerHandle peer)
        {
            if (!Enabled) return;
            PeerState state = StateFor(peer);
            if (state.ConnectPlanComplete) return;
            state.ConnectPlanComplete = true;
            state.ContinuousAfter = _clock.Elapsed + SettleDelay;
            state.NextReconcile = state.ContinuousAfter;
            Console.WriteLine("[resource-interest] connect plan complete for "
                + peer.DangerousGetHandle() + "; continuous lifecycle begins after "
                + SettleDelay.TotalMilliseconds.ToString("0") + " ms client-settle window.");
        }

        /// <summary>
        /// Guards component-interest against cross-channel unload races. Once a
        /// RemoveEntity succeeds and Loaded is cleared, a late interest packet cannot
        /// recreate native components for an entity the client no longer owns.
        /// </summary>
        public bool MayServe(ENetPeerHandle peer, long entityId)
        {
            WorldEntity? entity = _registry.ByEntityId(entityId);
            bool streamed = entity != null && ResourceInterestPolicy.IsStreamedResourceKey(entity.Key);
            bool loaded = _peers.TryGetValue(peer, out PeerState? state) && state.Loaded.Contains(entityId);
            return ResourceInterestPolicy.MayServeComponents(Interest.Enabled, streamed, loaded);
        }

        /// <summary>
        /// Observes the ground entity named by the client's 1073 relativeTo. Terrain
        /// ids are resolved through the world registry to a stable IslandId; ship and
        /// invalid ids deliberately leave the last terrain frame unchanged.
        /// </summary>
        public void ObserveRelativeTo(ENetPeerHandle peer, long relativeTo)
        {
            if (!Enabled) return;
            WorldEntity? ground = _registry.ByEntityId(relativeTo);
            IslandDefinition? island = _islands.ByWorldEntityKey(ground?.Key);
            if (island != null)
            {
                SetActiveIsland(peer, island.Id, "1073 relativeTo " + relativeTo);
            }
        }

        public void ObserveIslandLocalPosition(ENetPeerHandle peer, float x, float y, float z)
        {
            if (!Enabled) return;
            PeerState state = StateFor(peer);
            FixedPointPosition origin = _islands.Require(state.ActiveIsland).GlobalOrigin;
            state.Center = FixedPointPosition.FromMetres(
                origin.MetresX + x, origin.MetresY + y, origin.MetresZ + z);
        }

        /// <summary>
        /// Observes a position already expressed in global metres (a flown ship pose
        /// or an authoritative teleport destination). No island-local conversion is
        /// applied; the nearest island becomes the resource frame for reconciliation.
        /// </summary>
        public void ObserveGlobalPosition(ENetPeerHandle peer, FixedPointPosition position, string source)
        {
            if (!Enabled) return;
            PeerState state = StateFor(peer);
            state.Center = position;
            SetActiveIsland(peer,
                IslandResourceInterestPolicy.ClosestIsland(position, _islands.All), source);
        }

        /// <summary>
        /// The latest authoritative world-space centre observed for a peer. Ship
        /// replication shares this position rather than maintaining a second,
        /// eventually-divergent movement ledger. Before the first movement sample,
        /// the connect spawn point is the conservative answer.
        /// </summary>
        public FixedPointPosition CenterFor(ENetPeerHandle peer) =>
            _peers.TryGetValue(peer, out PeerState? state)
                ? state.Center
                : SpawnPolicy.PlayerSpawnPosition;

        public void Tick()
        {
            if (!Enabled) return;
            TimeSpan now = _clock.Elapsed;
            foreach ((ENetPeerHandle peer, PeerState state) in _peers.ToArray())
            {
                // The connect plan owns initial checkout. Running both producers at
                // once lets dynamic interest Add an entity that the plan later Adds
                // again, which corrupts the retail client's entity dictionary.
                if (!state.ConnectPlanComplete || now < state.ContinuousAfter) continue;
                if (now >= state.NextReconcile)
                {
                    state.NextReconcile = now + ReconcileInterval;
                    long requestedBeforeReconcile = state.AssetRequestedFor;
                    RegionDefinition activeRegion = _regions!.ByIsland(state.ActiveIsland)
                        ?? throw new InvalidOperationException(
                            "active island '" + state.ActiveIsland + "' has no region owner");
                    HashSet<string> retainedKeys = state.Loaded
                        .Select(id => _resources.TryGetValue(id, out WorldEntity? loaded)
                            ? loaded.Key : null)
                        .Where(key => key != null)
                        .Select(key => key!)
                        .ToHashSet(StringComparer.Ordinal);
                    IReadOnlyList<WorldEntity> candidates = _interestQuery!.Candidates(
                        activeRegion.Id, _resources.Values, retainedKeys);
                    ResourceInterestPolicy.ReplacePending(
                        state.Pending,
                        ResourceInterestPolicy.Reconcile(
                            state.Center,
                            IslandResourceInterestPolicy.ReconcileSet(
                                state.ActiveIsland,
                                candidates.Select(entity =>
                                {
                                    if (!_resourceIdsByKey.TryGetValue(entity.Key, out long entityId))
                                        throw new InvalidOperationException(
                                            "resource interest candidate '" + entity.Key
                                            + "' has no resource entity id");
                                    return new IslandResource(
                                        entityId, entity.Position, _resourceIslands[entityId]);
                                }),
                                state.Loaded),
                            state.Loaded,
                            Interest.RadiusMetres,
                            state.RemoveSupported ? UnloadRadiusMetres : double.PositiveInfinity),
                        MaxQueuedPerPeer);
                    // Carry an in-flight asset only when it is still the new queue's
                    // head. Otherwise its eventual callback is harmless and no stale
                    // AddEntity follows it.
                    state.AssetRequestedFor = state.Pending.Count > 0
                        && state.Pending.Peek().Kind == ResourceStreamActionKind.Add
                        && state.Pending.Peek().EntityId == requestedBeforeReconcile
                            ? requestedBeforeReconcile : 0;
                }
                if (now < state.NextSend || state.Pending.Count == 0) continue;
                state.NextSend = now + SendInterval;

                ResourceStreamAction action = state.Pending.Peek();
                // The spawn plan can complete this checkout after reconciliation
                // queued the same Add (most often while its asset request is in
                // flight). Revalidate at the last possible boundary: the client
                // cannot tolerate duplicate AddEntity for one id.
                if (!ResourceInterestPolicy.ShouldExecute(
                        state.ConnectPlanComplete, action, state.Loaded))
                {
                    state.Pending.Dequeue();
                    if (state.AssetRequestedFor == action.EntityId)
                    {
                        state.AssetRequestedFor = 0;
                    }
                    Console.WriteLine("[resource-interest] suppressed stale "
                        + action.Kind + " for entity " + action.EntityId
                        + "; current checkout state already satisfies it.");
                    continue;
                }
                if (action.Kind == ResourceStreamActionKind.Remove)
                {
                    state.Pending.Dequeue();
                    if (state.Loaded.Contains(action.EntityId)
                        && SendOPHelper.SendRemoveEntityOP(peer, action.EntityId))
                    {
                        state.Loaded.Remove(action.EntityId);
                        PeerCheckoutCleanup.RemoveEntity(peer, action.EntityId);
                        Console.WriteLine("[resource-interest] removed '"
                            + _resources[action.EntityId].Key + "' (" + action.EntityId
                            + ") from " + peer.DangerousGetHandle() + ".");
                    }
                    else if (state.Loaded.Contains(action.EntityId))
                    {
                        // Older clients negotiated only channels 0..4, so channel 5
                        // RemoveEntity is unavailable. Retain the checkout and its
                        // component references; otherwise a later re-add would create
                        // a visible but inert resource ghost for that peer.
                        state.RemoveSupported = false;
                        Console.WriteLine("[resource-interest] peer "
                            + peer.DangerousGetHandle() + " cannot receive RemoveEntity;"
                            + " retaining visited resources for compatibility.");
                    }
                    continue;
                }

                WorldEntity entity = _resources[action.EntityId];
                if (state.AssetRequestedFor != action.EntityId)
                {
                    SendOPHelper.SendAssetLoadRequestOP(peer, "notNeeded?", entity.AssetName, entity.AssetContext);
                    state.AssetRequestedFor = action.EntityId;
                    continue; // a full cadence for the asset callback before AddEntity
                }

                state.Pending.Dequeue();
                state.AssetRequestedFor = 0;
                if (SendOPHelper.SendAddEntityOP(peer, action.EntityId, entity.AssetName, entity.AssetContext))
                {
                    state.Loaded.Add(action.EntityId);
                    Console.WriteLine("[resource-interest] added '" + entity.Key + "' ("
                        + action.EntityId + ") to " + peer.DangerousGetHandle() + ".");
                }
            }
        }

        public void Forget(ENetPeerHandle peer) => _peers.Remove(peer);

        private void SetActiveIsland(ENetPeerHandle peer, IslandId islandId, string source)
        {
            PeerState state = StateFor(peer);
            if (state.ActiveIsland == islandId) return;
            IslandId previous = state.ActiveIsland;
            state.ActiveIsland = islandId;
            state.NextReconcile = TimeSpan.Zero;
            Console.WriteLine("[resource-interest] peer " + peer.DangerousGetHandle()
                + " changed island frame " + previous + " -> " + islandId
                + " (" + source + ").");
        }

        private PeerState StateFor(ENetPeerHandle peer)
        {
            if (_peers.TryGetValue(peer, out PeerState? state)) return state;

            int channelCount = EnetLayer.ENet_PeerChannelCount(peer);
            state = new PeerState
            {
                // RemoveEntity is channel 5. ENet's negotiated channel count is the
                // protocol capability signal: never send it merely because the server
                // binary happens to support serialization.
                RemoveSupported = channelCount > (int)EnetLayer.ENetChannel.REMOVE_ENTITY_OP,
            };
            _peers[peer] = state;
            Console.WriteLine("[resource-interest] peer " + peer.DangerousGetHandle()
                + " negotiated " + channelCount + " ENet channels; resource unload "
                + (state.RemoveSupported ? "enabled" : "disabled (retain-visited compatibility mode)") + ".");
            return state;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
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
        }

        private readonly IClock _clock;
        private readonly WorldEntityRegistry _registry;
        private readonly IslandRegistry _islands = IslandRegistry.CreateDefault();
        private readonly Dictionary<ENetPeerHandle, PeerState> _peers = new();
        private readonly Dictionary<long, WorldEntity> _resources = new();
        private readonly Dictionary<long, IslandId> _resourceIslands = new();

        public ResourceInterestService(IClock clock, WorldEntityRegistry registry)
        {
            _clock = clock;
            _registry = registry;
            // Fail-open compatibility: with interest disabled, do not even bind
            // resource ids early; the old spawn plan retains its allocation order.
            if (!Interest.Enabled) return;
            foreach (WorldEntity entity in registry.Registrations.Where(e => ResourceInterestPolicy.IsStreamedResourceKey(e.Key)))
            {
                long entityId = registry.EntityIdFor(entity);
                _resources[entityId] = entity;
                _resourceIslands[entityId] = IslandResourceInterestPolicy.ClosestIsland(
                    entity.Position, _islands.All);
            }
        }

        public bool Enabled => Interest.Enabled && _resources.Count > 0;
        public double UnloadRadiusMetres { get; } = ResourceInterestPolicy.UnloadRadiusFrom(
            Environment.GetEnvironmentVariable(ResourceInterestPolicy.UnloadRadiusEnvVar), Interest.RadiusMetres);

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
            if (!ResourceInterestPolicy.StreamsRuntimeRegistration(entity.Key, Interest.Enabled))
            {
                return false;
            }

            _resources[entityId] = entity;
            _resourceIslands[entityId] = IslandResourceInterestPolicy.ClosestIsland(
                entity.Position, _islands.All);
            // Force every known peer to reconcile on the next Tick, without
            // manufacturing a position for peers whose first 1073 has not arrived.
            foreach (PeerState state in _peers.Values) state.NextReconcile = TimeSpan.Zero;
            return true;
        }

        public void NoteLoaded(ENetPeerHandle peer, long entityId)
        {
            if (Enabled && _resources.ContainsKey(entityId)) StateFor(peer).Loaded.Add(entityId);
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

        public void Tick()
        {
            if (!Enabled) return;
            TimeSpan now = _clock.Elapsed;
            foreach ((ENetPeerHandle peer, PeerState state) in _peers.ToArray())
            {
                if (now >= state.NextReconcile)
                {
                    state.NextReconcile = now + ReconcileInterval;
                    long requestedBeforeReconcile = state.AssetRequestedFor;
                    ResourceInterestPolicy.ReplacePending(
                        state.Pending,
                        ResourceInterestPolicy.Reconcile(
                            state.Center,
                            IslandResourceInterestPolicy.ReconcileSet(
                                state.ActiveIsland,
                                _resources.Select(x => new IslandResource(
                                    x.Key, x.Value.Position, _resourceIslands[x.Key])),
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

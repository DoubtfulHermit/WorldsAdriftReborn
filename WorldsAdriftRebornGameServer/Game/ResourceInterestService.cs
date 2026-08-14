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
            public TimeSpan NextReconcile;
            public TimeSpan NextSend;
            public long AssetRequestedFor;
        }

        private readonly IClock _clock;
        private readonly WorldEntityRegistry _registry;
        private readonly Dictionary<ENetPeerHandle, PeerState> _peers = new();
        private readonly Dictionary<long, WorldEntity> _resources = new();

        public ResourceInterestService(IClock clock, WorldEntityRegistry registry)
        {
            _clock = clock;
            _registry = registry;
            // Fail-open compatibility: with interest disabled, do not even bind
            // resource ids early; the old spawn plan retains its allocation order.
            if (!Interest.Enabled) return;
            foreach (WorldEntity entity in registry.Registrations.Where(e => ResourceInterestPolicy.IsStreamedResourceKey(e.Key)))
            {
                _resources[registry.EntityIdFor(entity)] = entity;
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

        public void ObserveIslandLocalPosition(ENetPeerHandle peer, float x, float y, float z)
        {
            if (!Enabled) return;
            FixedPointPosition island = IslandCatalog.Haven.GlobalOrigin;
            StateFor(peer).Center = FixedPointPosition.FromMetres(
                island.MetresX + x, island.MetresY + y, island.MetresZ + z);
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
                            state.Center, _resources.Select(x => (x.Key, x.Value.Position)), state.Loaded,
                            Interest.RadiusMetres, UnloadRadiusMetres),
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
                }
            }
        }

        public void Forget(ENetPeerHandle peer) => _peers.Remove(peer);
        private PeerState StateFor(ENetPeerHandle peer) =>
            _peers.TryGetValue(peer, out PeerState? state) ? state : (_peers[peer] = new PeerState());
    }
}

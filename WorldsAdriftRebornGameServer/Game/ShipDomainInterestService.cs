using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// Per-peer whole-ship checkout. A domain leaves member-first/root-last and
    /// returns root-first/member-last, with one wire operation per cadence.
    /// </summary>
    internal sealed class ShipDomainInterestService
    {
        private enum Kind { Add, Remove }
        private readonly record struct Action(long HullEntityId, long EntityId, Kind Kind,
            bool WasMountedMember);

        private sealed class PeerState
        {
            public readonly Queue<Action> Pending = new();
            public readonly HashSet<long> RemovedMountedParts = new();
            public TimeSpan NextReconcile;
            public TimeSpan NextSend;
            public TimeSpan ContinuousAfter;
            public long AssetRequestedFor;
            public bool ConnectPlanComplete;
            public bool RemoveSupported;
        }

        private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(120);
        private readonly IClock _clock;
        private readonly ShipDomainRegistry _domains;
        private readonly WorldEntityRegistry _registry;
        private readonly Dictionary<ENetPeerHandle, PeerState> _peers = new();

        private double LoadRadiusMetres { get; } = ShipDomainInterestPolicy.LoadRadiusFrom(
            Environment.GetEnvironmentVariable(ShipDomainInterestPolicy.LoadRadiusEnvVar));
        private double UnloadRadiusMetres { get; }

        public ShipDomainInterestService(IClock clock, ShipDomainRegistry domains,
            WorldEntityRegistry registry)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _domains = domains ?? throw new ArgumentNullException(nameof(domains));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            UnloadRadiusMetres = ShipDomainInterestPolicy.UnloadRadiusFrom(
                Environment.GetEnvironmentVariable(ShipDomainInterestPolicy.UnloadRadiusEnvVar),
                LoadRadiusMetres);
        }

        public void NoteConnectPlanComplete(ENetPeerHandle peer)
        {
            PeerState state = StateFor(peer);
            if (state.ConnectPlanComplete) return;
            state.ConnectPlanComplete = true;
            state.ContinuousAfter = _clock.Elapsed
                + WorldsAdriftRebornGameServer.ResourceInterest.SettleDelay;
            state.NextReconcile = state.ContinuousAfter;
        }

        public void Tick()
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

        public void Forget(ENetPeerHandle peer) => _peers.Remove(peer);

        /// <summary>Compatibility peers cannot unload, so motion must not freeze into a ghost.</summary>
        public bool MustRetainMotion(ENetPeerHandle peer) =>
            _peers.TryGetValue(peer, out PeerState? state) && !state.RemoveSupported;

        public bool MayServe(ENetPeerHandle peer, long entityId)
        {
            bool managed = _domains.ByHull(entityId) != null
                || _domains.All.Any(domain =>
                    BuiltShips.DecksForHull(domain.HullEntityId).Contains(entityId)
                    || MountedParts.MountFor(entityId)?.HullEntityId == domain.HullEntityId)
                || (_peers.TryGetValue(peer, out PeerState? state)
                    && state.RemovedMountedParts.Contains(entityId));
            return ShipDomainInterestPolicy.MayServeComponents(managed,
                WorldsAdriftRebornGameServer.SentEntities.WasSent(peer, entityId));
        }

        private void Reconcile(ENetPeerHandle peer, PeerState state)
        {
            state.Pending.Clear();
            state.AssetRequestedFor = 0;
            ulong peerId = PeerIdentity.IdOf(peer);
            long playerEntityId = WorldsAdriftRebornGameServer.Players.EntityOf(peerId) ?? 0;
            FixedPointPosition center = WorldsAdriftRebornGameServer.ResourceInterest.CenterFor(peer);

            // A member detached while its former ship was checked out must return
            // to this peer as a normal loose world entity. Without this ownership
            // transfer it would remain absent forever after leaving the domain.
            foreach (long entityId in state.RemovedMountedParts.ToArray())
            {
                if (_registry.ByEntityId(entityId) == null)
                {
                    state.RemovedMountedParts.Remove(entityId);
                }
                else if (!MountedParts.Is(entityId)
                    && !WorldsAdriftRebornGameServer.SentEntities.WasSent(peer, entityId))
                {
                    state.Pending.Enqueue(new Action(0, entityId, Kind.Add, WasMountedMember: true));
                }
            }

            foreach (ShipDomain domain in _domains.All.OrderBy(x => x.HullEntityId))
            {
                long hull = domain.HullEntityId;
                bool protectedByInteraction = WorldsAdriftRebornGameServer.Flight.IsPilotOf(playerEntityId, hull)
                    || WorldsAdriftRebornGameServer.Aboard.ShipOf(peerId) == hull;
                bool hasAnyCrew = WorldsAdriftRebornGameServer.Aboard.AnyoneAboard(hull)
                    || WorldsAdriftRebornGameServer.Flight.IsPiloted(hull);
                FixedPointPosition hullPosition;
                if (!WorldsAdriftRebornGameServer.Flight.TryGetFlownPose(hull, out hullPosition, out _))
                    hullPosition = _registry.TransformSeedFor(hull);

                bool rootLoaded = WorldsAdriftRebornGameServer.SentEntities.WasSent(peer, hull);
                bool shouldLoad = !state.RemoveSupported
                    || ShipDomainInterestPolicy.ShouldBeLoaded(rootLoaded,
                        protectedByInteraction, hasAnyCrew, center, hullPosition,
                        LoadRadiusMetres, UnloadRadiusMetres);
                // Read the live ledgers, not the domain's last flight-tick snapshot:
                // restored and newly mounted parts can change while a ship has never
                // been piloted, and therefore before Flight.RefreshDomainMembership.
                long[] mounted = MountedParts.OnHull(hull).Select(x => x.Key).ToArray();
                IReadOnlyList<long> members = ShipDomainInterestPolicy.Members(
                    BuiltShips.DecksForHull(hull),
                    mounted);
                domain.ReplaceMembers(BuiltShips.DecksForHull(hull),
                    mounted);
                IReadOnlyList<long> order = shouldLoad
                    ? ShipDomainInterestPolicy.AddOrder(hull, members)
                    : ShipDomainInterestPolicy.RemoveOrder(hull, members);
                foreach (long entityId in order)
                {
                    bool sent = WorldsAdriftRebornGameServer.SentEntities.WasSent(peer, entityId);
                    if (shouldLoad ? !sent : sent)
                        state.Pending.Enqueue(new Action(hull, entityId,
                            shouldLoad ? Kind.Add : Kind.Remove,
                            WasMountedMember: mounted.Contains(entityId)));
                }
            }
        }

        private void Execute(ENetPeerHandle peer, PeerState state)
        {
            Action action = state.Pending.Peek();
            ulong peerId = PeerIdentity.IdOf(peer);
            long playerEntityId = WorldsAdriftRebornGameServer.Players.EntityOf(peerId) ?? 0;
            bool protectedNow = WorldsAdriftRebornGameServer.Flight.IsPilotOf(playerEntityId, action.HullEntityId)
                || WorldsAdriftRebornGameServer.Aboard.ShipOf(peerId) == action.HullEntityId
                || (action.HullEntityId > 0
                    && (WorldsAdriftRebornGameServer.Aboard.AnyoneAboard(action.HullEntityId)
                        || WorldsAdriftRebornGameServer.Flight.IsPiloted(action.HullEntityId)));
            if (action.Kind == Kind.Remove && protectedNow)
            {
                state.Pending.Clear();
                state.AssetRequestedFor = 0;
                state.NextReconcile = TimeSpan.Zero;
                return;
            }

            if (action.Kind == Kind.Add && action.HullEntityId == 0
                && MountedParts.Is(action.EntityId))
            {
                state.Pending.Dequeue();
                state.AssetRequestedFor = 0;
                state.NextReconcile = TimeSpan.Zero;
                return;
            }
            if (action.Kind == Kind.Add && action.WasMountedMember
                && action.HullEntityId > 0
                && MountedParts.MountFor(action.EntityId)?.HullEntityId != action.HullEntityId)
            {
                // Asset loading spans a cadence. The part may have detached or
                // remounted meanwhile; never add it under a stale domain order.
                state.Pending.Dequeue();
                state.AssetRequestedFor = 0;
                state.NextReconcile = TimeSpan.Zero;
                return;
            }

            if (action.Kind == Kind.Remove)
            {
                state.Pending.Dequeue();
                if (!state.RemoveSupported) return;
                if (WorldsAdriftRebornGameServer.SentEntities.WasSent(peer, action.EntityId)
                    && SendOPHelper.SendRemoveEntityOP(peer, action.EntityId))
                {
                    if (action.WasMountedMember)
                        state.RemovedMountedParts.Add(action.EntityId);
                    PeerCheckoutCleanup.RemoveEntity(peer, action.EntityId);
                    Console.WriteLine("[ship-interest] removed entity " + action.EntityId
                        + " of hull " + action.HullEntityId + " from peer "
                        + peer.DangerousGetHandle() + ".");
                }
                else if (WorldsAdriftRebornGameServer.SentEntities.WasSent(peer, action.EntityId))
                {
                    state.RemoveSupported = false;
                    state.Pending.Clear();
                    Console.WriteLine("[ship-interest] peer cannot receive RemoveEntity; retaining ships.");
                }
                return;
            }

            WorldEntity? entity = _registry.ByEntityId(action.EntityId);
            if (entity == null)
            {
                state.Pending.Dequeue();
                state.AssetRequestedFor = 0;
                return;
            }
            if (state.AssetRequestedFor != action.EntityId)
            {
                SendOPHelper.SendAssetLoadRequestOP(peer, "notNeeded?", entity.AssetName, entity.AssetContext);
                state.AssetRequestedFor = action.EntityId;
                return;
            }

            state.Pending.Dequeue();
            state.AssetRequestedFor = 0;
            if (BuiltShipSpawner.CheckoutToPeer(peer, action.EntityId, entity, requestAsset: false))
            {
                state.RemovedMountedParts.Remove(action.EntityId);
                Console.WriteLine("[ship-interest] added entity " + action.EntityId
                    + (action.HullEntityId > 0 ? " of hull " + action.HullEntityId : " as loose world entity")
                    + " to peer " + peer.DangerousGetHandle() + ".");
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
            _peers.Add(peer, state);
            return state;
        }
    }
}

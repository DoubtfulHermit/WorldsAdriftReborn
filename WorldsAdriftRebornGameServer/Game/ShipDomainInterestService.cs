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
            public readonly HashSet<long> RecallRefreshHulls = new();
            public TimeSpan NextReconcile;
            public TimeSpan NextSend;
            public TimeSpan ContinuousAfter;
            public long AssetRequestedFor;
            public bool ConnectPlanComplete;
            public bool RemoveSupported;
            public FixedPointPosition? ForcedRestoreCenter;
            public long ForcedRestoreHull;
            public readonly HashSet<long> MaterializedEntities = new();
        }

        internal enum RestoreCheckoutStatus { Unknown, Waiting, Ready }

        /// <summary>
        /// Preloads the ship nearest an open-sky logout position without lying
        /// about the player's current resource-interest centre. Ready means the
        /// client has requested components for the root and every generated deck,
        /// which is its native proof that those entities materialized.
        /// </summary>
        internal RestoreCheckoutStatus RequestRestoreDestination(ENetPeerHandle peer,
            FixedPointPosition destination, out long hullEntityId)
        {
            hullEntityId = 0;
            double nearestSquared = double.PositiveInfinity;
            foreach (ShipDomain domain in _domains.All)
            {
                FixedPointPosition pose = _registry.TransformSeedFor(domain.HullEntityId);
                uint rotation = _registry.RotationSeedFor(domain.HullEntityId);
                if (WorldsAdriftRebornGameServer.Flight.TryGetFlownPose(
                        domain.HullEntityId, out FixedPointPosition flown, out uint flownRotation))
                {
                    pose = flown;
                    rotation = flownRotation;
                }
                byte[]? bytes = BuiltShips.HullBytesFor(domain.HullEntityId);
                if (bytes == null || !ShipPlanModel.TryDecode(bytes,
                        out ShipPlanModel? plan, out _) || plan == null) continue;
                ShipHullMetrics metrics = ShipHullMetrics.Measure(plan);
                double yaw = ShipyardDockingPolicy.YawFromPacked(rotation);
                if (!ShipRestoreReadinessPolicy.IsWithinHullEnvelope(metrics,
                        pose.MetresX, pose.MetresY, pose.MetresZ, yaw,
                        destination.MetresX, destination.MetresY, destination.MetresZ))
                    continue;
                double dx = destination.MetresX - pose.MetresX;
                double dy = destination.MetresY - pose.MetresY;
                double dz = destination.MetresZ - pose.MetresZ;
                double squared = dx * dx + dy * dy + dz * dz;
                if (squared <= nearestSquared)
                {
                    nearestSquared = squared;
                    hullEntityId = domain.HullEntityId;
                }
            }
            if (hullEntityId == 0) return RestoreCheckoutStatus.Unknown;

            PeerState state = StateFor(peer);
            state.ForcedRestoreCenter = destination;
            state.ForcedRestoreHull = hullEntityId;
            state.NextReconcile = TimeSpan.Zero;

            IReadOnlyList<long> restoreDecks = BuiltShips.DecksForHull(hullEntityId);
            bool checkoutPresent = WorldsAdriftRebornGameServer.SentEntities
                .WasSent(peer, hullEntityId)
                && restoreDecks.All(deck => WorldsAdriftRebornGameServer.SentEntities
                    .WasSent(peer, deck));
            return checkoutPresent && ShipRestoreReadinessPolicy.IsReady(hullEntityId,
                restoreDecks, state.MaterializedEntities)
                    ? RestoreCheckoutStatus.Ready
                    : RestoreCheckoutStatus.Waiting;
        }

        internal void NoteComponentInterest(ENetPeerHandle peer, long entityId)
        {
            PeerState state = StateFor(peer);
            state.MaterializedEntities.Add(entityId);
        }

        internal void CompleteRestoreDestination(ENetPeerHandle peer, long hullEntityId)
        {
            if (!_peers.TryGetValue(peer, out PeerState? state)
                || state.ForcedRestoreHull != hullEntityId) return;
            state.ForcedRestoreCenter = null;
            state.ForcedRestoreHull = 0;
            state.NextReconcile = TimeSpan.Zero;
        }

        private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(120);
        private readonly IClock _clock;
        private readonly ShipDomainRegistry _domains;
        private readonly WorldEntityRegistry _registry;
        private readonly Dictionary<ENetPeerHandle, PeerState> _peers = new();

        internal double LoadRadiusMetres { get; } = ShipDomainInterestPolicy.LoadRadiusFrom(
            Environment.GetEnvironmentVariable(ShipDomainInterestPolicy.LoadRadiusEnvVar));
        internal double UnloadRadiusMetres { get; }

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

        /// <summary>
        /// Creates the peer slot at socket acceptance, before its connect plan
        /// begins serving entities. Recall can then retain a rebuild request for
        /// a peer that has received the old hull but has not reached the plan's
        /// final sentinel yet.
        /// </summary>
        public void NotePeerConnected(ENetPeerHandle peer) => StateFor(peer);

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

        /// <summary>
        /// Forces every capable peer currently holding this hull through a full
        /// member-first/root-last removal followed by root-first reconstruction.
        /// A recall is a discontinuous teleport: feeding it into an existing
        /// PathFollower can spline against minutes of stale trajectory and put
        /// the rendered hull kilometres off-map.
        /// </summary>
        public int RequestRecallRefresh(long hullEntityId)
        {
            int peers = 0;
            foreach ((ENetPeerHandle peer, PeerState state) in _peers)
            {
                bool rootCheckedOut = WorldsAdriftRebornGameServer.SentEntities
                    .WasSent(peer, hullEntityId);
                if (!ShipDomainInterestPolicy.ShouldQueueRecallRefresh(
                        state.RemoveSupported, rootCheckedOut)) continue;
                state.RecallRefreshHulls.Add(hullEntityId);
                state.NextReconcile = TimeSpan.Zero;
                peers++;
            }
            return peers;
        }

        /// <summary>Compatibility peers cannot unload, so motion must not freeze into a ghost.</summary>
        public bool MustRetainMotion(ENetPeerHandle peer) =>
            _peers.TryGetValue(peer, out PeerState? state) && !state.RemoveSupported;

        /// <summary>Peers whose checkout ledger currently contains this hull.</summary>
        public int SubscriberCountFor(long hullEntityId)
        {
            int count = 0;
            foreach ((ulong peerId, _) in WorldsAdriftRebornGameServer.Players.All())
            {
                ENetPeerHandle? peer = PeerIdentity.Instance.Resolve(new IntPtr((long)peerId));
                if (peer != null
                    && WorldsAdriftRebornGameServer.SentEntities.WasSent(peer, hullEntityId))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Which ship domains this peer's checkout ledger currently contains,
        /// for the interest section of the stats snapshot. The membership test
        /// is the SAME one <see cref="SubscriberCountFor"/> counts with - the
        /// hull's presence in the peer's sent-entity ledger - so the per-peer
        /// list and the per-hull subscriber count cannot disagree.
        /// </summary>
        public IReadOnlyList<string> CheckedOutDomainIdsFor(ENetPeerHandle peer)
        {
            List<string> ids = new();
            foreach (ShipDomain domain in _domains.All.OrderBy(x => x.HullEntityId))
            {
                if (WorldsAdriftRebornGameServer.SentEntities.WasSent(peer, domain.HullEntityId))
                    ids.Add(domain.Id.ToString());
            }
            return ids;
        }

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
            long requestedBeforeReconcile = state.AssetRequestedFor;
            state.Pending.Clear();
            ulong peerId = PeerIdentity.IdOf(peer);
            long playerEntityId = WorldsAdriftRebornGameServer.Players.EntityOf(peerId) ?? 0;
            FixedPointPosition center = state.ForcedRestoreCenter
                ?? WorldsAdriftRebornGameServer.ResourceInterest.CenterFor(peer);

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
                bool recallRefreshUnload = ShipDomainInterestPolicy.RecallRefreshForcesUnload(
                    state.RecallRefreshHulls.Contains(hull), rootLoaded);
                if (state.RecallRefreshHulls.Contains(hull) && !rootLoaded)
                {
                    // Root is removed last, so its absence proves the whole old
                    // domain checkout is gone. The same reconcile may now queue
                    // the clean root-first reconstruction at the recalled pose.
                    state.RecallRefreshHulls.Remove(hull);
                }
                bool shouldLoad = !recallRefreshUnload && (!state.RemoveSupported
                    || ShipDomainInterestPolicy.ShouldBeLoaded(rootLoaded,
                        protectedByInteraction, hasAnyCrew, center, hullPosition,
                        LoadRadiusMetres, UnloadRadiusMetres));
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

            long? nextAdd = state.Pending.Count > 0 && state.Pending.Peek().Kind == Kind.Add
                ? state.Pending.Peek().EntityId
                : null;
            state.AssetRequestedFor = ShipDomainInterestPolicy.AssetRequestAfterReconcile(
                requestedBeforeReconcile, nextAdd);
        }

        private void Execute(ENetPeerHandle peer, PeerState state)
        {
            Action action = state.Pending.Peek();

            // Reconciliation and runtime broadcasts share the per-peer ledger. A
            // broadcast can satisfy an Add (or salvage can satisfy a Remove) after
            // this action was queued. Revalidate immediately before the wire send:
            // duplicate AddEntity corrupts the retail client's entity dictionary.
            bool checkedOut = WorldsAdriftRebornGameServer.SentEntities
                .WasSent(peer, action.EntityId);
            if (!ShipDomainInterestPolicy.ShouldExecute(
                    action.Kind == Kind.Add, checkedOut))
            {
                state.Pending.Dequeue();
                if (state.AssetRequestedFor == action.EntityId)
                    state.AssetRequestedFor = 0;
                state.NextReconcile = TimeSpan.Zero;
                Console.WriteLine("[ship-interest] suppressed stale " + action.Kind
                    + " for entity " + action.EntityId + " of hull "
                    + action.HullEntityId + "; peer checkout already satisfies it.");
                return;
            }

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
                    state.MaterializedEntities.Remove(action.EntityId);
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
                if (SendOPHelper.SendAssetLoadRequestOP(peer, "notNeeded?",
                        entity.AssetName, entity.AssetContext))
                {
                    state.AssetRequestedFor = action.EntityId;
                }
                else
                {
                    Console.WriteLine("[ship-interest] failed to queue asset request for entity "
                        + action.EntityId + " of hull " + action.HullEntityId
                        + "; retaining Add for retry.");
                }
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
            else
            {
                // The ledger remains authoritative. If AddEntity was not queued it
                // stays absent and the next reconcile retries; if AddEntity queued
                // but component seeding failed, the existing component-interest path
                // can repair missing seeds without duplicating AddEntity.
                state.NextReconcile = TimeSpan.Zero;
                Console.WriteLine("[ship-interest] checkout failed for entity "
                    + action.EntityId + " of hull " + action.HullEntityId
                    + "; scheduling reconciliation.");
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

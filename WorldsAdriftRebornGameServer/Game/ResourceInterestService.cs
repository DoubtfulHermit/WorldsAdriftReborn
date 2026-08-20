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
    /// <summary>
    /// Movement-driven, per-peer checkout for static island resources.
    ///
    /// INTEREST IS KEYED ON THE ISLAND, NOT ON THE NODE. A peer holds an island's
    /// WHOLE resource set while it is within
    /// <see cref="IslandResourceCheckoutPolicy.DefaultLoadRadiusMetres"/> of that
    /// island's ENVELOPE. The reasoning, the measurement of the bug it fixes and the
    /// rejected alternatives are all in <see cref="IslandResourceCheckoutPolicy"/>;
    /// the short version is that a release island is up to 735 m across while the old
    /// player-centred bubble was 240 m across, so a player standing on Mount Spero
    /// held 2 of its 19 nodes and emptied the island by walking.
    ///
    /// A node's own distance to the player still decides the ORDER work is done in -
    /// nearest first, so what is at the player's feet arrives first - and no longer
    /// decides WHETHER it is done. That split is the fix.
    ///
    /// THE TERRAIN GATE IS UNTOUCHED AND STILL A CORRECTNESS REQUIREMENT. A resource
    /// is never added for an island whose terrain the peer has not checked out
    /// (<see cref="AttachTerrainReadiness"/>), and the drain in
    /// <see cref="DrainIslandBeforeTerrainRemoval"/> still runs before terrain is
    /// removed. That gate answers "has the client loaded this ground"; the radius
    /// above answers "should this peer have this island's contents at all". They are
    /// different questions and the terrain radius (4000 m in production) is much the
    /// wider of the two, so terrain is always long since present by the time an
    /// island's resources are admitted.
    /// </summary>
    internal sealed class ResourceInterestService
    {
        private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(120);
        private const int MaxQueuedPerPeer = 512;

        private sealed class PeerState
        {
            public readonly HashSet<long> Loaded = new();
            public readonly Queue<ResourceStreamAction> Pending = new();

            /// <summary>
            /// Which islands' resource sets this peer currently holds. This is the
            /// checkout unit now; <see cref="Loaded"/> is its consequence.
            /// </summary>
            public readonly HashSet<IslandId> Islands = new();

            public FixedPointPosition Center = SpawnPolicy.PlayerSpawnPosition;
            public IslandId ActiveIsland = IslandCatalog.HavenId;
            public TimeSpan NextReconcile;
            public TimeSpan NextSend;
            public long AssetRequestedFor;
            public bool RemoveSupported;
            public bool ConnectPlanComplete;
            public TimeSpan ContinuousAfter;
        }

        /// <summary>
        /// One island that carries resources, with everything admission needs: the
        /// definition its envelope is expressed relative to, the envelope itself, and
        /// how many entities admitting it costs.
        /// </summary>
        private sealed class IslandResourceGroup
        {
            public IslandResourceGroup(IslandDefinition island, IslandTerrainEnvelope envelope)
            {
                Island = island;
                Envelope = envelope;
            }

            public IslandDefinition Island { get; }
            public IslandTerrainEnvelope Envelope { get; }
            public int Count { get; set; }
        }

        private readonly IClock _clock;
        private readonly WorldEntityRegistry _registry;
        private readonly IslandRegistry _islands;
        private readonly RegionRegistry? _regions;
        private RegionInterestQuery? _interestQuery;
        private readonly Dictionary<ENetPeerHandle, PeerState> _peers = new();
        private readonly Dictionary<long, WorldEntity> _resources = new();
        private readonly Dictionary<string, long> _resourceIdsByKey = new(StringComparer.Ordinal);
        private readonly Dictionary<long, IslandId> _resourceIslands = new();
        private readonly Dictionary<IslandId, IslandResourceGroup> _byIsland = new();
        private Func<ENetPeerHandle, IslandId, bool>? _terrainReady;

        public ResourceInterestService(IClock clock, WorldEntityRegistry registry,
            IslandRegistry islands, RegionRegistry regions)
        {
            _clock = clock;
            _registry = registry;
            _islands = islands ?? throw new ArgumentNullException(nameof(islands));
            // Fail-open compatibility: with interest disabled, do not even bind
            // resource ids or construct routing topology early; the old spawn plan
            // retains both its allocation order and its previous failure boundary.
            if (!Interest.Enabled) return;
            _regions = regions ?? throw new ArgumentNullException(nameof(regions));
            _interestQuery = new RegionInterestQuery(
                WorldDirectory.Build(registry, _islands, _regions));
            foreach (WorldEntity entity in registry.Registrations.Where(e => ResourceInterestPolicy.IsStreamedResourceKey(e.Key)))
            {
                long entityId = registry.EntityIdFor(entity);
                _resources[entityId] = entity;
                _resourceIdsByKey[entity.Key] = entityId;
                IslandId owner = IslandResourceInterestPolicy.ClosestIsland(
                    entity.Position, _islands.All);
                _resourceIslands[entityId] = owner;
                GroupFor(owner).Count++;
            }
            ReportIslandCheckout();
        }

        public bool Enabled => Interest.Enabled && _resources.Count > 0;

        /// <summary>
        /// WHICH ISLAND OWNS A STREAMED RESOURCE. Read-only, and the map itself
        /// rather than a copy - this is asked once per entity per understorm, and a
        /// forty-seven-island world has thousands of entities.
        ///
        /// This service already had to answer this question for checkout (a peer
        /// holds an island's WHOLE resource set), and this accessor exists so the
        /// understorm can answer it too instead of growing a second, divergent
        /// classification. That matters: if the two ever disagreed, a storm would
        /// reset resources a player standing on the island does not hold, and hold
        /// resources no storm ever reaches.
        ///
        /// ⚠ IT IS EMPTY WHEN SPATIAL INTEREST IS OFF, because the constructor
        /// returns before populating it (fail-open compatibility). A caller that
        /// treats "not in this map" as "on no island" therefore silently resets
        /// NOTHING on a server with <c>WAREBORN_INTEREST_RADIUS_M</c> unset. See
        /// <c>WorldsAdriftRebornGameServer.IslandOwningResource</c>, which falls back
        /// to the same <see cref="IslandResourceInterestPolicy.ClosestIsland"/> this
        /// map was built from. (Production reads 120 on 2026-08-20, so the map IS
        /// populated there - the fallback is for the off configuration, not for it.)
        /// </summary>
        public IReadOnlyDictionary<long, IslandId> ResourceIslands => _resourceIslands;

        /// <summary>
        /// The island owning one resource, or null if this service has never
        /// classified it. See <see cref="ResourceIslands"/> for when that is "no such
        /// resource" and when it is "interest is switched off".
        /// </summary>
        public IslandId? IslandOf(long entityId) =>
            _resourceIslands.TryGetValue(entityId, out IslandId island) ? island : (IslandId?)null;

        /// <summary>
        /// Retained for the player-centred paths that still ask for it (the connect
        /// plan's telemetry and the operator console). Continuous checkout no longer
        /// consults it: see <see cref="IslandResourceCheckoutPolicy"/>.
        /// </summary>
        public double UnloadRadiusMetres { get; } = ResourceInterestPolicy.UnloadRadiusFrom(
            Environment.GetEnvironmentVariable(ResourceInterestPolicy.UnloadRadiusEnvVar), Interest.RadiusMetres);

        /// <summary>How near an island a peer must be for that island's resources to check out.</summary>
        public double IslandLoadRadiusMetres { get; } = IslandResourceCheckoutPolicy.LoadRadiusFrom(
            Environment.GetEnvironmentVariable(IslandResourceCheckoutPolicy.LoadRadiusEnvVar));

        /// <summary>How far past the load radius a held island is retained.</summary>
        public double IslandUnloadRadiusMetres => IslandResourceCheckoutPolicy.UnloadRadiusFor(
            IslandLoadRadiusMetres);

        /// <summary>The ceiling on resource entities one peer may hold at once.</summary>
        public int PerPeerResourceBudget { get; } = IslandResourceCheckoutPolicy.PerPeerBudgetFrom(
            Environment.GetEnvironmentVariable(IslandResourceCheckoutPolicy.PerPeerBudgetEnvVar));
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
            // The island tally is what the per-peer budget is spent against, so it
            // has to survive a re-registration of the same id rather than drift
            // upward every time one happens.
            bool wasKnown = _resourceIslands.TryGetValue(entityId, out IslandId previousIsland);
            IslandId owner = IslandResourceInterestPolicy.ClosestIsland(
                entity.Position, _islands.All);
            _resourceIslands[entityId] = owner;
            if (!wasKnown) GroupFor(owner).Count++;
            else if (previousIsland != owner)
            {
                GroupFor(previousIsland).Count--;
                GroupFor(owner).Count++;
            }
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

        /// <summary>
        /// Binds the terrain lifecycle after both services exist. A null/unbound gate
        /// is the legacy fail-open path and therefore preserves disabled-mode wire
        /// behaviour exactly. Once bound, no resource AddEntity or late component
        /// request can outrun its owning terrain root.
        /// </summary>
        public void AttachTerrainReadiness(Func<ENetPeerHandle, IslandId, bool> terrainReady)
        {
            _terrainReady = terrainReady ?? throw new ArgumentNullException(nameof(terrainReady));
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
        /// <summary>
        /// What this peer currently holds, counted per island, for the interest
        /// section of the stats snapshot. Counted from <c>Loaded</c> - the nodes
        /// the peer has actually been sent - rather than from the island checkout
        /// set, because "holds the island" and "has received its nodes" differ
        /// exactly while a checkout is streaming in, and the operator debugging a
        /// budget wants the delivered number.
        /// </summary>
        public IReadOnlyList<InterestPeerIslandStat> HoldingsFor(ENetPeerHandle peer)
        {
            if (!_peers.TryGetValue(peer, out PeerState? state) || state.Loaded.Count == 0)
                return Array.Empty<InterestPeerIslandStat>();
            Dictionary<IslandId, int> counts = new();
            foreach (long entityId in state.Loaded)
            {
                if (!_resourceIslands.TryGetValue(entityId, out IslandId island)) continue;
                counts[island] = counts.TryGetValue(island, out int held) ? held + 1 : 1;
            }
            return counts
                .OrderBy(pair => pair.Key)
                .Select(pair => new InterestPeerIslandStat(pair.Key.Value, pair.Value))
                .ToList();
        }

        public bool MayServe(ENetPeerHandle peer, long entityId)
        {
            WorldEntity? entity = _registry.ByEntityId(entityId);
            bool streamed = entity != null && ResourceInterestPolicy.IsStreamedResourceKey(entity.Key);
            bool loaded = _peers.TryGetValue(peer, out PeerState? state) && state.Loaded.Contains(entityId);
            bool checkoutAllows = ResourceInterestPolicy.MayServeComponents(
                Interest.Enabled, streamed, loaded);
            if (!streamed || !_resourceIslands.TryGetValue(entityId, out IslandId island))
            {
                return checkoutAllows;
            }
            bool terrainReady = _terrainReady == null || _terrainReady(peer, island);
            return IslandTerrainResourceOrderingPolicy.MayServeResourceComponents(
                checkoutAllows, _terrainReady != null, terrainReady);
        }

        /// <summary>
        /// Cancels this island's pending resource adds and drains its loaded resource
        /// entities before terrain removal. Returns true only after the wire-facing
        /// loaded ledger is empty. An old client without channel 5 necessarily keeps
        /// both resources and terrain for the rest of its session.
        /// </summary>
        public bool DrainIslandBeforeTerrainRemoval(ENetPeerHandle peer, IslandId island)
        {
            if (!Enabled || !_peers.TryGetValue(peer, out PeerState? state)) return true;

            // Release the island from the checkout set first. Island-keyed interest
            // would otherwise re-admit it on the very next reconcile and refill the
            // queue this call just drained - the drain has to be authoritative or it
            // is a race against ourselves.
            state.Islands.Remove(island);

            IReadOnlyList<ResourceStreamAction> drain =
                IslandTerrainResourceOrderingPolicy.DrainBeforeTerrainRemoval(
                    state.Pending, state.Loaded, _resourceIslands, island);
            ResourceInterestPolicy.ReplacePending(state.Pending, drain, MaxQueuedPerPeer);
            if (state.AssetRequestedFor != 0
                && _resourceIslands.TryGetValue(state.AssetRequestedFor, out IslandId requestedIsland)
                && requestedIsland == island)
            {
                state.AssetRequestedFor = 0;
            }
            state.NextSend = TimeSpan.Zero;
            state.NextReconcile = _clock.Elapsed + ReconcileInterval;
            return IslandTerrainResourceOrderingPolicy.IsDrained(
                state.Loaded, _resourceIslands, island);
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
        /// <summary>
        /// How many streamed resource nodes belong to an island. A pure read for
        /// the operator console: it neither registers nor reconciles anything.
        /// </summary>
        public int ResourceNodeCountFor(IslandId island)
        {
            int count = 0;
            foreach (IslandId owner in _resourceIslands.Values)
                if (owner == island) count++;
            return count;
        }

        /// <summary>
        /// How many of that island's resources are still checked out across all
        /// peers. Zero is what a completed terrain drain looks like, which is why
        /// the terrain console shows it beside the drain state rather than
        /// re-asking the drain gate (asking would mutate the send queue).
        /// </summary>
        public int CheckedOutResourceCountFor(IslandId island)
        {
            int count = 0;
            foreach (PeerState state in _peers.Values)
                foreach (long entityId in state.Loaded)
                    if (_resourceIslands.TryGetValue(entityId, out IslandId owner)
                        && owner == island) count++;
            return count;
        }

        public FixedPointPosition CenterFor(ENetPeerHandle peer) =>
            _peers.TryGetValue(peer, out PeerState? state)
                ? state.Center
                : SpawnPolicy.PlayerSpawnPosition;

        /// <summary>
        /// Returns the latest authoritative world-space centre held for an exact
        /// connected peer. Unlike <see cref="CenterFor(ENetPeerHandle)"/>, this
        /// does not manufacture the spawn point when the peer has never entered
        /// the interest ledger, so operator telemetry can distinguish "unknown"
        /// from a real position.
        /// </summary>
        public bool TryCenterFor(ulong peerId, out FixedPointPosition center)
        {
            foreach ((ENetPeerHandle peer, PeerState state) in _peers)
            {
                if (PeerIdentity.IdOf(peer) != peerId) continue;
                center = state.Center;
                return true;
            }
            center = default;
            return false;
        }

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

                    // WHICH ISLANDS, not which nodes. Envelope distance is zero
                    // everywhere on an island and changes only when the PLAYER
                    // travels, so an island under a standing player cannot be
                    // dropped and a node's own position can no longer decide its
                    // own fate.
                    AdmitIslands(state);

                    ResourceInterestPolicy.ReplacePending(
                        state.Pending,
                        ResourceInterestPolicy.Reconcile(
                            state.Center,
                            IslandResourceCheckoutPolicy.Desire(
                                candidates.Select(entity =>
                                {
                                    if (!_resourceIdsByKey.TryGetValue(entity.Key, out long entityId))
                                        throw new InvalidOperationException(
                                            "resource interest candidate '" + entity.Key
                                            + "' has no resource entity id");
                                    return new IslandResource(
                                        entityId, entity.Position, _resourceIslands[entityId]);
                                }),
                                state.Islands),
                            state.Loaded),
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
                IslandId actionIsland = _resourceIslands[action.EntityId];
                bool terrainReady = _terrainReady == null || _terrainReady(peer, actionIsland);
                if (!IslandTerrainResourceOrderingPolicy.MayAddResource(
                        _terrainReady != null, terrainReady))
                {
                    // Drop rather than park a stale head: the next reconcile will
                    // requeue it after terrain becomes ready, while removals and
                    // other-island work remain free to progress now.
                    state.Pending.Dequeue();
                    if (state.AssetRequestedFor == action.EntityId) state.AssetRequestedFor = 0;
                    Console.WriteLine("[resource-interest] deferred '" + entity.Key
                        + "' because terrain for island " + actionIsland + " is not ready for "
                        + peer.DangerousGetHandle() + ".");
                    continue;
                }
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

        /// <summary>
        /// Recomputes which islands' resource sets this peer holds, from where it is
        /// now and what it already holds. Two lines of geometry and one pure policy
        /// call: every rule lives in
        /// <see cref="IslandResourceCheckoutPolicy"/>/<see cref="IslandInterestAdmissionPolicy"/>,
        /// which is also what fauna uses, so the two features cannot disagree about
        /// what being on an island means.
        /// </summary>
        private void AdmitIslands(PeerState state)
        {
            List<IslandInterestCandidate> candidates =
                new List<IslandInterestCandidate>(_byIsland.Count);
            foreach ((IslandId islandId, IslandResourceGroup group) in _byIsland)
            {
                candidates.Add(new IslandInterestCandidate(islandId,
                    group.Envelope.DistanceSquaredTo(state.Center, group.Island),
                    group.Count));
            }

            IReadOnlyList<IslandId> admitted = IslandResourceCheckoutPolicy.Admit(
                candidates, state.Islands, IslandLoadRadiusMetres,
                // A peer with no channel 5 can never unload, so it must never be told
                // to: an infinite unload radius means "retain what you have".
                state.RemoveSupported ? IslandUnloadRadiusMetres : double.PositiveInfinity,
                PerPeerResourceBudget);

            state.Islands.Clear();
            foreach (IslandId islandId in admitted) state.Islands.Add(islandId);
        }

        /// <summary>
        /// The admission group for an island, created on first use.
        ///
        /// AN ISLAND WITHOUT AN EXTRACTED ENVELOPE FALLS BACK TO A POINT at its own
        /// origin, which makes envelope distance identical to origin distance - the
        /// metric <see cref="IslandResourceInterestPolicy.ClosestIsland"/> already
        /// uses to decide which island a resource belongs to. So an unmeasured island
        /// behaves exactly as it did before this change rather than throwing on the
        /// boot path, and every island the release catalogue or
        /// <see cref="IslandTerrainEnvelopes"/> knows about gets the real AABB.
        /// </summary>
        private IslandResourceGroup GroupFor(IslandId islandId)
        {
            if (_byIsland.TryGetValue(islandId, out IslandResourceGroup? group)) return group;

            IslandDefinition island = _islands.Require(islandId);
            IslandTerrainEnvelope envelope = IslandTerrainEnvelopes.ByIsland(islandId)
                ?? new IslandTerrainEnvelope(islandId, 0, 0, 0, 0, 0, 0);
            group = new IslandResourceGroup(island, envelope);
            _byIsland[islandId] = group;
            return group;
        }

        /// <summary>
        /// States the checkout contract at boot, so the numbers the multiplayer-safety
        /// rule asks about are printed rather than claimed - and so an island too big
        /// for the per-peer budget is found here instead of in a player report.
        /// </summary>
        private void ReportIslandCheckout()
        {
            int largest = _byIsland.Count == 0 ? 0 : _byIsland.Values.Max(group => group.Count);
            Console.WriteLine("[resource-interest] island-keyed checkout: "
                + _resources.Count + " resource(s) across " + _byIsland.Count
                + " island(s); load " + IslandLoadRadiusMetres.ToString("0")
                + " m / unload " + IslandUnloadRadiusMetres.ToString("0")
                + " m to the island ENVELOPE; per-peer budget "
                + PerPeerResourceBudget + " entities; largest island " + largest
                + "; worst-case " + (largest * 2 * SendInterval.TotalSeconds).ToString("0.0")
                + " s to stream one island at " + (1.0 / SendInterval.TotalSeconds).ToString("0.0")
                + " action/s.");

            string? warning = IslandResourceCheckoutPolicy.BudgetWarning(
                _byIsland.Select(entry => (entry.Key, entry.Value.Count)), PerPeerResourceBudget);
            if (warning != null) Console.WriteLine("[resource-interest] WARNING: " + warning);
        }

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

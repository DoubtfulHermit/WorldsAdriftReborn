namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The lifecycle of one atlas shard, server-authoritative.
    ///
    /// LODGED -> EXPOSED -> RELEASED -> COLLECTED, each transition one-way, and the
    /// middle two are BOTH pickable. This is the retail shape
    /// (worldsadrift.fandom.com/wiki/Mining, /wiki/Atlas_Shard): a shard is a green
    /// crystal in the CENTRE CORE of a metal node, hidden while the outer shell is
    /// intact; breaking enough shell EXPOSES it, and it can be taken by interacting
    /// (E) right there, still in the rock; only if you keep mining and DESTROY the
    /// node does it fall loose - which is why players were told to grab shards before
    /// finishing a node, since a loose one can roll off the island.
    ///
    ///   LODGED   - shell intact. 2102 isLodged=true, 1210 available=FALSE (no prompt).
    ///   EXPOSED  - shell broken enough (<see cref="MetalDepositExposure"/>). STILL in
    ///              the slot, so 2102 isLodged stays TRUE (the shard must not start
    ///              falling), but 1210 available=TRUE: the PickUp prompt appears.
    ///   RELEASED - the core was destroyed. 2102 isLodged=false + Dislodged (the
    ///              client's own MetalDepositAtlasVisualiser core-Exploded chain lets
    ///              the rigidbody go), 1210 still available.
    ///   COLLECTED- a PickUp transaction took it. 1210 unavailable, world shard sunk.
    ///
    /// See docs/research/findings-atlas-shards.md §2. The EXPOSED step is the
    /// correction to the original "released only on core destruction" reading, which
    /// made shards unobtainable until the node was gone.
    /// </summary>
    public enum AtlasShardState
    {
        /// <summary>In the core slot, shell intact. Not pickable; 2102 isLodged, 1210 unavailable.</summary>
        Lodged,

        /// <summary>
        /// Shell broken enough that the shard shows in the core. STILL lodged (2102
        /// isLodged stays true so it does not fall), but pickable: 1210 available.
        /// </summary>
        Exposed,

        /// <summary>Freed by core destruction. Pickable; 2102 dislodged, 1210 available.</summary>
        Released,

        /// <summary>Taken by a player. 1210 unavailable, world entity sunk/removed.</summary>
        Collected,
    }

    /// <summary>
    /// The server's ledger of every atlas shard it has put in the world and each
    /// shard's live acquisition state. The ONE place the lodged/released/collected
    /// transition and the pickup RESERVATION live, so the "two players cannot both win
    /// the same shard" guarantee is pinned by xUnit rather than by a running client.
    ///
    /// It is the atlas analogue of <see cref="NodeRegistry"/>, kept separate because a
    /// shard is a SECOND entity from its host deposit with its own lifecycle (a
    /// deposit's core destruction is what releases the shard, but the shard is then a
    /// free-standing pickup that a different player can reach first). The two meet only
    /// at the release seam: destroying a deposit core calls <see cref="ReleaseByHost"/>.
    ///
    /// Pure: no ENet, no Improbable types, no game install.
    ///
    /// NOT THREAD-SAFE, deliberately, like the rest of this assembly: the server is a
    /// single poll loop. The RESERVATION is still meaningful in a single thread: two
    /// 1211 PickUp events for the same shard can arrive in one drain and are processed
    /// back to back, so the first reserver must lock the others out across its own
    /// grant before it commits the collect.
    /// </summary>
    public sealed class AtlasShardRegistry
    {
        /// <summary>The sentinel "reserved by nobody" player entity id.</summary>
        private const long Unreserved = 0;

        private sealed class Shard
        {
            public Shard(long hostDepositEntityId, int slotId)
            {
                HostDepositEntityId = hostDepositEntityId;
                SlotId = slotId;
            }

            public long HostDepositEntityId { get; }
            public int SlotId { get; }
            public AtlasShardState State { get; set; } = AtlasShardState.Lodged;
            public long ReservedBy { get; set; } = Unreserved;
        }

        private readonly Dictionary<long, Shard> _byEntityId = new Dictionary<long, Shard>();

        /// <summary>
        /// Records that an entity id is a placed atlas shard lodged in
        /// <paramref name="hostDepositEntityId"/>'s core, at slot
        /// <paramref name="slotId"/>. Idempotent and keyed by entity id, exactly like
        /// <see cref="NodeRegistry.Register"/>: every joining client walks the identical
        /// spawn plan and reaches this shard's step, but there is one shard, so the
        /// second and later calls are no-ops that must NOT reset its acquisition state.
        /// </summary>
        /// <returns>True on the first registration of this id; false thereafter.</returns>
        public bool Register(long shardEntityId, long hostDepositEntityId, int slotId)
        {
            if (_byEntityId.ContainsKey(shardEntityId))
            {
                return false;
            }
            _byEntityId[shardEntityId] = new Shard(hostDepositEntityId, slotId);
            return true;
        }

        /// <summary>Whether an entity id is a placed atlas shard.</summary>
        public bool IsShard(long shardEntityId) => _byEntityId.ContainsKey(shardEntityId);

        /// <summary>The host deposit's entity id for a shard, or null for a non-shard id.</summary>
        public long? HostOf(long shardEntityId) =>
            _byEntityId.TryGetValue(shardEntityId, out Shard? s) ? s.HostDepositEntityId : (long?)null;

        /// <summary>The 1305 slotId for a shard, or null for a non-shard id.</summary>
        public int? SlotOf(long shardEntityId) =>
            _byEntityId.TryGetValue(shardEntityId, out Shard? s) ? s.SlotId : (int?)null;

        /// <summary>The acquisition state of a shard, or null for a non-shard id.</summary>
        public AtlasShardState? StateOf(long shardEntityId) =>
            _byEntityId.TryGetValue(shardEntityId, out Shard? s) ? s.State : (AtlasShardState?)null;

        /// <summary>
        /// Whether a shard is still PHYSICALLY IN THE CORE SLOT - the 2102 isLodged
        /// seed. True for both Lodged and EXPOSED: exposing a shard reveals it, it does
        /// not knock it out of the rock (a player who does not take it must find it
        /// still sitting there). Only core DESTRUCTION dislodges it. False for a
        /// non-shard id.
        /// </summary>
        public bool IsLodged(long shardEntityId) =>
            _byEntityId.TryGetValue(shardEntityId, out Shard? s)
            && (s.State == AtlasShardState.Lodged || s.State == AtlasShardState.Exposed);

        /// <summary>Whether a shard is exposed but still in the core (pickable in place).</summary>
        public bool IsExposed(long shardEntityId) =>
            _byEntityId.TryGetValue(shardEntityId, out Shard? s) && s.State == AtlasShardState.Exposed;

        /// <summary>Whether a shard has been released (dislodged, pickable). False for a non-shard id.</summary>
        public bool IsReleased(long shardEntityId) =>
            _byEntityId.TryGetValue(shardEntityId, out Shard? s) && s.State == AtlasShardState.Released;

        /// <summary>
        /// Whether a shard has been mined into reach - EXPOSED or RELEASED, i.e. taken
        /// out of hiding but not yet collected. The single predicate the pickup path
        /// and the 1210 seed both read, so "can I take it" has one definition.
        /// </summary>
        public bool IsTakeable(long shardEntityId) =>
            _byEntityId.TryGetValue(shardEntityId, out Shard? s)
            && (s.State == AtlasShardState.Exposed || s.State == AtlasShardState.Released);

        /// <summary>Whether a shard has been collected. False for a non-shard id.</summary>
        public bool IsCollected(long shardEntityId) =>
            _byEntityId.TryGetValue(shardEntityId, out Shard? s) && s.State == AtlasShardState.Collected;

        /// <summary>
        /// Whether a shard's 1210 prompt should read AVAILABLE: exposed or released,
        /// and not yet collected. Reservation does NOT flip this - a reservation is the
        /// momentary lock inside one pickup transaction, not a durable "in use by"
        /// state, and the transaction resolves within the same poll drain. False for a
        /// non-shard id.
        /// </summary>
        public bool IsAvailable(long shardEntityId) => IsTakeable(shardEntityId);

        /// <summary>
        /// Whether a shard is currently reserved by someone OTHER than
        /// <paramref name="playerEntityId"/>. The pickup policy rejects on this so a
        /// second player cannot even attempt a grant on a shard mid-transaction.
        /// </summary>
        public bool IsReservedByOther(long shardEntityId, long playerEntityId) =>
            _byEntityId.TryGetValue(shardEntityId, out Shard? s)
            && s.ReservedBy != Unreserved
            && s.ReservedBy != playerEntityId;

        /// <summary>
        /// EXPOSES every still-hidden shard in <paramref name="hostDepositEntityId"/>'s
        /// core, transitioning each Lodged -> Exposed exactly once. Called on the shot
        /// that first breaks enough crust (<see cref="MetalDepositExposure.IsExposed"/>),
        /// so the "the prompt appeared" broadcast fires ONCE however long the beam is
        /// held afterwards.
        ///
        /// A shard already Exposed, Released or Collected is left untouched - this can
        /// never walk a shard backwards or re-offer one somebody already took.
        /// </summary>
        /// <returns>The shard entity ids this call transitioned to Exposed, in id order.</returns>
        public IReadOnlyList<long> ExposeByHost(long hostDepositEntityId)
        {
            List<long> exposed = new List<long>();
            foreach (KeyValuePair<long, Shard> kv in _byEntityId)
            {
                if (kv.Value.HostDepositEntityId == hostDepositEntityId
                    && kv.Value.State == AtlasShardState.Lodged)
                {
                    kv.Value.State = AtlasShardState.Exposed;
                    exposed.Add(kv.Key);
                }
            }
            exposed.Sort();
            return exposed;
        }

        /// <summary>
        /// Releases every shard still IN <paramref name="hostDepositEntityId"/>'s core -
        /// Lodged or Exposed - transitioning each to Released exactly once. Called at
        /// the moment the deposit's core is destroyed (the SAME once-only deplete
        /// transition <see cref="NodeRegistry.MarkDestroyed"/> reports), so the release
        /// broadcast fires once and a shard already released or collected is left
        /// untouched.
        ///
        /// Lodged is accepted as well as Exposed so a deposit mined faster than the
        /// exposure threshold could ever fire - or one whose exposure knob is set to a
        /// fraction the shot count skips over - still yields its shard rather than
        /// stranding it inside a destroyed rock.
        /// </summary>
        /// <returns>The shard entity ids this call transitioned to Released, in id order.</returns>
        public IReadOnlyList<long> ReleaseByHost(long hostDepositEntityId)
        {
            List<long> released = new List<long>();
            foreach (KeyValuePair<long, Shard> kv in _byEntityId)
            {
                if (kv.Value.HostDepositEntityId == hostDepositEntityId
                    && (kv.Value.State == AtlasShardState.Lodged
                        || kv.Value.State == AtlasShardState.Exposed))
                {
                    kv.Value.State = AtlasShardState.Released;
                    released.Add(kv.Key);
                }
            }
            released.Sort();
            return released;
        }

        /// <summary>
        /// The shard entity ids lodged/attached to a host deposit, for the deposit's
        /// 2103 attachedEntities seed. All shards ever registered against the host are
        /// reported (attachment is a structural fact independent of lodged state), in
        /// id order. Empty for a deposit with no shard.
        /// </summary>
        public IReadOnlyList<long> ShardsForHost(long hostDepositEntityId)
        {
            List<long> shards = new List<long>();
            foreach (KeyValuePair<long, Shard> kv in _byEntityId)
            {
                if (kv.Value.HostDepositEntityId == hostDepositEntityId)
                {
                    shards.Add(kv.Key);
                }
            }
            shards.Sort();
            return shards;
        }

        /// <summary>
        /// Attempts to RESERVE a takeable shard for a player across a pickup
        /// transaction. Succeeds only if the shard is EXPOSED or Released and not
        /// already reserved by someone else; a re-reserve by the SAME player is
        /// idempotent-true (a retried event must not deadlock its own slot). This is
        /// the atomic guard that makes the grant safe: reserve first, then grant, then
        /// <see cref="Collect"/> or <see cref="Rollback"/>.
        /// </summary>
        /// <returns>True if the caller now holds the reservation.</returns>
        public bool Reserve(long shardEntityId, long playerEntityId)
        {
            if (playerEntityId == Unreserved)
            {
                return false;
            }
            if (!_byEntityId.TryGetValue(shardEntityId, out Shard? s)
                || !IsTakeable(shardEntityId))
            {
                return false;
            }
            if (s.ReservedBy != Unreserved && s.ReservedBy != playerEntityId)
            {
                return false;
            }
            s.ReservedBy = playerEntityId;
            return true;
        }

        /// <summary>
        /// Releases a reservation a player holds - the grant failed (unknown item type
        /// or a full inventory grid), so the shard must return to a pickable state for
        /// this or another player. A no-op unless the caller holds the reservation.
        /// </summary>
        /// <returns>True if the reservation was rolled back.</returns>
        public bool Rollback(long shardEntityId, long playerEntityId)
        {
            if (!_byEntityId.TryGetValue(shardEntityId, out Shard? s)
                || s.ReservedBy != playerEntityId)
            {
                return false;
            }
            s.ReservedBy = Unreserved;
            return true;
        }

        /// <summary>
        /// Commits a collection: the grant landed, so the shard becomes Collected and
        /// its reservation is cleared. Only the reserving player can collect, and only
        /// a TAKEABLE (exposed or released) shard - so a second event on an
        /// already-collected shard fails rather than double-granting.
        /// </summary>
        /// <returns>True if this call collected the shard.</returns>
        public bool Collect(long shardEntityId, long playerEntityId)
        {
            if (!_byEntityId.TryGetValue(shardEntityId, out Shard? s)
                || !IsTakeable(shardEntityId)
                || s.ReservedBy != playerEntityId)
            {
                return false;
            }
            s.State = AtlasShardState.Collected;
            s.ReservedBy = Unreserved;
            return true;
        }

        /// <summary>Every placed shard's entity id, in registration order. For fan-out and logs.</summary>
        public IReadOnlyList<long> EntityIds => _byEntityId.Keys.ToArray();

        /// <summary>How many shards are placed. For logs and tests.</summary>
        public int Count => _byEntityId.Count;
    }
}

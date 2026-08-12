namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The lifecycle of one atlas shard, server-authoritative.
    ///
    /// LODGED -> RELEASED -> COLLECTED, each transition one-way. A shard starts
    /// lodged in its host deposit's core (2102 isLodged=true, no pickup offered);
    /// destroying that core RELEASES it (2102 isLodged=false + Dislodged, 1210
    /// available=true, the PickUp prompt appears); a successful PickUp transaction
    /// COLLECTS it (1210 unavailable, the world shard removed/sunk). See
    /// docs/research/findings-atlas-shards.md §2.
    /// </summary>
    public enum AtlasShardState
    {
        /// <summary>In the core slot. Not pickable; 2102 isLodged, 1210 unavailable.</summary>
        Lodged,

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

        /// <summary>Whether a shard is still lodged (2102 isLodged seed). False for a non-shard id.</summary>
        public bool IsLodged(long shardEntityId) =>
            _byEntityId.TryGetValue(shardEntityId, out Shard? s) && s.State == AtlasShardState.Lodged;

        /// <summary>Whether a shard has been released (dislodged, pickable). False for a non-shard id.</summary>
        public bool IsReleased(long shardEntityId) =>
            _byEntityId.TryGetValue(shardEntityId, out Shard? s) && s.State == AtlasShardState.Released;

        /// <summary>Whether a shard has been collected. False for a non-shard id.</summary>
        public bool IsCollected(long shardEntityId) =>
            _byEntityId.TryGetValue(shardEntityId, out Shard? s) && s.State == AtlasShardState.Collected;

        /// <summary>
        /// Whether a shard's 1210 prompt should read AVAILABLE: released and not yet
        /// collected. Reservation does NOT flip this - a reservation is the momentary
        /// lock inside one pickup transaction, not a durable "in use by" state, and the
        /// transaction resolves within the same poll drain. False for a non-shard id.
        /// </summary>
        public bool IsAvailable(long shardEntityId) => IsReleased(shardEntityId);

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
        /// Releases every shard lodged in <paramref name="hostDepositEntityId"/>'s
        /// core, transitioning each Lodged -> Released exactly once. Called at the
        /// moment the deposit's core is destroyed (the SAME once-only deplete
        /// transition <see cref="NodeRegistry.MarkDestroyed"/> reports), so the release
        /// broadcast fires once and a shard already released or collected is left
        /// untouched.
        /// </summary>
        /// <returns>The shard entity ids this call transitioned to Released, in id order.</returns>
        public IReadOnlyList<long> ReleaseByHost(long hostDepositEntityId)
        {
            List<long> released = new List<long>();
            foreach (KeyValuePair<long, Shard> kv in _byEntityId)
            {
                if (kv.Value.HostDepositEntityId == hostDepositEntityId
                    && kv.Value.State == AtlasShardState.Lodged)
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
        /// Attempts to RESERVE a released shard for a player across a pickup
        /// transaction. Succeeds only if the shard is Released and not already reserved
        /// by someone else; a re-reserve by the SAME player is idempotent-true (a
        /// retried event must not deadlock its own slot). This is the atomic guard that
        /// makes the grant safe: reserve first, then grant, then <see cref="Collect"/>
        /// or <see cref="Rollback"/>.
        /// </summary>
        /// <returns>True if the caller now holds the reservation.</returns>
        public bool Reserve(long shardEntityId, long playerEntityId)
        {
            if (playerEntityId == Unreserved)
            {
                return false;
            }
            if (!_byEntityId.TryGetValue(shardEntityId, out Shard? s)
                || s.State != AtlasShardState.Released)
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
        /// a Released shard - so a second event on an already-collected shard fails
        /// rather than double-granting.
        /// </summary>
        /// <returns>True if this call collected the shard.</returns>
        public bool Collect(long shardEntityId, long playerEntityId)
        {
            if (!_byEntityId.TryGetValue(shardEntityId, out Shard? s)
                || s.State != AtlasShardState.Released
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

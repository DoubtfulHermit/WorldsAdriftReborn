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
    ///
    /// This is a THIN alias of the shared <see cref="LodgeablePickupState"/> so the
    /// atlas callers and tests keep their own vocabulary while the state machine
    /// itself lives once, in <see cref="LodgeablePickupRegistry"/>.
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
    /// shard's live acquisition state and host pairing.
    ///
    /// It COMPOSES the shared <see cref="LodgeablePickupRegistry"/> for the
    /// lodged/released/collected transition and the pickup RESERVATION (the "two
    /// players cannot both win the same shard" guarantee), and adds on top the ONE
    /// thing a shard has that a plain pickup does not: a HOST DEPOSIT it is lodged
    /// in, at a numbered slot. A shard is a SECOND entity from its host deposit
    /// (destroying the deposit's core is what releases the shard), which is why the
    /// host/slot map lives here rather than in the shared core.
    ///
    /// Pure: no ENet, no Improbable types, no game install.
    ///
    /// NOT THREAD-SAFE, deliberately, like the rest of this assembly: the server is a
    /// single poll loop. The RESERVATION is still meaningful in a single thread (see
    /// <see cref="LodgeablePickupRegistry"/>).
    /// </summary>
    public sealed class AtlasShardRegistry
    {
        /// <summary>The shared lodged/released/collected + reservation core, keyed by shard entity id.</summary>
        private readonly LodgeablePickupRegistry _core = new LodgeablePickupRegistry();

        /// <summary>Per-shard host deposit + slot, the atlas-only coupling on top of the core.</summary>
        private readonly Dictionary<long, (long HostDepositEntityId, int SlotId)> _hosts =
            new Dictionary<long, (long, int)>();

        /// <summary>
        /// Records that an entity id is a placed atlas shard lodged in
        /// <paramref name="hostDepositEntityId"/>'s core, at slot
        /// <paramref name="slotId"/>. Idempotent and keyed by entity id: every joining
        /// client walks the identical spawn plan and reaches this shard's step, but
        /// there is one shard, so the second and later calls are no-ops that must NOT
        /// reset its acquisition state. A shard starts LODGED (it is freed only when
        /// its host core is destroyed).
        /// </summary>
        /// <returns>True on the first registration of this id; false thereafter.</returns>
        public bool Register(long shardEntityId, long hostDepositEntityId, int slotId)
        {
            if (_hosts.ContainsKey(shardEntityId))
            {
                return false;
            }
            _hosts[shardEntityId] = (hostDepositEntityId, slotId);
            _core.Register(shardEntityId, startReleased: false);
            return true;
        }

        /// <summary>Whether an entity id is a placed atlas shard.</summary>
        public bool IsShard(long shardEntityId) => _hosts.ContainsKey(shardEntityId);

        /// <summary>The host deposit's entity id for a shard, or null for a non-shard id.</summary>
        public long? HostOf(long shardEntityId) =>
            _hosts.TryGetValue(shardEntityId, out (long Host, int Slot) s) ? s.Host : (long?)null;

        /// <summary>The 1305 slotId for a shard, or null for a non-shard id.</summary>
        public int? SlotOf(long shardEntityId) =>
            _hosts.TryGetValue(shardEntityId, out (long Host, int Slot) s) ? s.Slot : (int?)null;

        /// <summary>The acquisition state of a shard, or null for a non-shard id.</summary>
        public AtlasShardState? StateOf(long shardEntityId) => _core.StateOf(shardEntityId) switch
        {
            LodgeablePickupState.Lodged => AtlasShardState.Lodged,
            LodgeablePickupState.Released => AtlasShardState.Released,
            LodgeablePickupState.Collected => AtlasShardState.Collected,
            _ => (AtlasShardState?)null,
        };

        /// <summary>Whether a shard is still lodged (2102 isLodged seed). False for a non-shard id.</summary>
        public bool IsLodged(long shardEntityId) => _core.IsLodged(shardEntityId);

        /// <summary>Whether a shard has been released (dislodged, pickable). False for a non-shard id.</summary>
        public bool IsReleased(long shardEntityId) => _core.IsReleased(shardEntityId);

        /// <summary>Whether a shard has been collected. False for a non-shard id.</summary>
        public bool IsCollected(long shardEntityId) => _core.IsCollected(shardEntityId);

        /// <summary>
        /// Whether a shard's 1210 prompt should read AVAILABLE: released and not yet
        /// collected. Reservation does NOT flip this. False for a non-shard id.
        /// </summary>
        public bool IsAvailable(long shardEntityId) => _core.IsAvailable(shardEntityId);

        /// <summary>
        /// Whether a shard is currently reserved by someone OTHER than
        /// <paramref name="playerEntityId"/>.
        /// </summary>
        public bool IsReservedByOther(long shardEntityId, long playerEntityId) =>
            _core.IsReservedByOther(shardEntityId, playerEntityId);

        /// <summary>
        /// Releases every shard lodged in <paramref name="hostDepositEntityId"/>'s
        /// core, transitioning each Lodged -> Released exactly once. Called at the
        /// moment the deposit's core is destroyed, so the release broadcast fires once
        /// and a shard already released or collected is left untouched.
        /// </summary>
        /// <returns>The shard entity ids this call transitioned to Released, in id order.</returns>
        public IReadOnlyList<long> ReleaseByHost(long hostDepositEntityId)
        {
            List<long> released = new List<long>();
            foreach (KeyValuePair<long, (long Host, int Slot)> kv in _hosts)
            {
                if (kv.Value.Host == hostDepositEntityId && _core.Release(kv.Key))
                {
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
            foreach (KeyValuePair<long, (long Host, int Slot)> kv in _hosts)
            {
                if (kv.Value.Host == hostDepositEntityId)
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
        /// by someone else; a re-reserve by the SAME player is idempotent-true.
        /// </summary>
        /// <returns>True if the caller now holds the reservation.</returns>
        public bool Reserve(long shardEntityId, long playerEntityId) =>
            _core.Reserve(shardEntityId, playerEntityId);

        /// <summary>
        /// Releases a reservation a player holds - the grant failed, so the shard must
        /// return to a pickable state. A no-op unless the caller holds the reservation.
        /// </summary>
        /// <returns>True if the reservation was rolled back.</returns>
        public bool Rollback(long shardEntityId, long playerEntityId) =>
            _core.Rollback(shardEntityId, playerEntityId);

        /// <summary>
        /// Commits a collection: the grant landed, so the shard becomes Collected and
        /// its reservation is cleared. Only the reserving player can collect, and only
        /// a Released shard.
        /// </summary>
        /// <returns>True if this call collected the shard.</returns>
        public bool Collect(long shardEntityId, long playerEntityId) =>
            _core.Collect(shardEntityId, playerEntityId);

        /// <summary>Every placed shard's entity id. For fan-out and logs.</summary>
        public IReadOnlyList<long> EntityIds => _core.EntityIds;

        /// <summary>How many shards are placed. For logs and tests.</summary>
        public int Count => _core.Count;
    }
}

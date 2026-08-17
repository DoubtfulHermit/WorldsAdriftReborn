namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The lifecycle of one LODGEABLE PICKUP - a world entity carrying a
    /// <c>LodgeableState</c> (2102) that a player frees and then collects with a
    /// native 1211 PickUp. Server-authoritative, each transition one-way:
    ///
    ///   LODGED -> RELEASED -> COLLECTED
    ///
    /// LODGED   : fixed in place (2102 isLodged, kinematic), not yet pickable.
    /// RELEASED : freed (dislodged), the 1210 PickUp prompt is available.
    /// COLLECTED: taken by a player, the world entity sunk/removed.
    ///
    /// This is the SHARED core extracted from the atlas-shard vertical. An ATLAS
    /// SHARD is a lodgeable pickup lodged in a metal-deposit core: it starts LODGED
    /// and is RELEASED when the core is mined out. A FUEL POD (a "fuel egg",
    /// <c>FuelPodVisualiser_fsim</c> [Require]s only <c>LodgeableState.Reader</c>,
    /// no host-core link) is a HOST-LESS lodgeable pickup: it starts already
    /// RELEASED (there is no core to mine to free it), so a player can pick it up
    /// directly. Both share the release/reservation/collect machinery below; only
    /// the host-coupling differs, which is why <see cref="AtlasShardRegistry"/>
    /// keeps its own host/slot map on TOP of this core.
    /// </summary>
    public enum LodgeablePickupState
    {
        /// <summary>Fixed in place, not pickable. 2102 isLodged, 1210 unavailable.</summary>
        Lodged,

        /// <summary>Freed. Pickable; 2102 dislodged, 1210 available.</summary>
        Released,

        /// <summary>Taken by a player. 1210 unavailable, world entity sunk/removed.</summary>
        Collected,
    }

    /// <summary>
    /// The server's ledger of every lodgeable pickup it has put in the world and
    /// each one's live acquisition state. The ONE place the lodged/released/collected
    /// transition and the pickup RESERVATION live, so the "two players cannot both
    /// win the same pickup" guarantee is pinned by xUnit rather than by a running
    /// client.
    ///
    /// HOST-LESS by design: a pickup is keyed by its own entity id and knows nothing
    /// about a host. The atlas shard's host/deposit pairing lives one layer up in
    /// <see cref="AtlasShardRegistry"/>, which composes this core; a fuel pod needs
    /// no host at all and uses this registry directly.
    ///
    /// Pure: no ENet, no Improbable types, no game install.
    ///
    /// NOT THREAD-SAFE, deliberately, like the rest of this assembly: the server is a
    /// single poll loop. The RESERVATION is still meaningful in a single thread: two
    /// 1211 PickUp events for the same pickup can arrive in one drain and are
    /// processed back to back, so the first reserver must lock the others out across
    /// its own grant before it commits the collect.
    /// </summary>
    public sealed class LodgeablePickupRegistry
    {
        /// <summary>The sentinel "reserved by nobody" player entity id.</summary>
        private const long Unreserved = 0;

        private sealed class Entry
        {
            public Entry(LodgeablePickupState state)
            {
                State = state;
            }

            public LodgeablePickupState State { get; set; }
            public long ReservedBy { get; set; } = Unreserved;
        }

        private readonly Dictionary<long, Entry> _byEntityId = new Dictionary<long, Entry>();

        /// <summary>
        /// Records an entity id as a placed lodgeable pickup. Idempotent and keyed by
        /// entity id: every joining client walks the identical spawn plan and reaches
        /// this step, but there is one pickup, so the second and later calls are
        /// no-ops that must NOT reset its acquisition state.
        /// </summary>
        /// <param name="startReleased">
        /// Whether the pickup starts RELEASED (immediately pickable) rather than
        /// LODGED. A host-less pod starts released - there is no core to mine to free
        /// it; an atlas shard starts lodged and is released by
        /// <see cref="Release"/> when its host core is destroyed.
        /// </param>
        /// <returns>True on the first registration of this id; false thereafter.</returns>
        public bool Register(long entityId, bool startReleased = false)
        {
            if (_byEntityId.ContainsKey(entityId))
            {
                return false;
            }
            _byEntityId[entityId] = new Entry(
                startReleased ? LodgeablePickupState.Released : LodgeablePickupState.Lodged);
            return true;
        }

        /// <summary>Whether an entity id is a placed lodgeable pickup.</summary>
        public bool Contains(long entityId) => _byEntityId.ContainsKey(entityId);

        /// <summary>The acquisition state of a pickup, or null for an unknown id.</summary>
        public LodgeablePickupState? StateOf(long entityId) =>
            _byEntityId.TryGetValue(entityId, out Entry? e) ? e.State : (LodgeablePickupState?)null;

        /// <summary>Whether a pickup is still lodged (2102 isLodged seed). False for an unknown id.</summary>
        public bool IsLodged(long entityId) =>
            _byEntityId.TryGetValue(entityId, out Entry? e) && e.State == LodgeablePickupState.Lodged;

        /// <summary>Whether a pickup has been released (dislodged, pickable). False for an unknown id.</summary>
        public bool IsReleased(long entityId) =>
            _byEntityId.TryGetValue(entityId, out Entry? e) && e.State == LodgeablePickupState.Released;

        /// <summary>Whether a pickup has been collected. False for an unknown id.</summary>
        public bool IsCollected(long entityId) =>
            _byEntityId.TryGetValue(entityId, out Entry? e) && e.State == LodgeablePickupState.Collected;

        /// <summary>
        /// Whether a pickup's 1210 prompt should read AVAILABLE: released and not yet
        /// collected. Reservation does NOT flip this - a reservation is the momentary
        /// lock inside one pickup transaction, not a durable "in use by" state, and the
        /// transaction resolves within the same poll drain. False for an unknown id.
        /// </summary>
        public bool IsAvailable(long entityId) => IsReleased(entityId);

        /// <summary>
        /// Whether a pickup is currently reserved by someone OTHER than
        /// <paramref name="playerEntityId"/>. The pickup policy rejects on this so a
        /// second player cannot even attempt a grant on a pickup mid-transaction.
        /// </summary>
        public bool IsReservedByOther(long entityId, long playerEntityId) =>
            _byEntityId.TryGetValue(entityId, out Entry? e)
            && e.ReservedBy != Unreserved
            && e.ReservedBy != playerEntityId;

        /// <summary>
        /// Releases a pickup, transitioning it Lodged -> Released exactly once. For an
        /// atlas shard this fires when the host core is destroyed; a host-less pod is
        /// registered already released and never needs this. Idempotent: an already
        /// released or collected pickup is left untouched.
        /// </summary>
        /// <returns>True if this call transitioned the pickup to Released.</returns>
        public bool Release(long entityId)
        {
            if (_byEntityId.TryGetValue(entityId, out Entry? e)
                && e.State == LodgeablePickupState.Lodged)
            {
                e.State = LodgeablePickupState.Released;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Attempts to RESERVE a released pickup for a player across a pickup
        /// transaction. Succeeds only if the pickup is Released and not already
        /// reserved by someone else; a re-reserve by the SAME player is
        /// idempotent-true (a retried event must not deadlock its own slot). This is
        /// the atomic guard that makes the grant safe: reserve, then grant, then
        /// <see cref="Collect"/> or <see cref="Rollback"/>.
        /// </summary>
        /// <returns>True if the caller now holds the reservation.</returns>
        public bool Reserve(long entityId, long playerEntityId)
        {
            if (playerEntityId == Unreserved)
            {
                return false;
            }
            if (!_byEntityId.TryGetValue(entityId, out Entry? e)
                || e.State != LodgeablePickupState.Released)
            {
                return false;
            }
            if (e.ReservedBy != Unreserved && e.ReservedBy != playerEntityId)
            {
                return false;
            }
            e.ReservedBy = playerEntityId;
            return true;
        }

        /// <summary>
        /// Releases a reservation a player holds - the grant failed (unknown item type
        /// or a full inventory grid), so the pickup must return to a pickable state for
        /// this or another player. A no-op unless the caller holds the reservation.
        /// </summary>
        /// <returns>True if the reservation was rolled back.</returns>
        public bool Rollback(long entityId, long playerEntityId)
        {
            if (!_byEntityId.TryGetValue(entityId, out Entry? e)
                || e.ReservedBy != playerEntityId)
            {
                return false;
            }
            e.ReservedBy = Unreserved;
            return true;
        }

        /// <summary>
        /// Commits a collection: the grant landed, so the pickup becomes Collected and
        /// its reservation is cleared. Only the reserving player can collect, and only
        /// a Released pickup - so a second event on an already-collected pickup fails
        /// rather than double-granting.
        /// </summary>
        /// <returns>True if this call collected the pickup.</returns>
        public bool Collect(long entityId, long playerEntityId)
        {
            if (!_byEntityId.TryGetValue(entityId, out Entry? e)
                || e.State != LodgeablePickupState.Released
                || e.ReservedBy != playerEntityId)
            {
                return false;
            }
            e.State = LodgeablePickupState.Collected;
            e.ReservedBy = Unreserved;
            return true;
        }

        /// <summary>Every placed pickup's entity id, in registration order. For fan-out and logs.</summary>
        public IReadOnlyList<long> EntityIds => _byEntityId.Keys.ToArray();

        /// <summary>How many pickups are placed. For logs and tests.</summary>
        public int Count => _byEntityId.Count;
    }
}

namespace WorldsAdriftRebornGameServer.Multiplayer.Placement
{
    /// <summary>
    /// The stateful half of station pickup: the per-entity RESERVATION that makes
    /// "two simultaneous PickUp events cannot both grant" true, and the PICKED-UP
    /// TOMBSTONE that keeps a packed station gone for late joiners.
    ///
    /// WHY A TOMBSTONE AND NOT JUST LEDGER REMOVAL. WAReborn's transport has no
    /// RemoveEntityOp, so a packed station's world-entity registration stays in the
    /// registry and is still served to a late joiner by the spawn plan. The
    /// serializer therefore consults <see cref="IsPickedUp"/> to seed that joiner
    /// the SAME disappeared state everyone present was pushed live: a sunk 190602
    /// and an unavailable 1210 - exactly the atlas-shard collected pattern. A
    /// REBOOT never resurrects it either, because the pickup transaction removes
    /// the persisted placed-deployable record; this tombstone only has to cover
    /// the rest of the current session.
    ///
    /// The reserve -> commit/rollback shape mirrors <see cref="AtlasShardRegistry"/>'s
    /// shard reservation. Instance-based with a process-wide <see cref="Shared"/>
    /// (like <see cref="ShipyardBuildAccess"/>) so tests construct their own.
    ///
    /// NOT thread-safe, deliberately: every caller runs on the single server poll
    /// loop, like every other ledger.
    /// </summary>
    public sealed class StationPickupLedger
    {
        /// <summary>The process-wide ledger the live server uses.</summary>
        public static StationPickupLedger Shared { get; } = new StationPickupLedger();

        private readonly Dictionary<long, long> _reservedBy = new Dictionary<long, long>();
        private readonly HashSet<long> _pickedUp = new HashSet<long>();

        /// <summary>Whether this station has already been packed into someone's inventory.</summary>
        public bool IsPickedUp(long stationEntityId) => _pickedUp.Contains(stationEntityId);

        /// <summary>Whether another player holds this station's pickup reservation.</summary>
        public bool IsReservedByOther(long stationEntityId, long playerEntityId) =>
            _reservedBy.TryGetValue(stationEntityId, out long holder) && holder != playerEntityId;

        /// <summary>
        /// Takes the pickup reservation for <paramref name="playerEntityId"/>.
        /// FIRST EVENT WINS: returns false when the station is already picked up or
        /// another player holds the reservation, so a second concurrent PickUp
        /// event can never reach the grant and duplicate the item. Idempotent for
        /// the same player (a re-reserve while holding it succeeds).
        /// </summary>
        public bool Reserve(long stationEntityId, long playerEntityId)
        {
            if (_pickedUp.Contains(stationEntityId))
            {
                return false;
            }
            if (_reservedBy.TryGetValue(stationEntityId, out long holder) && holder != playerEntityId)
            {
                return false;
            }
            _reservedBy[stationEntityId] = playerEntityId;
            return true;
        }

        /// <summary>
        /// Releases a failed transaction's reservation so the station stays
        /// pickable (a full inventory now might have room later). Only the holder
        /// can roll back; returns whether anything was released.
        /// </summary>
        public bool Rollback(long stationEntityId, long playerEntityId)
        {
            if (!_reservedBy.TryGetValue(stationEntityId, out long holder) || holder != playerEntityId)
            {
                return false;
            }
            _reservedBy.Remove(stationEntityId);
            return true;
        }

        /// <summary>
        /// Marks the station PICKED UP after a successful grant: the reservation is
        /// consumed and <see cref="IsPickedUp"/> answers true forever after (for
        /// this session; the persisted record's removal covers the next boot).
        /// Only the reservation holder can commit; returns whether it did.
        /// </summary>
        public bool Commit(long stationEntityId, long playerEntityId)
        {
            if (!_reservedBy.TryGetValue(stationEntityId, out long holder) || holder != playerEntityId)
            {
                return false;
            }
            _reservedBy.Remove(stationEntityId);
            _pickedUp.Add(stationEntityId);
            return true;
        }
    }
}

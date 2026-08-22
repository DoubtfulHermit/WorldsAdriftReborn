namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// A ship restore is safe only after the retail client has requested
    /// components for the root and every generated deck. Those requests happen
    /// after AddEntity materialization and are the protocol's native readiness
    /// evidence; a server-side "sent" ledger alone proves only queueing.
    /// </summary>
    public static class ShipRestoreReadinessPolicy
    {
        public const double MaximumShipDistanceMetres = 40.0;

        public static bool IsReady(long hullEntityId, IReadOnlyList<long> deckEntityIds,
            IReadOnlySet<long> materializedEntityIds)
        {
            if (hullEntityId <= 0 || !materializedEntityIds.Contains(hullEntityId))
                return false;
            foreach (long deckEntityId in deckEntityIds)
                if (deckEntityId <= 0 || !materializedEntityIds.Contains(deckEntityId))
                    return false;
            return true;
        }
    }
}

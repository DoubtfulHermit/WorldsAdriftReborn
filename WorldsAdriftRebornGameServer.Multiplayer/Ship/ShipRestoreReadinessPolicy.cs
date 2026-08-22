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
        public const double HorizontalPaddingMetres = 2.0;
        public const double BelowHullPaddingMetres = 1.0;
        public const double AboveDeckPaddingMetres = 3.0;

        public static bool IsWithinHullEnvelope(ShipHullMetrics hull, double hullX,
            double hullY, double hullZ, double yawRadians,
            double pointX, double pointY, double pointZ)
        {
            if (hull.CellCount <= 0 || !double.IsFinite(yawRadians)) return false;
            double dx = pointX - hullX;
            double dy = pointY - hullY;
            double dz = pointZ - hullZ;
            if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(dz))
                return false;

            double sin = Math.Sin(yawRadians);
            double cos = Math.Cos(yawRadians);
            double localX = dx * cos - dz * sin;
            double localZ = dx * sin + dz * cos;
            return Math.Abs(localX) <= hull.BeamMetres * 0.5 + HorizontalPaddingMetres
                && localZ >= hull.SternLocalZMetres - HorizontalPaddingMetres
                && localZ <= hull.BowLocalZMetres + HorizontalPaddingMetres
                && dy >= -BelowHullPaddingMetres
                && dy <= hull.DeckPlaneMetres + AboveDeckPaddingMetres;
        }

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

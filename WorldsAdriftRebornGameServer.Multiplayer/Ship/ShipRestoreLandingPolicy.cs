namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// Chooses a deterministic deck-panel centre for a returning player instead
    /// of blindly restoring the exact logout point. An exact point can be valid
    /// ground while also intersecting a component mounted after/next to it;
    /// Unity resolves that penetration with an unbounded rigidbody impulse.
    /// </summary>
    public static class ShipRestoreLandingPolicy
    {
        public const double FootClearanceMetres = 0.45;
        // Character capsule plus the common generator/core/trunk half-width.
        // Deliberately conservative: a login may move a few metres to a clear
        // panel, while one underestimated collider can throw it out of the world.
        public const double MountedPartHorizontalClearanceMetres = 2.0;
        public const double MountedPartBelowMetres = 1.0;
        public const double MountedPartAboveMetres = 2.5;
        public const int MaxDecks = 256;
        public const int MaxMountedParts = 256;

        public static bool TryChooseLocal(
            FixedPointPosition requestedLocal,
            IReadOnlyList<FixedPointPosition> deckLocalOffsets,
            IReadOnlyList<FixedPointPosition> mountedPartLocalOffsets,
            out FixedPointPosition landingLocal)
        {
            landingLocal = default;
            if (deckLocalOffsets == null || deckLocalOffsets.Count == 0
                || deckLocalOffsets.Count > MaxDecks
                || mountedPartLocalOffsets == null
                || mountedPartLocalOffsets.Count > MaxMountedParts)
                return false;

            double requestedX = requestedLocal.MetresX;
            double requestedY = requestedLocal.MetresY;
            double requestedZ = requestedLocal.MetresZ;
            if (!Finite(requestedX, requestedY, requestedZ)) return false;

            bool foundClear = false;
            double bestRequestedDistance = double.PositiveInfinity;
            double bestObstacleDistance = double.NegativeInfinity;
            FixedPointPosition best = default;
            double clearanceSquared = MountedPartHorizontalClearanceMetres
                * MountedPartHorizontalClearanceMetres;

            foreach (FixedPointPosition deck in deckLocalOffsets
                .OrderBy(x => x.X).ThenBy(x => x.Y).ThenBy(x => x.Z))
            {
                double x = deck.MetresX;
                double y = deck.MetresY;
                double z = deck.MetresZ;
                if (!Finite(x, y, z)) continue;

                bool clear = true;
                double nearestObstacleSquared = double.PositiveInfinity;
                foreach (FixedPointPosition part in mountedPartLocalOffsets)
                {
                    double px = part.MetresX;
                    double py = part.MetresY;
                    double pz = part.MetresZ;
                    if (!Finite(px, py, pz)) continue;
                    if (py < y - MountedPartBelowMetres
                        || py > y + MountedPartAboveMetres) continue;

                    double dx = px - x;
                    double dz = pz - z;
                    double horizontalSquared = dx * dx + dz * dz;
                    nearestObstacleSquared = Math.Min(nearestObstacleSquared, horizontalSquared);
                    if (horizontalSquared < clearanceSquared) clear = false;
                }

                double rx = x - requestedX;
                double ry = (y + FootClearanceMetres) - requestedY;
                double rz = z - requestedZ;
                double requestedDistance = rx * rx + ry * ry + rz * rz;

                // Prefer any clear panel over every obstructed one. Among clear
                // panels preserve locality; if every panel is crowded, choose the
                // one with the largest measured separation rather than restoring
                // inside the original obstacle again.
                bool take = clear
                    ? !foundClear || requestedDistance < bestRequestedDistance
                    : !foundClear && (nearestObstacleSquared > bestObstacleDistance
                        || (nearestObstacleSquared == bestObstacleDistance
                            && requestedDistance < bestRequestedDistance));
                if (!take) continue;

                foundClear |= clear;
                bestRequestedDistance = requestedDistance;
                bestObstacleDistance = nearestObstacleSquared;
                best = FixedPointPosition.FromMetres(x, y + FootClearanceMetres, z);
            }

            if (bestRequestedDistance == double.PositiveInfinity) return false;
            landingLocal = best;
            return true;
        }

        private static bool Finite(double x, double y, double z) =>
            double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z);
    }
}

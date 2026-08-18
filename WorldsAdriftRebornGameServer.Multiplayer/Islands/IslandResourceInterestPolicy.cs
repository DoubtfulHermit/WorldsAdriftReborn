namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>One spatial resource annotated with its owning island.</summary>
    public readonly record struct IslandResource(
        long EntityId,
        FixedPointPosition Position,
        IslandId IslandId);

    /// <summary>
    /// Pure island-specific policy used by resource checkout. It deliberately does
    /// not generalise ships, players and structures into a universal interest graph:
    /// it only answers which island frame a resource belongs to.
    ///
    /// It used to also answer WHICH resources a peer holds, with a filter on the one
    /// island the peer's 1073 said it was standing on. That is now
    /// <see cref="IslandResourceCheckoutPolicy"/>'s job, and the difference is not
    /// cosmetic: the old filter still handed its survivors to a 120 m player-centred
    /// sphere, which is what left a player standing on a 735 m island holding 2 of
    /// its 19 nodes.
    /// </summary>
    public static class IslandResourceInterestPolicy
    {
        /// <summary>
        /// Assigns a global position to the nearest registered island origin. Island
        /// resource fields sit within a few hundred metres of their terrain while the
        /// production islands are kilometres apart, making this deterministic without
        /// inventing biome bounds. Ties use stable <see cref="IslandId"/> ordering.
        /// </summary>
        public static IslandId ClosestIsland(
            FixedPointPosition position,
            IEnumerable<IslandDefinition> islands)
        {
            if (islands == null) throw new ArgumentNullException(nameof(islands));

            IslandDefinition? best = null;
            double bestDistanceSquared = double.PositiveInfinity;
            foreach (IslandDefinition island in islands)
            {
                double dx = position.MetresX - island.GlobalOrigin.MetresX;
                double dy = position.MetresY - island.GlobalOrigin.MetresY;
                double dz = position.MetresZ - island.GlobalOrigin.MetresZ;
                double d2 = dx * dx + dy * dy + dz * dz;
                if (best == null || d2 < bestDistanceSquared
                    || (d2 == bestDistanceSquared && island.Id.CompareTo(best.Id) < 0))
                {
                    best = island;
                    bestDistanceSquared = d2;
                }
            }

            return best?.Id ?? throw new ArgumentException(
                "at least one island is required to classify a resource", nameof(islands));
        }
    }
}

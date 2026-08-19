namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// THE RELEASE WORLD'S LOOT CONTAINERS AS SPAWNABLE ENTITIES - the seam between
    /// the authored seats in <see cref="ReleaseLootCatalog"/> and the world registry.
    /// The exact counterpart of <see cref="ReleaseWorldTrees"/>, and deliberately as
    /// short, for the same reasons:
    ///
    ///   * <c>WorldResourceActivation.Activate</c> recognises a container by its KEY
    ///     PREFIX, the way it already recognises a fuel canister, so nothing here
    ///     needs to know about the ledger.
    ///   * <c>ResourceInterestPolicy.IsStreamedResourceKey</c> streams any
    ///     <c>loot-</c> key, which is why the keys here keep that stem. That is not
    ///     cosmetic: a key outside the allowlist is broadcast eagerly to every peer
    ///     instead of streamed, AND is skipped by <c>ActivateBoundResources</c> - the
    ///     "renders but does nothing" bug class the handover records.
    ///   * The sink that keeps a chest on the ground is
    ///     <see cref="LootContainers.Sink"/>, the SAME method Haven's seats go
    ///     through. There is exactly one place in this codebase where a container's
    ///     height is adjusted, and it is not here.
    /// </summary>
    public static class ReleaseWorldLoot
    {
        /// <summary>
        /// Key prefix. Must keep the <c>loot-</c> stem - see the class remarks.
        /// </summary>
        public const string KeyPrefix = LootContainers.KeyPrefix + "release-";

        /// <summary>The registration key for container <paramref name="index"/> on an island.</summary>
        public static string KeyFor(string workshopId, int index) =>
            KeyPrefix + workshopId + "-" + index;

        /// <summary>
        /// Every loot container on one release island, or nothing at all when the
        /// island's surface could not seat one. Positions are the island's own
        /// authored seats, sunk, then lifted into world fixed point by the island's
        /// definition - the same conversion the deposits, databanks and trees beside
        /// them use.
        ///
        /// NO SEEDED COMPONENTS, for the reason every resource here carries none: the
        /// client checks the entity out and states what it needs over
        /// SEND_COMPONENT_INTEREST, and a seed batch is all-or-nothing where the
        /// interest serve is best-effort. A container needs 1210 and 1081 and it will
        /// ask for both.
        ///
        /// AfterPlayer: nobody stands on a chest, and every step ordered before the
        /// player is a step the loading screen waits on.
        /// </summary>
        public static IEnumerable<WorldEntity> For(ReleaseIslandRecord island)
        {
            ReleaseLootIsland? seats = ReleaseLootCatalog.ForWorkshopId(island.Survey.WorkshopId);
            if (seats == null)
            {
                yield break;
            }

            for (int i = 0; i < seats.Points.Count; i++)
            {
                (double x, double y, double z) = seats.Points[i];
                LootContainers.Placement sunk = LootContainers.Sink(x, y, z);

                yield return new WorldEntity(
                    KeyFor(seats.WorkshopId, i),
                    LootContainers.AssetName,
                    WorldEntities.DefaultAssetContext,
                    island.Definition.LocalToGlobal(sunk.LocalX, sunk.LocalY, sunk.LocalZ),
                    seedComponents: null,
                    order: SpawnOrder.AfterPlayer);
            }
        }

        /// <summary>
        /// The island tier a container key belongs to, which is what decides its
        /// contents. Returns null for a key this catalogue does not own - Haven's
        /// containers, or anything that is not a container at all.
        ///
        /// The tier travels with the KEY rather than being looked up from the
        /// player's position, because loot must be the same for every peer that opens
        /// the same chest. See <see cref="Loot.LootTable"/>.
        /// </summary>
        public static int? TierForKey(string? key)
        {
            if (key == null || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            string rest = key.Substring(KeyPrefix.Length);
            int dash = rest.LastIndexOf('-');
            if (dash <= 0)
            {
                return null;
            }

            ReleaseLootIsland? island = ReleaseLootCatalog.ForWorkshopId(rest.Substring(0, dash));
            return island?.Tier;
        }
    }
}

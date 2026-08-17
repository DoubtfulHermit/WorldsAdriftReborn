namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// THE RELEASE WORLD'S TREES AS SPAWNABLE ENTITIES - the seam between the
    /// authored seats in <see cref="ReleaseTreeCatalog"/> and the world registry.
    ///
    /// WHY TREES ARE THE FIRST WORLD-CONTENT GAP TO CLOSE. Wood is the most-used
    /// crafting material in the game and until now it existed on exactly one
    /// island. The moment a player flew off Haven the gather-craft-build loop
    /// stopped, no matter how much terrain had been enabled underneath them.
    ///
    /// WHY IT IS ALSO THE CHEAPEST. Every hard part was already solved and proven,
    /// which is the whole reason this file is short:
    ///
    ///   * <c>WorldResourceActivation.Activate</c> recognises a tree purely by
    ///     <c>TreeSpecies.WoodFor(entity.AssetName)</c>, with no coupling to the
    ///     registration key, so a new tree anywhere in the world becomes
    ///     authoritative through the existing path with no edit at all.
    ///   * <c>ResourceInterestPolicy.IsStreamedResourceKey</c> already streams any
    ///     <c>tree-*</c> key, which is why the keys here keep that prefix. This is
    ///     not cosmetic: a resource key outside that list is broadcast eagerly
    ///     instead of spatially streamed, and - worse - is skipped by
    ///     <c>ActivateBoundResources</c>, which is exactly the "renders but yields
    ///     nothing" bug class docs/HANDOVER.md records. Every tree registered here
    ///     is streamed, activated, harvestable and respawning on the same five
    ///     minute timer as Haven's.
    ///   * <c>TreeHarvest</c>, <c>TreeTopologies</c> and the eight-wood
    ///     <c>VerifiedSpecies</c> table already work; they were simply switched off
    ///     and Haven-only.
    ///
    /// So this was a placement problem and nothing else, and it is solved the way
    /// this repo already solves placement: offline, deterministically, from
    /// measured surface points.
    ///
    /// SPECIES VARIETY IS ON HERE, AND UNLIKE HAVEN'S IT IS NOT A COIN FLIP.
    /// Haven's <c>DistributedTrees(varySpecies)</c> defaults to false because
    /// cycling eight woods through one island's seats is a decision with no
    /// evidence behind it. Out here there IS evidence: the survey names the species
    /// that grew on each island, so an island recorded as cedar/elm/birch/oak grows
    /// those four and only those four. That is a reconstruction, not a preference.
    /// </summary>
    public static class ReleaseWorldTrees
    {
        /// <summary>
        /// Key prefix. Must keep the <c>tree-</c> stem - see the class remarks.
        /// </summary>
        public const string KeyPrefix = "tree-release-";

        /// <summary>The registration key for tree <paramref name="index"/> on an island.</summary>
        public static string KeyFor(string workshopId, int index) =>
            KeyPrefix + workshopId + "-" + index;

        /// <summary>
        /// Every tree on one release island, or nothing at all if the survey
        /// records none. Positions are the island's own authored seats lifted from
        /// island-local metres into world fixed point by the island's definition,
        /// the same conversion the deposits and databanks beside them use.
        ///
        /// NO SEEDED COMPONENTS, for the same reason Haven's trees carry none: the
        /// client checks the entity out and states what it needs over
        /// SEND_COMPONENT_INTEREST, and an unprompted seed list would add a second
        /// all-or-nothing batch containing our guess at that need rather than the
        /// client's own statement of it.
        ///
        /// AfterPlayer: nobody stands on a tree, and every step ordered before the
        /// player is a step the loading screen waits on.
        ///
        /// A wood with no verified prefab is SKIPPED, loudly rather than silently
        /// substituted - a wrong species is a tree that pays out the wrong wood
        /// forever, which is worse than a missing tree. With the shipped tables
        /// this cannot happen: the survey vocabulary is exactly the eight woods and
        /// all eight are verified.
        /// </summary>
        public static IEnumerable<WorldEntity> For(ReleaseIslandRecord island)
        {
            ReleaseTreeIsland? seats = ReleaseTreeCatalog.ForWorkshopId(island.Survey.WorkshopId);
            if (seats == null)
            {
                yield break;
            }

            for (int i = 0; i < seats.Points.Count; i++)
            {
                string? asset = ReleaseTreeSpecies.PrefabAt(seats.Woods, i);
                if (asset == null)
                {
                    continue;
                }

                (double x, double y, double z) = seats.Points[i];
                yield return new WorldEntity(
                    KeyFor(seats.WorkshopId, i),
                    asset,
                    WorldEntities.DefaultAssetContext,
                    island.Definition.LocalToGlobal(x, y, z),
                    seedComponents: null,
                    order: SpawnOrder.AfterPlayer);
            }
        }
    }
}

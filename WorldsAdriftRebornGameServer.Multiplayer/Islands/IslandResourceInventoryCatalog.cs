namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>World-wide totals, so a UI never re-derives a count by hand.</summary>
    public readonly record struct IslandResourceTotals(
        int Islands,
        int Deposits,
        int Databanks,
        int Trees,
        int WoodedIslands,
        int IslandsWithRecoveredOres,
        int IslandsWithInferredOres,
        int InferredDeposits,
        int IslandsWithRecoveredWoods,
        int IslandsWithInferredWoods,
        int InferredTrees);

    /// <summary>
    /// Joins the release island catalogue to the tree catalogue and rolls each
    /// island up into one countable inventory: how many databanks, how many metal
    /// deposits and of which ore, how many trees.
    ///
    /// This is pure and has no I/O of its own - it reads the same two embedded
    /// catalogues the game server seeds from, so the panel and the world can never
    /// disagree. It is also NOT gated by
    /// <see cref="ReleaseWorldRolloutPolicy"/>: it always describes all 254
    /// islands, because "what is on this island" is a fact about the preserved
    /// world, not about which districts this process happens to be simulating.
    /// Which islands are live is a separate, live question the admin page already
    /// answers from the game server's own stats.
    ///
    /// The one join in the system nobody owned before lives here:
    /// <see cref="ByMapAsset"/> takes the admin map's <c>"&lt;id&gt;.json"</c> asset
    /// string and finds the island, which is the only key the projected MapFile
    /// carries.
    /// </summary>
    public static class IslandResourceInventoryCatalog
    {
        private static readonly IReadOnlyList<IslandResourceInventory> Records = Build();

        private static readonly IReadOnlyDictionary<string, IslandResourceInventory> ByAsset =
            Records.ToDictionary(record => record.WorkshopId, StringComparer.Ordinal);

        /// <summary>Every release island, in catalogue order.</summary>
        public static IReadOnlyList<IslandResourceInventory> All => Records;

        /// <summary>World totals across <see cref="All"/>.</summary>
        public static IslandResourceTotals Totals { get; } = Sum(Records);

        /// <summary>Look up by bare workshop id, e.g. "846584820".</summary>
        public static IslandResourceInventory? ByWorkshopId(string? workshopId)
            => workshopId != null && ByAsset.TryGetValue(workshopId, out IslandResourceInventory? found)
                ? found
                : null;

        /// <summary>
        /// Look up by the admin map's asset string, e.g. "846584820.json". Haven's
        /// "1431299145.json" resolves to null on purpose: Haven is hand-tuned and
        /// is not in the release catalogue.
        /// </summary>
        public static IslandResourceInventory? ByMapAsset(string? asset)
            => ByWorkshopId(WorkshopIdFromMapAsset(asset));

        /// <summary>
        /// Strips the MapFile's ".json" suffix. Kept separate and public so the
        /// admin projection and the tests agree on exactly one spelling of the
        /// join, rather than each writing their own <c>Replace</c>.
        /// </summary>
        public static string? WorkshopIdFromMapAsset(string? asset)
        {
            if (string.IsNullOrWhiteSpace(asset)) return null;
            string trimmed = asset.Trim();
            const string suffix = ".json";
            return trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? trimmed[..^suffix.Length]
                : trimmed;
        }

        private static IReadOnlyList<IslandResourceInventory> Build()
        {
            var built = new List<IslandResourceInventory>(ReleaseWorldCatalog.All.Count);
            foreach (ReleaseIslandRecord record in ReleaseWorldCatalog.All)
            {
                ReleaseTreeIsland? trees = ReleaseTreeCatalog.ForWorkshopId(record.Survey.WorkshopId);
                built.Add(new IslandResourceInventory(
                    record,
                    trees?.Points.Count ?? 0,
                    trees?.Woods ?? Array.Empty<string>(),
                    // No record at all means the survey said "No trees" - that is the
                    // only reason an island is absent from the tree catalogue, and it
                    // is an observation, not a gap.
                    trees?.WoodSource ?? WoodTableSource.SurveyNone,
                    TallyOres(record)));
            }
            return built;
        }

        /// <summary>
        /// Counts the island's deposits by the ore they actually carry.
        ///
        /// Counted from <see cref="ReleaseIslandRecord.Deposits"/> rather than
        /// computed from the metal table's length, because the stamping rule is
        /// the generator's business and this must report what was seeded even if
        /// that rule changes. Quality comes from the deposits too; a table that
        /// listed one ore at two qualities would show as two rows, which is the
        /// truthful shape.
        /// </summary>
        private static IReadOnlyList<IslandOreTally> TallyOres(ReleaseIslandRecord record)
        {
            ResourceProvenance provenance =
                IslandResourceInventory.ProvenanceOf(record.Survey.MetalSource);
            return record.Deposits
                .GroupBy(node => (node.MetalType, node.Quality))
                .Select(group => new IslandOreTally(
                    group.Key.MetalType, group.Key.Quality, group.Count(), provenance))
                .OrderByDescending(tally => tally.Deposits)
                .ThenBy(tally => tally.Metal, StringComparer.Ordinal)
                .ToList();
        }

        private static IslandResourceTotals Sum(IReadOnlyList<IslandResourceInventory> records)
            => new(
                Islands: records.Count,
                Deposits: records.Sum(record => record.Deposits),
                Databanks: records.Sum(record => record.Databanks),
                Trees: records.Sum(record => record.Trees),
                WoodedIslands: records.Count(record => record.Trees > 0),
                IslandsWithRecoveredOres: records.Count(record => !record.OresAreInferred),
                IslandsWithInferredOres: records.Count(record => record.OresAreInferred),
                InferredDeposits: records.Where(record => record.OresAreInferred)
                    .Sum(record => record.Deposits),
                IslandsWithRecoveredWoods: records.Count(record => !record.WoodsAreInferred),
                IslandsWithInferredWoods: records.Count(record => record.WoodsAreInferred),
                InferredTrees: records.Where(record => record.WoodsAreInferred)
                    .Sum(record => record.Trees));
    }
}

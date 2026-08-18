namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// Everything the islands of ONE MapFile tier cell add up to.
    ///
    /// This exists because a zone is a thing an operator clicks, and a zone's
    /// answer must be arithmetic over the same catalogue the game server seeds
    /// from - not a number a page re-derives in JavaScript while it draws. The
    /// admin map used to sum these in the browser, which is how an abbreviated
    /// roll-up ended up stamped over the terrain: a number that is cheap to make
    /// on the drawing surface gets drawn on the drawing surface. Computing it
    /// here, once, keeps the map a map and gives the panel a value it can trust.
    ///
    /// Pure: it reads <see cref="IslandResourceInventoryCatalog"/> and does sums.
    /// No I/O, no engine types, no clock.
    /// </summary>
    public sealed class IslandCellRollup
    {
        internal IslandCellRollup(
            string cellId,
            IReadOnlyList<IslandResourceInventory> members,
            IReadOnlyList<IslandOreTally> ores,
            IReadOnlyList<string> treeSpecies)
        {
            CellId = cellId;
            Members = members;
            Ores = ores;
            TreeSpecies = treeSpecies;
        }

        /// <summary>The cell this rolls up, e.g. "A2" or "unassigned-t4-1".</summary>
        public string CellId { get; }

        /// <summary>The islands counted, in catalogue order.</summary>
        public IReadOnlyList<IslandResourceInventory> Members { get; }

        public int Islands => Members.Count;

        public int Databanks => Members.Sum(member => member.Databanks);

        public int Deposits => Members.Sum(member => member.Deposits);

        public int Trees => Members.Sum(member => member.Trees);

        public int WoodedIslands => Members.Count(member => member.Trees > 0);

        public int IslandsWithInferredOres => Members.Count(member => member.OresAreInferred);

        public int IslandsWithRecoveredOres => Members.Count(member => !member.OresAreInferred);

        /// <summary>Deposits sitting on an island whose ore table was never surveyed.</summary>
        public int InferredDeposits =>
            Members.Where(member => member.OresAreInferred).Sum(member => member.Deposits);

        /// <summary>
        /// The cell's deposits broken down by ore type and quality, richest first.
        ///
        /// A group's provenance is the WEAKEST of the rows in it: if any island
        /// contributing "Iron q4" only has an inferred table, the whole group is
        /// marked inferred. Rounding provenance the other way would let one
        /// surveyed island launder 20 composed ones, which is exactly the failure
        /// the provenance vocabulary exists to prevent.
        /// </summary>
        public IReadOnlyList<IslandOreTally> Ores { get; }

        /// <summary>Every wood species surveyed anywhere in the cell, alphabetical.</summary>
        public IReadOnlyList<string> TreeSpecies { get; }

        /// <summary>True when at least one island here carries a composed ore table.</summary>
        public bool HasInferredOres => IslandsWithInferredOres > 0;
    }

    /// <summary>
    /// The per-cell roll-ups for the whole preserved release world, built once
    /// from <see cref="IslandResourceInventoryCatalog.All"/>.
    /// </summary>
    public static class IslandCellRollupCatalog
    {
        private static readonly IReadOnlyList<IslandCellRollup> Records =
            Build(IslandResourceInventoryCatalog.All);

        private static readonly IReadOnlyDictionary<string, IslandCellRollup> ByCell =
            Records.ToDictionary(record => record.CellId, StringComparer.Ordinal);

        /// <summary>Every cell that has at least one catalogued island, cell id order.</summary>
        public static IReadOnlyList<IslandCellRollup> All => Records;

        /// <summary>
        /// The roll-up for a cell, or null when no catalogued island sits in it.
        /// Null rather than an empty roll-up: "no islands here" is a fact worth
        /// stating in words, and a zero-filled object reads like a measurement.
        /// </summary>
        public static IslandCellRollup? ForCell(string? cellId)
            => cellId != null && ByCell.TryGetValue(cellId, out IslandCellRollup? found)
                ? found
                : null;

        /// <summary>
        /// Rolls an explicit set of islands up under one cell id. Public so a test
        /// can hold the arithmetic - especially the provenance-weakening rule -
        /// against a handful of islands rather than against all 254.
        /// </summary>
        public static IslandCellRollup Aggregate(
            string cellId, IEnumerable<IslandResourceInventory> islands)
        {
            if (cellId is null) throw new ArgumentNullException(nameof(cellId));
            if (islands is null) throw new ArgumentNullException(nameof(islands));

            List<IslandResourceInventory> members = islands.ToList();

            List<IslandOreTally> ores = members
                .SelectMany(member => member.Ores)
                .GroupBy(ore => (ore.Metal, ore.Quality))
                .Select(group => new IslandOreTally(
                    group.Key.Metal,
                    group.Key.Quality,
                    group.Sum(ore => ore.Deposits),
                    group.Any(ore => ore.Provenance == ResourceProvenance.Inferred)
                        ? ResourceProvenance.Inferred
                        : ResourceProvenance.Recovered))
                .OrderByDescending(ore => ore.Deposits)
                .ThenBy(ore => ore.Metal, StringComparer.Ordinal)
                .ThenBy(ore => ore.Quality)
                .ToList();

            List<string> species = members
                .SelectMany(member => member.TreeSpecies)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(wood => wood, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new IslandCellRollup(cellId, members, ores, species);
        }

        private static IReadOnlyList<IslandCellRollup> Build(
            IReadOnlyList<IslandResourceInventory> all)
            => all.GroupBy(island => island.CellId, StringComparer.Ordinal)
                  .OrderBy(group => group.Key, StringComparer.Ordinal)
                  .Select(group => Aggregate(group.Key, group))
                  .ToList();
    }
}

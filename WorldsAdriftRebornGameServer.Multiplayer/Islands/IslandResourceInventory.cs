namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// How strongly a number is evidenced. This is the repo's provenance
    /// vocabulary, made machine-readable so a UI cannot present a guess as a fact.
    /// </summary>
    public enum ResourceProvenance
    {
        /// <summary>
        /// Counted from the preserved release MapFile or from the final Cardinal
        /// survey of that exact island. Bossa's number, not ours.
        /// </summary>
        Recovered,

        /// <summary>
        /// The island itself was never read. The value is composed from the cohort
        /// of same-tier islands that WERE read, by
        /// <c>tools/world-import/metal_inference.py</c>. Plausible, not evidence.
        /// </summary>
        Inferred,

        /// <summary>
        /// A number this project chose because retail's did not survive. Honest
        /// game design, never presented as preservation.
        /// </summary>
        WarebornTuning,
    }

    /// <summary>
    /// One ore type on one island: how many of that island's deposits carry it,
    /// and at what quality.
    ///
    /// Deposits are stamped round-robin from the island's effective metal table,
    /// so this tally is exact given that table - but the TABLE itself may be
    /// inferred. <see cref="Provenance"/> carries that distinction down to the
    /// individual ore row, because that is the level a reader makes decisions at.
    /// </summary>
    public readonly record struct IslandOreTally(
        string Metal,
        int Quality,
        int Deposits,
        ResourceProvenance Provenance);

    /// <summary>
    /// Everything one release island actually has on it, counted - never
    /// estimated - from the two embedded catalogues, with each figure's provenance
    /// attached.
    ///
    /// Every count here is a count of real seeded entities. Where a figure could
    /// not be recovered it is either absent or explicitly marked
    /// <see cref="ResourceProvenance.Inferred"/> / <see cref="ResourceProvenance.WarebornTuning"/>;
    /// nothing is estimated to fill a gap.
    /// </summary>
    public sealed class IslandResourceInventory
    {
        internal IslandResourceInventory(
            ReleaseIslandRecord record,
            int trees,
            IReadOnlyList<string> treeSpecies,
            WoodTableSource woodSource,
            IReadOnlyList<IslandOreTally> ores,
            int lootContainers)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
            Trees = trees;
            TreeSpecies = treeSpecies;
            WoodSource = woodSource;
            Ores = ores;
            LootContainers = lootContainers;
        }

        /// <summary>The joined island this inventory belongs to.</summary>
        public ReleaseIslandRecord Record { get; }

        /// <summary>Bare Steam Workshop id, e.g. "846584820". The universal join key.</summary>
        public string WorkshopId => Record.Survey.WorkshopId;

        /// <summary>The runtime island identity, e.g. "release-846584820".</summary>
        public IslandId IslandId => Record.Definition.Id;

        /// <summary>The island's authored name, e.g. "Suizo".</summary>
        public string DisplayName => Record.Definition.DisplayName;

        /// <summary>The MapFile biome cell this island sits in, e.g. "B3".</summary>
        public string CellId => Record.CellId;

        /// <summary>The tier of that cell. This is the tier the map is coloured by.</summary>
        public int CellTier => Record.CellTier;

        /// <summary>The tier the Cardinal survey recorded for the island itself.</summary>
        public int SurveyTier => Record.Survey.Tier;

        /// <summary>"saborian" or "kioki".</summary>
        public string Culture => Record.Survey.Culture;

        /// <summary>Seeded ancient databanks. Recovered: the survey counted them.</summary>
        public int Databanks => Record.Databanks.Count;

        /// <summary>Seeded mineable metal deposits.</summary>
        public int Deposits => Record.Deposits.Count;

        /// <summary>
        /// Seeded trees. Zero on exactly three islands: the two the survey records
        /// as "No trees", and Belial, whose three-sample surface has no room left
        /// once its own surveyed databanks are placed.
        /// </summary>
        public int Trees { get; }

        /// <summary>The wood species seeded here, lower-cased, in survey order.</summary>
        public IReadOnlyList<string> TreeSpecies { get; }

        /// <summary>Where the effective wood list came from.</summary>
        public WoodTableSource WoodSource { get; }

        /// <summary>True when no survey of this island's species was ever recovered.</summary>
        public bool WoodsAreInferred => WoodSource == WoodTableSource.InferredTier;

        /// <summary>
        /// The provenance every wood row on this island carries. Trees have no
        /// equivalent of the PvP shard reading, so the ladder is shorter than the
        /// ore one: the survey said it, or this project composed it.
        /// </summary>
        public ResourceProvenance WoodProvenance => ProvenanceOf(WoodSource);

        /// <summary>
        /// Fuel pods. Always 0: retail's per-island fuel-pod placements did not
        /// survive, and the ones this server seeds are hand-placed on Haven only.
        /// Stated rather than omitted so an empty column is not read as a bug.
        /// </summary>
        public int FuelPods => 0;

        /// <summary>
        /// Loot chests / lootable containers.
        ///
        /// This USED to be a hard-coded zero, on the grounds that retail carried
        /// loot in component 1244 (LootablePerAreaDataState) and 1244 did not ship.
        /// That was half right and the half it got wrong is the interesting half:
        /// the COUNTS did not ship, but the FORMULA did.
        /// <c>acs/LootablePerAreaDataVisualizer.cs:50-62</c> is retail's own clamped
        /// exponential lerp on mostly-flat surface area and
        /// <c>IslandDataBankAndLootableSpawnerVisualizer.cs</c> is the placement pass
        /// that consumed it, 20 m spacing rule and all. So unlike trees - which
        /// retail authored by hand and which are unrecoverable in principle - these
        /// are the same procedure over the same measured surfaces, with only the
        /// nineteen tuning constants replaced.
        ///
        /// Those constants are <see cref="ResourceProvenance.WarebornTuning"/>, which
        /// is what <see cref="LootProvenance"/> reports, and the number here is the
        /// length of a list the game server actually seeds from - not an estimate.
        /// </summary>
        public int LootContainers { get; }

        /// <summary>
        /// Where the container count came from. Always
        /// <see cref="ResourceProvenance.WarebornTuning"/>: the placement algorithm
        /// and the 20 m spacing are recovered, but the budget constants that decide
        /// HOW MANY are this project's, so no UI may present the number as Bossa's.
        /// </summary>
        public ResourceProvenance LootProvenance => ResourceProvenance.WarebornTuning;

        /// <summary>The island's deposits broken down by ore type, richest first.</summary>
        public IReadOnlyList<IslandOreTally> Ores { get; }

        /// <summary>Where the effective ore table came from.</summary>
        public MetalTableSource MetalSource => Record.Survey.MetalSource;

        /// <summary>True when no survey of this island's metal was ever recovered.</summary>
        public bool OresAreInferred => Record.Survey.MetalsAreInferred;

        /// <summary>The provenance every ore row on this island carries.</summary>
        public ResourceProvenance OreProvenance => ProvenanceOf(MetalSource);

        /// <summary>Whether the survey recorded a revival chamber here.</summary>
        public bool HasRevivalChamber => Record.Survey.HasRevivalChamber;

        /// <summary>Whether the survey flagged this island dangerous.</summary>
        public bool Dangerous => Record.Survey.Dangerous;

        /// <summary>Whether the survey recorded turrets here.</summary>
        public bool HasTurrets => Record.Survey.HasTurrets;

        /// <summary>
        /// Deposits + databanks + trees + loot containers: everything a player can
        /// work. Containers joined this sum when they became real; an island's
        /// "worth landing on" number would otherwise silently ignore its loot.
        /// </summary>
        public int TotalResources => Deposits + Databanks + Trees + LootContainers;

        /// <summary>
        /// The provenance of a metal table, by the rung
        /// <c>metal_inference.py</c> recorded for it. A PvP reading is still a
        /// reading OF THAT ISLAND, one ruleset removed - so it is recovered, not
        /// inferred - and the source enum keeps the distinction for anyone who
        /// needs the finer grain.
        /// </summary>
        public static ResourceProvenance ProvenanceOf(MetalTableSource source) => source switch
        {
            MetalTableSource.SurveyPve => ResourceProvenance.Recovered,
            MetalTableSource.SurveyPvp => ResourceProvenance.Recovered,
            MetalTableSource.InferredTier => ResourceProvenance.Inferred,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };

        /// <summary>The short label a UI shows for a metal table's rung.</summary>
        public static string LabelOf(MetalTableSource source) => source switch
        {
            MetalTableSource.SurveyPve => "RECOVERED (PvE survey)",
            MetalTableSource.SurveyPvp => "RECOVERED (PvP survey)",
            MetalTableSource.InferredTier => "INFERRED (tier cohort)",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };

        /// <summary>
        /// The provenance of a wood list, by the rung <c>wood_inference.py</c>
        /// recorded for it. "No trees" is an OBSERVATION of that island and is
        /// therefore recovered, exactly as a surveyed species list is.
        /// </summary>
        public static ResourceProvenance ProvenanceOf(WoodTableSource source) => source switch
        {
            WoodTableSource.Survey => ResourceProvenance.Recovered,
            WoodTableSource.SurveyNone => ResourceProvenance.Recovered,
            WoodTableSource.InferredTier => ResourceProvenance.Inferred,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };

        /// <summary>The short label a UI shows for a wood list's rung.</summary>
        public static string LabelOf(WoodTableSource source) => source switch
        {
            WoodTableSource.Survey => "RECOVERED (species survey)",
            WoodTableSource.SurveyNone => "RECOVERED (surveyed treeless)",
            WoodTableSource.InferredTier => "INFERRED (tier cohort)",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };
    }
}

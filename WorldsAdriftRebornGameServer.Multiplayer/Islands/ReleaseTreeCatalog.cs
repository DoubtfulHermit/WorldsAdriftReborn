using System.Reflection;
using System.Text.Json;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// Where an island's wood species came from - the exact counterpart of
    /// <see cref="MetalTableSource"/>, and for the exact same reason: a species
    /// this project composed must never be readable as a species Bossa authored.
    /// </summary>
    public enum WoodTableSource
    {
        /// <summary>
        /// The Cardinal Guild survey recorded this island's own species list.
        /// 72 of 254 islands.
        /// </summary>
        Survey,

        /// <summary>
        /// The survey recorded the literal value "No trees" for this island - a
        /// statement of absence, not a gap. Honoured: no seats are authored at
        /// all, so no island in this state ever appears in the catalogue. 2 of
        /// 254 islands, both tier 4.
        /// </summary>
        SurveyNone,

        /// <summary>
        /// The island's <c>trees</c> array is EMPTY, which means nobody surveyed
        /// it. Composed by <c>tools/world-import/wood_inference.py</c> from the
        /// tier cohort that WAS surveyed. NOT Bossa data. 180 of 254 islands.
        /// </summary>
        InferredTier,
    }

    /// <summary>
    /// One island's authored tree seats: island-local metres, plus the woods those
    /// seats cycle through and where that species list came from.
    /// </summary>
    public sealed class ReleaseTreeIsland
    {
        internal ReleaseTreeIsland(string workshopId, string name, int lod0Cells,
            IReadOnlyList<string> woods, WoodTableSource woodSource,
            IReadOnlyList<(double X, double Y, double Z)> points)
        {
            WorkshopId = workshopId;
            Name = name;
            Lod0Cells = lod0Cells;
            Woods = woods;
            WoodSource = woodSource;
            Points = points;
        }

        /// <summary>Steam Workshop id, the join key against the runtime catalogue.</summary>
        public string WorkshopId { get; }

        public string Name { get; }

        /// <summary>LOD0 cell count of the extracted surface, carried so
        /// <see cref="ReleaseTreeBudget"/> can be re-derived and asserted.</summary>
        public int Lod0Cells { get; }

        /// <summary>The island's woods, lower-cased, in survey (or inferred) order.</summary>
        public IReadOnlyList<string> Woods { get; }

        /// <summary>Where <see cref="Woods"/> came from. Never guess from the list.</summary>
        public WoodTableSource WoodSource { get; }

        /// <summary>True when no survey of this island's species was ever recovered.</summary>
        public bool WoodsAreInferred => WoodSource == WoodTableSource.InferredTier;

        /// <summary>Tree seats in island-local metres.</summary>
        public IReadOnlyList<(double X, double Y, double Z)> Points { get; }
    }

    /// <summary>
    /// THE AUTHORED TREE SEATS FOR THE RELEASE WORLD - 13,266 of them across 252
    /// of the 254 islands. Every island except the two the survey explicitly calls
    /// treeless has wood on it.
    ///
    /// IT USED TO BE 3,767 ACROSS 72 ISLANDS, AND THAT WAS A BUG. The generator
    /// read the survey's `trees` array and skipped any island whose array was
    /// EMPTY - but an empty array is an island nobody surveyed for trees, not an
    /// island without trees. The evidence is set out in
    /// <c>tools/world-import/wood_inference.py</c> and in
    /// docs/research/findings-island-resource-population.md; it is the same
    /// survey-coverage gap that had left 216 islands with no ore, one field over.
    /// The symptom was a player graduating from the Wilderness shrine onto Mount
    /// Spero - tier 1, 275 LOD0 cells - and finding nothing to chop, along with 31
    /// other tier-1 islands.
    ///
    /// The two islands the survey records as "No trees" (Desert University and The
    /// Carcass, both tier 4) are honoured and carry no record here at all.
    ///
    /// WHY THESE ARE SHIPPED RATHER THAN COMPUTED AT BOOT. The rules that produced
    /// them need each island's full extracted collision surface, and that is 45 MB
    /// across 255 files. Only Haven's and the Trades Challenge's surfaces are
    /// embedded in this assembly; the rest exist under docs/research/world-data/
    /// for offline use. So the placement pass runs offline in
    /// tools/world-import/generate-release-tree-placements.py and its 80 KB result
    /// is embedded - exactly the arrangement the deposit and databank points
    /// already use in release-runtime-catalog.json.
    ///
    /// WHY IT IS A SEPARATE FILE FROM THAT CATALOGUE. Deliberate separability. The
    /// runtime catalogue is a shared, invariant-bearing artefact (254 islands, 20
    /// cells, 1,930 deposits, 1,233 databanks - all asserted by tests and all being
    /// worked on elsewhere). Regenerating it to add a tree array would churn every
    /// one of those bytes for content that is orthogonal to them. A new file adds
    /// trees without touching a single number anyone else depends on.
    ///
    /// WHAT IS EVIDENCE HERE, PRECISELY.
    ///
    ///   RECOVERED - which species an island grows, on the 72 islands the survey
    ///   recorded one for, and the fact that two islands had none.
    ///
    ///   RECOVERED - where a seat may be. That island's TRS-correct extracted
    ///   surface, filtered by the same upward-normal (0.94) and spacing (15 m)
    ///   rules that produced Haven's working 80, with the island's existing
    ///   deposits and databanks passed in as occupied so no tree grows through one.
    ///   Every coordinate in this file is a measured LOD0 vertex; nothing is
    ///   nudged, offset or synthesised.
    ///
    ///   RECOVERED - which prefab a species becomes. <see cref="ReleaseTreeSpecies"/>.
    ///
    ///   INFERRED - which species the other 180 islands grow. Composed from the
    ///   tier cohort by <c>tools/world-import/wood_inference.py</c> and stamped
    ///   <see cref="WoodTableSource.InferredTier"/> on every island it touches.
    ///
    ///   WAREBORN TUNING - the per-island count (<see cref="ReleaseTreeBudget"/>),
    ///   and the rule that an island's first four seats are drawn from within 60 m
    ///   of its arrival pad so a graduating player lands in sight of wood. The
    ///   latter changes WHICH measured samples win, never what a sample is.
    /// </summary>
    public static class ReleaseTreeCatalog
    {
        private const string ResourceSuffix = "release-tree-placements.json";
        private static readonly IReadOnlyList<ReleaseTreeIsland> Records = Load();
        private static readonly IReadOnlyDictionary<string, ReleaseTreeIsland> ByWorkshopId =
            Records.ToDictionary(record => record.WorkshopId, StringComparer.Ordinal);

        /// <summary>Every wooded island, in catalogue order.</summary>
        public static IReadOnlyList<ReleaseTreeIsland> All => Records;

        /// <summary>Total authored seats across the whole release world.</summary>
        public static int TotalTrees => Records.Sum(record => record.Points.Count);

        /// <summary>
        /// The tree seats for an island, or null when no seats were authored for
        /// it at all. Null is now the RARE case - the two islands the survey calls
        /// "No trees" - and one further island, Belial, carries a record with zero
        /// seats because its extracted surface is three samples wide and its own
        /// surveyed databanks already occupy all of them.
        /// </summary>
        public static ReleaseTreeIsland? ForWorkshopId(string? workshopId) =>
            workshopId != null && ByWorkshopId.TryGetValue(workshopId, out ReleaseTreeIsland? record)
                ? record
                : null;

        private static IReadOnlyList<ReleaseTreeIsland> Load()
        {
            Assembly assembly = typeof(ReleaseTreeCatalog).Assembly;
            string resource = assembly.GetManifestResourceNames().Single(name =>
                name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using JsonDocument document = JsonDocument.Parse(stream);

            List<ReleaseTreeIsland> records = new();
            foreach (JsonElement item in document.RootElement.GetProperty("islands").EnumerateArray())
            {
                List<string> woods = item.GetProperty("woods").EnumerateArray()
                    .Select(wood => wood.GetString()!)
                    .ToList();
                List<(double, double, double)> points = item.GetProperty("points").EnumerateArray()
                    .Select(point => (point[0].GetDouble(), point[1].GetDouble(), point[2].GetDouble()))
                    .ToList();
                records.Add(new ReleaseTreeIsland(
                    item.GetProperty("asset").GetString()!,
                    item.GetProperty("name").GetString()!,
                    item.GetProperty("cells").GetInt32(),
                    woods,
                    ParseWoodSource(item.GetProperty("woodSource").GetString()),
                    points));
            }

            return records;
        }

        /// <summary>
        /// Throws on an unknown value rather than defaulting to
        /// <see cref="WoodTableSource.Survey"/>. A silent default here would
        /// relabel an inference as evidence, which is the one failure mode the
        /// provenance vocabulary exists to prevent.
        /// </summary>
        private static WoodTableSource ParseWoodSource(string? value) => value switch
        {
            "survey" => WoodTableSource.Survey,
            "survey-none" => WoodTableSource.SurveyNone,
            "inferred-tier" => WoodTableSource.InferredTier,
            _ => throw new InvalidDataException(
                "release-tree-placements.json carries an unknown woodSource: " + (value ?? "<null>")),
        };
    }
}

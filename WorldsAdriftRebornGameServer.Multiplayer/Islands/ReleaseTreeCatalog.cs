using System.Reflection;
using System.Text.Json;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// One island's authored tree seats: island-local metres, plus the surveyed
    /// woods those seats cycle through.
    /// </summary>
    public sealed class ReleaseTreeIsland
    {
        internal ReleaseTreeIsland(string workshopId, string name, int lod0Cells,
            IReadOnlyList<string> woods, IReadOnlyList<(double X, double Y, double Z)> points)
        {
            WorkshopId = workshopId;
            Name = name;
            Lod0Cells = lod0Cells;
            Woods = woods;
            Points = points;
        }

        /// <summary>Steam Workshop id, the join key against the runtime catalogue.</summary>
        public string WorkshopId { get; }

        public string Name { get; }

        /// <summary>LOD0 cell count of the extracted surface, carried so
        /// <see cref="ReleaseTreeBudget"/> can be re-derived and asserted.</summary>
        public int Lod0Cells { get; }

        /// <summary>The island's surveyed woods, lower-cased, in survey order.</summary>
        public IReadOnlyList<string> Woods { get; }

        /// <summary>Tree seats in island-local metres.</summary>
        public IReadOnlyList<(double X, double Y, double Z)> Points { get; }
    }

    /// <summary>
    /// THE AUTHORED TREE SEATS FOR THE RELEASE WORLD - 3,767 of them across the 72
    /// islands the Cardinal Guild survey records as wooded.
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
    /// cells, 354 deposits, 1,233 databanks - all asserted by tests and all being
    /// worked on elsewhere). Regenerating it to add a tree array would churn every
    /// one of those bytes for content that is orthogonal to them. A new file adds
    /// trees without touching a single number anyone else depends on.
    ///
    /// WHAT IS EVIDENCE HERE, PRECISELY. Which islands are wooded and with which
    /// species: the survey. Where a seat may be: that island's TRS-correct
    /// extracted surface, filtered by the same upward-normal (0.94) and spacing
    /// (15 m) rules that produced Haven's working 80, with the island's existing
    /// deposits and databanks passed in as occupied so no tree grows through one.
    /// Which prefab a species becomes: <see cref="ReleaseTreeSpecies"/>. The single
    /// calibrated number is the per-island count, and <see cref="ReleaseTreeBudget"/>
    /// carries that admission.
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
        /// The tree seats for an island, or null if the survey records no trees on
        /// it. Null is the common case - 182 of the 254 islands are treeless, and
        /// two of those say so explicitly ("No trees") rather than by omission.
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
                    points));
            }

            return records;
        }
    }
}

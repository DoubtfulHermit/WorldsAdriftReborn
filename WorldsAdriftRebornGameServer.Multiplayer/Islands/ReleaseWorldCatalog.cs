using System.Reflection;
using System.Text.Json;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// The one place on an island a player may be TELEPORTED onto: an evidenced
    /// LOD0 collision-surface sample, in ISLAND-LOCAL metres, with the numbers it
    /// was chosen by.
    ///
    /// This is a different question from where a resource may sit, which is why it
    /// is a different field: a deposit is allowed to cling to a slope, a player
    /// arriving out of a loading screen is not. The server has no terrain query -
    /// see <see cref="IslandLocationPolicy"/> and <see cref="PlayerPositionPolicy"/>,
    /// which say so for the same reason - so landing on a MEASURED sample with
    /// measured neighbours is the strongest claim it can make about solid ground.
    ///
    /// Derived offline by tools/world-import/generate-release-runtime-catalog.py
    /// from docs/research/world-data/island-surfaces/&lt;asset&gt;.json, deterministically
    /// and with no RNG, so the pad never moves between regenerations.
    /// </summary>
    /// <param name="LocalX">Island-local metres, east.</param>
    /// <param name="LocalY">Island-local metres, up. The SURFACE height; the
    /// player stand-off is added at the point of use, not baked in.</param>
    /// <param name="LocalZ">Island-local metres, north.</param>
    /// <param name="UpwardNormal">The sample's own <c>ny</c>. 1.0 is dead flat.</param>
    /// <param name="SupportingColumns">How many neighbouring 8 m columns within
    /// 12 m sit within the ladder's step tolerance of this one. 8 means every
    /// cardinal and diagonal neighbour is level: a plateau, not a spire.</param>
    /// <param name="WorstStepMetres">The largest height difference to any of those
    /// neighbours.</param>
    /// <param name="Reviewed">True when this coordinate was derived and reviewed
    /// by hand BEFORE the generator existed and is pinned so the generated field
    /// cannot contradict a point the server already names elsewhere.</param>
    public readonly record struct IslandLandingPoint(
        double LocalX,
        double LocalY,
        double LocalZ,
        double UpwardNormal,
        int SupportingColumns,
        double WorstStepMetres,
        bool Reviewed);

    /// <summary>
    /// One ordinary release-world island joined across Bossa's MapFile, the final
    /// Cardinal survey, and the extracted TRS-correct collision surface.
    /// </summary>
    public sealed class ReleaseIslandRecord
    {
        internal ReleaseIslandRecord(IslandDefinition definition, string cellId, int cellTier,
            IslandTerrainEnvelope envelope, IslandSurveyProfile survey,
            IReadOnlyList<IslandShellPoint> shell,
            IReadOnlyList<MetalNode> deposits,
            IReadOnlyList<FixedPointPosition> databanks,
            IslandLandingPoint landing)
        {
            Definition = definition;
            CellId = cellId;
            CellTier = cellTier;
            Envelope = envelope;
            Survey = survey;
            Shell = shell;
            Deposits = deposits;
            Databanks = databanks;
            Landing = landing;
        }

        public IslandDefinition Definition { get; }
        public string CellId { get; }
        public int CellTier { get; }
        public IslandTerrainEnvelope Envelope { get; }
        public IslandSurveyProfile Survey { get; }
        public IReadOnlyList<IslandShellPoint> Shell { get; }
        public IReadOnlyList<MetalNode> Deposits { get; }
        public IReadOnlyList<FixedPointPosition> Databanks { get; }

        /// <summary>Where a teleport may put a player on this island.</summary>
        public IslandLandingPoint Landing { get; }
    }

    /// <summary>
    /// Complete active release-world catalogue: 254 ordinary islands. Haven is
    /// intentionally separate because the MapFile contains twelve reserve Haven
    /// placements and only instance #5 is the active starter terrain.
    /// </summary>
    public static class ReleaseWorldCatalog
    {
        private const string ResourceSuffix = "release-runtime-catalog.json";
        private static readonly IReadOnlyList<ReleaseIslandRecord> Records = Load();
        private static readonly IReadOnlyDictionary<IslandId, ReleaseIslandRecord> ById =
            Records.ToDictionary(record => record.Definition.Id);
        private static readonly IReadOnlyDictionary<string, MetalNode> DepositsByKey =
            Records.SelectMany(record => record.Deposits)
                .ToDictionary(node => node.Key, StringComparer.Ordinal);

        public static IReadOnlyList<ReleaseIslandRecord> All => Records;
        public static ReleaseIslandRecord? ByIsland(IslandId id) =>
            ById.TryGetValue(id, out ReleaseIslandRecord? record) ? record : null;
        public static ReleaseIslandRecord Require(IslandId id) => ByIsland(id)
            ?? throw new KeyNotFoundException("no release-world record exists for island '" + id + "'");
        public static MetalNode? DepositByKey(string? key) => key != null
            && DepositsByKey.TryGetValue(key, out MetalNode? node) ? node : null;

        private static IReadOnlyList<ReleaseIslandRecord> Load()
        {
            Assembly assembly = typeof(ReleaseWorldCatalog).Assembly;
            string resource = assembly.GetManifestResourceNames().Single(name =>
                name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using JsonDocument document = JsonDocument.Parse(stream);
            List<ReleaseIslandRecord> records = new();
            foreach (JsonElement item in document.RootElement.GetProperty("islands").EnumerateArray())
            {
                string asset = item.GetProperty("asset").GetString()!;
                IslandDefinition definition = KnownDefinition(asset) ?? new IslandDefinition(
                    new IslandId("release-" + asset),
                    item.GetProperty("name").GetString()!,
                    "island-release-" + asset,
                    FixedPointPosition.FromMetres(
                        item.GetProperty("x").GetDouble(),
                        item.GetProperty("y").GetDouble(),
                        item.GetProperty("z").GetDouble()),
                    asset + "@Island",
                    IslandCatalog.DefaultTerrainAssetContext,
                    SpawnOrder.AfterPlayer);
                JsonElement box = item.GetProperty("aabb");
                IslandTerrainEnvelope envelope = new(definition.Id,
                    box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(),
                    box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble());
                string cell = item.GetProperty("cell").GetString()!;
                IslandSurveyProfile survey = new(
                    definition.Id, asset, item.GetProperty("tier").GetInt32(),
                    item.GetProperty("culture").GetString()!, cell,
                    item.GetProperty("databanks").GetInt32(),
                    item.GetProperty("revival").GetBoolean(),
                    item.GetProperty("dangerous").GetBoolean(),
                    item.GetProperty("turrets").GetBoolean(),
                    Strings(item.GetProperty("trees")),
                    Metals(item.GetProperty("pveMetals")),
                    Metals(item.GetProperty("pvpMetals")),
                    Metals(item.GetProperty("metals")),
                    SourceOf(item.GetProperty("metalSource").GetString()));
                List<IslandShellPoint> shell = item.GetProperty("shell").EnumerateArray()
                    .Select(point => new IslandShellPoint(point[0].GetDouble(), point[1].GetDouble()))
                    .ToList();
                List<MetalNode> deposits = new();
                int depositIndex = 0;
                foreach (JsonElement point in item.GetProperty("deposits").EnumerateArray())
                {
                    deposits.Add(new MetalNode(
                        "deposit-release-" + asset + "-" + depositIndex,
                        point.GetProperty("metal").GetString()!.ToLowerInvariant(),
                        point.GetProperty("quality").GetInt32(),
                        definition.LocalToGlobal(point.GetProperty("x").GetDouble(),
                            point.GetProperty("y").GetDouble(), point.GetProperty("z").GetDouble()),
                        isDeposit: true,
                        variantId: MetalDeposits.VariantIdFor(depositIndex++)));
                }
                List<FixedPointPosition> databanks = item.GetProperty("databankPoints")
                    .EnumerateArray().Select(point => definition.LocalToGlobal(
                        point.GetProperty("x").GetDouble(), point.GetProperty("y").GetDouble(),
                        point.GetProperty("z").GetDouble())).ToList();
                JsonElement pad = item.GetProperty("landing");
                IslandLandingPoint landing = new(
                    pad.GetProperty("x").GetDouble(),
                    pad.GetProperty("y").GetDouble(),
                    pad.GetProperty("z").GetDouble(),
                    pad.GetProperty("ny").GetDouble(),
                    pad.GetProperty("support").GetInt32(),
                    pad.GetProperty("step").GetDouble(),
                    pad.GetProperty("reviewed").GetBoolean());
                records.Add(new ReleaseIslandRecord(definition, cell,
                    item.GetProperty("cellTier").GetInt32(), envelope, survey,
                    shell.AsReadOnly(), deposits.AsReadOnly(), databanks.AsReadOnly(),
                    landing));
            }
            return records.AsReadOnly();
        }

        private static IEnumerable<string> Strings(JsonElement values) =>
            values.EnumerateArray().Select(value => value.GetString()!);
        /// <summary>
        /// The catalogue's provenance string. An unrecognised value THROWS rather
        /// than defaulting: a silent fallback to SurveyPve would relabel inferred
        /// metals as Bossa data, which is the exact confusion the field exists to
        /// prevent.
        /// </summary>
        private static MetalTableSource SourceOf(string? source) => source switch
        {
            "survey-pve" => MetalTableSource.SurveyPve,
            "survey-pvp" => MetalTableSource.SurveyPvp,
            "inferred-tier" => MetalTableSource.InferredTier,
            _ => throw new InvalidDataException(
                "release catalogue states an unknown metalSource '" + source + "'"),
        };

        private static IEnumerable<SurveyedMetal> Metals(JsonElement values) =>
            values.EnumerateArray().Select(value => new SurveyedMetal(
                value.GetProperty("name").GetString()!, value.GetProperty("quality").GetInt32()));

        private static IslandDefinition? KnownDefinition(string asset) => asset switch
        {
            "1206286558" => IslandCatalog.TradesChallenge,
            "650186469" => IslandCatalog.AnchorageIsle,
            "1673355094" => IslandCatalog.OldMilitaryAcademy,
            "949069116" => IslandCatalog.ShatteredMausoleum,
            "1143725558" => IslandCatalog.MentalFacility,
            "950242829" => IslandCatalog.BetrayalCopperKing,
            "1206946500" => IslandCatalog.HighlandsHills,
            "942473835" => IslandCatalog.LandManForgot,
            "924807150" => IslandCatalog.DrunkRavenInn,
            "742077672" => IslandCatalog.BeautifulWildlands,
            "1129983108" => IslandCatalog.TheThree,
            "1483206813" => IslandCatalog.RoxboroughIsle,
            "1319380815" => IslandCatalog.CampsDaurats,
            "1675054039" => IslandCatalog.TriphalionCity,
            "966489234" => IslandCatalog.SplitpeakPass,
            "938282702" => IslandCatalog.CrimsonParadise,
            _ => null,
        };
    }
}

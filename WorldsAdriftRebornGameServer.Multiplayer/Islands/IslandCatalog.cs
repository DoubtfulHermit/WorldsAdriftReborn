namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>The evidenced island definitions shipped by this server.</summary>
    public static class IslandCatalog
    {
        public const string HavenTerrainAssetName = "1431299145@Island";
        public const string DefaultTerrainAssetContext = "notNeeded?";
        public static readonly IslandId HavenId = new IslandId("haven");
        public static readonly IslandId TradesChallengeId = new IslandId("the-trades-challenge");
        public static readonly IslandId AnchorageIsleId = new IslandId("anchorage-isle");
        public static readonly IslandId OldMilitaryAcademyId =
            new IslandId("the-old-military-academy");
        public static readonly IslandId ShatteredMausoleumId =
            new IslandId("shattered-mausoleum");

        /// <summary>Haven instance #5 from the preserved Bossa WAMap.</summary>
        public static readonly IslandDefinition Haven = new IslandDefinition(
            HavenId,
            "Haven",
            EntityIdAllocator.IslandKey,
            new FixedPointPosition(69650145, -1305269, -4645549),
            HavenTerrainAssetName,
            DefaultTerrainAssetContext,
            SpawnOrder.BeforePlayer);

        /// <summary>
        /// The closest distinct release-world island to the active Haven instance.
        /// Position and asset are from the preserved Bossa MapFile; the matching
        /// client bundle and extracted collision surface are both present locally.
        /// </summary>
        public static readonly IslandDefinition TradesChallenge = new IslandDefinition(
            TradesChallengeId,
            "The Trades Challenge",
            "island-the-trades-challenge",
            new FixedPointPosition(54286560, -791844, -8077469),
            "1206286558@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        /// <summary>The next release-world C6 terrain candidate after The Trades Challenge.</summary>
        public static readonly IslandDefinition AnchorageIsle = new IslandDefinition(
            AnchorageIsleId,
            "Anchorage Isle",
            "island-anchorage-isle",
            new FixedPointPosition(53748326, -1229240, 1475919),
            "650186469@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        /// <summary>The release-world C6 island north of Anchorage Isle.</summary>
        public static readonly IslandDefinition OldMilitaryAcademy = new IslandDefinition(
            OldMilitaryAcademyId,
            "The Old Military Academy",
            "island-the-old-military-academy",
            new FixedPointPosition(58796532, 277533, 7307445),
            "1673355094@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        /// <summary>The southern release-world C6 terrain candidate.</summary>
        public static readonly IslandDefinition ShatteredMausoleum = new IslandDefinition(
            ShatteredMausoleumId,
            "Shattered Mausoleum",
            "island-shattered-mausoleum",
            new FixedPointPosition(58660618, -2158603, -19035735),
            "949069116@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        /// <summary>
        /// The evidenced first-region terrain in rollout order. Haven is the required
        /// loading-barrier terrain; the remaining four entries are optional
        /// <see cref="SpawnOrder.AfterPlayer"/> candidates selected as a prefix.
        /// </summary>
        public static readonly IReadOnlyList<IslandDefinition> FirstRegionTerrain =
            Array.AsReadOnly(new[]
            {
                Haven,
                TradesChallenge,
                AnchorageIsle,
                OldMilitaryAcademy,
                ShatteredMausoleum,
            });
    }
}

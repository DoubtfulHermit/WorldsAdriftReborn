namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>The evidenced island definitions shipped by this server.</summary>
    public static class IslandCatalog
    {
        public const string HavenTerrainAssetName = "1431299145@Island";
        public const string DefaultTerrainAssetContext = "notNeeded?";
        public static readonly IslandId HavenId = new IslandId("haven");
        public static readonly IslandId TradesChallengeId = new IslandId("the-trades-challenge");

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
    }
}

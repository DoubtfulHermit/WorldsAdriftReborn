using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer.Regions
{
    /// <summary>The first stable regions, derived only from registered islands.</summary>
    public static class RegionCatalog
    {
        public static readonly RegionId HavenRegionId = new RegionId("haven-region");
        public static readonly RegionId TradesChallengeRegionId =
            new RegionId("the-trades-challenge-region");
        public static readonly RegionId FirstTierOneRegionId = new RegionId("tier1-b3-region");

        public static readonly RegionDefinition Haven = new RegionDefinition(
            HavenRegionId,
            "Haven Region",
            new[] { IslandCatalog.HavenId });

        public static readonly RegionDefinition TradesChallenge = new RegionDefinition(
            TradesChallengeRegionId,
            "The Trades Challenge Region",
            new[] { IslandCatalog.TradesChallengeId });

        /// <summary>
        /// Builds the opt-in first tier-1 B3 region for an evidenced terrain prefix.
        /// This is deliberately separate from the production default topology.
        /// </summary>
        public static RegionDefinition FirstTierOne(IEnumerable<IslandId> islandIds) =>
            new RegionDefinition(FirstTierOneRegionId, "Tier 1 B3 Region", islandIds);
    }
}

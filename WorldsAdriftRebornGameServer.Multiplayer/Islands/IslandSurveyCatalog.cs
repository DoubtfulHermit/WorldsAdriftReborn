namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>Evidence-backed gameplay metadata for the complete tier-1 B3 district.</summary>
    public static class IslandSurveyCatalog
    {
        private static IslandSurveyProfile Profile(
            IslandDefinition island,
            int databanks,
            bool revival,
            bool turrets,
            string[] trees,
            SurveyedMetal[]? pveMetals = null) =>
            new IslandSurveyProfile(
                island.Id,
                island.TerrainAssetName.Split('@')[0],
                tier: 1,
                culture: "Saborian",
                district: "B3",
                databankCount: databanks,
                hasRevivalChamber: revival,
                dangerous: false,
                hasTurrets: turrets,
                trees: trees,
                pveMetals: pveMetals);

        /// <summary>
        /// Profiles follow the terrain rollout order. Empty metal lists mean the
        /// survey recorded no table; they must never be interpreted as permission
        /// to invent a generic metal population.
        /// </summary>
        public static readonly IReadOnlyList<IslandSurveyProfile> FirstTierOneB3 =
            Array.AsReadOnly(new[]
            {
                Profile(IslandCatalog.MentalFacility, 5, revival: true, turrets: true,
                    trees: new[] { "Elm" }),
                Profile(IslandCatalog.BetrayalCopperKing, 4, revival: true, turrets: false,
                    trees: new[] { "Birch" }),
                Profile(IslandCatalog.HighlandsHills, 3, revival: true, turrets: false,
                    trees: new[] { "Elm", "Birch" }),
                Profile(IslandCatalog.LandManForgot, 4, revival: true, turrets: false,
                    trees: new[] { "Oak" }),
                Profile(IslandCatalog.DrunkRavenInn, 5, revival: false, turrets: false,
                    trees: new[] { "Chestnut" }),
                Profile(IslandCatalog.BeautifulWildlands, 5, revival: false, turrets: false,
                    trees: Array.Empty<string>()),
                Profile(IslandCatalog.TheThree, 4, revival: false, turrets: false,
                    trees: new[] { "Chestnut", "Elm", "Ash" }),
                Profile(IslandCatalog.RoxboroughIsle, 5, revival: false, turrets: false,
                    trees: Array.Empty<string>()),
                Profile(IslandCatalog.CampsDaurats, 4, revival: true, turrets: false,
                    trees: new[] { "Chestnut", "Birch" }),
                Profile(IslandCatalog.TriphalionCity, 5, revival: false, turrets: false,
                    trees: Array.Empty<string>()),
                Profile(IslandCatalog.SplitpeakPass, 5, revival: true, turrets: false,
                    trees: new[] { "Elm", "Birch", "Oak" }),
                Profile(IslandCatalog.CrimsonParadise, 5, revival: true, turrets: false,
                    trees: new[] { "Chestnut", "Palm" },
                    pveMetals: new[] { new SurveyedMetal("Iron", 3) }),
            });

        private static readonly IReadOnlyDictionary<IslandId, IslandSurveyProfile> ById =
            FirstTierOneB3.ToDictionary(profile => profile.IslandId);

        public static IslandSurveyProfile? ByIsland(IslandId islandId) =>
            ById.TryGetValue(islandId, out IslandSurveyProfile? profile) ? profile : null;

        public static IslandSurveyProfile Require(IslandId islandId) =>
            ByIsland(islandId)
            ?? throw new KeyNotFoundException("no survey profile exists for island '" + islandId + "'");
    }
}

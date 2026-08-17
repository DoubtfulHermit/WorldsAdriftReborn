using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    public sealed class IslandSurveyCatalogTests
    {
        [Fact]
        public void Complete_B3_survey_matches_the_optional_terrain_catalogue()
        {
            IReadOnlyList<IslandDefinition> terrain =
                IslandCatalog.FirstRegionTerrain.Skip(1).ToArray();

            Assert.Equal(12, terrain.Count);
            Assert.Equal(terrain.Select(island => island.Id),
                IslandSurveyCatalog.FirstTierOneB3.Select(profile => profile.IslandId));
            Assert.All(IslandSurveyCatalog.FirstTierOneB3, profile =>
            {
                Assert.Equal(1, profile.Tier);
                Assert.Equal("Saborian", profile.Culture);
                Assert.Equal("B3", profile.District);
                Assert.False(profile.Dangerous);
                Assert.Equal(
                    profile.WorkshopId + "@Island",
                    IslandCatalog.FirstRegionTerrain.Single(
                        island => island.Id == profile.IslandId).TerrainAssetName);
            });
        }

        [Theory]
        [InlineData("mental-facility", 5, true, true, "Elm")]
        [InlineData("betrayal-of-the-copper-king", 4, true, false, "Birch")]
        [InlineData("highlands-hills", 3, true, false, "Elm|Birch")]
        [InlineData("the-land-that-man-forgot", 4, true, false, "Oak")]
        [InlineData("drunkraven-inn", 5, false, false, "Chestnut")]
        [InlineData("beautiful-wildlands", 5, false, false, "")]
        [InlineData("the-three", 4, false, false, "Chestnut|Elm|Ash")]
        [InlineData("roxborough-isle", 5, false, false, "")]
        [InlineData("camps-daurats", 4, true, false, "Chestnut|Birch")]
        [InlineData("triphalion-city", 5, false, false, "")]
        [InlineData("splitpeak-pass", 5, true, false, "Elm|Birch|Oak")]
        [InlineData("crimson-paradise", 5, true, false, "Chestnut|Palm")]
        public void Surveyed_databanks_revival_turrets_and_trees_are_pinned(
            string islandId,
            int databanks,
            bool revival,
            bool turrets,
            string trees)
        {
            IslandSurveyProfile profile =
                IslandSurveyCatalog.Require(new IslandId(islandId));

            Assert.Equal(databanks, profile.DatabankCount);
            Assert.Equal(revival, profile.HasRevivalChamber);
            Assert.Equal(turrets, profile.HasTurrets);
            Assert.Equal(trees, string.Join('|', profile.Trees));
        }

        [Fact]
        public void Empty_metal_tables_remain_empty_and_are_not_generic_populations()
        {
            foreach (IslandSurveyProfile profile in IslandSurveyCatalog.FirstTierOneB3
                .Where(profile => profile.IslandId != IslandCatalog.CrimsonParadiseId))
            {
                Assert.Empty(profile.PveMetals);
                Assert.Empty(profile.PvpMetals);
            }

            IslandSurveyProfile crimson =
                IslandSurveyCatalog.Require(IslandCatalog.CrimsonParadiseId);
            SurveyedMetal metal = Assert.Single(crimson.PveMetals);
            Assert.Equal("Iron", metal.Name);
            Assert.Equal(3, metal.Quality);
            Assert.Empty(crimson.PvpMetals);
        }

        [Fact]
        public void Survey_catalogue_rejects_unrelated_islands()
        {
            Assert.Null(IslandSurveyCatalog.ByIsland(IslandCatalog.HavenId));
            Assert.Null(IslandSurveyCatalog.ByIsland(IslandCatalog.TradesChallengeId));
            Assert.Throws<KeyNotFoundException>(() =>
                IslandSurveyCatalog.Require(IslandCatalog.HavenId));
        }
    }
}

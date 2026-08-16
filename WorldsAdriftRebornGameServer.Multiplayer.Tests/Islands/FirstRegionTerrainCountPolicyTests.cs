using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    public sealed class FirstRegionTerrainCountPolicyTests
    {
        [Theory]
        [InlineData(-10, 0)]
        [InlineData(-1, 0)]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(4, 4)]
        [InlineData(5, 4)]
        [InlineData(100, 4)]
        public void Clamp_bounds_optional_terrain_to_zero_through_four(int count, int expected)
        {
            Assert.Equal(expected, FirstRegionTerrainCountPolicy.Clamp(count));
        }

        [Theory]
        [InlineData(null, 0)]
        [InlineData("", 0)]
        [InlineData("junk", 0)]
        [InlineData("-2", 0)]
        [InlineData("0", 0)]
        [InlineData("2", 2)]
        [InlineData("4", 4)]
        [InlineData("99", 4)]
        public void Missing_invalid_and_out_of_range_configuration_is_safe_and_bounded(
            string? configuredCount,
            int expected)
        {
            Assert.Equal(expected, FirstRegionTerrainCountPolicy.CountFrom(configuredCount));
        }

        [Fact]
        public void Maximum_matches_the_after_player_catalogue_suffix()
        {
            Assert.Equal(
                FirstRegionTerrainCountPolicy.MaximumOptionalTerrain,
                IslandCatalog.FirstRegionTerrain.Count - 1);
        }
    }
}

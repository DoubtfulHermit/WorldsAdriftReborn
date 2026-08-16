using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    public sealed class IslandTerrainConnectPolicyTests
    {
        [Fact]
        public void Disabled_mode_manages_nothing_and_preserves_base_initial_result()
        {
            Assert.False(IslandTerrainConnectPolicy.IsManaged(false, IslandCatalog.MentalFacility));
            Assert.True(IslandTerrainConnectPolicy.IsInitial(true, false,
                default, IslandCatalog.MentalFacility, 100));
        }

        [Fact]
        public void Haven_is_never_removed_from_the_mandatory_connect_ground()
        {
            Assert.False(IslandTerrainConnectPolicy.IsManaged(true, IslandCatalog.Haven));
        }

        [Fact]
        public void Optional_after_player_terrain_is_managed_as_one_request_add_unit()
        {
            Assert.True(IslandTerrainConnectPolicy.IsManaged(true, IslandCatalog.MentalFacility));
            Assert.False(IslandTerrainConnectPolicy.IsInitial(true, true,
                IslandCatalog.Haven.GlobalOrigin,
                IslandCatalog.MentalFacility, 1000));
            Assert.True(IslandTerrainConnectPolicy.IsInitial(false, true,
                IslandCatalog.MentalFacility.GlobalOrigin,
                IslandCatalog.MentalFacility, 1000));
        }
    }
}

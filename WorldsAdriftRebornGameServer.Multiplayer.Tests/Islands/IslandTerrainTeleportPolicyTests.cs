using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    public sealed class IslandTerrainTeleportPolicyTests
    {
        [Fact]
        public void Disabled_or_unmanaged_terrain_preserves_immediate_teleport()
        {
            Assert.Equal(TerrainTeleportDecision.Send,
                IslandTerrainTeleportPolicy.Decide(false, false, false, true));
        }

        [Fact]
        public void Managed_destination_waits_until_ready_then_sends()
        {
            Assert.Equal(TerrainTeleportDecision.Wait,
                IslandTerrainTeleportPolicy.Decide(true, true, false, false));
            Assert.Equal(TerrainTeleportDecision.Send,
                IslandTerrainTeleportPolicy.Decide(true, true, true, false));
        }

        [Fact]
        public void Unknown_or_expired_destination_refuses_safely()
        {
            Assert.Equal(TerrainTeleportDecision.Refuse,
                IslandTerrainTeleportPolicy.Decide(true, false, false, false));
            Assert.Equal(TerrainTeleportDecision.Refuse,
                IslandTerrainTeleportPolicy.Decide(true, true, false, true));
            Assert.False(IslandTerrainTeleportPolicy.WaitExpired(
                TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(10)));
            Assert.True(IslandTerrainTeleportPolicy.WaitExpired(
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)));
        }
    }
}

using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public class RuntimeEntityCatchupPolicyTests
    {
        [Theory]
        [InlineData("placed-shipyard:2")]
        [InlineData("placed-territoryControlBeacon:7")]
        [InlineData("built-ship:2:hull")]
        [InlineData("built-ship:2:deck:5")]
        [InlineData("loose-part:12:sail")]
        public void Runtime_player_made_entities_need_late_join_catchup(string key)
        {
            Assert.True(RuntimeEntityCatchupPolicy.NeedsLateJoinCatchup(key));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("island")]
        [InlineData("tree-12")]
        [InlineData("deposit-3")]
        [InlineData("global")]
        public void Boot_and_interest_owned_entities_do_not_use_runtime_catchup(string? key)
        {
            Assert.False(RuntimeEntityCatchupPolicy.NeedsLateJoinCatchup(key));
        }

        [Fact]
        public void Placement_broadcast_while_peer_is_loading_is_not_queued_again()
        {
            Assert.False(RuntimeEntityCatchupPolicy.ShouldQueue(
                "placed-shipyard:2", isBound: true, addEntityAlreadySent: true, retired: false));
        }

        [Fact]
        public void Picked_up_or_salvaged_entity_is_not_queued()
        {
            Assert.False(RuntimeEntityCatchupPolicy.ShouldQueue(
                "placed-shipyard:2", isBound: true, addEntityAlreadySent: false, retired: true));
        }
    }
}

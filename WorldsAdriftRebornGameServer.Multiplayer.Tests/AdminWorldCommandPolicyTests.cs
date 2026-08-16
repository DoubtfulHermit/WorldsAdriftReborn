using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public class AdminWorldCommandPolicyTests
    {
        [Fact]
        public void Ship_recall_is_exactly_thirty_metres_over_the_player_without_sideways_drift()
        {
            FixedPointPosition player = FixedPointPosition.FromMetres(8450.395, 277.946, 8327.664);

            FixedPointPosition recalled = AdminShipRecallPolicy.DestinationAbove(player);

            Assert.Equal(player.X, recalled.X);
            Assert.Equal(player.Z, recalled.Z);
            Assert.Equal(30 * FixedPointPosition.UnitsPerMetre, recalled.Y - player.Y);
            Assert.Equal(30.0, AdminShipRecallPolicy.HeightAbovePlayerMetres);
        }

        [Theory]
        [InlineData("reset-resources all", AdminWorldCommandKind.ResetResources, 0, 0)]
        [InlineData("recall-ship 83 12", AdminWorldCommandKind.RecallShip, 83, 12)]
        [InlineData("stop-ship 83", AdminWorldCommandKind.StopShip, 83, 0)]
        [InlineData("release-helm 83", AdminWorldCommandKind.ReleaseHelm, 83, 0)]
        [InlineData("delete-ship 83 DELETE", AdminWorldCommandKind.DeleteShip, 83, 0)]
        public void Exact_allowlisted_commands_parse(string text, AdminWorldCommandKind kind,
            long hull, long player)
        {
            Assert.True(AdminWorldCommandPolicy.TryParse(text, out AdminWorldCommand command,
                out string error), error);
            Assert.Equal(kind, command.Kind);
            Assert.Equal(hull, command.HullEntityId);
            Assert.Equal(player, command.PlayerEntityId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("reset-resources haven")]
        [InlineData("recall-ship 0 12")]
        [InlineData("recall-ship 83 -1")]
        [InlineData("stop-ship all")]
        [InlineData("release-helm -1")]
        [InlineData("delete-ship 83 delete")]
        [InlineData("delete-ship 83 DELETE extra")]
        [InlineData("shell rm -rf")]
        public void Everything_outside_the_contract_is_rejected(string text)
        {
            Assert.False(AdminWorldCommandPolicy.TryParse(text, out _, out _));
        }
    }
}

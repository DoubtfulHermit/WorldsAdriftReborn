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
        [InlineData("stage-ship 83 17720 900 -4.5", AdminWorldCommandKind.StageShip, 83, 0)]
        [InlineData("release-staged-ship 83", AdminWorldCommandKind.ReleaseStagedShip, 83, 0)]
        public void Exact_allowlisted_commands_parse(string text, AdminWorldCommandKind kind,
            long hull, long player)
        {
            Assert.True(AdminWorldCommandPolicy.TryParse(text, out AdminWorldCommand command,
                out string error), error);
            Assert.Equal(kind, command.Kind);
            Assert.Equal(hull, command.HullEntityId);
            Assert.Equal(player, command.PlayerEntityId);
        }

        [Fact]
        public void Stage_ship_coordinates_are_invariant_finite_and_world_bounded()
        {
            Assert.True(AdminWorldCommandPolicy.TryParse("stage-ship 83 -17720.25 1001.5 2",
                out AdminWorldCommand command, out _));
            Assert.Equal(-17720.25, command.X);
            Assert.Equal(1001.5, command.Y);
            Assert.Equal(2, command.Z);

            Assert.False(AdminWorldCommandPolicy.TryParse("stage-ship 83 NaN 0 0", out _, out _));
            Assert.False(AdminWorldCommandPolicy.TryParse("stage-ship 83 Infinity 0 0", out _, out _));
            Assert.False(AdminWorldCommandPolicy.TryParse("stage-ship 83 18051 0 0", out _, out _));
            Assert.False(AdminWorldCommandPolicy.TryParse("stage-ship 83 0 1101 0", out _, out _));
        }

        [Fact]
        public void Offline_stage_preserves_only_a_nearby_owners_exact_deck_offset()
        {
            FixedPointPosition hull = FixedPointPosition.FromMetres(10, 20, 30);
            FixedPointPosition player = FixedPointPosition.FromMetres(12.5, 24, 27);
            FixedPointPosition destination = FixedPointPosition.FromMetres(17720, 900, -4);

            Assert.True(AdminShipStagePolicy.TryCarryLogoutPosition(
                hull, player, destination, out FixedPointPosition carried));
            Assert.Equal(17722.5, carried.MetresX);
            Assert.Equal(904, carried.MetresY);
            Assert.Equal(-7, carried.MetresZ);

            Assert.False(AdminShipStagePolicy.TryCarryLogoutPosition(hull,
                FixedPointPosition.FromMetres(51, 20, 30), destination, out _));
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
        [InlineData("stage-ship 83 1,5 0 0")]
        [InlineData("stage-ship 83 0 0")]
        [InlineData("release-staged-ship 0")]
        [InlineData("shell rm -rf")]
        public void Everything_outside_the_contract_is_rejected(string text)
        {
            Assert.False(AdminWorldCommandPolicy.TryParse(text, out _, out _));
        }
    }
}

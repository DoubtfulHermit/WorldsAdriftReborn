using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public sealed class PlayerPositionPolicyTests
    {
        private static readonly FixedPointPosition Spawn = SpawnPolicy.PlayerSpawnPosition;

        [Fact]
        public void A_stored_position_in_the_world_is_restored()
        {
            FixedPointPosition stored = FixedPointPosition.FromMetres(13418.7, -188.9, -2028.1);

            Assert.Equal(PositionRestoreVerdict.Restore,
                PlayerPositionPolicy.Decide(stored, Spawn));
        }

        [Fact]
        public void A_character_who_has_never_logged_out_uses_the_spawn_point()
        {
            Assert.Equal(PositionRestoreVerdict.NoStoredPosition,
                PlayerPositionPolicy.Decide(null, Spawn));
        }

        /// <summary>
        /// The restore is the one operation that can drop a player somewhere they
        /// cannot escape, so every rejection falls back to the spawn point rather
        /// than trusting the stored value.
        /// </summary>
        [Fact]
        public void A_position_under_the_world_is_refused_rather_than_dropping_the_player_there()
        {
            FixedPointPosition drowned = FixedPointPosition.FromMetres(17212.4, -5000.0, -1130.2);
            Assert.True(FallPolicy.IsBelowDeepFloor(drowned));

            Assert.Equal(PositionRestoreVerdict.BelowTheWorld,
                PlayerPositionPolicy.Decide(drowned, Spawn));
        }

        [Theory]
        [InlineData(90000.0, 0.0)]
        [InlineData(-90000.0, 0.0)]
        [InlineData(0.0, 90000.0)]
        [InlineData(0.0, -90000.0)]
        public void A_position_outside_the_world_box_is_refused(double x, double z)
        {
            FixedPointPosition stray = FixedPointPosition.FromMetres(x, -200.0, z);

            Assert.Equal(PositionRestoreVerdict.OutsideTheWorld,
                PlayerPositionPolicy.Decide(stray, Spawn));
        }

        [Fact]
        public void Every_real_release_world_island_is_inside_the_world_box()
        {
            // The box must not be so tight that it refuses a legitimate logout on
            // the furthest island in the authored map.
            Assert.All(ReleaseWorldCatalog.All, record =>
                Assert.True(PlayerPositionPolicy.IsInsideTheWorldBox(
                    record.Definition.GlobalOrigin),
                    record.Definition.Id + " would be refused as outside the world"));
        }

        [Fact]
        public void Restoring_the_spawn_point_itself_is_a_no_op()
        {
            Assert.Equal(PositionRestoreVerdict.AlreadyAtSpawn,
                PlayerPositionPolicy.Decide(Spawn, Spawn));
        }

        [Fact]
        public void The_first_save_of_a_session_always_writes()
        {
            Assert.True(PlayerPositionPolicy.ShouldSave(null, Spawn));
        }

        [Fact]
        public void Standing_still_never_writes_but_walking_away_does()
        {
            FixedPointPosition start = FixedPointPosition.FromMetres(17212.4, -311.9, -1130.2);
            FixedPointPosition shuffled = FixedPointPosition.FromMetres(17214.4, -311.9, -1130.2);
            FixedPointPosition walked = FixedPointPosition.FromMetres(17232.4, -311.9, -1130.2);

            Assert.False(PlayerPositionPolicy.ShouldSave(start, start));
            Assert.False(PlayerPositionPolicy.ShouldSave(start, shuffled));
            Assert.True(PlayerPositionPolicy.ShouldSave(start, walked));
        }

        [Fact]
        public void Distance_is_measured_in_metres_not_fixed_point_units()
        {
            FixedPointPosition a = FixedPointPosition.FromMetres(0, 0, 0);
            FixedPointPosition b = FixedPointPosition.FromMetres(3, 4, 0);

            Assert.Equal(5.0, PlayerPositionPolicy.MetresBetween(a, b), 3);
        }

        [Fact]
        public void Every_verdict_explains_itself()
        {
            foreach (PositionRestoreVerdict verdict in Enum.GetValues<PositionRestoreVerdict>())
                Assert.NotEqual("unknown verdict", PlayerPositionPolicy.Explain(verdict));
        }
    }
}

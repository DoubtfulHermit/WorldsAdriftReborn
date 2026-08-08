using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// Where entities go, and which entity gets which seed.
    ///
    /// The bug this whole module exists to kill: component seeding switched on
    /// component id alone, so the island and the player were handed identical
    /// 190602 TransformState data. Haven turns that from untidy into fatal - it
    /// is one asset placed at twelve world positions, so there is no "default"
    /// position that is right for anybody.
    /// </summary>
    public class SpawnPolicyTests
    {
        private const long IslandEntity = 0;
        private const long FirstPlayerEntity = 1;

        // ------------------------------------------------------------------
        // Entity awareness - the point of the module
        // ------------------------------------------------------------------

        [Fact]
        public void The_island_and_a_player_never_receive_the_same_transform_seed()
        {
            Assert.NotEqual(
                SpawnPolicy.TransformSeedFor(IslandEntity, IslandEntity),
                SpawnPolicy.TransformSeedFor(FirstPlayerEntity, IslandEntity));
        }

        [Fact]
        public void The_shared_island_entity_id_is_recognised_as_the_island()
        {
            Assert.Equal(SeededEntityKind.Island, SpawnPolicy.KindOf(IslandEntity, IslandEntity));
        }

        [Theory]
        [InlineData(1L)]
        [InlineData(2L)]
        [InlineData(97L)]
        public void Every_other_entity_this_server_creates_is_a_player(long entityId)
        {
            Assert.Equal(SeededEntityKind.Player, SpawnPolicy.KindOf(entityId, IslandEntity));
        }

        [Fact]
        public void Nothing_is_the_island_before_the_island_id_has_been_allocated()
        {
            // The id is handed out lazily, on the island's AddEntityOp. Asking
            // "is this the island?" must not be what allocates it, or the answer
            // would depend on which entity asked first - and entity 0 would be
            // mistaken for the island.
            Assert.Equal(SeededEntityKind.Player, SpawnPolicy.KindOf(0, null));
            Assert.Equal(SpawnPolicy.PlayerSpawnPosition, SpawnPolicy.TransformSeedFor(0, null));
        }

        [Fact]
        public void The_island_id_is_not_assumed_to_be_zero()
        {
            // It comes off the same monotonic counter as player ids and is only
            // 0 today because it is allocated first.
            Assert.Equal(SeededEntityKind.Island, SpawnPolicy.KindOf(41, 41));
            Assert.Equal(SeededEntityKind.Player, SpawnPolicy.KindOf(0, 41));
        }

        [Fact]
        public void A_mirrored_remote_avatar_is_seeded_as_a_player_not_as_the_island()
        {
            // Remote rigs are seeded with 190602 too (MirrorSendPolicy.RemoteSeedComponents).
            // Handing one the island's position would park somebody else's body
            // in the middle of the terrain until the first relayed update.
            Assert.Contains(190602u, MirrorSendPolicy.RemoteSeedComponents);
            Assert.Equal(SpawnPolicy.PlayerSpawnPosition, SpawnPolicy.TransformSeedFor(7, IslandEntity));
        }

        // ------------------------------------------------------------------
        // The island itself
        // ------------------------------------------------------------------

        [Fact]
        public void The_island_is_Haven()
        {
            Assert.Equal("1431299145@Island", SpawnPolicy.IslandAssetName);
        }

        [Fact]
        public void The_island_is_no_longer_the_28_MiB_one_we_shipped_before()
        {
            Assert.NotEqual(SpawnPolicy.PreviousIslandAssetName, SpawnPolicy.IslandAssetName);
        }

        [Fact]
        public void The_island_sits_at_Haven_instance_five_not_at_the_world_origin()
        {
            Assert.Equal(new FixedPointPosition(69650145, -1305269, -4645549), SpawnPolicy.IslandPosition);
            Assert.NotEqual(new FixedPointPosition(0, 0, 0), SpawnPolicy.IslandPosition);
        }

        // ------------------------------------------------------------------
        // The player spawn
        // ------------------------------------------------------------------

        [Fact]
        public void The_player_spawn_is_the_provisional_point_by_the_ruined_camp()
        {
            Assert.Equal(new FixedPointPosition(70469345, -1289049, -4625069), SpawnPolicy.PlayerSpawnPosition);
        }

        [Fact]
        public void The_player_spawns_above_the_island_not_below_it()
        {
            // Y is the axis under revision; the sign of the difference is not.
            Assert.True(SpawnPolicy.PlayerSpawnPosition.Y > SpawnPolicy.IslandPosition.Y,
                "the player spawn is below the island origin - re-derive it before shipping");
        }

        [Fact]
        public void The_player_spawns_within_a_few_hundred_metres_of_the_island()
        {
            // Haven's props span island-local x 164..223 and the island itself is
            // a few hundred metres across. This is the guard that catches a
            // corrected altitude being pasted into the wrong axis, or a
            // half-updated coordinate: anything that lands the player kilometres
            // away is an infinite fall, because this server has no fall damage
            // and WorldEdgePushback never runs.
            Assert.True(Math.Abs(SpawnPolicy.PlayerSpawnPosition.MetresX - SpawnPolicy.IslandPosition.MetresX) < 500);
            Assert.True(Math.Abs(SpawnPolicy.PlayerSpawnPosition.MetresY - SpawnPolicy.IslandPosition.MetresY) < 500);
            Assert.True(Math.Abs(SpawnPolicy.PlayerSpawnPosition.MetresZ - SpawnPolicy.IslandPosition.MetresZ) < 500);
        }

        [Fact]
        public void The_player_no_longer_spawns_at_the_world_origin()
        {
            // The old seed was {0, 100, 0}, i.e. (0, 0.024, 0) m - the origin,
            // which only worked because the island was there too.
            Assert.NotEqual(new FixedPointPosition(0, 100, 0), SpawnPolicy.PlayerSpawnPosition);
        }

        // ------------------------------------------------------------------
        // 8055 NewPlayerState
        // ------------------------------------------------------------------

        [Fact]
        public void New_players_are_seeded_as_NOT_in_Haven_even_though_they_spawn_on_Haven()
        {
            // `true` is a permanent prison: the exit is 8056 LeaveHavenRequest,
            // which has zero references in the client, is consumed server-side
            // only, and is unimplemented here. It would cost five UI features
            // forever and suppress every biome banner in the game.
            Assert.False(SpawnPolicy.SeedIsNewPlayer);
        }

        [Fact]
        public void Nothing_grants_a_client_authority_over_NewPlayerState()
        {
            // The client has no writer for 8055, so if the server ever seeded
            // true there would be no path back to false from either side.
            Assert.DoesNotContain(8055u, MirrorSendPolicy.AuthoritativeComponents);
        }
    }
}

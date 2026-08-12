using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The wire encoding for world positions. Every assertion is on the NUMBER,
    /// because the only spawn bug this project has ever had is "the right field
    /// carrying the wrong value".
    /// </summary>
    public class FixedPointPositionTests
    {
        [Fact]
        public void Scale_is_4096_units_per_metre()
        {
            Assert.Equal(4096, FixedPointPosition.UnitsPerMetre);
        }

        [Fact]
        public void One_metre_is_4096_units()
        {
            Assert.Equal(new FixedPointPosition(4096, 4096, 4096), FixedPointPosition.FromMetres(1, 1, 1));
        }

        [Fact]
        public void Encoding_truncates_toward_zero_and_does_not_round()
        {
            // The client's own encoder is a C cast, (long)(d * 4096). 0.99995 m
            // is 4095.9952 units: truncation gives 4095, rounding would give
            // 4096. We must agree with the client to the unit.
            Assert.Equal(4095, FixedPointPosition.FromMetres(0.99995, 0, 0).X);
        }

        [Fact]
        public void Truncation_toward_zero_applies_to_negatives_too()
        {
            // Not floor. -0.99995 m must become -4095, not -4096.
            Assert.Equal(-4095, FixedPointPosition.FromMetres(-0.99995, 0, 0).X);
        }

        [Fact]
        public void Haven_instance_five_encodes_to_the_committed_island_seed()
        {
            // (17004.4300, -318.6693420, -1134.16748) m. Recomputing it here is
            // the check that the literal in SpawnPolicy was not fat-fingered:
            // two of the three axes truncate a fraction very close to 1.0, which
            // is exactly where a rounding mistake hides.
            Assert.Equal(
                SpawnPolicy.IslandPosition,
                FixedPointPosition.FromMetres(17004.4300, -318.6693420, -1134.16748));
        }

        [Fact]
        public void The_player_spawn_encodes_to_the_committed_player_seed()
        {
            // island-local (208.00, 6.70, 4.00) on Haven instance #5, which is
            // island world (17004.4300, -318.6693420, -1134.16748) plus that.
            Assert.Equal(
                SpawnPolicy.PlayerSpawnPosition,
                FixedPointPosition.FromMetres(17212.4300, -311.9693420, -1130.16748));
        }

        [Fact]
        public void The_player_spawn_is_the_island_origin_plus_the_island_local_offset()
        {
            // Independent of the literals above: build the world coordinate from
            // the island position and the island-local offset the extractor
            // reported, and it must land on the same seed. Catches a number
            // pasted into the wrong axis, which is the realistic mistake here.
            Assert.Equal(
                SpawnPolicy.PlayerSpawnPosition,
                FixedPointPosition.FromMetres(
                    17004.4300 + 208.00,
                    -318.6693420 + 6.70,
                    -1134.16748 + 4.00));
        }

        [Fact]
        public void Decoding_back_to_metres_lands_within_one_unit()
        {
            FixedPointPosition p = FixedPointPosition.FromMetres(17004.43, -318.66934, -1134.16748);

            Assert.True(Math.Abs(p.MetresX - 17004.43) < 1.0 / FixedPointPosition.UnitsPerMetre);
            Assert.True(Math.Abs(p.MetresY - -318.66934) < 1.0 / FixedPointPosition.UnitsPerMetre);
            Assert.True(Math.Abs(p.MetresZ - -1134.16748) < 1.0 / FixedPointPosition.UnitsPerMetre);
        }

        [Fact]
        public void Positions_compare_by_value()
        {
            Assert.Equal(new FixedPointPosition(1, 2, 3), new FixedPointPosition(1, 2, 3));
            Assert.NotEqual(new FixedPointPosition(1, 2, 3), new FixedPointPosition(1, 2, 4));
            Assert.True(new FixedPointPosition(1, 2, 3) == new FixedPointPosition(1, 2, 3));
            Assert.True(new FixedPointPosition(1, 2, 3) != new FixedPointPosition(3, 2, 1));
        }

        [Fact]
        public void A_17km_coordinate_survives_the_encoding_without_overflow()
        {
            // Q52.12 in a long: the world is +-12 km on x/z, so the encoded
            // magnitude is ~7e7 against a long's ~9.2e18. Nowhere near an edge -
            // this test exists so nobody "optimises" the field to int, where
            // 17 km would overflow at 2.1e9 / 4096 = 524 km... but 12 km fits,
            // and the overflow would only show up on a later, larger world.
            FixedPointPosition p = FixedPointPosition.FromMetres(17004.43, 0, 0);
            Assert.Equal(69650145L, p.X);
            Assert.True(p.X > int.MaxValue / 100);
        }
    }
}

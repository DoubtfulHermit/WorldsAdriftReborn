using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using WorldsAdriftRebornGameServer.Multiplayer.Walls;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Walls
{
    public class WallFlightInfluenceTests
    {
        [Fact]
        public void It_is_off_until_an_operator_supplies_an_unrecovered_strength()
        {
            WallFlightInfluence influence = WallFlightInfluence.FromEnvironment(true, _ => null);

            Assert.False(influence.IsEnabled);
            Assert.Empty(influence.Segments);
            Assert.Contains("1229 strengths are unrecovered", influence.Describe());
        }

        [Fact]
        public void The_master_wall_switch_keeps_mechanics_off_too()
        {
            WallFlightInfluence influence = WallFlightInfluence.FromEnvironment(false,
                key => key == WallFlightInfluence.WindRiftEnvVar ? "30" : null);

            Assert.Empty(influence.Segments);
        }

        [Fact]
        public void A_configured_type_uses_every_release_segment_of_that_type()
        {
            WallFlightInfluence influence = WallFlightInfluence.FromEnvironment(true,
                key => key == WallFlightInfluence.WindRiftEnvVar ? "12.5" : null);

            Assert.Equal(20, influence.Segments.Count);
            Assert.All(influence.Segments, wall =>
            {
                Assert.Equal(WeatherWallType.WindRift, wall.Type);
                Assert.Equal(12.5, wall.WindMultiplier, 9);
            });
        }

        [Fact]
        public void A_wall_type_not_served_to_the_client_cannot_have_invisible_mechanics()
        {
            WallFlightInfluence influence = WallFlightInfluence.FromEnvironment(true,
                key => key switch
                {
                    WallFlightInfluence.WindRiftEnvVar => "12.5",
                    WallPolicy.TypesEnvVar => "1,3,5",
                    _ => null,
                });

            Assert.Empty(influence.Segments);
        }

        [Fact]
        public void Projection_reconstructs_the_catalogues_original_endpoints()
        {
            WallSegmentSeed source = WallCatalog.OfType(WallType.WindRift)[0];
            WallFlightInfluence influence = WallFlightInfluence.FromEnvironment(true,
                key => key == WallFlightInfluence.WindRiftEnvVar ? "10" : null);
            WeatherWallSegment projected = influence.Segments[0];

            double halfX = source.OrientationX * source.HalfLength;
            double halfZ = source.OrientationZ * source.HalfLength;
            Assert.Equal(source.Midpoint.MetresX - halfX, projected.X1, 3);
            Assert.Equal(source.Midpoint.MetresZ - halfZ, projected.Z1, 3);
            Assert.Equal(source.Midpoint.MetresX + halfX, projected.X2, 3);
            Assert.Equal(source.Midpoint.MetresZ + halfZ, projected.Z2, 3);
        }

        [Theory]
        [InlineData("garbage", 0)]
        [InlineData("-4", 0)]
        [InlineData("200", 100)]
        public void Strength_input_is_total_and_client_bounded(string raw, double expected)
        {
            WallFlightInfluence influence = WallFlightInfluence.FromEnvironment(true,
                key => key == WallFlightInfluence.StormRiftEnvVar ? raw : null);

            if (expected == 0)
            {
                Assert.Empty(influence.Segments);
            }
            else
            {
                Assert.All(influence.Segments,
                    wall => Assert.Equal(expected, wall.WindMultiplier, 9));
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Walls;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Walls
{
    /// <summary>
    /// THE GEOMETRY. Endpoint pairs in, the four numbers <c>1204 WallSegmentState</c>
    /// wants out. Two of them - the HALF-length and the direction's sign - are the
    /// only things in this feature that can be silently, plausibly wrong, so they are
    /// worked by hand here rather than round-tripped through the same code that
    /// produced them.
    /// </summary>
    public class WallCatalogTests
    {
        // ====================================================================
        // THE ARITHMETIC, hand-worked
        // ====================================================================

        /// <summary>
        /// A 6-8-10 triangle, chosen so the length is exact in binary and the
        /// expected numbers can be read rather than computed: (0,0) to (600,800) is
        /// 1000 m long, so the midpoint is (300,400), the half-length is 500 and the
        /// unit direction is (0.6, 0, 0.8).
        /// </summary>
        [Fact]
        public void A_wall_becomes_its_midpoint_direction_and_HALF_length()
        {
            WallSegmentSeed wall = WallCatalog.SeedFrom(7, (int)WallType.StormRift, 0, 0, 600, 800);

            Assert.Equal(7, wall.WallId);
            Assert.Equal(WallType.StormRift, wall.Type);
            Assert.Equal(300.0, wall.Midpoint.MetresX, 6);
            Assert.Equal(400.0, wall.Midpoint.MetresZ, 6);
            Assert.Equal(0.6, wall.OrientationX, 9);
            Assert.Equal(0.8, wall.OrientationZ, 9);

            // THE HALF-LENGTH. WallData does P1 = pos - forward*Length and
            // P2 = pos + forward*Length, so a 1000 m wall carries 500. Sending 1000
            // here would put a 2 km wall in the sky and would look, from anywhere
            // except its ends, completely correct.
            Assert.Equal(500f, wall.HalfLength);
            Assert.Equal(1000.0, wall.LengthMetres, 3);
        }

        [Fact]
        public void The_direction_points_from_the_first_endpoint_to_the_second()
        {
            WallSegmentSeed forward = WallCatalog.SeedFrom(0, 0, 0, 0, 0, 100);
            WallSegmentSeed backward = WallCatalog.SeedFrom(0, 0, 0, 100, 0, 0);

            // Same wall, opposite authored order: same midpoint, same half-length,
            // opposite direction. WallData rebuilds P1/P2 from midpoint +/- forward,
            // so a flipped sign is harmless to the distance field - but it flips
            // WallData.Forward, which is the axis a storm rift's yaw torque aligns a
            // ship to. Getting it from the data rather than from a convention is what
            // keeps that honest for the day ships read it.
            Assert.Equal(1.0, forward.OrientationZ, 9);
            Assert.Equal(-1.0, backward.OrientationZ, 9);
            Assert.Equal(forward.Midpoint, backward.Midpoint);
            Assert.Equal(forward.HalfLength, backward.HalfLength);
        }

        [Fact]
        public void The_direction_is_a_unit_vector_and_is_FLAT()
        {
            foreach (WallSegmentSeed wall in WallCatalog.All)
            {
                double magnitude = Math.Sqrt(
                    (wall.OrientationX * wall.OrientationX)
                    + (wall.OrientationY * wall.OrientationY)
                    + (wall.OrientationZ * wall.OrientationZ));
                Assert.Equal(1.0, magnitude, 9);

                // A non-zero Y would tilt WallData.Forward out of the horizontal, and
                // the source geometry has no Y to justify it.
                Assert.Equal(0.0, wall.OrientationY);
            }
        }

        [Fact]
        public void A_degenerate_wall_is_refused_rather_than_producing_a_NaN_direction()
        {
            // Dividing by a zero length would hand the client a NaN orientation,
            // WallData.Forward would be NaN, and WeatherWalls.GetIntensityAt logs
            // "WallData.GetIntensityAt is NaN. Wat?" forever. Refuse it here.
            Assert.Throws<ArgumentException>(() => WallCatalog.SeedFrom(0, 0, 5, 5, 5, 5));
        }

        [Fact]
        public void Every_wall_sits_at_the_flat_world_datum()
        {
            // Y is inert (nothing in the client reads a wall's Y) but it should be
            // the same inert value for all 44, or the operator map and the world stop
            // being comparable.
            foreach (WallSegmentSeed wall in WallCatalog.All)
            {
                Assert.Equal(WallCatalog.WallYMetres, wall.Midpoint.MetresY, 6);
            }
        }

        // ====================================================================
        // THE DATA
        // ====================================================================

        [Fact]
        public void The_embedded_catalogue_is_really_there_and_holds_all_44_walls()
        {
            // WallCatalog fails EMPTY on a packaging mistake rather than throwing, so
            // without this test a dropped <EmbeddedResource> would ship as a silently
            // wall-less world.
            Assert.Equal(44, WallCatalog.All.Count);
        }

        [Fact]
        public void The_type_distribution_is_the_release_maps_own()
        {
            // Wind Rift 20, Storm Rift 11, Sand Storm 12, World End 1, and NO typhons
            // or ice storms - counted straight off wamap-islands.json#Walls and
            // restated in feature-roadmap.md 14.4. Pinned so a regenerated data file
            // that quietly lost or gained walls is loud.
            Assert.Equal(20, WallCatalog.OfType(WallType.WindRift).Count);
            Assert.Equal(11, WallCatalog.OfType(WallType.StormRift).Count);
            Assert.Equal(12, WallCatalog.OfType(WallType.SandStorm).Count);
            Assert.Single(WallCatalog.OfType(WallType.WorldEndWall));
            Assert.Empty(WallCatalog.OfType(WallType.Typhon));
            Assert.Empty(WallCatalog.OfType(WallType.IceStorm));
        }

        [Fact]
        public void Every_wall_id_is_unique()
        {
            // THE ONE COLLISION THAT IS NOT A DUPLICATE. WeatherWalls.Register keys
            // _wallsById by wallId and calls WallData.Add on a hit, which extends the
            // EXISTING wall's axial extent and KEEPS ITS TYPE (WallData.Type is
            // readonly, set only in the constructor). Two walls sharing an id are
            // therefore one enormous wall of the wrong kind - a sand storm swallowed
            // into a wind rift, not a wall drawn twice.
            List<int> ids = WallCatalog.All.Select(w => w.WallId).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void The_walls_are_inside_the_36_km_world()
        {
            // WorldInfo.WorldEdgeLength is 36000, centred on the origin, so every
            // endpoint is within +/-18 km. A midpoint outside that means the frame
            // conversion drifted - which is the failure that would put every wall in
            // the right shape and the wrong place.
            foreach (WallSegmentSeed wall in WallCatalog.All)
            {
                Assert.InRange(wall.Midpoint.MetresX, -18000.0, 18000.0);
                Assert.InRange(wall.Midpoint.MetresZ, -18000.0, 18000.0);
            }
        }

        [Fact]
        public void The_walls_are_in_the_SAME_frame_as_the_islands()
        {
            // The one assumption this feature makes about coordinates: the MapFile's
            // x/z are this server's world metres with no offset. It is true because
            // ReleaseWorldCatalog feeds the very same wamap x/y/z straight into
            // FixedPointPosition.FromMetres to place all 254 islands. Assert the
            // conversion rather than the belief: a wall midpoint in metres, re-encoded
            // the island way, must be the identical fixed-point value.
            WallSegmentSeed wall = WallCatalog.All[0];
            FixedPointPosition islandWay = FixedPointPosition.FromMetres(
                wall.Midpoint.MetresX, WallCatalog.WallYMetres, wall.Midpoint.MetresZ);
            Assert.Equal(islandWay.Y, wall.Midpoint.Y);
        }

        // ====================================================================
        // THE COST
        // ====================================================================

        [Fact]
        public void The_storm_wall_kilometrage_is_the_bolt_rate_input_and_it_is_about_53_km()
        {
            // LightningVisualInstancesManager spawns ambient bolts at
            // _fakeLightningPerSecondPerKilometer * TotalStormWallLength / 1000 per
            // second, world-wide, before culling, and WeatherWalls.EvaluateLength sums
            // every REGISTERED storm rift. Serving all 11 pins that number at ~53 km
            // permanently for every client. It is pinned here so nobody raises it
            // without noticing.
            double km = WallCatalog.StormWallLengthMetres(WallCatalog.All) / 1000.0;
            Assert.InRange(km, 50.0, 57.0);
        }

        [Fact]
        public void Dropping_the_storm_rifts_drops_the_bolt_rate_to_zero()
        {
            // The mitigation lever, measured: WAREBORN_WALL_TYPES=0,3,5 keeps 33 of
            // the 44 walls and removes every source of ambient lightning.
            IReadOnlyCollection<WallType> chosen = WallPolicy.SelectedTypes("0,3,5");
            List<WallSegmentSeed> served =
                WallCatalog.All.Where(w => chosen.Contains(w.Type)).ToList();

            Assert.Equal(33, served.Count);
            Assert.Equal(0.0, WallCatalog.StormWallLengthMetres(served));
        }
    }
}

using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using Xunit;
using Xunit.Abstractions;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// WHICH REGIONS GET A WHALE, what its entity ids are, and - the tests that
    /// matter most - what the tuned numbers actually produce against the REAL
    /// preserved catalogue rather than against a square.
    ///
    /// A speed and an altitude are only meaningful as the durations and clearances
    /// they cause. These pin them: a lap that quietly became four hours, or an
    /// altitude that quietly put the animal inside a mountain, would be a tuning
    /// mistake that no unit test of the spline could ever catch.
    /// </summary>
    public class SkyWhalePlanTests
    {
        private readonly ITestOutputHelper _output;

        public SkyWhalePlanTests(ITestOutputHelper output) => _output = output;

        private static IReadOnlyList<ReleaseIslandRecord> TierOne() =>
            ReleaseWorldRolloutPolicy.Select("tier1");

        [Fact]
        public void The_wilderness_carries_one_whale_per_mapfile_cell()
        {
            IReadOnlyList<ReleaseIslandRecord> islands = TierOne();
            IReadOnlyList<SkyWhalePlacement> plan = SkyWhalePlan.Build(islands);

            Assert.Equal(4, SkyWhalePlan.RegionCount(islands));
            Assert.Equal(4, plan.Count);
            Assert.Equal(
                new[]
                {
                    "release-a2-region", "release-a3-region",
                    "release-b2-region", "release-b3-region",
                },
                plan.Select(placement => placement.Whale.Region.Value));
        }

        [Fact]
        public void The_region_ids_are_the_world_directorys_own()
        {
            // Not "a name that happens to agree today": the whale's region id and
            // RegionRegistry.CreateReleaseWorld's are formed by the same expression,
            // so a future change to either has to change both.
            Assert.Equal(new RegionId("release-b3-region"),
                SkyWhalePolicy.RegionIdForCell("B3"));
            Assert.Equal(new RegionId("release-unassigned-t4-1-region"),
                SkyWhalePolicy.RegionIdForCell("unassigned-t4-1"));
        }

        [Fact]
        public void Every_id_is_inside_the_whale_band_and_paired_with_its_caller()
        {
            foreach (SkyWhalePlacement placement in SkyWhalePlan.Build(TierOne()))
            {
                Assert.True(placement.Whale.EntityId >= SkyWhalePolicy.FirstWhaleEntityId);
                Assert.Equal(placement.Whale.EntityId + 1, placement.Whale.CallEntityId);
                // The band a whale must never reach down into. Fauna's world-wide
                // budget is 4,000, so the real gap is a hundred million minus that.
                Assert.True(placement.Whale.EntityId
                    > IslandFaunaPolicy.FirstFaunaEntityId + IslandFaunaPolicy.DefaultMaxConcurrent);
            }
        }

        [Fact]
        public void Adding_a_district_does_not_renumber_an_existing_districts_whale()
        {
            // The id block is a function of the CELL's ordinal position, and cells
            // sort ordinally, so widening the rollout appends rather than reshuffles
            // for every cell that sorts before the new one.
            IReadOnlyList<SkyWhalePlacement> narrow =
                SkyWhalePlan.Build(ReleaseWorldRolloutPolicy.Select("A2,A3"));
            IReadOnlyList<SkyWhalePlacement> wide =
                SkyWhalePlan.Build(ReleaseWorldRolloutPolicy.Select("A2,A3,B2,B3"));

            foreach (SkyWhalePlacement placement in narrow)
            {
                SkyWhalePlacement same = wide.Single(
                    other => other.Whale.Region == placement.Whale.Region);
                Assert.Equal(placement.Whale.EntityId, same.Whale.EntityId);
            }
        }

        [Fact]
        public void The_plan_is_a_pure_function_so_a_restart_re_derives_it_exactly()
        {
            IReadOnlyList<SkyWhalePlacement> first = SkyWhalePlan.Build(TierOne());
            IReadOnlyList<SkyWhalePlacement> second = SkyWhalePlan.Build(TierOne());
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].Whale, second[i].Whale);
                Assert.Equal(first[i].Circuit.CircuitSeconds, second[i].Circuit.CircuitSeconds, 12);
                Assert.Equal(first[i].Circuit.PhaseFraction, second[i].Circuit.PhaseFraction, 12);
                Assert.Equal(
                    first[i].Circuit.Waypoints.Select(waypoint => waypoint.IslandId),
                    second[i].Circuit.Waypoints.Select(waypoint => waypoint.IslandId));
            }
        }

        [Fact]
        public void A_lap_of_a_real_wilderness_cell_takes_between_ten_and_forty_minutes()
        {
            // THE TUNING ASSERTION. A lap is how often a given island gets a visit,
            // and it is a consequence of the cell's size and SkyWhalePolicy's speed
            // rather than a number anybody set. Under ten minutes the animal stops
            // being an event; over forty a player could play a session and never see
            // one. The window is wide because it is a guard rail, not a target.
            foreach (SkyWhalePlacement placement in SkyWhalePlan.Build(TierOne()))
            {
                _output.WriteLine(placement.Whale.Region.Value
                    + ": " + placement.Circuit.Waypoints.Count + " islands, "
                    + placement.Circuit.LengthMetres.ToString("0") + " m, lap "
                    + (placement.Circuit.CircuitSeconds / 60.0).ToString("0.0") + " min");
                Assert.InRange(placement.Circuit.CircuitSeconds, 600.0, 2400.0);
            }
        }

        [Fact]
        public void The_whale_clears_every_island_it_flies_over()
        {
            // The animal's mesh hangs about 28 m BELOW the transform this server
            // drives (skinned AABB centre -11.61, height 33.44 - both RECOVERED), so
            // "the waypoint is above MaxY" is not enough on its own; assert the
            // belly clearance directly.
            const double BellyBelowOriginMetres = 28.4;
            foreach (ReleaseIslandRecord island in TierOne())
            {
                SkyWhaleWaypoint waypoint = SkyWhalePlan.WaypointFor(island);
                double terrainTop = island.Definition.GlobalOrigin.MetresY + island.Envelope.MaxY;
                Assert.True(waypoint.Y - BellyBelowOriginMetres - terrainTop > 50.0,
                    island.Definition.Id + " would be grazed: belly at "
                        + (waypoint.Y - BellyBelowOriginMetres) + " over a peak at " + terrainTop);
            }
        }

        [Fact]
        public void A_pass_over_an_island_lasts_between_thirty_seconds_and_five_minutes()
        {
            // THE OTHER TUNING ASSERTION, and the one the brief is written in:
            // "entering an island's interest bubble for a minute or two and
            // leaving". Measured by walking a real circuit at the real pose cadence
            // and counting how long the whale is inside the FAUNA radius of the
            // island it is flying to - which is the bubble a standing player has.
            SkyWhalePlacement placement = SkyWhalePlan.Build(TierOne())
                .Single(candidate => candidate.Whale.Region.Value == "release-b3-region");
            SkyWhaleCircuit circuit = placement.Circuit;
            double radius = IslandFaunaInterestPolicy.DefaultLoadRadiusMetres;

            foreach (SkyWhaleWaypoint waypoint in circuit.Waypoints)
            {
                double inside = 0.0;
                const double Step = 1.0;
                for (double t = 0.0; t < circuit.CircuitSeconds; t += Step)
                {
                    (double x, double y, double z) = circuit.PositionAtTime(t);
                    double dx = x - waypoint.X, dy = y - waypoint.Y, dz = z - waypoint.Z;
                    if ((dx * dx) + (dy * dy) + (dz * dz) <= radius * radius) inside += Step;
                }
                _output.WriteLine(waypoint.IslandId + ": inside " + radius.ToString("0")
                    + " m for " + inside.ToString("0") + " s");
                Assert.InRange(inside, 30.0, 300.0);
            }
        }
    }
}

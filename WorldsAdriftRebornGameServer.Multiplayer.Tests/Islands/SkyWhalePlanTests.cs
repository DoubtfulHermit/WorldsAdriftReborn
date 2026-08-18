using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using Xunit;
using Xunit.Abstractions;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// WHAT THE WORLD'S ONE WHALE ACTUALLY GETS, what its entity ids are, and - the
    /// tests that matter most - what the tuned numbers produce against the REAL
    /// preserved catalogue rather than against a square.
    ///
    /// A speed and an altitude are only meaningful as the durations and clearances
    /// they cause, and with a single migrating animal the durations decide whether
    /// the feature reads as "rare and worth seeing" or as "absent". These pin them:
    /// a world lap that quietly became six hours, or an altitude that quietly put
    /// the animal inside a mountain, would be a tuning mistake that no unit test of
    /// the spline could ever catch.
    /// </summary>
    public class SkyWhalePlanTests
    {
        private readonly ITestOutputHelper _output;

        public SkyWhalePlanTests(ITestOutputHelper output) => _output = output;

        private static IReadOnlyList<ReleaseIslandRecord> TierOne() =>
            ReleaseWorldRolloutPolicy.Select("tier1");

        private static SkyWhalePlacement Plan() => SkyWhalePlan.Build(TierOne())!.Value;

        [Fact]
        public void The_wilderness_carries_exactly_one_whale_for_all_four_cells()
        {
            IReadOnlyList<ReleaseIslandRecord> islands = TierOne();
            SkyWhalePlacement placement = SkyWhalePlan.Build(islands)!.Value;

            Assert.Equal(4, SkyWhalePlan.RegionCount(islands));
            // NAMED AFTER THE CELLS, because the route is a function of them and the
            // map joins its geometry on exactly this string.
            Assert.Equal("release-route-a2-a3-b2-b3", placement.Whale.RouteId);
            Assert.Equal(SkyWhaleRoute.RouteIdFor(islands.Select(record => record.CellId)),
                placement.Whale.RouteId);
            // Every island of every cell, on ONE route.
            Assert.Equal(islands.Count, placement.Circuit.IslandCount);
            Assert.Equal(
                new[]
                {
                    "release-a2-region", "release-a3-region",
                    "release-b2-region", "release-b3-region",
                },
                placement.Circuit.Regions.Select(region => region.Value).OrderBy(id => id));
        }

        [Fact]
        public void The_region_ids_are_the_world_directorys_own()
        {
            // Not "a name that happens to agree today": the whale's zone ids and
            // RegionRegistry.CreateReleaseWorld's are formed by the same expression,
            // so a future change to either has to change both.
            Assert.Equal(new RegionId("release-b3-region"),
                SkyWhalePolicy.RegionIdForCell("B3"));
            Assert.Equal(new RegionId("release-unassigned-t4-1-region"),
                SkyWhalePolicy.RegionIdForCell("unassigned-t4-1"));
        }

        [Fact]
        public void The_id_is_the_bottom_of_the_whale_band_and_paired_with_its_caller()
        {
            SkyWhalePlacement placement = Plan();
            Assert.Equal(SkyWhalePolicy.FirstWhaleEntityId, placement.Whale.EntityId);
            Assert.Equal(placement.Whale.EntityId + 1, placement.Whale.CallEntityId);
            // The band a whale must never reach down into. Fauna's own world-wide
            // budget is 4,000, so the real gap is a hundred million minus that.
            Assert.True(placement.Whale.EntityId
                > IslandFaunaPolicy.FirstFaunaEntityId + IslandFaunaPolicy.DefaultMaxConcurrent);
        }

        [Fact]
        public void Widening_the_rollout_renames_the_route_but_never_renumbers_the_whale()
        {
            // TWO PROPERTIES THAT PULL IN OPPOSITE DIRECTIONS, and both are wanted.
            //
            // The ENTITY IDS must not move, ever: a reconnecting player must never
            // be handed an id that used to mean something else. There is one whale
            // at the bottom of the band, so that is now trivially true and is
            // asserted anyway, because it is the reason the ids are pinned at all.
            //
            // The ROUTE NAME must move, and that is not an accident either. The
            // route is a FUNCTION of which cells were rolled out - a two-cell world
            // and a four-cell world have different orders, lap times and phases - so
            // a shared name would let the map draw one while the game flew the
            // other. The name is what makes that impossible.
            SkyWhale narrow =
                SkyWhalePlan.Build(ReleaseWorldRolloutPolicy.Select("A2,A3"))!.Value.Whale;
            SkyWhale wide =
                SkyWhalePlan.Build(ReleaseWorldRolloutPolicy.Select("A2,A3,B2,B3"))!.Value.Whale;

            Assert.Equal(narrow.EntityId, wide.EntityId);
            Assert.Equal(narrow.CallEntityId, wide.CallEntityId);
            Assert.Equal("release-route-a2-a3", narrow.RouteId);
            Assert.Equal("release-route-a2-a3-b2-b3", wide.RouteId);
        }

        [Fact]
        public void The_plan_is_a_pure_function_so_a_restart_re_derives_it_exactly()
        {
            SkyWhalePlacement first = Plan();
            SkyWhalePlacement second = Plan();

            Assert.Equal(first.Whale, second.Whale);
            Assert.Equal(first.Circuit.CircuitSeconds, second.Circuit.CircuitSeconds, 12);
            Assert.Equal(first.Circuit.PhaseFraction, second.Circuit.PhaseFraction, 12);
            Assert.Equal(
                first.Circuit.Waypoints.Select(waypoint => waypoint.IslandId),
                second.Circuit.Waypoints.Select(waypoint => waypoint.IslandId));
            Assert.Equal(
                first.Circuit.Waypoints.Select(waypoint => waypoint.IsTransit),
                second.Circuit.Waypoints.Select(waypoint => waypoint.IsTransit));
        }

        [Fact]
        public void A_world_lap_takes_between_forty_minutes_and_three_hours()
        {
            // THE TUNING ASSERTION, and the one the whole rework turns on. A world
            // lap is now how often a given island gets a visit - it used to be a
            // region lap, three or four times an hour. Under forty minutes the
            // migration stops being a migration and the animal is back to being
            // scenery; over three hours a player could play several sessions and
            // never see one, which is the failure mode the brief explicitly warned
            // against ("the point is that seeing it should feel like an event, not
            // that it should be absent"). The window is wide because it is a guard
            // rail, not a target: the number is a CONSEQUENCE of the world's size
            // and SkyWhalePolicy's speed rather than something anybody set.
            SkyWhaleCircuit circuit = Plan().Circuit;
            _output.WriteLine(circuit.IslandCount + " islands across "
                + circuit.Regions.Count + " zones, "
                + (circuit.Waypoints.Count - circuit.IslandCount) + " crossing points, "
                + circuit.LengthMetres.ToString("0") + " m, world lap "
                + (circuit.CircuitSeconds / 60.0).ToString("0.0") + " min");
            Assert.InRange(circuit.CircuitSeconds, 2400.0, 10800.0);
        }

        [Fact]
        public void A_zone_holds_the_whale_for_long_enough_to_be_worth_travelling_to()
        {
            // The other half of the same tuning question. Rarity is only good if the
            // visit, when it comes, is long enough to catch: measure how much of a
            // world lap the animal actually spends over each cell.
            SkyWhaleCircuit circuit = Plan().Circuit;
            Dictionary<string, double> inZone = new Dictionary<string, double>();
            const double Step = 5.0;
            for (double t = 0.0; t < circuit.CircuitSeconds; t += Step)
            {
                SkyWhaleWhereabouts where = circuit.WhereAt(t);
                if (where.InTransit) continue;
                string id = where.Region.Value;
                inZone[id] = inZone.TryGetValue(id, out double so_far) ? so_far + Step : Step;
            }

            foreach (KeyValuePair<string, double> zone in inZone.OrderBy(entry => entry.Key))
            {
                _output.WriteLine(zone.Key + ": the whale is inside it for "
                    + (zone.Value / 60.0).ToString("0.0") + " min of every "
                    + (circuit.CircuitSeconds / 60.0).ToString("0.0") + " min lap");
            }
            Assert.Equal(4, inZone.Count);
            foreach (double seconds in inZone.Values)
            {
                Assert.InRange(seconds, 600.0, circuit.CircuitSeconds * 0.5);
            }
        }

        [Fact]
        public void The_boot_log_can_name_where_and_when_to_stand()
        {
            // A feature nobody can find is indistinguishable from one that is
            // broken, and with ONE whale that is not the worst case, it is the
            // normal case: most zones are empty most of the time by design. The boot
            // log says where to stand; this pins that the arithmetic behind it is
            // right, by walking the route forward and checking the whale really IS
            // over the named island when it said it would be.
            SkyWhaleCircuit circuit = Plan().Circuit;
            (IslandId island, double seconds) = circuit.NextArrivalAfter(0.0);
            _output.WriteLine("stand on " + island + ", look up in " + seconds.ToString("0") + " s");

            // Never "now", never more than a lap away.
            Assert.InRange(seconds, 0.0, circuit.CircuitSeconds);

            SkyWhaleWaypoint expected = circuit.Waypoints.First(
                waypoint => !waypoint.IsTransit && waypoint.IslandId == island);
            (double x, double y, double z) = circuit.PositionAtTime(seconds);
            Assert.Equal(expected.X, x, 3);
            Assert.Equal(expected.Y, y, 3);
            Assert.Equal(expected.Z, z, 3);

            // And the NEXT answer from just after that arrival is a DIFFERENT
            // island - so the line advances round the route instead of sticking.
            (IslandId after, double _) = circuit.NextArrivalAfter(seconds + 1.0);
            Assert.NotEqual(island, after);
        }

        [Fact]
        public void The_boot_log_can_name_the_next_zone_and_a_countdown_to_it()
        {
            // The line the migration added, and the one an operator uses to find the
            // animal at all: which cell is about to get it, over which island, and
            // in how long. Sampled right round the lap so it is never "no idea".
            SkyWhaleCircuit circuit = Plan().Circuit;
            int transits = 0, inZone = 0;
            for (double t = 0.0; t < circuit.CircuitSeconds; t += circuit.CircuitSeconds / 200.0)
            {
                SkyWhaleWhereabouts where = circuit.WhereAt(t);
                if (where.InTransit) transits++; else inZone++;

                Assert.NotEqual(default, where.NextRegion);
                Assert.NotEqual(default, where.NextRegionIsland);
                Assert.InRange(where.SecondsToNextRegion, 0.0, circuit.CircuitSeconds);
                // The zone it is heading to is never the one it is already over.
                if (!where.InTransit) Assert.NotEqual(where.Region, where.NextRegion);
                // It really IS over that island when the countdown expires.
                SkyWhaleWaypoint entry = circuit.Waypoints.First(
                    waypoint => !waypoint.IsTransit
                        && waypoint.IslandId == where.NextRegionIsland);
                (double x, double _, double z) =
                    circuit.PositionAtTime(t + where.SecondsToNextRegion);
                Assert.Equal(entry.X, x, 3);
                Assert.Equal(entry.Z, z, 3);
            }
            _output.WriteLine("of 200 samples round the lap: " + inZone
                + " over a zone, " + transits + " crossing open sky");
            // A GUARD RAIL ON THE SHAPE, not a target. No crossings at all would
            // mean the migration had collapsed back into a single ring; crossings
            // for most of the lap would mean the animal spends its life over the
            // void. The released world's cells are about as far apart as they are
            // wide, so an even split is the geometry rather than a choice.
            Assert.InRange(transits / 200.0, 0.2, 0.6);
            Assert.InRange(inZone / 200.0, 0.4, 0.8);
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
            // leaving". Measured by walking the real route and counting how long the
            // whale is inside the FAUNA radius of each island of one cell - which is
            // the bubble a standing player has. Unchanged by the migration, and that
            // is the point of measuring it again: the visit should feel exactly as
            // it did, only rarer.
            SkyWhaleCircuit circuit = Plan().Circuit;
            double radius = IslandFaunaInterestPolicy.DefaultLoadRadiusMetres;

            foreach (SkyWhaleWaypoint waypoint in circuit.Waypoints)
            {
                if (waypoint.IsTransit
                    || waypoint.Region.Value != "release-b3-region") continue;

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

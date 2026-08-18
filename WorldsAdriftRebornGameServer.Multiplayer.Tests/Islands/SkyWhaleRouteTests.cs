using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using Xunit;
using Xunit.Abstractions;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// THE MIGRATION'S ORDER: that the whale tours a zone before leaving it, that
    /// it enters each zone from the side the last one was on, that the crossings
    /// are flown at the whale's own speed rather than at six times it, and that all
    /// of that is a pure function of where the islands are.
    ///
    /// These are the tests the single-whale rework is really made of. The spline
    /// evaluator did not change; what changed is which control points it is handed
    /// and in what order, so this is where a mistake would live.
    /// </summary>
    public class SkyWhaleRouteTests
    {
        private readonly ITestOutputHelper _output;

        public SkyWhaleRouteTests(ITestOutputHelper output) => _output = output;

        private static SkyWhaleWaypoint At(string id, double x, double z) =>
            new SkyWhaleWaypoint(new IslandId(id), x, 500.0, z);

        /// <summary>Four zones on a 2x2 block, three islands each - the released world in miniature.</summary>
        private static IReadOnlyList<SkyWhaleZone> Block() => new[]
        {
            new SkyWhaleZone(new RegionId("nw"), new[]
            {
                At("nw1", -2000.0, 2000.0), At("nw2", -1600.0, 2400.0),
                At("nw3", -2400.0, 2400.0),
            }),
            new SkyWhaleZone(new RegionId("ne"), new[]
            {
                At("ne1", 2000.0, 2000.0), At("ne2", 2400.0, 2400.0),
                At("ne3", 1600.0, 2400.0),
            }),
            new SkyWhaleZone(new RegionId("sw"), new[]
            {
                At("sw1", -2000.0, -2000.0), At("sw2", -1600.0, -2400.0),
                At("sw3", -2400.0, -2400.0),
            }),
            new SkyWhaleZone(new RegionId("se"), new[]
            {
                At("se1", 2000.0, -2000.0), At("se2", 2400.0, -2400.0),
                At("se3", 1600.0, -2400.0),
            }),
        };

        [Fact]
        public void Every_island_in_the_world_is_on_the_route_exactly_once()
        {
            // The whole promise of one whale: it does not skip a cell, and it does
            // not visit one twice a lap either - "once a lap" is the rarity the
            // feature is tuned around and a duplicated island would halve it.
            IReadOnlyList<SkyWhaleWaypoint> route = SkyWhaleRoute.Build(Block());
            string[] islands = route
                .Where(waypoint => !waypoint.IsTransit)
                .Select(waypoint => waypoint.IslandId.Value)
                .ToArray();

            Assert.Equal(12, islands.Length);
            Assert.Equal(12, islands.Distinct().Count());
        }

        [Fact]
        public void A_zone_is_toured_completely_before_the_next_one_is_entered()
        {
            // THE SHAPE, asserted rather than eyeballed. The alternative design -
            // sorting every island in the world by bearing about the world centroid
            // - passes the test above and fails this one: it fans in and out of each
            // cell instead of touring it, so "it passes each island of your zone in
            // turn" stops being true.
            List<string> zones = new List<string>();
            foreach (SkyWhaleWaypoint waypoint in SkyWhaleRoute.Build(Block()))
            {
                if (waypoint.IsTransit) continue;
                if (zones.Count == 0 || zones[zones.Count - 1] != waypoint.Region.Value)
                {
                    zones.Add(waypoint.Region.Value);
                }
            }

            _output.WriteLine("zone order: " + string.Join(" -> ", zones));
            Assert.Equal(4, zones.Count);
            Assert.Equal(4, zones.Distinct().Count());
        }

        [Fact]
        public void Consecutive_zones_are_neighbours_rather_than_diagonal_opposites()
        {
            // Angular order about the world centroid, which on a 2x2 block is a
            // four-cycle around the edges. Cell-id order (nw, ne, se, sw is fine;
            // a2, a3, b2, b3 is not) would send the animal across the diagonal.
            List<RegionId> zones = new List<RegionId>();
            foreach (SkyWhaleWaypoint waypoint in SkyWhaleRoute.Build(Block()))
            {
                if (waypoint.IsTransit) continue;
                if (zones.Count == 0 || zones[zones.Count - 1] != waypoint.Region)
                {
                    zones.Add(waypoint.Region);
                }
            }

            Dictionary<string, (double X, double Z)> centres = new()
            {
                ["nw"] = (-2000, 2266), ["ne"] = (2000, 2266),
                ["sw"] = (-2000, -2266), ["se"] = (2000, -2266),
            };
            for (int i = 0; i < zones.Count; i++)
            {
                (double ax, double az) = centres[zones[i].Value];
                (double bx, double bz) = centres[zones[(i + 1) % zones.Count].Value];
                // A diagonal hop differs on BOTH axes; an edge hop on one.
                Assert.True(Math.Abs(ax - bx) < 1.0 || Math.Abs(az - bz) < 1.0,
                    zones[i] + " -> " + zones[(i + 1) % zones.Count] + " is a diagonal");
            }
        }

        [Fact]
        public void Each_zone_is_entered_at_its_nearest_island_to_the_zone_before_it()
        {
            // The animal arrives at the NEAR edge of a cell, which is what an
            // approach looks like; the long haul back across the cell happens on the
            // way out. Anchored on the previous zone's centroid rather than on its
            // exit waypoint, so no zone has to be solved before another one can be.
            IReadOnlyList<SkyWhaleWaypoint> route = SkyWhaleRoute.Build(Block());
            List<(RegionId Region, SkyWhaleWaypoint First)> entries =
                new List<(RegionId, SkyWhaleWaypoint)>();
            RegionId last = default;
            foreach (SkyWhaleWaypoint waypoint in route)
            {
                if (waypoint.IsTransit) continue;
                if (waypoint.Region != last)
                {
                    entries.Add((waypoint.Region, waypoint));
                    last = waypoint.Region;
                }
            }

            Dictionary<string, (double X, double Z)> centres = new()
            {
                ["nw"] = (-2000, 2266.6666666666665), ["ne"] = (2000, 2266.6666666666665),
                ["sw"] = (-2000, -2266.6666666666665), ["se"] = (2000, -2266.6666666666665),
            };
            for (int i = 0; i < entries.Count; i++)
            {
                (double px, double pz) = centres[
                    entries[((i - 1) % entries.Count + entries.Count) % entries.Count].Region.Value];
                SkyWhaleWaypoint entry = entries[i].First;
                double best = route
                    .Where(waypoint => !waypoint.IsTransit && waypoint.Region == entries[i].Region)
                    .Min(waypoint => ((waypoint.X - px) * (waypoint.X - px))
                        + ((waypoint.Z - pz) * (waypoint.Z - pz)));
                double got = ((entry.X - px) * (entry.X - px)) + ((entry.Z - pz) * (entry.Z - pz));
                Assert.Equal(best, got, 6);
            }
        }

        [Fact]
        public void The_crossings_between_zones_are_resampled_to_the_size_of_the_legs_inside_one()
        {
            // THE CORRECTNESS REQUIREMENT, not a tidiness one. Uniform Catmull-Rom
            // gives every segment an EQUAL slice of the lap, so an unresampled
            // crossing several times longer than its neighbours would be flown
            // several times faster. Assert the geometry directly: no leg on the
            // finished route may be dramatically longer than the median.
            IReadOnlyList<SkyWhaleWaypoint> route = SkyWhaleRoute.Build(Block());
            List<double> legs = new List<double>();
            for (int i = 0; i < route.Count; i++)
            {
                SkyWhaleWaypoint from = route[i], to = route[(i + 1) % route.Count];
                legs.Add(Math.Sqrt(((to.X - from.X) * (to.X - from.X))
                    + ((to.Y - from.Y) * (to.Y - from.Y))
                    + ((to.Z - from.Z) * (to.Z - from.Z))));
            }
            List<double> sorted = new List<double>(legs);
            sorted.Sort();
            double median = sorted[sorted.Count / 2];
            _output.WriteLine("legs " + sorted[0].ToString("0") + " - "
                + sorted[sorted.Count - 1].ToString("0") + " m, median "
                + median.ToString("0") + " m");

            Assert.True(sorted[sorted.Count - 1] <= median * 2.0,
                "the longest leg is " + sorted[sorted.Count - 1].ToString("0")
                + " m against a median of " + median.ToString("0")
                + " m, so the whale would cross it far too fast");
            Assert.Contains(route, waypoint => waypoint.IsTransit);
        }

        [Fact]
        public void A_crossing_point_is_anchored_to_the_nearer_of_the_two_islands()
        {
            // Every coordinate published to the map is an island-local offset,
            // because the map places islands from the preserved MapFile and the game
            // server from its own catalogue. A crossing point anchored to the far
            // end would carry the larger of the two possible errors.
            IReadOnlyList<SkyWhaleWaypoint> route = SkyWhaleRoute.Build(Block());
            Dictionary<string, SkyWhaleWaypoint> islands = route
                .Where(waypoint => !waypoint.IsTransit)
                .ToDictionary(waypoint => waypoint.IslandId.Value, waypoint => waypoint);

            foreach (SkyWhaleWaypoint waypoint in route)
            {
                if (!waypoint.IsTransit) continue;
                SkyWhaleWaypoint anchor = islands[waypoint.IslandId.Value];
                double mine = Distance(waypoint, anchor);
                foreach (SkyWhaleWaypoint other in islands.Values)
                {
                    // Only the two ends of the leg are candidates, so it is enough
                    // that no island is closer than the anchor by a real margin.
                    Assert.True(Distance(waypoint, other) >= mine - 1e-6
                        || other.IslandId == anchor.IslandId,
                        "a crossing point is anchored to " + anchor.IslandId
                        + " but " + other.IslandId + " is nearer");
                }
            }
        }

        [Fact]
        public void The_route_is_a_pure_function_of_the_islands_so_a_restart_reproduces_it()
        {
            // The property everything else rests on. Note the SCRAMBLED input: the
            // order zones and islands arrive in must not be able to change the
            // migration, or two boots of the same world would fly different routes.
            IReadOnlyList<SkyWhaleWaypoint> first = SkyWhaleRoute.Build(Block());
            IReadOnlyList<SkyWhaleWaypoint> second = SkyWhaleRoute.Build(
                Block().Reverse().Select(zone =>
                    new SkyWhaleZone(zone.Region, zone.Islands.Reverse().ToArray())));

            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i], second[i]);
            }
        }

        [Fact]
        public void A_zone_with_a_single_island_is_still_on_the_route()
        {
            // A CHANGE, and a deliberate one: a cell used to need three islands to
            // carry a whale of its own and was otherwise silently skipped. On a
            // world route its islands are simply more control points, so no cell can
            // be left out of the migration for being small.
            IReadOnlyList<SkyWhaleZone> zones = Block()
                .Append(new SkyWhaleZone(new RegionId("lonely"), new[] { At("lonely1", 0.0, 6000.0) }))
                .ToArray();

            IReadOnlyList<SkyWhaleWaypoint> route = SkyWhaleRoute.Build(zones);
            Assert.Contains(route, waypoint => waypoint.IslandId.Value == "lonely1");
        }

        [Fact]
        public void An_empty_world_produces_no_route_rather_than_throwing()
        {
            Assert.Empty(SkyWhaleRoute.Build(Array.Empty<SkyWhaleZone>()));
            Assert.Empty(SkyWhaleRoute.Build(new[]
            {
                new SkyWhaleZone(new RegionId("empty"), Array.Empty<SkyWhaleWaypoint>()),
            }));
        }

        [Fact]
        public void The_real_world_route_crosses_every_released_cell_and_stays_evenly_spaced()
        {
            // THE MEASUREMENT, against the preserved catalogue rather than a square.
            IReadOnlyList<ReleaseIslandRecord> islands =
                ReleaseWorldRolloutPolicy.Select("tier1");
            IReadOnlyList<SkyWhaleWaypoint> route =
                SkyWhaleRoute.Build(SkyWhalePlan.ZonesOf(islands));

            int transit = route.Count(waypoint => waypoint.IsTransit);
            _output.WriteLine(route.Count + " waypoints: " + (route.Count - transit)
                + " islands + " + transit + " crossing points");
            Assert.Equal(islands.Count, route.Count - transit);
            Assert.Equal(4, route.Where(waypoint => !waypoint.IsTransit)
                .Select(waypoint => waypoint.Region).Distinct().Count());

            List<double> legs = new List<double>();
            for (int i = 0; i < route.Count; i++)
            {
                legs.Add(Distance(route[i], route[(i + 1) % route.Count]));
            }
            legs.Sort();
            _output.WriteLine("legs " + legs[0].ToString("0") + " - "
                + legs[legs.Count - 1].ToString("0") + " m, median "
                + legs[legs.Count / 2].ToString("0") + " m");
            Assert.True(legs[legs.Count - 1] <= legs[legs.Count / 2] * 3.0,
                "the longest leg is " + legs[legs.Count - 1].ToString("0")
                + " m against a median of " + legs[legs.Count / 2].ToString("0") + " m");
        }

        private static double Distance(SkyWhaleWaypoint from, SkyWhaleWaypoint to)
        {
            double dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }
    }
}

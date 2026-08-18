using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// THE PATH ITSELF: that it is closed, that it actually visits the islands, and
    /// that it never asks the client to do something the animal cannot be shown
    /// doing.
    ///
    /// The last one is the point of most of these. The whale carries ONE animation
    /// clip with no turn state (RECOVERED), so a path with a heading discontinuity
    /// would show a 173 m creature pivoting on the spot. C1 continuity is therefore
    /// not a nicety here; it is a requirement of the asset, and it is asserted
    /// rather than assumed.
    /// </summary>
    public class SkyWhaleCircuitTests
    {
        private static readonly string Route = SkyWhaleRoute.RouteIdFor(new[] { "test" });

        /// <summary>A square ring, so every expected value can be reasoned about by hand.</summary>
        private static SkyWhaleWaypoint[] Square() => new[]
        {
            Waypoint("a", 100.0, 0.0, 100.0),
            Waypoint("b", -100.0, 0.0, 100.0),
            Waypoint("c", -100.0, 0.0, -100.0),
            Waypoint("d", 100.0, 0.0, -100.0),
        };

        private static SkyWhaleWaypoint Waypoint(string id, double x, double y, double z) =>
            new SkyWhaleWaypoint(new IslandId(id), x, y, z);

        [Fact]
        public void A_world_with_fewer_than_three_waypoints_carries_no_whale()
        {
            Assert.Null(SkyWhaleCircuit.Build(Route, new[]
            {
                Waypoint("a", 0.0, 0.0, 0.0),
                Waypoint("b", 100.0, 0.0, 0.0),
            }));
            Assert.Null(SkyWhaleCircuit.Build(Route, Array.Empty<SkyWhaleWaypoint>()));
        }

        [Fact]
        public void A_degenerate_route_of_coincident_islands_carries_no_whale()
        {
            // Zero chord length would divide by zero into the circuit period. It
            // returns null rather than throwing: a district selection must never be
            // able to stop a server booting.
            Assert.Null(SkyWhaleCircuit.Build(Route, new[]
            {
                Waypoint("a", 10.0, 20.0, 30.0),
                Waypoint("b", 10.0, 20.0, 30.0),
                Waypoint("c", 10.0, 20.0, 30.0),
            }));
        }

        [Fact]
        public void The_circuit_flies_the_order_it_is_handed_rather_than_re_sorting_it()
        {
            // THE SPLIT this rework introduced. Ordering used to happen here, when
            // a circuit was one region's ring and "bearing about the centroid" was
            // the whole answer. A world route has to order the ZONES as well, so
            // that decision moved to SkyWhaleRoute and is tested there - and this
            // type must now take what it is handed VERBATIM, because the map is
            // published from the same order and a quiet re-sort here would make the
            // browser fly a different migration than the game.
            SkyWhaleWaypoint[] deliberate =
            {
                Waypoint("c", -100.0, 0.0, -100.0),
                Waypoint("a", 100.0, 0.0, 100.0),
                Waypoint("d", 100.0, 0.0, -100.0),
                Waypoint("b", -100.0, 0.0, 100.0),
            };

            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Route, deliberate)!;
            Assert.Equal(new[] { "c", "a", "d", "b" },
                circuit.Waypoints.Select(waypoint => waypoint.IslandId.Value));
        }

        [Fact]
        public void The_circuit_period_is_its_chord_length_at_the_tuned_speed()
        {
            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Route, Square())!;
            Assert.Equal(800.0, circuit.LengthMetres, 9);
            Assert.Equal(800.0 / SkyWhalePolicy.MetresPerSecond, circuit.CircuitSeconds, 9);
        }

        [Fact]
        public void The_whale_is_exactly_over_an_island_at_that_islands_lap_fraction()
        {
            // Catmull-Rom INTERPOLATES its control points, and that is the whole
            // reason it was chosen: "does the whale visit my island" is an identity,
            // not a tolerance.
            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Route, Square())!;
            for (int i = 0; i < circuit.Waypoints.Count; i++)
            {
                SkyWhaleWaypoint waypoint = circuit.Waypoints[i];
                (double x, double y, double z) =
                    circuit.PositionAt((double)i / circuit.Waypoints.Count);
                Assert.Equal(waypoint.X, x, 9);
                Assert.Equal(waypoint.Y, y, 9);
                Assert.Equal(waypoint.Z, z, 9);
            }
        }

        [Fact]
        public void The_circuit_is_closed_so_a_lap_returns_to_where_it_started()
        {
            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Route, Square())!;
            (double x0, double y0, double z0) = circuit.PositionAt(0.0);
            (double x1, double y1, double z1) = circuit.PositionAt(1.0);
            Assert.Equal(x0, x1, 9);
            Assert.Equal(y0, y1, 9);
            Assert.Equal(z0, z1, 9);

            // And at a negative lap, and at a lap deep into a month of uptime.
            (double xn, double _, double zn) = circuit.PositionAt(-3.0);
            Assert.Equal(x0, xn, 9);
            Assert.Equal(z0, zn, 9);
            (double xf, double _, double zf) = circuit.PositionAt(9_999.0);
            Assert.Equal(x0, xf, 9);
            Assert.Equal(z0, zf, 9);
        }

        [Fact]
        public void The_heading_is_continuous_across_every_waypoint()
        {
            // THE ASSET REQUIREMENT. One clip, no turn state: a heading that jumped
            // at a control point would show the animal pivoting. A Catmull-Rom
            // spline is C1, so the tangent either side of a knot agrees - assert it
            // rather than trust it, on an UNEVEN ring where a broken implementation
            // would actually show.
            SkyWhaleWaypoint[] uneven =
            {
                Waypoint("a", 900.0, 40.0, 60.0),
                Waypoint("b", 120.0, 10.0, 700.0),
                Waypoint("c", -400.0, 90.0, 220.0),
                Waypoint("d", -80.0, 0.0, -650.0),
                Waypoint("e", 500.0, 55.0, -300.0),
            };
            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Route, uneven)!;

            const double Epsilon = 1e-7;
            for (int i = 0; i < circuit.Waypoints.Count; i++)
            {
                double knot = (double)i / circuit.Waypoints.Count;
                (double bx, double by, double bz) = Normalise(circuit.TangentAt(knot - Epsilon));
                (double ax, double ay, double az) = Normalise(circuit.TangentAt(knot + Epsilon));
                // The dot product of two unit headings is 1 when they agree. A
                // polyline through the same points would fail this by a wide margin
                // at every knot.
                Assert.Equal(1.0, (bx * ax) + (by * ay) + (bz * az), 5);
            }
        }

        [Fact]
        public void The_whale_never_stops_so_it_always_has_a_heading()
        {
            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Route, Square())!;
            for (int step = 0; step < 400; step++)
            {
                (double x, double y, double z) = circuit.TangentAt(step / 400.0);
                Assert.True((x * x) + (y * y) + (z * z) > 1e-6,
                    "the spline's derivative vanished at lap " + (step / 400.0));
            }
        }

        [Fact]
        public void Different_route_names_start_at_different_points_on_their_laps()
        {
            // The phase is a pure function of the route's NAME, and it decides
            // which zone the animal is in when a server comes back up.
            double a = SkyWhalePolicy.PhaseFractionFor("release-a2-region");
            double b = SkyWhalePolicy.PhaseFractionFor(Route);
            Assert.NotEqual(a, b, 6);
            Assert.InRange(a, 0.0, 1.0);
            Assert.InRange(b, 0.0, 1.0);
        }

        [Fact]
        public void The_routes_phase_survives_a_restart()
        {
            // FNV-1a rather than string.GetHashCode, which .NET randomises PER
            // PROCESS: a restarted server would otherwise re-phase the whale and a
            // returning player would find the animal in a different ZONE entirely -
            // which with one whale is not a cosmetic difference.
            Assert.Equal(
                SkyWhalePolicy.PhaseFractionFor("release-b3-region"),
                SkyWhalePolicy.PhaseFractionFor("release-b3-region"));
            Assert.Equal(0.9235563746187836,
                SkyWhalePolicy.PhaseFractionFor("release-b3-region"), 12);
        }

        [Fact]
        public void A_transit_waypoint_is_never_offered_as_somewhere_to_stand()
        {
            // NextArrivalAfter feeds the boot log's "stand on X and look up", and a
            // crossing point is over open sky with nothing under it. A route whose
            // every other point is a crossing is the cruel case.
            SkyWhaleWaypoint[] withCrossings =
            {
                Waypoint("a", 100.0, 0.0, 100.0),
                Crossing("a", 0.0, 0.0, 140.0),
                Waypoint("b", -100.0, 0.0, 100.0),
                Crossing("b", -140.0, 0.0, 0.0),
                Waypoint("c", -100.0, 0.0, -100.0),
                Crossing("c", 0.0, 0.0, -140.0),
                Waypoint("d", 100.0, 0.0, -100.0),
                Crossing("d", 140.0, 0.0, 0.0),
            };
            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Route, withCrossings)!;
            for (double t = 0.0; t < circuit.CircuitSeconds; t += circuit.CircuitSeconds / 97.0)
            {
                (IslandId island, double seconds) = circuit.NextArrivalAfter(t);
                Assert.Contains(island.Value, new[] { "a", "b", "c", "d" });
                Assert.InRange(seconds, 0.0, circuit.CircuitSeconds);
                // And it really IS over that island when it said it would be.
                SkyWhaleWaypoint expected = withCrossings.First(
                    waypoint => !waypoint.IsTransit && waypoint.IslandId == island);
                (double x, double _, double z) = circuit.PositionAtTime(t + seconds);
                Assert.Equal(expected.X, x, 6);
                Assert.Equal(expected.Z, z, 6);
            }
        }

        [Fact]
        public void The_whereabouts_call_zones_apart_from_the_crossings_between_them()
        {
            // The headline fact of the single-whale design, pinned on a route built
            // by hand so every expected answer is obvious: two zones of three
            // islands, one crossing point each way.
            RegionId north = new RegionId("north"), south = new RegionId("south");
            SkyWhaleWaypoint[] route =
            {
                In(north, "n1", 0.0, 0.0, 1000.0),
                In(north, "n2", 200.0, 0.0, 1200.0),
                In(north, "n3", -200.0, 0.0, 1200.0),
                Between(north, "n3", 0.0, 0.0, 0.0),
                In(south, "s1", 0.0, 0.0, -1000.0),
                In(south, "s2", 200.0, 0.0, -1200.0),
                In(south, "s3", -200.0, 0.0, -1200.0),
                Between(south, "s3", 0.0, 0.0, -50.0),
            };
            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Route, route,
                phaseFraction: 0.0)!;
            double perSegment = circuit.CircuitSeconds / route.Length;

            // Mid-way along the n1->n2 leg: in the north, next island n2.
            SkyWhaleWhereabouts inside = circuit.WhereAt(perSegment * 0.5);
            Assert.False(inside.InTransit);
            Assert.Equal(north, inside.Region);
            Assert.Equal(new IslandId("n2"), inside.NextIsland);
            Assert.Equal(south, inside.NextRegion);
            Assert.Equal(new IslandId("s1"), inside.NextRegionIsland);

            // Mid-way along the crossing: no zone at all, heading south.
            SkyWhaleWhereabouts crossing = circuit.WhereAt(perSegment * 3.5);
            Assert.True(crossing.InTransit);
            Assert.Equal(default, crossing.Region);
            Assert.Equal(south, crossing.NextRegion);
            Assert.Equal(new IslandId("s1"), crossing.NextRegionIsland);
            Assert.Equal(new IslandId("s1"), crossing.NextIsland);
            // Half a segment from the first southern island.
            Assert.Equal(perSegment * 0.5, crossing.SecondsToNextRegion, 6);

            // The leg OUT of a zone counts as transit, not as still being in it -
            // the animal has left the last rock of the cell.
            Assert.True(circuit.WhereAt(perSegment * 2.5).InTransit);
        }

        [Fact]
        public void A_one_zone_world_says_it_has_no_next_zone_rather_than_naming_itself()
        {
            RegionId only = new RegionId("only");
            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Route, new[]
            {
                In(only, "a", 100.0, 0.0, 100.0),
                In(only, "b", -100.0, 0.0, 100.0),
                In(only, "c", -100.0, 0.0, -100.0),
            }, phaseFraction: 0.0)!;

            SkyWhaleWhereabouts where = circuit.WhereAt(1.0);
            Assert.Equal(only, where.Region);
            Assert.Equal(default, where.NextRegion);
            Assert.Equal(0.0, where.SecondsToNextRegion);
        }

        private static SkyWhaleWaypoint Crossing(string anchor, double x, double y, double z) =>
            new SkyWhaleWaypoint(new IslandId(anchor), x, y, z, default, true);

        private static SkyWhaleWaypoint In(RegionId region, string id,
            double x, double y, double z) =>
            new SkyWhaleWaypoint(new IslandId(id), x, y, z, region, false);

        private static SkyWhaleWaypoint Between(RegionId region, string anchor,
            double x, double y, double z) =>
            new SkyWhaleWaypoint(new IslandId(anchor), x, y, z, region, true);

        private static (double X, double Y, double Z) Normalise(
            (double X, double Y, double Z) vector)
        {
            double length = Math.Sqrt((vector.X * vector.X)
                + (vector.Y * vector.Y) + (vector.Z * vector.Z));
            return (vector.X / length, vector.Y / length, vector.Z / length);
        }
    }
}

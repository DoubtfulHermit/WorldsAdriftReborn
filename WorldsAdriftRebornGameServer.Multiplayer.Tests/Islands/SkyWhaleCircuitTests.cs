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
        private static readonly RegionId Region = new RegionId("release-b3-region");

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
        public void A_region_with_fewer_than_three_islands_carries_no_whale()
        {
            Assert.Null(SkyWhaleCircuit.Build(Region, new[]
            {
                Waypoint("a", 0.0, 0.0, 0.0),
                Waypoint("b", 100.0, 0.0, 0.0),
            }));
            Assert.Null(SkyWhaleCircuit.Build(Region, Array.Empty<SkyWhaleWaypoint>()));
        }

        [Fact]
        public void A_degenerate_ring_of_coincident_islands_carries_no_whale()
        {
            // Zero chord length would divide by zero into the circuit period. It
            // returns null rather than throwing: a district selection must never be
            // able to stop a server booting.
            Assert.Null(SkyWhaleCircuit.Build(Region, new[]
            {
                Waypoint("a", 10.0, 20.0, 30.0),
                Waypoint("b", 10.0, 20.0, 30.0),
                Waypoint("c", 10.0, 20.0, 30.0),
            }));
        }

        [Fact]
        public void The_ring_is_ordered_by_bearing_about_the_centroid_whatever_order_it_arrives_in()
        {
            SkyWhaleWaypoint[] scrambled =
            {
                Waypoint("c", -100.0, 0.0, -100.0),
                Waypoint("a", 100.0, 0.0, 100.0),
                Waypoint("d", 100.0, 0.0, -100.0),
                Waypoint("b", -100.0, 0.0, 100.0),
            };

            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Region, scrambled)!;
            Assert.Equal(new[] { "c", "d", "a", "b" },
                circuit.Waypoints.Select(waypoint => waypoint.IslandId.Value));

            // The same set in a different arrival order must produce the same ring,
            // because the id blocks and therefore the wire depend on it.
            SkyWhaleCircuit again = SkyWhaleCircuit.Build(Region, Square())!;
            Assert.Equal(circuit.Waypoints.Select(waypoint => waypoint.IslandId),
                again.Waypoints.Select(waypoint => waypoint.IslandId));
        }

        [Fact]
        public void The_circuit_period_is_its_chord_length_at_the_tuned_speed()
        {
            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Region, Square())!;
            Assert.Equal(800.0, circuit.LengthMetres, 9);
            Assert.Equal(800.0 / SkyWhalePolicy.MetresPerSecond, circuit.CircuitSeconds, 9);
        }

        [Fact]
        public void The_whale_is_exactly_over_an_island_at_that_islands_lap_fraction()
        {
            // Catmull-Rom INTERPOLATES its control points, and that is the whole
            // reason it was chosen: "does the whale visit my island" is an identity,
            // not a tolerance.
            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Region, Square())!;
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
            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Region, Square())!;
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
            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Region, uneven)!;

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
            SkyWhaleCircuit circuit = SkyWhaleCircuit.Build(Region, Square())!;
            for (int step = 0; step < 400; step++)
            {
                (double x, double y, double z) = circuit.TangentAt(step / 400.0);
                Assert.True((x * x) + (y * y) + (z * z) > 1e-6,
                    "the spline's derivative vanished at lap " + (step / 400.0));
            }
        }

        [Fact]
        public void Two_regions_start_at_different_points_on_their_laps()
        {
            // The phase is what stops every whale in the world being over an island
            // - and therefore calling - at the same instant.
            double a = SkyWhalePolicy.PhaseFractionFor(new RegionId("release-a2-region"));
            double b = SkyWhalePolicy.PhaseFractionFor(new RegionId("release-b3-region"));
            Assert.NotEqual(a, b, 6);
            Assert.InRange(a, 0.0, 1.0);
            Assert.InRange(b, 0.0, 1.0);
        }

        [Fact]
        public void A_regions_phase_survives_a_restart()
        {
            // FNV-1a rather than string.GetHashCode, which .NET randomises PER
            // PROCESS: a restarted server would otherwise re-phase every whale and a
            // returning player would find the animal somewhere else entirely.
            Assert.Equal(
                SkyWhalePolicy.PhaseFractionFor(new RegionId("release-b3-region")),
                SkyWhalePolicy.PhaseFractionFor(new RegionId("release-b3-region")));
            Assert.Equal(0.9235563746187836,
                SkyWhalePolicy.PhaseFractionFor(new RegionId("release-b3-region")), 12);
        }

        private static (double X, double Y, double Z) Normalise(
            (double X, double Y, double Z) vector)
        {
            double length = Math.Sqrt((vector.X * vector.X)
                + (vector.Y * vector.Y) + (vector.Z * vector.Z));
            return (vector.X / length, vector.Y / length, vector.Z / length);
        }
    }
}

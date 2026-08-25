using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// Pins the client-side attitude spline that replaces retail's bare slerp.
    ///
    /// The wire has no angular velocity (1130 ShipControlPoint carries LINEAR
    /// velocity only), so retail interpolates position as a C1 cubic Hermite and
    /// attitude as a C0 slerp. These tests pin both halves of the fix: that the
    /// squad path removes the angular-rate kink at a control point, and that it
    /// is the exact identity everywhere it must not act.
    /// </summary>
    public class ShipRotationSplinePolicyTests
    {
        private const double SendInterval = 0.24;

        // A 240 ms point of a hard turn is single-digit degrees; use a rate that
        // makes the arithmetic recognisable rather than a realistic 20 deg/s.
        private static SplineRotationSample Yaw(double time, double degrees)
        {
            double half = degrees * Math.PI / 360.0;
            return new SplineRotationSample(time, 0.0, Math.Sin(half), 0.0, Math.Cos(half));
        }

        private static double YawDegrees(SplineRotationSample q)
        {
            return 2.0 * Math.Atan2(q.Y, q.W) * (180.0 / Math.PI);
        }

        private static SplineRotationSample StockSlerp(
            SplineRotationSample a, SplineRotationSample b, double t)
        {
            // A yaw-only pair never crosses a hemisphere here, so the shortest-path
            // slerp retail runs is reproduced by the plain great-circle form.
            double dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            if (dot > 1.0) dot = 1.0;
            double theta = Math.Acos(dot);
            if (theta < 1e-12)
            {
                return b;
            }
            double wa = Math.Sin((1.0 - t) * theta) / Math.Sin(theta);
            double wb = Math.Sin(t * theta) / Math.Sin(theta);
            return new SplineRotationSample(
                b.Time,
                a.X * wa + b.X * wb,
                a.Y * wa + b.Y * wb,
                a.Z * wa + b.Z * wb,
                a.W * wa + b.W * wb);
        }

        // ---- the identity cases: where this must change nothing ----------------

        [Fact]
        public void A_steady_turn_reproduces_the_slerp_retail_already_draws()
        {
            // Constant 25 deg/s about yaw, evenly stamped: the two weighted
            // tangent logs cancel, so squad collapses to slerp. A held turn is
            // already smooth and must not be disturbed.
            const double rate = 25.0;
            var previous = Yaw(0.00, 0.0);
            var from = Yaw(SendInterval, rate * SendInterval);
            var to = Yaw(2 * SendInterval, rate * 2 * SendInterval);
            var next = Yaw(3 * SendInterval, rate * 3 * SendInterval);

            for (double param = 0.0; param <= 1.0; param += 0.05)
            {
                Assert.True(ShipRotationSplinePolicy.TrySmooth(
                    from, to, true, previous, true, next, param, out var smoothed));

                var stock = StockSlerp(from, to, param);
                Assert.Equal(YawDegrees(stock), YawDegrees(smoothed), 9);
            }
        }

        [Fact]
        public void A_steady_turn_on_UNEVEN_stamps_still_reproduces_slerp()
        {
            // The non-uniform weights exist for this: the same constant rate
            // sampled at 180/240/300 ms must still collapse to slerp. A uniform
            // squad would bend the arc here and invent a wobble of its own.
            const double rate = 25.0;
            double[] times = { 0.0, 0.18, 0.42, 0.72 };
            var previous = Yaw(times[0], rate * times[0]);
            var from = Yaw(times[1], rate * times[1]);
            var to = Yaw(times[2], rate * times[2]);
            var next = Yaw(times[3], rate * times[3]);

            for (double param = 0.0; param <= 1.0; param += 0.05)
            {
                Assert.True(ShipRotationSplinePolicy.TrySmooth(
                    from, to, true, previous, true, next, param, out var smoothed));

                var stock = StockSlerp(from, to, param);
                Assert.Equal(YawDegrees(stock), YawDegrees(smoothed), 9);
            }
        }

        [Fact]
        public void Straight_flight_is_untouched()
        {
            var previous = Yaw(0.00, 30.0);
            var from = Yaw(0.24, 30.0);
            var to = Yaw(0.48, 30.0);
            var next = Yaw(0.72, 30.0);

            Assert.True(ShipRotationSplinePolicy.TrySmooth(
                from, to, true, previous, true, next, 0.5, out var smoothed));
            Assert.Equal(30.0, YawDegrees(smoothed), 9);
        }

        // ---- the correction: the kink at a control point --------------------

        /// <summary>
        /// Angular rate in degrees per second at the very end of the segment that
        /// ARRIVES at a point, and at the very start of the segment that LEAVES
        /// it. Retail's slerp makes these two different numbers; that step is the
        /// defect, and it is what a lever arm multiplies into visible shake.
        /// </summary>
        private static void RatesAcrossJoin(
            Func<SplineRotationSample, SplineRotationSample, double, double> sample,
            SplineRotationSample a, SplineRotationSample b, SplineRotationSample c,
            out double arriving, out double leaving)
        {
            const double dp = 1e-6;
            double inSpan = b.Time - a.Time;
            double outSpan = c.Time - b.Time;
            arriving = (sample(a, b, 1.0) - sample(a, b, 1.0 - dp)) / (dp * inSpan);
            leaving = (sample(b, c, dp) - sample(b, c, 0.0)) / (dp * outSpan);
        }

        [Fact]
        public void Retail_slerp_steps_the_angular_rate_when_the_turn_rate_changes()
        {
            // Pins the DEFECT. Winding into a turn: 2 deg over the first point,
            // 8 deg over the second. Slerp plays each segment at its own constant
            // rate, so the rate jumps at the join instead of ramping.
            var a = Yaw(0.00, 0.0);
            var b = Yaw(0.24, 2.0);
            var c = Yaw(0.48, 10.0);

            RatesAcrossJoin(
                (x, y, t) => YawDegrees(StockSlerp(x, y, t)),
                a, b, c, out double arriving, out double leaving);

            Assert.Equal(2.0 / SendInterval, arriving, 3);
            Assert.Equal(8.0 / SendInterval, leaving, 3);
            Assert.True(Math.Abs(leaving - arriving) > 20.0,
                "retail's attitude rate should step by 25 deg/s at this join");
        }

        [Fact]
        public void The_spline_matches_the_angular_rate_across_a_control_point()
        {
            // Pins the CORRECTION on the same data. s_b is built from a, b and c
            // alone, so the segment arriving at b and the segment leaving b agree
            // on the rate there: C1 instead of C0.
            var a = Yaw(0.00, 0.0);
            var b = Yaw(0.24, 2.0);
            var c = Yaw(0.48, 10.0);
            var d = Yaw(0.72, 22.0);

            RatesAcrossJoin(
                (x, y, t) =>
                {
                    bool arrivingSegment = x.Time == a.Time;
                    Assert.True(ShipRotationSplinePolicy.TrySmooth(
                        x, y,
                        true, arrivingSegment ? Yaw(-0.24, 0.0) : a,
                        true, arrivingSegment ? c : d,
                        t, out var smoothed));
                    return YawDegrees(smoothed);
                },
                a, b, c, out double arriving, out double leaving);

            // Retail steps 25 deg/s here (the test above). What is left is the
            // one-sided finite-difference error of the probe itself, not a kink.
            Assert.True(Math.Abs(leaving - arriving) < 0.05,
                "attitude rate should be continuous across the control point, was "
                + arriving + " -> " + leaving);
        }

        [Fact]
        public void The_spline_never_moves_the_authoritative_endpoints()
        {
            var previous = Yaw(0.00, 0.0);
            var from = Yaw(0.24, 2.0);
            var to = Yaw(0.48, 10.0);
            var next = Yaw(0.72, 22.0);

            Assert.True(ShipRotationSplinePolicy.TrySmooth(
                from, to, true, previous, true, next, 0.0, out var atStart));
            Assert.True(ShipRotationSplinePolicy.TrySmooth(
                from, to, true, previous, true, next, 1.0, out var atEnd));

            Assert.Equal(2.0, YawDegrees(atStart), 9);
            Assert.Equal(10.0, YawDegrees(atEnd), 9);
        }

        [Fact]
        public void The_spline_stays_inside_the_arc_it_is_smoothing()
        {
            // Bounded deviation: a smoothed sample may lead or lag the chord but
            // must not swing outside the neighbourhood of the two server poses.
            var previous = Yaw(0.00, 0.0);
            var from = Yaw(0.24, 2.0);
            var to = Yaw(0.48, 10.0);
            var next = Yaw(0.72, 22.0);

            for (double param = 0.0; param <= 1.0; param += 0.02)
            {
                Assert.True(ShipRotationSplinePolicy.TrySmooth(
                    from, to, true, previous, true, next, param, out var smoothed));
                double yaw = YawDegrees(smoothed);
                Assert.InRange(yaw, 2.0 - 8.0, 10.0 + 8.0);
            }
        }

        // ---- buffer edges and fail-safe -------------------------------------

        [Fact]
        public void Both_neighbours_missing_falls_back_to_stock()
        {
            Assert.False(ShipRotationSplinePolicy.TrySmooth(
                Yaw(0.24, 2.0), Yaw(0.48, 10.0),
                false, default, false, default, 0.5, out _));
        }

        [Fact]
        public void One_neighbour_missing_still_smooths_and_still_hits_the_endpoints()
        {
            var from = Yaw(0.24, 2.0);
            var to = Yaw(0.48, 10.0);
            var next = Yaw(0.72, 22.0);

            Assert.True(ShipRotationSplinePolicy.TrySmooth(
                from, to, false, default, true, next, 0.5, out var mid));
            Assert.True(ShipRotationSplinePolicy.TrySmooth(
                from, to, false, default, true, next, 0.0, out var start));
            Assert.True(ShipRotationSplinePolicy.TrySmooth(
                from, to, false, default, true, next, 1.0, out var end));

            Assert.Equal(2.0, YawDegrees(start), 9);
            Assert.Equal(10.0, YawDegrees(end), 9);
            Assert.InRange(YawDegrees(mid), 2.0, 10.0);
        }

        [Fact]
        public void A_neighbour_across_a_long_gap_is_not_trusted_as_a_tangent()
        {
            // The buffer was cleared or halted across this gap, so the implied
            // rate describes a discontinuity, not the turn. With the other side
            // absent too, that leaves nothing and the caller keeps stock slerp.
            var stale = Yaw(-4.00, 0.0);
            Assert.False(ShipRotationSplinePolicy.TrySmooth(
                Yaw(0.24, 2.0), Yaw(0.48, 10.0),
                true, stale, false, default, 0.5, out _));
        }

        [Fact]
        public void A_neighbour_at_or_before_the_segment_is_rejected()
        {
            var notEarlier = Yaw(0.24, 0.0);
            Assert.False(ShipRotationSplinePolicy.TrySmooth(
                Yaw(0.24, 2.0), Yaw(0.48, 10.0),
                true, notEarlier, false, default, 0.5, out _));
        }

        [Fact]
        public void A_teleport_sized_step_is_left_to_stock_slerp()
        {
            Assert.False(ShipRotationSplinePolicy.TrySmooth(
                Yaw(0.24, 0.0), Yaw(0.48, 170.0),
                true, Yaw(0.0, -2.0), true, Yaw(0.72, 175.0), 0.5, out _));
        }

        [Fact]
        public void A_teleport_sized_NEIGHBOUR_is_dropped_without_losing_the_other_side()
        {
            // The far neighbour is nonsense; the near one is fine. The segment is
            // still smoothed from the good side and still hits its endpoints.
            var from = Yaw(0.24, 2.0);
            var to = Yaw(0.48, 10.0);
            Assert.True(ShipRotationSplinePolicy.TrySmooth(
                from, to, true, Yaw(0.0, 0.0), true, Yaw(0.72, 175.0), 1.0, out var end));
            Assert.Equal(10.0, YawDegrees(end), 9);
        }

        [Fact]
        public void A_non_advancing_segment_falls_back_to_stock()
        {
            Assert.False(ShipRotationSplinePolicy.TrySmooth(
                Yaw(0.48, 2.0), Yaw(0.48, 10.0),
                true, Yaw(0.24, 0.0), true, Yaw(0.72, 22.0), 0.5, out _));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void Non_finite_input_falls_back_to_stock(double poison)
        {
            var poisoned = new SplineRotationSample(0.48, 0.0, poison, 0.0, 1.0);
            Assert.False(ShipRotationSplinePolicy.TrySmooth(
                Yaw(0.24, 2.0), poisoned,
                true, Yaw(0.0, 0.0), true, Yaw(0.72, 22.0), 0.5, out _));

            Assert.False(ShipRotationSplinePolicy.TrySmooth(
                Yaw(0.24, 2.0), Yaw(0.48, 10.0),
                true, Yaw(0.0, 0.0), true, Yaw(0.72, 22.0), poison, out _));
        }

        [Fact]
        public void A_zero_quaternion_falls_back_to_stock()
        {
            var zero = new SplineRotationSample(0.48, 0.0, 0.0, 0.0, 0.0);
            Assert.False(ShipRotationSplinePolicy.TrySmooth(
                Yaw(0.24, 2.0), zero,
                true, Yaw(0.0, 0.0), true, Yaw(0.72, 22.0), 0.5, out _));
        }

        [Fact]
        public void A_hemisphere_flipped_neighbour_is_realigned_not_swung_the_long_way()
        {
            // Quaternion32 round-tripping can hand back -q for the same attitude.
            // Negating one point must not change the rendered result at all.
            var previous = Yaw(0.00, 0.0);
            var from = Yaw(0.24, 2.0);
            var to = Yaw(0.48, 10.0);
            var next = Yaw(0.72, 22.0);
            var flipped = new SplineRotationSample(
                previous.Time, -previous.X, -previous.Y, -previous.Z, -previous.W);

            Assert.True(ShipRotationSplinePolicy.TrySmooth(
                from, to, true, previous, true, next, 0.5, out var normal));
            Assert.True(ShipRotationSplinePolicy.TrySmooth(
                from, to, true, flipped, true, next, 0.5, out var viaFlip));

            Assert.Equal(YawDegrees(normal), YawDegrees(viaFlip), 9);
        }

        [Fact]
        public void The_result_stays_a_unit_quaternion()
        {
            var previous = Yaw(0.00, 0.0);
            var from = Yaw(0.24, 2.0);
            var to = Yaw(0.48, 10.0);
            var next = Yaw(0.72, 22.0);

            for (double param = 0.0; param <= 1.0; param += 0.05)
            {
                Assert.True(ShipRotationSplinePolicy.TrySmooth(
                    from, to, true, previous, true, next, param, out var smoothed));
                double square = smoothed.X * smoothed.X + smoothed.Y * smoothed.Y
                    + smoothed.Z * smoothed.Z + smoothed.W * smoothed.W;
                Assert.Equal(1.0, square, 9);
            }
        }

        [Fact]
        public void Smoothing_works_off_the_yaw_axis_too()
        {
            // Bank-roll chases yaw rate on a turning hull, so the correction must
            // not be yaw-special. A tilted axis must still hit its endpoints.
            SplineRotationSample About(double time, double degrees)
            {
                double half = degrees * Math.PI / 360.0;
                double s = Math.Sin(half);
                double n = 1.0 / Math.Sqrt(3.0);
                return new SplineRotationSample(
                    time, s * n, s * n, s * n, Math.Cos(half));
            }

            var from = About(0.24, 2.0);
            var to = About(0.48, 10.0);

            Assert.True(ShipRotationSplinePolicy.TrySmooth(
                from, to, true, About(0.0, 0.0), true, About(0.72, 22.0), 1.0, out var end));

            Assert.Equal(to.X, end.X, 9);
            Assert.Equal(to.Y, end.Y, 9);
            Assert.Equal(to.Z, end.Z, 9);
            Assert.Equal(to.W, end.W, 9);
        }
    }
}

using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The ferry path. Every property here is one the client will silently punish
    /// if it is wrong: a timestamp that regresses or crowds its predecessor is
    /// dropped, a non-zero final velocity drifts the hull past the destination
    /// forever, a position off the straight line is a ship that flies a curve
    /// nobody asked for.
    /// </summary>
    public class ShipFerryPlanTests
    {
        private const double Step = ShipMotionPolicy.SendIntervalSeconds; // 0.24
        private const double Speed = 15.0;
        private const long Anchor = 5_000_000;

        // A 300 m hop due north (+Z), the service's default shape.
        private static readonly FixedPointPosition Start = FixedPointPosition.FromMetres(100.0, 50.0, -1000.0);
        private static readonly FixedPointPosition End = FixedPointPosition.FromMetres(100.0, 50.0, -700.0);

        private static ShipFerryPlan Plan() => new ShipFerryPlan(Start, End, Speed, Step, Anchor);

        [Fact]
        public void The_length_is_the_straight_line_distance()
        {
            Assert.Equal(300.0, Plan().LengthMetres, 3);
        }

        [Fact]
        public void Sample_zero_is_the_start_at_the_anchor_timestamp_and_full_cruise_velocity()
        {
            ShipControlPointSpec s0 = Plan().Spec(0);

            Assert.Equal(Anchor, s0.TimestampMs);
            Assert.Equal(Start.MetresX, s0.X, 3);
            Assert.Equal(Start.MetresY, s0.Y, 3);
            Assert.Equal(Start.MetresZ, s0.Z, 3);

            // Moving north at the cruise speed - the path derivative.
            Assert.Equal(0.0, s0.Vx, 6);
            Assert.Equal(0.0, s0.Vy, 6);
            Assert.Equal(Speed, s0.Vz, 6);
            Assert.False(s0.Arrived);
        }

        [Fact]
        public void Timestamps_are_monotonic_and_exactly_one_step_apart()
        {
            ShipFerryPlan plan = Plan();
            long previous = plan.Spec(0).TimestampMs;
            for (long i = 1; i < 2000; i++)
            {
                long ms = plan.Spec(i).TimestampMs;
                Assert.True(ShipMotionPolicy.IsLegalSeparation(previous, ms),
                    "pair " + previous + "->" + ms + " must clear the client's reject floor");
                previous = ms;
            }
        }

        [Fact]
        public void Every_point_lies_on_the_segment_between_start_and_end()
        {
            ShipFerryPlan plan = Plan();
            // X and Y never move on a due-north hop; Z climbs from start to end.
            double previousZ = Start.MetresZ;
            for (long i = 0; i <= plan.ArrivalIndex + 5; i++)
            {
                ShipControlPointSpec s = plan.Spec(i);
                Assert.Equal(Start.MetresX, s.X, 3);
                Assert.Equal(Start.MetresY, s.Y, 3);
                Assert.InRange(s.Z, Start.MetresZ - 1e-6, End.MetresZ + 1e-6);
                Assert.True(s.Z >= previousZ - 1e-6, "Z must not go backwards");
                previousZ = s.Z;
            }
        }

        [Fact]
        public void The_arrival_index_lands_the_ship_exactly_on_the_destination_at_rest()
        {
            ShipFerryPlan plan = Plan();
            ShipControlPointSpec arrival = plan.Spec(plan.ArrivalIndex);

            Assert.True(arrival.Arrived);
            Assert.Equal(End.MetresX, arrival.X, 3);
            Assert.Equal(End.MetresY, arrival.Y, 3);
            Assert.Equal(End.MetresZ, arrival.Z, 3);

            // Zero velocity is what makes the resting point safe to repeat: every
            // extrapolation the client does from it lands on the same place.
            Assert.Equal(0.0, arrival.Vx, 6);
            Assert.Equal(0.0, arrival.Vy, 6);
            Assert.Equal(0.0, arrival.Vz, 6);
        }

        [Fact]
        public void The_sample_before_arrival_has_not_yet_arrived()
        {
            ShipFerryPlan plan = Plan();
            Assert.True(plan.ArrivalIndex > 0);
            Assert.False(plan.Spec(plan.ArrivalIndex - 1).Arrived);
        }

        [Fact]
        public void Points_past_arrival_repeat_the_resting_point_but_keep_advancing_the_timestamp()
        {
            ShipFerryPlan plan = Plan();
            ShipControlPointSpec arrival = plan.Spec(plan.ArrivalIndex);
            ShipControlPointSpec later = plan.Spec(plan.ArrivalIndex + 7);

            Assert.True(later.Arrived);
            Assert.Equal(arrival.X, later.X, 6);
            Assert.Equal(arrival.Y, later.Y, 6);
            Assert.Equal(arrival.Z, later.Z, 6);
            Assert.Equal(0.0, later.Vz, 6);
            Assert.True(later.TimestampMs > arrival.TimestampMs);
            Assert.True(ShipMotionPolicy.IsLegalSeparation(arrival.TimestampMs, plan.Spec(plan.ArrivalIndex + 1).TimestampMs));
        }

        [Fact]
        public void The_cruise_speed_is_actually_the_speed()
        {
            // Distance between two consecutive cruising points / their time delta
            // must equal the configured speed.
            ShipFerryPlan plan = Plan();
            ShipControlPointSpec a = plan.Spec(1);
            ShipControlPointSpec b = plan.Spec(2);
            double d = System.Math.Sqrt(
                (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y) + (b.Z - a.Z) * (b.Z - a.Z));
            Assert.Equal(Speed * Step, d, 4);
        }

        [Fact]
        public void A_zero_length_plan_is_a_resting_point_at_the_start_not_a_crash()
        {
            ShipFerryPlan plan = new ShipFerryPlan(Start, Start, Speed, Step, Anchor);
            Assert.Equal(0, plan.ArrivalIndex);

            ShipControlPointSpec s = plan.Spec(3);
            Assert.True(s.Arrived);
            Assert.Equal(Start.MetresX, s.X, 6);
            Assert.Equal(Start.MetresZ, s.Z, 6);
            Assert.Equal(0.0, s.Vz, 6);
        }

        [Fact]
        public void A_negative_index_throws_rather_than_inventing_a_point_in_the_past()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Plan().Spec(-1));
        }

        [Fact]
        public void A_non_positive_step_or_speed_is_rejected_at_construction()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ShipFerryPlan(Start, End, Speed, 0.0, Anchor));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ShipFerryPlan(Start, End, 0.0, Step, Anchor));
        }
    }
}

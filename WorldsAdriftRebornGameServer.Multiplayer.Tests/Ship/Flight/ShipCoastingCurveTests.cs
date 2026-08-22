using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// End-to-end coast curves for the recovered two-stage GetDrag calculation.
    /// These are intentionally time AND distance assertions: a model can appear to
    /// stop at the right moment while still carrying a ship an implausible distance.
    /// </summary>
    public class ShipCoastingCurveTests
    {
        private const double ServerStepSeconds = 0.24;

        private static (double TimeSeconds, double DistanceMetres) Coast(
            double initialSpeedMps, double dtSeconds = ServerStepSeconds)
        {
            double speed = initialSpeedMps;
            double elapsed = 0.0;
            double distance = 0.0;
            int guard = 0;
            while (speed != 0.0 && guard++ < 100_000)
            {
                double next = ShipForceModel.StepSpeed(speed, 0.0, dtSeconds);
                distance += 0.5 * (Math.Abs(speed) + Math.Abs(next)) * dtSeconds;
                speed = next;
                elapsed += dtSeconds;
            }

            Assert.True(guard < 100_000, "coast never reached exact rest");
            return (elapsed, distance);
        }

        [Fact]
        public void Residual_drag_is_present_above_one_metre_per_second()
        {
            const double speed = 4.11;
            const double dt = ServerStepSeconds;
            double primaryAccel = ShipForceModel.AirResistanceCoefficient
                * Math.Pow(speed, ShipForceModel.AirResistanceExponent);
            double expected = speed
                - (primaryAccel * dt)
                - (ShipForceModel.LowSpeedSettleAccelMps2 * dt);

            Assert.Equal(expected, ShipForceModel.StepSpeed(speed, 0.0, dt), 12);
        }

        [Fact]
        public void Below_point_one_retail_uses_only_the_residual_correction()
        {
            const double speed = 0.08;
            double expected = speed
                - (ShipForceModel.LowSpeedSettleAccelMps2 * ServerStepSeconds);

            Assert.Equal(expected,
                ShipForceModel.StepSpeed(speed, 0.0, ServerStepSeconds), 12);
        }

        [Theory]
        [InlineData(0.50, 16.56, 4.09)]
        [InlineData(1.00, 31.44, 15.14)]
        [InlineData(2.00, 51.84, 44.61)]
        [InlineData(4.11, 67.68, 89.49)]
        [InlineData(8.00, 74.40, 125.92)]
        [InlineData(12.00, 76.08, 142.97)]
        [InlineData(30.00, 77.52, 167.12)]
        public void Recovered_coast_matrix_pins_time_and_distance(
            double startSpeed, double expectedTime, double expectedDistance)
        {
            (double time, double distance) = Coast(startSpeed);

            Assert.InRange(time, expectedTime - 0.001, expectedTime + 0.001);
            Assert.InRange(distance, expectedDistance - 0.01, expectedDistance + 0.01);
        }

        [Fact]
        public void Reverse_coast_is_symmetric_and_never_crosses_zero()
        {
            double speed = -4.11;
            double distance = 0.0;
            while (speed != 0.0)
            {
                double next = ShipForceModel.StepSpeed(speed, 0.0, ServerStepSeconds);
                Assert.InRange(next, speed, 0.0);
                distance += 0.5 * (Math.Abs(speed) + Math.Abs(next)) * ServerStepSeconds;
                speed = next;
            }

            Assert.InRange(distance, 89.48, 89.50);
        }

        [Fact]
        public void Server_cadence_stays_close_to_retails_fifty_hertz_curve()
        {
            (double retailTime, double retailDistance) = Coast(4.11, 0.02);
            (double serverTime, double serverDistance) = Coast(4.11, ServerStepSeconds);

            Assert.InRange(Math.Abs(serverTime - retailTime), 0.0, 0.25);
            Assert.InRange(Math.Abs(serverDistance - retailDistance), 0.0, 0.60);
        }

        [Fact]
        public void Driven_equilibrium_includes_the_recovered_residual_acceleration()
        {
            const double massKg = 3094.0;
            const double thrustNewtons = 1600.0;
            const double windAlong = 0.73;
            double expected = windAlong + Math.Pow(
                ((thrustNewtons / massKg) - ShipForceModel.LowSpeedSettleAccelMps2)
                    / ShipForceModel.AirResistanceCoefficient,
                1.0 / ShipForceModel.AirResistanceExponent);

            Assert.Equal(expected,
                ShipForceModel.PredictedSettledSpeedMps(thrustNewtons, massKg, windAlong), 12);

            double speed = 0.0;
            for (int i = 0; i < 4000; i++)
            {
                speed = ShipForceModel.StepSpeed(
                    speed, thrustNewtons / massKg, ServerStepSeconds, windAlong);
            }
            Assert.Equal(expected, speed, 6);
        }

        [Theory]
        [InlineData(0.24)]
        [InlineData(1.0)]
        [InlineData(5.0)]
        [InlineData(60.0)]
        public void Coarse_steps_cannot_make_drag_reverse_the_ship(double dtSeconds)
        {
            double forward = ShipForceModel.StepSpeed(4.11, 0.0, dtSeconds);
            double reverse = ShipForceModel.StepSpeed(-4.11, 0.0, dtSeconds);

            Assert.InRange(forward, 0.0, 4.11);
            Assert.InRange(reverse, -4.11, 0.0);
        }
    }
}

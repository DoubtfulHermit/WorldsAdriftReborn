using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// The calibration is a matrix, not a single flattering screenshot. These
    /// rows cover a light legacy hull, the reference hull, the live 3,094 kg ship
    /// that exposed the old tuning, and the recovered wind-attenuation ceiling.
    /// Every row crosses 1-4 sails with the eight cardinal/intercardinal headings.
    /// </summary>
    public class SailCalibrationMatrixTests
    {
        // The previously deployed lower member of the Update-27 balance bracket.
        private const double PreviousSailPowerNewtonsPerWind = 420.0;
        private static readonly double[] MassesKg = { 595.0, 800.0, 3094.0, 4000.0 };

        private static double PropulsionAcceleration(
            double massKg, int sails, double heading, double power) =>
            ShipForceModel.SailForwardNewtons(sails, heading, power) / massKg;

        // Exact ordinary driven equilibrium after the recovered residual drag
        // correction is included on every physics step. This is kept local so
        // sail calibration remains independent of the separate settling patch.
        private static double RetailSettled(
            double massKg, int sails, double heading, double power)
        {
            double force = ShipForceModel.SailForwardNewtons(sails, heading, power);
            double acceleration = Math.Abs(force) / massKg;
            double relative = acceleration <= ShipForceModel.LowSpeedSettleAccelMps2
                ? 0.0
                : Math.Pow(
                    (acceleration - ShipForceModel.LowSpeedSettleAccelMps2)
                        / ShipForceModel.AirResistanceCoefficient,
                    1.0 / ShipForceModel.AirResistanceExponent);
            return ShipForceModel.BaselineDriveSpeedMps(massKg)
                + (Math.Sign(force) * relative);
        }

        [Fact]
        public void Representative_mass_sail_heading_matrix_is_finite_and_bounded()
        {
            foreach (double mass in MassesKg)
            foreach (int sails in new[] { 1, 2, 3, 4 })
            for (int degrees = 0; degrees < 360; degrees += 45)
            {
                double heading = degrees * Math.PI / 180.0;
                double speed = RetailSettled(
                    mass, sails, heading, ShipForceModel.DefaultSailPowerNewtonsPerWind);
                double acceleration = PropulsionAcceleration(
                    mass, sails, heading, ShipForceModel.DefaultSailPowerNewtonsPerWind);
                Assert.True(double.IsFinite(speed), $"{mass}kg/{sails}s/{degrees}deg");
                Assert.True(double.IsFinite(acceleration),
                    $"acceleration {mass}kg/{sails}s/{degrees}deg");
                Assert.InRange(Math.Abs(speed), 0.0, 36.01); // shipped 70-knot dial
                Assert.InRange(acceleration, 0.0, 13.0);
            }
        }

        [Fact]
        public void Four_sails_put_the_reference_hull_above_fast_but_below_full_scale()
        {
            double best = double.NegativeInfinity;
            for (int degrees = 0; degrees < 360; degrees++)
                best = Math.Max(best, RetailSettled(
                    800.0, 4, degrees * Math.PI / 180.0,
                    ShipForceModel.DefaultSailPowerNewtonsPerWind));

            const double knotsPerMps = 1.94384449;
            Assert.InRange(best * knotsPerMps, 37.0, 39.0);
        }

        [Fact]
        public void The_live_heavy_hull_is_useful_but_not_fast_with_two_sails()
        {
            double slow = double.PositiveInfinity, fast = double.NegativeInfinity;
            for (int degrees = 0; degrees < 360; degrees++)
            {
                double speed = Math.Abs(RetailSettled(
                    3094.0, 2, degrees * Math.PI / 180.0,
                    ShipForceModel.DefaultSailPowerNewtonsPerWind));
                slow = Math.Min(slow, speed);
                fast = Math.Max(fast, speed);
            }
            const double knotsPerMps = 1.94384449;
            Assert.InRange(slow * knotsPerMps, 1.5, 3.0);
            Assert.InRange(fast * knotsPerMps, 16.0, 18.0);
        }

        [Fact]
        public void Live_hull_observed_heading_has_exact_before_after_calibration()
        {
            const double massKg = 3094.0;
            double heading = -139.0 * Math.PI / 180.0;

            double beforeAcceleration = PropulsionAcceleration(
                massKg, 2, heading, PreviousSailPowerNewtonsPerWind);
            double afterAcceleration = PropulsionAcceleration(
                massKg, 2, heading, ShipForceModel.DefaultSailPowerNewtonsPerWind);
            double beforeSettled = RetailSettled(
                massKg, 2, heading, PreviousSailPowerNewtonsPerWind);
            double afterSettled = RetailSettled(
                massKg, 2, heading, ShipForceModel.DefaultSailPowerNewtonsPerWind);

            Assert.Equal(0.4193458094, beforeAcceleration, 9);
            Assert.Equal(0.8386916189, afterAcceleration, 9);
            Assert.Equal(5.928805162, beforeSettled, 9);
            Assert.Equal(7.623446512, afterSettled, 9);
        }

        [Fact]
        public void Community_69_sail_observation_is_approached_without_overfitting_it()
        {
            // 595 kg legacy hull + 69 mounted sails + helm/core under the current
            // conservative 50 kg per-part placeholder. Retail part masses are lost.
            double totalMassKg = 595.0 + ((69.0 + 2.0) * 50.0);
            double best = double.NegativeInfinity;
            for (int degrees = 0; degrees < 360; degrees++)
                best = Math.Max(best, RetailSettled(
                    totalMassKg, 69, degrees * Math.PI / 180.0,
                    ShipForceModel.DefaultSailPowerNewtonsPerWind));

            const double knotsPerMps = 1.94384449;
            Assert.InRange(best * knotsPerMps, 55.0, 60.0);
            Assert.True(best < 36.01); // 70-knot instrument full scale
        }

        [Fact]
        public void Every_extra_sail_helps_but_returns_diminish()
        {
            // 135 degrees is the best integer-degree point of sail for the
            // recovered boom/keel geometry. Exact downwind is deliberately a
            // poor point of sail because the keel strips the lateral force.
            double heading = 135.0 * Math.PI / 180.0;
            double previous = ShipForceModel.BaselineDriveSpeedMps(800.0);
            double previousGain = double.PositiveInfinity;
            for (int sails = 1; sails <= 4; sails++)
            {
                double speed = RetailSettled(
                    800.0, sails, heading, ShipForceModel.DefaultSailPowerNewtonsPerWind);
                double gain = speed - previous;
                Assert.True(gain > 0.0);
                Assert.True(gain < previousGain);
                previous = speed;
                previousGain = gain;
            }
        }

        [Theory]
        [InlineData(0.0, 1.0, 0.0, 90.0)]
        [InlineData(0.0, 0.0, 1.0, 0.0)]
        [InlineData(0.0, 0.0, -1.0, 180.0)]
        [InlineData(0.0, 1.0, 1.0, 45.0)]
        public void Wind_angle_is_the_signed_bow_relative_sample(
            double heading, double windX, double windZ, double expectedDegrees)
        {
            Assert.Equal(expectedDegrees,
                ShipForceModel.WindAngleDegrees(heading, windX, windZ), 6);
        }
    }
}

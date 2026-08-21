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
        private static readonly double[] MassesKg = { 595.0, 800.0, 3094.0, 4000.0 };

        private static double Settled(double massKg, int sails, double heading)
        {
            double force = ShipForceModel.SailForwardNewtons(
                sails, heading, ShipForceModel.DefaultSailPowerNewtonsPerWind);
            double carry = ShipForceModel.BaselineDriveSpeedMps(massKg);
            return ShipForceModel.PredictedSettledSpeedMps(force, massKg, carry);
        }

        [Fact]
        public void Representative_mass_sail_heading_matrix_is_finite_and_bounded()
        {
            foreach (double mass in MassesKg)
            foreach (int sails in new[] { 1, 2, 3, 4 })
            for (int degrees = 0; degrees < 360; degrees += 45)
            {
                double speed = Settled(mass, sails, degrees * Math.PI / 180.0);
                Assert.True(double.IsFinite(speed), $"{mass}kg/{sails}s/{degrees}deg");
                Assert.InRange(Math.Abs(speed), 0.0, 36.01); // shipped 70-knot dial
            }
        }

        [Fact]
        public void Four_sails_put_the_reference_hull_at_the_shipped_fast_mark()
        {
            double best = double.NegativeInfinity;
            for (int degrees = 0; degrees < 360; degrees++)
                best = Math.Max(best, Settled(800.0, 4, degrees * Math.PI / 180.0));

            const double knotsPerMps = 1.94384449;
            Assert.InRange(best * knotsPerMps, 29.0, 31.0);
        }

        [Fact]
        public void The_live_heavy_hull_is_useful_but_not_fast_with_two_sails()
        {
            double slow = double.PositiveInfinity, fast = double.NegativeInfinity;
            for (int degrees = 0; degrees < 360; degrees++)
            {
                double speed = Math.Abs(Settled(3094.0, 2, degrees * Math.PI / 180.0));
                slow = Math.Min(slow, speed);
                fast = Math.Max(fast, speed);
            }
            const double knotsPerMps = 1.94384449;
            Assert.InRange(slow * knotsPerMps, 2.0, 3.0);
            Assert.InRange(fast * knotsPerMps, 13.0, 14.0);
        }

        [Fact]
        public void Every_extra_sail_helps_but_returns_diminish()
        {
            double heading = Math.Atan2(ShipForceModel.DefaultWindX, ShipForceModel.DefaultWindZ);
            double previous = ShipForceModel.BaselineDriveSpeedMps(800.0);
            double previousGain = double.PositiveInfinity;
            for (int sails = 1; sails <= 4; sails++)
            {
                double speed = Settled(800.0, sails, heading);
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

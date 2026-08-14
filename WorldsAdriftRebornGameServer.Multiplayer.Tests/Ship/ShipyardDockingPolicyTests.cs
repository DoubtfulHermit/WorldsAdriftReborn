using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public class ShipyardDockingPolicyTests
    {
        private static readonly FixedPointPosition Yard = FixedPointPosition.FromMetres(100, 20, -50);
        private static readonly FixedPointPosition Dock = ShipyardDockingPolicy.DockPose(Yard);

        [Fact]
        public void Settled_owned_ship_can_enter_an_empty_yard()
        {
            Assert.True(ShipyardDockingPolicy.CanDock(true, true, true, false,
                "owner", "owner", Dock, Yard));
        }

        [Fact]
        public void Occupied_foreign_moving_or_unarmed_ships_cannot_capture()
        {
            Assert.False(ShipyardDockingPolicy.CanDock(true, true, true, true, "a", "a", Dock, Yard));
            Assert.False(ShipyardDockingPolicy.CanDock(true, true, true, false, "a", "b", Dock, Yard));
            Assert.False(ShipyardDockingPolicy.CanDock(true, false, true, false, "a", "a", Dock, Yard));
            Assert.False(ShipyardDockingPolicy.CanDock(false, true, true, false, "a", "a", Dock, Yard));
        }

        [Fact]
        public void Capture_and_rearm_radii_form_a_no_churn_hysteresis_band()
        {
            FixedPointPosition tenMetresAway = FixedPointPosition.FromMetres(
                Dock.MetresX + 10, Dock.MetresY, Dock.MetresZ);
            Assert.False(ShipyardDockingPolicy.IsWithin(
                tenMetresAway, Yard, ShipyardDockingPolicy.CaptureRadiusMetres));
            Assert.True(ShipyardDockingPolicy.IsWithin(
                tenMetresAway, Yard, ShipyardDockingPolicy.RearmRadiusMetres));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.7)]
        [InlineData(-2.4)]
        public void Packed_yaw_round_trips(double yaw)
        {
            double decoded = ShipyardDockingPolicy.YawFromPacked(
                ShipyardDockingPolicy.PackedYaw(yaw));
            Assert.InRange(Math.Abs(decoded - yaw), 0, 0.004);
        }
    }
}

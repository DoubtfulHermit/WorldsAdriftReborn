using System;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public class ShipRelativeLogoutPolicyTests
    {
        [Theory]
        [InlineData(0.0)]
        [InlineData(0.73)]
        [InlineData(-2.4)]
        [InlineData(3.141592653589793)]
        public void Captured_local_point_follows_ship_translation_and_yaw(double yaw)
        {
            FixedPointPosition firstHull = FixedPointPosition.FromMetres(100, 20, -30);
            uint firstRotation = Yaw(yaw);
            FixedPointPosition player = ShipSalvagePolicy.DropPose(firstHull, firstRotation,
                FixedPointPosition.FromMetres(3.25, 2.0, -5.5), Quaternion32Packing.Identity).Position;

            ShipLogoutAnchor anchor = ShipRelativeLogoutPolicy.Capture(
                7, player, firstHull, firstRotation)!.Value;
            FixedPointPosition secondHull = FixedPointPosition.FromMetres(-800, 95, 1200);
            uint secondRotation = Yaw(yaw + 1.1);
            FixedPointPosition restored = ShipRelativeLogoutPolicy.Resolve(
                anchor, secondHull, secondRotation)!.Value;
            FixedPointPosition expected = ShipSalvagePolicy.DropPose(secondHull, secondRotation,
                FixedPointPosition.FromMetres(3.25, 2.0, -5.5), Quaternion32Packing.Identity).Position;

            Assert.InRange(Math.Abs(restored.MetresX - expected.MetresX), 0, 0.003);
            Assert.InRange(Math.Abs(restored.MetresY - expected.MetresY), 0, 0.003);
            Assert.InRange(Math.Abs(restored.MetresZ - expected.MetresZ), 0, 0.003);
        }

        [Fact]
        public void Missing_identity_or_implausible_offset_fails_closed()
        {
            FixedPointPosition origin = FixedPointPosition.FromMetres(0, 0, 0);
            Assert.Null(ShipRelativeLogoutPolicy.Capture(null, origin, origin, Quaternion32Packing.Identity));
            Assert.Null(ShipRelativeLogoutPolicy.Capture(-1, origin, origin, Quaternion32Packing.Identity));
            Assert.Null(ShipRelativeLogoutPolicy.Capture(1,
                FixedPointPosition.FromMetres(300, 0, 0), origin, Quaternion32Packing.Identity));
            Assert.Null(ShipRelativeLogoutPolicy.Resolve(
                new ShipLogoutAnchor(1, FixedPointPosition.FromMetres(0, 300, 0)),
                origin, Quaternion32Packing.Identity));
        }

        private static uint Yaw(double radians) => Quaternion32Packing.Encode(
            (float)Math.Cos(radians * 0.5), 0f, (float)Math.Sin(radians * 0.5), 0f);
    }
}

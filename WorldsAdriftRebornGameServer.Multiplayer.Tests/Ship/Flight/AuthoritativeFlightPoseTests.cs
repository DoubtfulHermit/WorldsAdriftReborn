using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class AuthoritativeFlightPoseTests
    {
        private static AuthoritativeFlightPose IdentityPoseWith(FlightAuthorityStamp stamp) =>
            new AuthoritativeFlightPose(
                stamp,
                1.0, 2.0, 3.0,
                1.0, 0.0, 0.0, 0.0,
                0.5, -0.5, 0.25,
                0.0, 0.1, 0.0);

        [Fact]
        public void A_finite_unit_quaternion_pose_with_a_valid_stamp_is_valid()
        {
            var pose = IdentityPoseWith(new FlightAuthorityStamp(12, 3));
            Assert.True(pose.IsFinite);
            Assert.True(pose.IsValid);
        }

        [Fact]
        public void An_invalid_stamp_invalidates_an_otherwise_finite_pose()
        {
            var pose = IdentityPoseWith(default);
            Assert.True(pose.IsFinite);
            Assert.False(pose.IsValid);
        }

        [Fact]
        public void Non_finite_position_or_velocity_is_rejected()
        {
            var stamp = new FlightAuthorityStamp(12, 3);
            var nanPosition = IdentityPoseWith(stamp) with { X = double.NaN };
            var infiniteVelocity = IdentityPoseWith(stamp) with { VyMps = double.PositiveInfinity };
            var nanAngular = IdentityPoseWith(stamp) with { AngVzRadPerSec = double.NaN };
            Assert.False(nanPosition.IsValid);
            Assert.False(infiniteVelocity.IsValid);
            Assert.False(nanAngular.IsValid);
        }

        [Fact]
        public void A_non_unit_quaternion_is_rejected()
        {
            var stamp = new FlightAuthorityStamp(12, 3);
            var zeroRotation = IdentityPoseWith(stamp) with { QW = 0.0 };
            var scaledRotation = IdentityPoseWith(stamp) with { QW = 1.001 };
            Assert.False(zeroRotation.IsValid);
            Assert.False(scaledRotation.IsValid);
        }

        [Fact]
        public void A_quaternion_within_tolerance_of_unit_is_accepted()
        {
            var pose = IdentityPoseWith(new FlightAuthorityStamp(12, 3)) with { QW = 1.0 + 4e-7 };
            Assert.True(pose.IsValid);
        }

        [Fact]
        public void Default_pose_is_invalid()
        {
            Assert.False(default(AuthoritativeFlightPose).IsValid);
        }
    }
}

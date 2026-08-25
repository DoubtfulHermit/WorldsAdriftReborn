using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class FlightAuthorityStampTests
    {
        [Fact]
        public void Default_stamp_is_invalid()
        {
            Assert.False(default(FlightAuthorityStamp).IsValid);
        }

        [Fact]
        public void Step_zero_in_a_live_generation_is_valid()
        {
            Assert.True(new FlightAuthorityStamp(0, 1).IsValid);
        }

        [Fact]
        public void Negative_step_or_non_positive_generation_is_invalid()
        {
            Assert.False(new FlightAuthorityStamp(-1, 1).IsValid);
            Assert.False(new FlightAuthorityStamp(5, 0).IsValid);
            Assert.False(new FlightAuthorityStamp(5, -3).IsValid);
        }

        [Fact]
        public void Equality_is_member_wise()
        {
            Assert.Equal(new FlightAuthorityStamp(7, 2), new FlightAuthorityStamp(7, 2));
            Assert.NotEqual(new FlightAuthorityStamp(7, 2), new FlightAuthorityStamp(8, 2));
            Assert.NotEqual(new FlightAuthorityStamp(7, 2), new FlightAuthorityStamp(7, 3));
        }

        [Fact]
        public void Strictly_newer_step_in_same_generation_supersedes()
        {
            var last = new FlightAuthorityStamp(10, 2);
            Assert.True(new FlightAuthorityStamp(11, 2).SupersedesWithinGeneration(last));
        }

        [Fact]
        public void Same_or_older_step_never_supersedes()
        {
            var last = new FlightAuthorityStamp(10, 2);
            Assert.False(new FlightAuthorityStamp(10, 2).SupersedesWithinGeneration(last));
            Assert.False(new FlightAuthorityStamp(9, 2).SupersedesWithinGeneration(last));
        }

        [Fact]
        public void A_different_generation_never_supersedes_within_generation()
        {
            var last = new FlightAuthorityStamp(10, 2);
            Assert.False(new FlightAuthorityStamp(11, 3).SupersedesWithinGeneration(last));
            Assert.False(new FlightAuthorityStamp(11, 1).SupersedesWithinGeneration(last));
        }

        [Fact]
        public void An_invalid_stamp_never_supersedes()
        {
            var last = new FlightAuthorityStamp(10, 2);
            Assert.False(new FlightAuthorityStamp(-1, 2).SupersedesWithinGeneration(last));
            Assert.False(new FlightAuthorityStamp(11, 0).SupersedesWithinGeneration(last));
        }
    }
}

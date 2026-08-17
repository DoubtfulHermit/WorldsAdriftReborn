using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public sealed class PlayerWorldInterestPolicyTests
    {
        [Theory]
        [InlineData(FallVerdict.InTheWorld)]
        [InlineData(FallVerdict.Descending)]
        [InlineData(FallVerdict.Rescue)]
        [InlineData(FallVerdict.RescueInFlight)]
        [InlineData(FallVerdict.GaveUp)]
        [InlineData(FallVerdict.Abandoned)]
        public void Unparented_on_foot_transform_can_drive_world_interest(FallVerdict verdict)
        {
            Assert.True(PlayerWorldInterestPolicy.MayUseTransform190602(verdict, isAboard: false));
        }

        [Fact]
        public void Parented_transform_cannot_drive_world_interest()
        {
            Assert.False(PlayerWorldInterestPolicy.MayUseTransform190602(
                FallVerdict.Parented, isAboard: false));
        }

        [Theory]
        [InlineData(FallVerdict.InTheWorld)]
        [InlineData(FallVerdict.Descending)]
        [InlineData(FallVerdict.Rescue)]
        [InlineData(FallVerdict.RescueInFlight)]
        [InlineData(FallVerdict.GaveUp)]
        [InlineData(FallVerdict.Abandoned)]
        [InlineData(FallVerdict.Parented)]
        public void Aboard_transform_never_competes_with_ship_derived_interest(FallVerdict verdict)
        {
            Assert.False(PlayerWorldInterestPolicy.MayUseTransform190602(verdict, isAboard: true));
        }
    }
}

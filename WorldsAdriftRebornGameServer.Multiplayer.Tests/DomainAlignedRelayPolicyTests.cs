using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public class DomainAlignedRelayPolicyTests
    {
        [Theory]
        [InlineData(true, false, true)]
        [InlineData(true, true, true)]
        [InlineData(false, true, true)]
        [InlineData(false, false, false)]
        public void Regular_relay_or_an_emitted_aboard_domain_releases_the_sender(
            bool regularCadenceDue, bool aboardDomainFrame, bool expected)
        {
            Assert.Equal(expected, DomainAlignedRelayPolicy.ShouldEmitSender(
                regularCadenceDue, aboardDomainFrame));
        }
    }
}

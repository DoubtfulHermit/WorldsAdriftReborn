using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public class EntitySendLedgerTests
    {
        [Fact]
        public void Send_is_recorded_before_any_component_interest_exists()
        {
            var ledger = new EntitySendLedger<string>();

            Assert.False(ledger.WasSent("loading-peer", 195));
            ledger.MarkSent("loading-peer", 195);
            Assert.True(ledger.WasSent("loading-peer", 195));
        }

        [Fact]
        public void Departed_peer_does_not_leak_visibility_to_reused_handle()
        {
            var ledger = new EntitySendLedger<string>();
            ledger.MarkSent("peer", 195);

            ledger.ForgetPeer("peer");

            Assert.False(ledger.WasSent("peer", 195));
        }
    }
}

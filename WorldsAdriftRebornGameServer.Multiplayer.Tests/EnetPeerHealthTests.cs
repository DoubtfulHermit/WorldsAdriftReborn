using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The judgement calls around the native ENetPeer health snapshot.
    ///
    /// The snapshot itself is raw Marshal reads at hardcoded offsets (see
    /// DLLCommunication.EnetPeerProbe), and a wrong offset produces confident
    /// garbage with no error anywhere. The plausibility gate is the only thing
    /// standing between that and a debugging session chasing a 3-billion-ms
    /// RTT, so its edges are pinned here.
    /// </summary>
    public class EnetPeerHealthTests
    {
        private static EnetPeerHealth Healthy(uint state = EnetPeerHealthPolicy.StateConnected, uint mtu = 1392)
        {
            return new EnetPeerHealth(
                state: state,
                roundTripTimeMs: 48,
                roundTripTimeVarianceMs: 12,
                packetsSent: 1290,
                packetsLost: 3,
                reliableDataInTransit: 1448,
                mtu: mtu);
        }

        // ------------------------------------------------------------------
        // PLAUSIBILITY - the layout tripwire
        // ------------------------------------------------------------------

        [Fact]
        public void A_connected_peer_with_a_sane_mtu_is_plausible()
        {
            Assert.True(EnetPeerHealthPolicy.IsPlausible(Healthy()));
        }

        [Fact]
        public void Every_legal_peer_state_is_plausible()
        {
            // 0 DISCONNECTED .. 9 ZOMBIE, enet 1.3.17's full enum. The gate is
            // a layout check, not a liveness check - a zombie peer read at the
            // RIGHT offsets should still report, not vanish.
            for (uint state = 0; state <= EnetPeerHealthPolicy.MaxState; state++)
            {
                Assert.True(EnetPeerHealthPolicy.IsPlausible(Healthy(state: state)));
            }
        }

        [Fact]
        public void A_state_beyond_the_enum_is_a_wrong_layout()
        {
            Assert.False(EnetPeerHealthPolicy.IsPlausible(Healthy(state: 10)));
            Assert.False(EnetPeerHealthPolicy.IsPlausible(Healthy(state: 0xDEADBEEF)));
        }

        [Fact]
        public void An_mtu_outside_the_protocol_clamp_is_a_wrong_layout()
        {
            // enet clamps MTU to 576..4096 at connect; no genuine read can be
            // outside it.
            Assert.False(EnetPeerHealthPolicy.IsPlausible(Healthy(mtu: 575)));
            Assert.False(EnetPeerHealthPolicy.IsPlausible(Healthy(mtu: 4097)));
            Assert.False(EnetPeerHealthPolicy.IsPlausible(Healthy(mtu: 0)));
            Assert.True(EnetPeerHealthPolicy.IsPlausible(Healthy(mtu: EnetPeerHealthPolicy.MinMtu)));
            Assert.True(EnetPeerHealthPolicy.IsPlausible(Healthy(mtu: EnetPeerHealthPolicy.MaxMtu)));
        }

        // ------------------------------------------------------------------
        // THE PRINTED LINE
        // ------------------------------------------------------------------

        [Fact]
        public void A_connected_peer_prints_short()
        {
            // CONNECTED is the boring case; the state is noise there.
            Assert.Equal("rtt 48+/-12ms, lost 3/1290, 1448B in-flight",
                EnetPeerHealthPolicy.Describe(Healthy()));
        }

        [Fact]
        public void Any_other_state_is_loud()
        {
            // A peer read mid-teardown (or mid-timeout) should say so.
            Assert.EndsWith(", state 7", EnetPeerHealthPolicy.Describe(Healthy(state: 7)));
        }
    }
}

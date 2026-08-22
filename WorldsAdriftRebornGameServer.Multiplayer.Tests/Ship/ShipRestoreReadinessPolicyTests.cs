using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public sealed class ShipRestoreReadinessPolicyTests
    {
        [Fact]
        public void Sent_root_without_materialized_decks_is_not_ready()
        {
            Assert.False(ShipRestoreReadinessPolicy.IsReady(
                10, new long[] { 11, 12 }, new HashSet<long> { 10, 11 }));
            Assert.True(ShipRestoreReadinessPolicy.IsReady(
                10, new long[] { 11, 12 }, new HashSet<long> { 10, 11, 12 }));
        }

        [Fact]
        public void A_bare_hull_is_ready_only_after_its_own_materialization()
        {
            Assert.False(ShipRestoreReadinessPolicy.IsReady(
                10, Array.Empty<long>(), new HashSet<long>()));
            Assert.True(ShipRestoreReadinessPolicy.IsReady(
                10, Array.Empty<long>(), new HashSet<long> { 10 }));
        }

        [Fact]
        public void Invalid_members_fail_closed()
        {
            Assert.False(ShipRestoreReadinessPolicy.IsReady(
                0, Array.Empty<long>(), new HashSet<long> { 0 }));
            Assert.False(ShipRestoreReadinessPolicy.IsReady(
                10, new long[] { 0 }, new HashSet<long> { 10, 0 }));
        }

        [Fact]
        public void Authored_rotated_hull_envelope_distinguishes_deck_from_island_below()
        {
            var hull = new ShipHullMetrics(3, 1, 0, 2,
                beamMetres: 4.54, keelMetres: 12,
                bowLocalZMetres: 10, sternLocalZMetres: -2,
                deckPlaneMetres: 3.4);

            Assert.True(ShipRestoreReadinessPolicy.IsWithinHullEnvelope(hull,
                17212.373, -283.315, -1130.451, -2.58,
                17209.959, -279.786, -1131.338));
            Assert.False(ShipRestoreReadinessPolicy.IsWithinHullEnvelope(hull,
                17212.373, -283.315, -1130.451, -2.58,
                17209.959, -313.4, -1131.338));
            Assert.False(ShipRestoreReadinessPolicy.IsWithinHullEnvelope(hull,
                17212.373, -283.315, -1130.451, -2.58,
                17250, -279.786, -1131.338));
        }
    }
}

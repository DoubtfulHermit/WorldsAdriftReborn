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
    }
}

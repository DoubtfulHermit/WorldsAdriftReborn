using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Domains
{
    public class ShipReplicationCursorTests
    {
        [Fact]
        public void Sequence_is_monotonic_within_one_authority_generation()
        {
            var cursor = new ShipReplicationCursor();

            Assert.True(cursor.TryNext(197, 4, out ShipReplicationStamp first));
            Assert.True(cursor.TryNext(197, 4, out ShipReplicationStamp second));

            Assert.Equal(1, first.Sequence);
            Assert.Equal(2, second.Sequence);
            Assert.Equal(4, second.AuthorityGeneration);
        }

        [Fact]
        public void New_generation_restarts_sequence_and_stale_authority_is_rejected()
        {
            var cursor = new ShipReplicationCursor();
            Assert.True(cursor.TryNext(197, 4, out _));
            Assert.True(cursor.TryNext(197, 5, out ShipReplicationStamp handoff));

            Assert.Equal(1, handoff.Sequence);
            Assert.False(cursor.TryNext(197, 4, out _));
        }

        [Fact]
        public void Forget_retires_generation_and_sequence_for_a_destroyed_domain()
        {
            var cursor = new ShipReplicationCursor();
            Assert.True(cursor.TryNext(197, 5, out _));
            Assert.True(cursor.TryNext(197, 5, out _));

            cursor.Forget(197);

            Assert.True(cursor.TryNext(197, 1, out ShipReplicationStamp replacement));
            Assert.Equal(1, replacement.AuthorityGeneration);
            Assert.Equal(1, replacement.Sequence);
        }

        [Theory]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        public void Member_never_delivers_without_relevant_delivered_root(
            bool relevant, bool rootDelivered, bool memberCheckedOut)
        {
            Assert.False(ShipDomainDeliveryPolicy.DeliverMember(
                relevant, rootDelivered,
                auxiliaryRequired: false, auxiliaryDelivered: false,
                memberCheckedOut));
        }

        [Fact]
        public void Checked_out_member_follows_a_relevant_delivered_root()
        {
            Assert.True(ShipDomainDeliveryPolicy.DeliverMember(
                true, true, auxiliaryRequired: false, auxiliaryDelivered: false,
                memberCheckedOut: true));
        }

        [Fact]
        public void Member_waits_when_required_parent_timeline_was_not_delivered()
        {
            Assert.False(ShipDomainDeliveryPolicy.DeliverMember(
                domainRelevant: true, rootDelivered: true,
                auxiliaryRequired: true, auxiliaryDelivered: false,
                memberCheckedOut: true));
            Assert.True(ShipDomainDeliveryPolicy.DeliverMember(
                domainRelevant: true, rootDelivered: true,
                auxiliaryRequired: true, auxiliaryDelivered: true,
                memberCheckedOut: true));
        }

        [Fact]
        public void Primary_and_auxiliary_roots_must_both_target_the_domain_hull()
        {
            Assert.True(ShipDomainDeliveryPolicy.RootTargetsHull(197, 197, 197));
            Assert.False(ShipDomainDeliveryPolicy.RootTargetsHull(197, 222, 197));
            Assert.False(ShipDomainDeliveryPolicy.RootTargetsHull(197, 197, 222));
        }
    }
}

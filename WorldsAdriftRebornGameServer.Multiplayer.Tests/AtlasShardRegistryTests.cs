using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The shard lifecycle and the pickup reservation: LODGED -> RELEASED ->
    /// COLLECTED, once-only release on core destruction, and the reservation that
    /// stops two players winning the same shard. Pure, so the state machine is pinned
    /// natively - the standing caveat bites hardest on exactly this kind of state.
    /// </summary>
    public class AtlasShardRegistryTests
    {
        private const long Host = 5000;
        private const long Shard = 5001;
        private const long PlayerA = 9001;
        private const long PlayerB = 9002;

        private static AtlasShardRegistry WithLodgedShard()
        {
            AtlasShardRegistry reg = new AtlasShardRegistry();
            Assert.True(reg.Register(Shard, Host, AtlasShardCatalogue.DefaultSlotId));
            return reg;
        }

        [Fact]
        public void A_registered_shard_starts_lodged_and_knows_its_host_and_slot()
        {
            AtlasShardRegistry reg = WithLodgedShard();

            Assert.True(reg.IsShard(Shard));
            Assert.Equal(Host, reg.HostOf(Shard));
            Assert.Equal(AtlasShardCatalogue.DefaultSlotId, reg.SlotOf(Shard));
            Assert.Equal(AtlasShardState.Lodged, reg.StateOf(Shard));
            Assert.True(reg.IsLodged(Shard));
            Assert.False(reg.IsReleased(Shard));
            Assert.False(reg.IsAvailable(Shard));
        }

        [Fact]
        public void Registration_is_idempotent_so_a_second_joiner_cannot_relodge_a_taken_shard()
        {
            AtlasShardRegistry reg = WithLodgedShard();
            reg.ReleaseByHost(Host);
            Assert.True(reg.Reserve(Shard, PlayerA));
            Assert.True(reg.Collect(Shard, PlayerA));

            // The second client walks the same spawn plan and re-registers - it must be
            // a no-op that does not reset the shard back to lodged.
            Assert.False(reg.Register(Shard, Host, AtlasShardCatalogue.DefaultSlotId));
            Assert.True(reg.IsCollected(Shard));
        }

        [Fact]
        public void A_non_shard_id_answers_null_and_false_everywhere()
        {
            AtlasShardRegistry reg = WithLodgedShard();
            const long stranger = 12345;

            Assert.False(reg.IsShard(stranger));
            Assert.Null(reg.HostOf(stranger));
            Assert.Null(reg.SlotOf(stranger));
            Assert.Null(reg.StateOf(stranger));
            Assert.False(reg.IsLodged(stranger));
            Assert.False(reg.IsReleased(stranger));
            Assert.False(reg.IsCollected(stranger));
            Assert.False(reg.IsAvailable(stranger));
        }

        [Fact]
        public void Destroying_the_host_core_releases_the_shard_exactly_once()
        {
            AtlasShardRegistry reg = WithLodgedShard();

            IReadOnlyList<long> first = reg.ReleaseByHost(Host);
            Assert.Equal(new[] { Shard }, first);
            Assert.True(reg.IsReleased(Shard));
            Assert.True(reg.IsAvailable(Shard));

            // A held beam keeps hitting the destroyed core; the release must not fire
            // again (it would re-broadcast a Dislodged on an already-loose shard).
            IReadOnlyList<long> second = reg.ReleaseByHost(Host);
            Assert.Empty(second);
        }

        [Fact]
        public void Release_only_touches_shards_of_that_host()
        {
            AtlasShardRegistry reg = WithLodgedShard();
            const long otherHost = 6000;
            const long otherShard = 6001;
            reg.Register(otherShard, otherHost, 0);

            IReadOnlyList<long> released = reg.ReleaseByHost(Host);
            Assert.Equal(new[] { Shard }, released);
            Assert.True(reg.IsLodged(otherShard));
        }

        [Fact]
        public void ShardsForHost_lists_attachments_for_the_2103_seed()
        {
            AtlasShardRegistry reg = WithLodgedShard();
            Assert.Equal(new[] { Shard }, reg.ShardsForHost(Host));
            Assert.Empty(reg.ShardsForHost(999999));
        }

        [Fact]
        public void A_lodged_shard_cannot_be_reserved_or_collected()
        {
            AtlasShardRegistry reg = WithLodgedShard();

            Assert.False(reg.Reserve(Shard, PlayerA));
            Assert.False(reg.Collect(Shard, PlayerA));
            Assert.True(reg.IsLodged(Shard));
        }

        [Fact]
        public void Two_players_cannot_both_reserve_the_same_released_shard()
        {
            AtlasShardRegistry reg = WithLodgedShard();
            reg.ReleaseByHost(Host);

            Assert.True(reg.Reserve(Shard, PlayerA));
            // The second event in the same drain must lose.
            Assert.False(reg.Reserve(Shard, PlayerB));
            Assert.True(reg.IsReservedByOther(Shard, PlayerB));
            Assert.False(reg.IsReservedByOther(Shard, PlayerA));
        }

        [Fact]
        public void The_same_player_may_re_reserve_so_a_retried_event_does_not_deadlock()
        {
            AtlasShardRegistry reg = WithLodgedShard();
            reg.ReleaseByHost(Host);

            Assert.True(reg.Reserve(Shard, PlayerA));
            Assert.True(reg.Reserve(Shard, PlayerA));
        }

        [Fact]
        public void Reserve_zero_player_id_is_rejected()
        {
            AtlasShardRegistry reg = WithLodgedShard();
            reg.ReleaseByHost(Host);
            // 0 is the "reserved by nobody" sentinel; a real player id is never 0.
            Assert.False(reg.Reserve(Shard, 0));
        }

        [Fact]
        public void A_successful_pickup_reserves_grants_then_collects()
        {
            AtlasShardRegistry reg = WithLodgedShard();
            reg.ReleaseByHost(Host);

            Assert.True(reg.Reserve(Shard, PlayerA));
            Assert.True(reg.Collect(Shard, PlayerA));
            Assert.True(reg.IsCollected(Shard));
            Assert.False(reg.IsAvailable(Shard));

            // A late-arriving second event on the taken shard is refused.
            Assert.False(reg.Reserve(Shard, PlayerB));
            Assert.False(reg.Collect(Shard, PlayerB));
        }

        [Fact]
        public void Only_the_reserver_can_collect()
        {
            AtlasShardRegistry reg = WithLodgedShard();
            reg.ReleaseByHost(Host);
            Assert.True(reg.Reserve(Shard, PlayerA));

            Assert.False(reg.Collect(Shard, PlayerB));
            Assert.True(reg.IsReleased(Shard));
        }

        [Fact]
        public void A_failed_grant_rolls_the_reservation_back_and_reopens_the_shard()
        {
            AtlasShardRegistry reg = WithLodgedShard();
            reg.ReleaseByHost(Host);
            Assert.True(reg.Reserve(Shard, PlayerA));

            // Grant failed (unknown item / full grid): roll back.
            Assert.True(reg.Rollback(Shard, PlayerA));
            Assert.True(reg.IsReleased(Shard));
            Assert.False(reg.IsReservedByOther(Shard, PlayerB));

            // Another player (or a retry) can now win it.
            Assert.True(reg.Reserve(Shard, PlayerB));
            Assert.True(reg.Collect(Shard, PlayerB));
        }

        [Fact]
        public void Rollback_by_a_non_reserver_is_a_no_op()
        {
            AtlasShardRegistry reg = WithLodgedShard();
            reg.ReleaseByHost(Host);
            Assert.True(reg.Reserve(Shard, PlayerA));

            Assert.False(reg.Rollback(Shard, PlayerB));
            Assert.True(reg.IsReservedByOther(Shard, PlayerB)); // still A's
        }
    }
}

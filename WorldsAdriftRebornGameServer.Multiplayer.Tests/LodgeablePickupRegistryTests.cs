using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The SHARED lodgeable-pickup core: the lodged/released/collected state machine
    /// and the pickup reservation, host-less, exercised on its own. Both the atlas
    /// shard (via <see cref="AtlasShardRegistry"/>) and the fuel pod build on this, so
    /// the "two players cannot both win the same pickup" guarantee is pinned here once.
    /// </summary>
    public class LodgeablePickupRegistryTests
    {
        private const long Pod = 5000;
        private const long PlayerA = 11;
        private const long PlayerB = 22;

        [Fact]
        public void A_pickup_registered_lodged_starts_lodged_and_is_not_pickable()
        {
            LodgeablePickupRegistry reg = new LodgeablePickupRegistry();
            Assert.True(reg.Register(Pod, startReleased: false));
            Assert.True(reg.Contains(Pod));
            Assert.Equal(LodgeablePickupState.Lodged, reg.StateOf(Pod));
            Assert.True(reg.IsLodged(Pod));
            Assert.False(reg.IsReleased(Pod));
            Assert.False(reg.IsAvailable(Pod));
            Assert.False(reg.Reserve(Pod, PlayerA));
        }

        [Fact]
        public void A_pickup_registered_released_is_immediately_pickable_no_host_needed()
        {
            LodgeablePickupRegistry reg = new LodgeablePickupRegistry();
            Assert.True(reg.Register(Pod, startReleased: true));
            Assert.Equal(LodgeablePickupState.Released, reg.StateOf(Pod));
            Assert.True(reg.IsReleased(Pod));
            Assert.True(reg.IsAvailable(Pod));
            Assert.True(reg.Reserve(Pod, PlayerA));
        }

        [Fact]
        public void Registration_is_idempotent_and_never_resets_state()
        {
            LodgeablePickupRegistry reg = new LodgeablePickupRegistry();
            reg.Register(Pod, startReleased: true);
            Assert.True(reg.Reserve(Pod, PlayerA));
            Assert.True(reg.Collect(Pod, PlayerA));
            Assert.True(reg.IsCollected(Pod));

            // A second joiner walking the identical spawn plan must NOT revive it.
            Assert.False(reg.Register(Pod, startReleased: true));
            Assert.True(reg.IsCollected(Pod));
        }

        [Fact]
        public void An_unknown_id_answers_null_and_false_everywhere()
        {
            LodgeablePickupRegistry reg = new LodgeablePickupRegistry();
            Assert.False(reg.Contains(Pod));
            Assert.Null(reg.StateOf(Pod));
            Assert.False(reg.IsLodged(Pod));
            Assert.False(reg.IsReleased(Pod));
            Assert.False(reg.IsCollected(Pod));
            Assert.False(reg.IsAvailable(Pod));
            Assert.False(reg.IsReservedByOther(Pod, PlayerA));
            Assert.False(reg.Release(Pod));
            Assert.False(reg.Reserve(Pod, PlayerA));
            Assert.False(reg.Collect(Pod, PlayerA));
        }

        [Fact]
        public void Release_moves_lodged_to_released_exactly_once()
        {
            LodgeablePickupRegistry reg = new LodgeablePickupRegistry();
            reg.Register(Pod, startReleased: false);
            Assert.True(reg.Release(Pod));
            Assert.True(reg.IsReleased(Pod));
            // Idempotent: a second release is a no-op.
            Assert.False(reg.Release(Pod));
        }

        [Fact]
        public void Two_players_cannot_both_reserve_the_same_released_pickup()
        {
            LodgeablePickupRegistry reg = new LodgeablePickupRegistry();
            reg.Register(Pod, startReleased: true);
            Assert.True(reg.Reserve(Pod, PlayerA));
            Assert.True(reg.IsReservedByOther(Pod, PlayerB));
            Assert.False(reg.Reserve(Pod, PlayerB));
        }

        [Fact]
        public void The_same_player_may_re_reserve_so_a_retried_event_does_not_deadlock()
        {
            LodgeablePickupRegistry reg = new LodgeablePickupRegistry();
            reg.Register(Pod, startReleased: true);
            Assert.True(reg.Reserve(Pod, PlayerA));
            Assert.True(reg.Reserve(Pod, PlayerA));
        }

        [Fact]
        public void Reserve_zero_player_id_is_rejected()
        {
            LodgeablePickupRegistry reg = new LodgeablePickupRegistry();
            reg.Register(Pod, startReleased: true);
            Assert.False(reg.Reserve(Pod, 0));
        }

        [Fact]
        public void A_successful_pickup_reserves_then_collects_and_locks_others_out()
        {
            LodgeablePickupRegistry reg = new LodgeablePickupRegistry();
            reg.Register(Pod, startReleased: true);
            Assert.True(reg.Reserve(Pod, PlayerA));
            Assert.True(reg.Collect(Pod, PlayerA));
            Assert.True(reg.IsCollected(Pod));
            Assert.False(reg.IsAvailable(Pod));
            Assert.False(reg.Reserve(Pod, PlayerB));
            Assert.False(reg.Collect(Pod, PlayerB));
        }

        [Fact]
        public void Only_the_reserver_can_collect()
        {
            LodgeablePickupRegistry reg = new LodgeablePickupRegistry();
            reg.Register(Pod, startReleased: true);
            Assert.True(reg.Reserve(Pod, PlayerA));
            Assert.False(reg.Collect(Pod, PlayerB));
        }

        [Fact]
        public void A_failed_grant_rolls_the_reservation_back_and_reopens_the_pickup()
        {
            LodgeablePickupRegistry reg = new LodgeablePickupRegistry();
            reg.Register(Pod, startReleased: true);
            Assert.True(reg.Reserve(Pod, PlayerA));
            Assert.True(reg.Rollback(Pod, PlayerA));
            // Reopened: another player can now win it.
            Assert.True(reg.Reserve(Pod, PlayerB));
            Assert.True(reg.Collect(Pod, PlayerB));
        }

        [Fact]
        public void Rollback_by_a_non_reserver_is_a_no_op()
        {
            LodgeablePickupRegistry reg = new LodgeablePickupRegistry();
            reg.Register(Pod, startReleased: true);
            Assert.True(reg.Reserve(Pod, PlayerA));
            Assert.False(reg.Rollback(Pod, PlayerB));
            // A still holds it.
            Assert.False(reg.Reserve(Pod, PlayerB));
        }

        [Fact]
        public void EntityIds_and_count_track_registrations()
        {
            LodgeablePickupRegistry reg = new LodgeablePickupRegistry();
            reg.Register(1, startReleased: true);
            reg.Register(2, startReleased: false);
            Assert.Equal(2, reg.Count);
            Assert.Contains(1L, (IEnumerable<long>)reg.EntityIds);
            Assert.Contains(2L, (IEnumerable<long>)reg.EntityIds);
        }
    }
}

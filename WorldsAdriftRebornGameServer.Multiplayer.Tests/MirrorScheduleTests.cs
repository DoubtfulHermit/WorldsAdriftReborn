using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public class MirrorScheduleTests
    {
        private const ulong PeerA = 0x1000;
        private const ulong PeerB = 0x2000;

        private const long EntityA = 11;
        private const long EntityB = 22;

        /// <summary>A clock that only moves when a test says so.</summary>
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; private set; }

            public void Advance(TimeSpan by)
            {
                Elapsed += by;
            }

            public void Advance(double seconds)
            {
                Advance(TimeSpan.FromSeconds(seconds));
            }
        }

        private static readonly TimeSpan Flush = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan Resend = TimeSpan.FromSeconds(3);

        private static (FakeClock, MirrorSchedule) NewSchedule(int attempts = 3)
        {
            FakeClock clock = new();
            return (clock, new MirrorSchedule(clock, Flush, Resend, attempts));
        }

        private static MirrorIntent AddEntity(ulong target, long entity)
        {
            return new MirrorIntent(target, MirrorOp.AddEntity, entity);
        }

        // ---------------------------------------------------------------
        // The bug this type was extracted to kill.
        // ---------------------------------------------------------------

        [Fact]
        public void A_burst_of_poll_iterations_does_not_expire_the_flush_timeout()
        {
            // THIS IS THE REGRESSION TEST. The old main loop counted ENet poll
            // ITERATIONS and called 40 of them "~2s", on the assumption that each
            // iteration blocks for its 50 ms timeout. It does not:
            // enet_host_service returns immediately whenever an event is already
            // queued, so the loop spins once per EVENT. With a second player
            // publishing transform and skeleton bytes every frame the loop runs
            // hundreds of times a second and the "two second" grace period for a
            // client to load the Traveller prefab collapsed to a fraction of a
            // second - so the mirror flushed before the prefab existed, the
            // client dropped the AddEntity, and the remote avatar never spawned.
            //
            // Against a tick-counting implementation this loop expires the
            // timeout on iteration 40. Against a clock, no amount of polling
            // does: only time does.
            (FakeClock clock, MirrorSchedule schedule) = NewSchedule();
            schedule.Park(PeerA, AddEntity(PeerA, EntityB));

            for (int burst = 0; burst < 1000; burst++)
            {
                Assert.Empty(schedule.DueForFlush());
            }

            clock.Advance(Flush);

            Assert.Equal(new[] { PeerA }, schedule.DueForFlush());
        }

        [Fact]
        public void A_burst_of_poll_iterations_does_not_expire_the_resend_interval()
        {
            // Same defect, second victim: three resends spaced "~3s" apart all
            // burned inside the first fraction of a second, every one of them
            // before the prefab had loaded.
            (FakeClock clock, MirrorSchedule schedule) = NewSchedule();
            schedule.Park(PeerA, AddEntity(PeerA, EntityB));
            schedule.TakeParked(PeerA);

            for (int burst = 0; burst < 1000; burst++)
            {
                Assert.Empty(schedule.DueForResend());
            }

            clock.Advance(Resend);

            Assert.Single(schedule.DueForResend());
        }

        [Fact]
        public void Elapsed_time_is_what_expires_the_flush_even_with_no_polling_in_between()
        {
            // The mirror image of the bug: a QUIET server used to under-fire,
            // because with no events the loop only ticked 20 times a second.
            (FakeClock clock, MirrorSchedule schedule) = NewSchedule();
            schedule.Park(PeerA, AddEntity(PeerA, EntityB));

            clock.Advance(1.99);
            Assert.Empty(schedule.DueForFlush());

            clock.Advance(0.01);
            Assert.Single(schedule.DueForFlush());
        }

        // ---------------------------------------------------------------
        // Parking
        // ---------------------------------------------------------------

        [Fact]
        public void Parking_the_first_op_for_a_peer_reports_that_the_asset_must_be_requested()
        {
            (_, MirrorSchedule schedule) = NewSchedule();

            Assert.True(schedule.Park(PeerA, AddEntity(PeerA, EntityB)));
        }

        [Fact]
        public void Parking_further_ops_for_the_same_peer_does_not_re_request_the_asset()
        {
            (_, MirrorSchedule schedule) = NewSchedule();
            schedule.Park(PeerA, AddEntity(PeerA, EntityB));

            Assert.False(schedule.Park(PeerA, new MirrorIntent(PeerA, MirrorOp.AddComponents, EntityB)));
            Assert.Equal(1, schedule.ParkedPeerCount);
        }

        [Fact]
        public void Each_peer_requests_the_asset_for_itself()
        {
            (_, MirrorSchedule schedule) = NewSchedule();

            Assert.True(schedule.Park(PeerA, AddEntity(PeerA, EntityB)));
            Assert.True(schedule.Park(PeerB, AddEntity(PeerB, EntityA)));
            Assert.Equal(2, schedule.ParkedPeerCount);
        }

        [Fact]
        public void Later_ops_do_not_reset_a_peers_flush_deadline()
        {
            // The deadline belongs to the batch, not to the last op added, or a
            // steady trickle of joins would postpone the fallback forever.
            (FakeClock clock, MirrorSchedule schedule) = NewSchedule();
            schedule.Park(PeerA, AddEntity(PeerA, EntityB));

            clock.Advance(1.5);
            schedule.Park(PeerA, new MirrorIntent(PeerA, MirrorOp.AddComponents, EntityB));
            clock.Advance(0.5);

            Assert.Single(schedule.DueForFlush());
        }

        [Fact]
        public void TakeParked_returns_the_ops_in_the_order_they_were_parked()
        {
            // AddEntity must precede AddComponents: the client has nothing to
            // attach components to otherwise.
            (_, MirrorSchedule schedule) = NewSchedule();
            schedule.Park(PeerA, AddEntity(PeerA, EntityB));
            schedule.Park(PeerA, new MirrorIntent(PeerA, MirrorOp.AddComponents, EntityB));

            IReadOnlyList<MirrorIntent> ops = schedule.TakeParked(PeerA);

            Assert.Equal(2, ops.Count);
            Assert.Equal(MirrorOp.AddEntity, ops[0].Op);
            Assert.Equal(MirrorOp.AddComponents, ops[1].Op);
        }

        [Fact]
        public void TakeParked_of_a_peer_with_nothing_parked_is_empty()
        {
            // Every asset-load ack calls this, and most acks belong to a peer
            // that is not mirroring anyone.
            (_, MirrorSchedule schedule) = NewSchedule();

            Assert.Empty(schedule.TakeParked(PeerA));
        }

        [Fact]
        public void TakeParked_consumes_the_batch_so_ops_are_never_flushed_twice()
        {
            (FakeClock clock, MirrorSchedule schedule) = NewSchedule();
            schedule.Park(PeerA, AddEntity(PeerA, EntityB));
            schedule.TakeParked(PeerA);

            clock.Advance(10);

            Assert.Empty(schedule.DueForFlush());
            Assert.Equal(0, schedule.ParkedPeerCount);
        }

        [Fact]
        public void One_peers_deadline_does_not_flush_another_peers_ops()
        {
            (FakeClock clock, MirrorSchedule schedule) = NewSchedule();
            schedule.Park(PeerA, AddEntity(PeerA, EntityB));
            clock.Advance(1.5);
            schedule.Park(PeerB, AddEntity(PeerB, EntityA));

            clock.Advance(0.5);

            Assert.Equal(new[] { PeerA }, schedule.DueForFlush());
        }

        // ---------------------------------------------------------------
        // Resends
        // ---------------------------------------------------------------

        [Fact]
        public void Flushing_arms_the_resends()
        {
            (FakeClock clock, MirrorSchedule schedule) = NewSchedule();
            schedule.Park(PeerA, AddEntity(PeerA, EntityB));
            schedule.TakeParked(PeerA);

            Assert.Equal(1, schedule.ResendingPeerCount);

            clock.Advance(Resend);
            MirrorResend batch = Assert.Single(schedule.DueForResend());

            Assert.Equal(PeerA, batch.PeerId);
            Assert.Equal(EntityB, Assert.Single(batch.Ops).EntityId);
        }

        [Fact]
        public void Resends_stop_after_the_configured_number_of_attempts()
        {
            (FakeClock clock, MirrorSchedule schedule) = NewSchedule(attempts: 3);
            schedule.Park(PeerA, AddEntity(PeerA, EntityB));
            schedule.TakeParked(PeerA);

            int sends = 0;
            for (int i = 0; i < 10; i++)
            {
                clock.Advance(Resend);
                sends += schedule.DueForResend().Count;
            }

            Assert.Equal(3, sends);
            Assert.Equal(0, schedule.ResendingPeerCount);
        }

        [Fact]
        public void Each_resend_reports_how_many_attempts_remain()
        {
            (FakeClock clock, MirrorSchedule schedule) = NewSchedule(attempts: 3);
            schedule.Park(PeerA, AddEntity(PeerA, EntityB));
            schedule.TakeParked(PeerA);

            List<int> remaining = new();
            for (int i = 0; i < 3; i++)
            {
                clock.Advance(Resend);
                remaining.Add(Assert.Single(schedule.DueForResend()).AttemptsLeft);
            }

            Assert.Equal(new[] { 2, 1, 0 }, remaining);
        }

        [Fact]
        public void A_resend_re_arms_the_interval_rather_than_firing_every_call()
        {
            (FakeClock clock, MirrorSchedule schedule) = NewSchedule();
            schedule.Park(PeerA, AddEntity(PeerA, EntityB));
            schedule.TakeParked(PeerA);

            clock.Advance(Resend);
            Assert.Single(schedule.DueForResend());
            Assert.Empty(schedule.DueForResend());

            clock.Advance(Resend);
            Assert.Single(schedule.DueForResend());
        }

        [Fact]
        public void A_flush_with_nothing_parked_arms_no_resends()
        {
            (FakeClock clock, MirrorSchedule schedule) = NewSchedule();
            schedule.TakeParked(PeerA);

            clock.Advance(60);

            Assert.Equal(0, schedule.ResendingPeerCount);
            Assert.Empty(schedule.DueForResend());
        }

        // ---------------------------------------------------------------
        // Forgetting a peer
        // ---------------------------------------------------------------

        [Fact]
        public void Forget_clears_every_collection_the_schedule_holds_for_a_peer()
        {
            // The leak this replaced: five parallel per-peer dictionaries in the
            // main loop, none of them touched on disconnect. Two records now, and
            // one method that empties both.
            (FakeClock clock, MirrorSchedule schedule) = NewSchedule();

            schedule.Park(PeerA, AddEntity(PeerA, EntityB));
            schedule.TakeParked(PeerA);          // arms resends
            schedule.Park(PeerA, AddEntity(PeerA, 99));  // and park a fresh batch

            Assert.True(schedule.Forget(PeerA));

            Assert.False(schedule.IsTracking(PeerA));
            Assert.Equal(0, schedule.ParkedPeerCount);
            Assert.Equal(0, schedule.ResendingPeerCount);
            Assert.Empty(schedule.TakeParked(PeerA));

            clock.Advance(60);
            Assert.Empty(schedule.DueForFlush());
            Assert.Empty(schedule.DueForResend());
        }

        [Fact]
        public void Forget_does_not_disturb_the_players_who_stayed()
        {
            (FakeClock clock, MirrorSchedule schedule) = NewSchedule();
            schedule.Park(PeerA, AddEntity(PeerA, EntityB));
            schedule.Park(PeerB, AddEntity(PeerB, EntityA));

            schedule.Forget(PeerA);
            clock.Advance(Flush);

            Assert.Equal(new[] { PeerB }, schedule.DueForFlush());
        }

        [Fact]
        public void Forget_of_an_untracked_peer_reports_nothing_and_does_not_throw()
        {
            // Disconnects arrive for peers that never mirrored anyone.
            (_, MirrorSchedule schedule) = NewSchedule();

            Assert.False(schedule.Forget(PeerA));
        }

        [Fact]
        public void A_peer_that_rejoins_the_same_slot_starts_from_a_clean_deadline()
        {
            // ENet reuses peer slots. Without Forget, the newcomer inherits the
            // departed player's already-expired deadline and its ops flush
            // instantly - the exact misattribution the leak allowed.
            (FakeClock clock, MirrorSchedule schedule) = NewSchedule();
            schedule.Park(PeerA, AddEntity(PeerA, EntityB));
            clock.Advance(10);

            schedule.Forget(PeerA);
            schedule.Park(PeerA, AddEntity(PeerA, 77));

            Assert.Empty(schedule.DueForFlush());
        }

        // ---------------------------------------------------------------
        // The production clock
        // ---------------------------------------------------------------

        [Fact]
        public void The_monotonic_clock_never_goes_backwards()
        {
            MonotonicClock clock = new();

            TimeSpan first = clock.Elapsed;
            TimeSpan second = clock.Elapsed;

            Assert.True(second >= first);
        }
    }
}

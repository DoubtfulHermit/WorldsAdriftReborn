using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The server's side of the loading-barrier handshake as a state machine: a
    /// peer is activated exactly once, whether it signals ready or times out, and a
    /// peer that never signals is never trapped.
    /// </summary>
    public class LoadBarrierTrackerTests
    {
        private static readonly TimeSpan T0 = TimeSpan.Zero;
        private static readonly TimeSpan T5 = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan T10 = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan T20 = TimeSpan.FromSeconds(20);

        [Fact]
        public void An_armed_peer_is_pending_until_it_completes()
        {
            LoadBarrierTracker tracker = new LoadBarrierTracker();
            Assert.False(tracker.IsPending(1));

            tracker.Arm(1, T10);
            Assert.True(tracker.IsPending(1));
            Assert.Equal(1, tracker.PendingCount);

            Assert.True(tracker.Complete(1));
            Assert.False(tracker.IsPending(1));
            Assert.Equal(0, tracker.PendingCount);
        }

        [Fact]
        public void A_readiness_signal_completes_a_peer_exactly_once()
        {
            // The readiness path pushes activation and releases the player; doing it
            // twice (a client that publishes 190001 Loaded=true more than once) must
            // not push activation twice.
            LoadBarrierTracker tracker = new LoadBarrierTracker();
            tracker.Arm(7, T10);

            Assert.True(tracker.Complete(7));
            Assert.False(tracker.Complete(7));
        }

        [Fact]
        public void A_peer_that_never_signals_ready_times_out_after_its_deadline()
        {
            LoadBarrierTracker tracker = new LoadBarrierTracker();
            tracker.Arm(1, T10);

            // Not yet overdue.
            Assert.Empty(tracker.DueTimeouts(T5));
            Assert.True(tracker.IsPending(1));

            // Past the deadline: released exactly once.
            IReadOnlyList<ulong> due = tracker.DueTimeouts(T20);
            Assert.Equal(new ulong[] { 1 }, due);
            Assert.False(tracker.IsPending(1));
            Assert.Empty(tracker.DueTimeouts(T20));
        }

        [Fact]
        public void The_deadline_boundary_is_inclusive()
        {
            LoadBarrierTracker tracker = new LoadBarrierTracker();
            tracker.Arm(1, T10);
            Assert.Equal(new ulong[] { 1 }, tracker.DueTimeouts(T10));
        }

        [Fact]
        public void A_peer_that_signals_ready_is_not_also_timed_out()
        {
            // The exactly-once guarantee across BOTH paths: complete removes it, so a
            // later timeout sweep past its deadline finds nothing.
            LoadBarrierTracker tracker = new LoadBarrierTracker();
            tracker.Arm(1, T10);

            Assert.True(tracker.Complete(1));
            Assert.Empty(tracker.DueTimeouts(T20));
        }

        [Fact]
        public void Only_overdue_peers_are_released_by_a_timeout_sweep()
        {
            LoadBarrierTracker tracker = new LoadBarrierTracker();
            tracker.Arm(1, T5);
            tracker.Arm(2, T20);

            IReadOnlyList<ulong> due = tracker.DueTimeouts(T10);
            Assert.Equal(new ulong[] { 1 }, due);
            Assert.True(tracker.IsPending(2));
        }

        [Fact]
        public void Re_arming_replaces_the_deadline_rather_than_stacking()
        {
            LoadBarrierTracker tracker = new LoadBarrierTracker();
            tracker.Arm(1, T5);
            tracker.Arm(1, T20); // e.g. a re-sent setup

            Assert.Empty(tracker.DueTimeouts(T10)); // old deadline is gone
            Assert.True(tracker.IsPending(1));
            Assert.Equal(new ulong[] { 1 }, tracker.DueTimeouts(T20));
        }

        [Fact]
        public void Forgetting_a_departed_peer_stops_it_ever_timing_out()
        {
            LoadBarrierTracker tracker = new LoadBarrierTracker();
            tracker.Arm(1, T10);

            tracker.Forget(1);
            Assert.False(tracker.IsPending(1));
            Assert.Empty(tracker.DueTimeouts(T20));

            // Forgetting an unknown peer is silent.
            tracker.Forget(999);
        }

        [Fact]
        public void A_timeout_sweep_with_nothing_pending_is_empty()
        {
            LoadBarrierTracker tracker = new LoadBarrierTracker();
            Assert.Empty(tracker.DueTimeouts(T20));
        }
    }
}

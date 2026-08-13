using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The spawn-chain ack timeout: the rule that makes a permanent handshake
    /// stall impossible. One lost ack (2026-08-12: the chain parked at the
    /// 'global' entity and the placed stations behind it never reached the
    /// client) must cost one bounded pause, never the rest of the plan.
    /// </summary>
    public class SpawnAckTimeoutPolicyTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        // ------------------------------------------------------------------
        // The advance decision
        // ------------------------------------------------------------------

        [Fact]
        public void A_performed_step_advances_once_the_timeout_has_elapsed()
        {
            Assert.True(SpawnAckTimeoutPolicy.ShouldAdvance(
                performed: true,
                isLastStep: false,
                performedAt: TimeSpan.FromSeconds(10),
                now: TimeSpan.FromSeconds(15),
                timeout: Timeout));
        }

        [Fact]
        public void A_performed_step_keeps_waiting_inside_the_timeout()
        {
            Assert.False(SpawnAckTimeoutPolicy.ShouldAdvance(
                performed: true,
                isLastStep: false,
                performedAt: TimeSpan.FromSeconds(10),
                now: TimeSpan.FromSeconds(14.999),
                timeout: Timeout));
        }

        [Fact]
        public void A_step_not_yet_performed_never_times_out()
        {
            // A step held back by the pacer (Performed stays false across turns)
            // has not asked for an ack; only a sent op can be waited on.
            Assert.False(SpawnAckTimeoutPolicy.ShouldAdvance(
                performed: false,
                isLastStep: false,
                performedAt: TimeSpan.Zero,
                now: TimeSpan.FromHours(1),
                timeout: Timeout));
        }

        [Fact]
        public void The_last_step_never_advances()
        {
            // Parking at the last step is the plan's normal "done" state - the
            // ack path treats index Count-1 the same way.
            Assert.False(SpawnAckTimeoutPolicy.ShouldAdvance(
                performed: true,
                isLastStep: true,
                performedAt: TimeSpan.FromSeconds(10),
                now: TimeSpan.FromHours(1),
                timeout: Timeout));
        }

        // ------------------------------------------------------------------
        // Env-to-config - clamped, never off
        // ------------------------------------------------------------------

        [Fact]
        public void An_unset_or_unparsable_timeout_is_the_default()
        {
            Assert.Equal(SpawnAckTimeoutPolicy.DefaultTimeoutMs,
                SpawnAckTimeoutPolicy.TimeoutFrom(null).TotalMilliseconds);
            Assert.Equal(SpawnAckTimeoutPolicy.DefaultTimeoutMs,
                SpawnAckTimeoutPolicy.TimeoutFrom("").TotalMilliseconds);
            Assert.Equal(SpawnAckTimeoutPolicy.DefaultTimeoutMs,
                SpawnAckTimeoutPolicy.TimeoutFrom("soon").TotalMilliseconds);
        }

        [Fact]
        public void Zero_or_negative_never_disables_the_safety_net()
        {
            // "0 disables it" is the convention for the perf knobs; this one is a
            // correctness net, so 0 falls back to the default instead of "wait
            // forever".
            Assert.Equal(SpawnAckTimeoutPolicy.DefaultTimeoutMs,
                SpawnAckTimeoutPolicy.TimeoutFrom("0").TotalMilliseconds);
            Assert.Equal(SpawnAckTimeoutPolicy.DefaultTimeoutMs,
                SpawnAckTimeoutPolicy.TimeoutFrom("-1").TotalMilliseconds);
        }

        [Fact]
        public void The_timeout_is_clamped_to_the_sane_band()
        {
            Assert.Equal(SpawnAckTimeoutPolicy.MinTimeoutMs,
                SpawnAckTimeoutPolicy.TimeoutFrom("10").TotalMilliseconds);
            Assert.Equal(SpawnAckTimeoutPolicy.MaxTimeoutMs,
                SpawnAckTimeoutPolicy.TimeoutFrom("999999999").TotalMilliseconds);
            Assert.Equal(8000, SpawnAckTimeoutPolicy.TimeoutFrom("8000").TotalMilliseconds);
        }
    }
}

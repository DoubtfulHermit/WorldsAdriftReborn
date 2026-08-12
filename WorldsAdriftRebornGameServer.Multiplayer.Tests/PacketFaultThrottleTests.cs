using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The fault log must never hide the FIRST occurrence of a bad packet, and
    /// must never let a peer that throws forever spam a line per packet.
    /// </summary>
    public class PacketFaultThrottleTests
    {
        [Fact]
        public void Every_fault_within_the_initial_burst_is_logged()
        {
            PacketFaultThrottle throttle = new(first: 3, every: 1000);

            Assert.True(throttle.ShouldLog(out long t1));
            Assert.True(throttle.ShouldLog(out long t2));
            Assert.True(throttle.ShouldLog(out long t3));

            Assert.Equal(1, t1);
            Assert.Equal(2, t2);
            Assert.Equal(3, t3);
        }

        [Fact]
        public void After_the_burst_only_every_nth_fault_is_logged()
        {
            PacketFaultThrottle throttle = new(first: 2, every: 5);

            Assert.True(throttle.ShouldLog(out _));   // 1 - burst
            Assert.True(throttle.ShouldLog(out _));   // 2 - burst
            Assert.False(throttle.ShouldLog(out _));  // 3 - suppressed
            Assert.False(throttle.ShouldLog(out _));  // 4
            Assert.True(throttle.ShouldLog(out long t5)); // 5 - every 5th
            Assert.Equal(5, t5);
            Assert.False(throttle.ShouldLog(out _));  // 6
            Assert.False(throttle.ShouldLog(out _));  // 7
            Assert.False(throttle.ShouldLog(out _));  // 8
            Assert.False(throttle.ShouldLog(out _));  // 9
            Assert.True(throttle.ShouldLog(out long t10)); // 10
            Assert.Equal(10, t10);
        }

        [Fact]
        public void The_running_total_counts_suppressed_faults_too()
        {
            PacketFaultThrottle throttle = new(first: 1, every: 1000);

            throttle.ShouldLog(out _);
            for (int i = 0; i < 41; i++)
            {
                throttle.ShouldLog(out _);
            }

            Assert.Equal(42, throttle.Count);
        }

        [Fact]
        public void A_first_of_zero_still_logs_on_the_every_cadence()
        {
            // Degenerate but must not divide-by-zero or go silent forever.
            PacketFaultThrottle throttle = new(first: 0, every: 1);

            Assert.True(throttle.ShouldLog(out _));
            Assert.True(throttle.ShouldLog(out _));
        }
    }
}

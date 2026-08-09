using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The floor on how often a new AfterPlayer world entity starts loading on a
    /// joining client. It exists because the spawn handshake, though already
    /// ack-gated per step, drains ~44 world entities in a couple of milliseconds
    /// on a LAN and the client's asset loader is synchronous - one long first-load
    /// hitch. These assert the env parsing and the streaming arithmetic natively,
    /// and that the reused <see cref="CadenceTimer"/> actually spaces releases.
    /// </summary>
    public class SpawnPacePolicyTests
    {
        private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

        // ------------------------------------------------------------------
        // ENV -> INTERVAL
        // ------------------------------------------------------------------

        [Fact]
        public void UnsetOrGarbageFallsBackToDefault()
        {
            TimeSpan expected = Ms(SpawnPacePolicy.DefaultMs);
            Assert.Equal(expected, SpawnPacePolicy.IntervalFrom(null));
            Assert.Equal(expected, SpawnPacePolicy.IntervalFrom(""));
            Assert.Equal(expected, SpawnPacePolicy.IntervalFrom("   "));
            Assert.Equal(expected, SpawnPacePolicy.IntervalFrom("slow"));
        }

        [Fact]
        public void NegativeIsNonsenseAndFallsBackToDefault()
        {
            Assert.Equal(Ms(SpawnPacePolicy.DefaultMs), SpawnPacePolicy.IntervalFrom("-5"));
        }

        [Fact]
        public void ValidValuesParse()
        {
            Assert.Equal(Ms(25), SpawnPacePolicy.IntervalFrom("25"));
            Assert.Equal(Ms(100), SpawnPacePolicy.IntervalFrom("100"));
        }

        [Fact]
        public void LargeValuesAreClampedSoATypoCannotStallStreamingForMinutes()
        {
            Assert.Equal(Ms(SpawnPacePolicy.MaxMs), SpawnPacePolicy.IntervalFrom("60000"));
        }

        [Fact]
        public void ZeroDisablesPacing()
        {
            // The one-line rollback to the old one-burst behaviour.
            Assert.Equal(TimeSpan.Zero, SpawnPacePolicy.IntervalFrom("0"));
            Assert.False(SpawnPacePolicy.IsEnabled(SpawnPacePolicy.IntervalFrom("0")));
        }

        [Fact]
        public void AnyPositiveIntervalIsEnabled()
        {
            Assert.True(SpawnPacePolicy.IsEnabled(SpawnPacePolicy.IntervalFrom(null)));
            Assert.True(SpawnPacePolicy.IsEnabled(Ms(1)));
            Assert.False(SpawnPacePolicy.IsEnabled(TimeSpan.Zero));
        }

        // ------------------------------------------------------------------
        // STREAM DURATION (first immediate, rest one interval apart)
        // ------------------------------------------------------------------

        [Fact]
        public void OneOrZeroEntitiesStreamInstantly()
        {
            Assert.Equal(TimeSpan.Zero, SpawnPacePolicy.StreamDurationFor(0, Ms(40)));
            Assert.Equal(TimeSpan.Zero, SpawnPacePolicy.StreamDurationFor(1, Ms(40)));
        }

        [Fact]
        public void NEntitiesTakeNMinusOneIntervals()
        {
            // 43 AfterPlayer entities at the 40 ms default: the first is immediate,
            // the other 42 are 40 ms apart, so the world is in after ~1.68 s.
            Assert.Equal(Ms(40 * 42), SpawnPacePolicy.StreamDurationFor(43, Ms(40)));
        }

        [Fact]
        public void DisabledPacingHasNoDuration()
        {
            Assert.Equal(TimeSpan.Zero, SpawnPacePolicy.StreamDurationFor(43, TimeSpan.Zero));
        }

        // ------------------------------------------------------------------
        // COMPOSITION WITH THE METRONOME (how the server actually paces)
        // ------------------------------------------------------------------

        [Fact]
        public void AMetronomeAtThePaceReleasesOnePerIntervalNotAllAtOnce()
        {
            // Mirrors the server: one CadenceTimer per peer, fed the monotonic
            // clock, gates each ready AfterPlayer entity. Ten entities become ready
            // at t=0 (an ack burst); the pacer must let exactly one through now.
            TimeSpan interval = SpawnPacePolicy.IntervalFrom("40");
            CadenceTimer pacer = new CadenceTimer(interval);

            int releasedAtZero = 0;
            for (int i = 0; i < 10; i++)
            {
                if (pacer.Due(TimeSpan.Zero))
                {
                    releasedAtZero++;
                }
            }
            Assert.Equal(1, releasedAtZero);

            // One interval later, exactly one more.
            Assert.True(pacer.Due(interval));
            Assert.False(pacer.Due(interval));
        }
    }
}

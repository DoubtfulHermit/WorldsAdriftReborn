using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The synthetic timeline. The property everything hangs on: the receiver
    /// pairs "latest 1073 stamp" with each arriving 190602 position and
    /// COLLAPSES queue entries that share a stamp, so every emitted sample must
    /// carry a stamp strictly greater than the last - including across float
    /// narrowing, hours of session, and seed re-serves.
    /// </summary>
    public class RelayTimestampPolicyTests
    {
        private const double Step20Hz = 0.05;

        // ------------------------------------------------------------------
        // THE EPOCH
        // ------------------------------------------------------------------

        [Fact]
        public void SeedIsTwiceTheReceiversInterpolationDelay()
        {
            // 2 x the client's hardcoded 0.1 s DEFAULT_INTERPOLATION_DELAY, so
            // the first live samples land AHEAD of the receiver's playback
            // clock. The old seed said 100 while live senders publish ~0.0x - a
            // guaranteed snap on first sight of another player.
            Assert.Equal(0.2f, RelayTimestampPolicy.SeedTimestampSeconds);
        }

        [Fact]
        public void SampleZeroIsTheSeed()
        {
            Assert.Equal(RelayTimestampPolicy.SeedTimestampSeconds, RelayTimestampPolicy.StampFor(0, Step20Hz));
        }

        [Fact]
        public void FirstLiveEmitIsOneStepPastTheSeed()
        {
            Assert.Equal(0.25f, RelayTimestampPolicy.StampFor(1, Step20Hz));
        }

        [Fact]
        public void StepDerivesFromTheConfiguredCadence()
        {
            // At 18 Hz the step is 1/18 - the policy takes whatever the cadence
            // says, never a hardcoded rate.
            double step18 = RelayCadencePolicy.StepSecondsFor(18.0);
            Assert.Equal(0.2f + (float)(1.0 / 18.0), RelayTimestampPolicy.StampFor(1, step18), 6);
        }

        // ------------------------------------------------------------------
        // STRICT MONOTONICITY - THE WHOLE POINT
        // ------------------------------------------------------------------

        [Fact]
        public void StampsAreStrictlyIncreasingForAWholeSession()
        {
            // Four hours at 20 Hz = 288,000 samples. Computed in double and
            // narrowed to float: the narrowing is where a naive scheme (uptime
            // seconds) loses sub-tick precision, so THAT is what is asserted.
            float previous = RelayTimestampPolicy.StampFor(0, Step20Hz);
            for (long i = 1; i <= 288_000; i++)
            {
                float stamp = RelayTimestampPolicy.StampFor(i, Step20Hz);
                Assert.True(stamp > previous, $"stamp {stamp} at sample {i} did not increase past {previous}");
                previous = stamp;
            }
        }

        [Fact]
        public void TimelineNeverProducesABadPairAcrossAWholeSession()
        {
            // The self-check counter the emitter logs. Nonzero means two
            // positions under one effective timestamp - the exact bug class the
            // rewrite exists to remove - so the tests hold it at zero.
            SyntheticTimeline timeline = new();
            float previous = RelayTimestampPolicy.SeedTimestampSeconds;
            for (int i = 0; i < 288_000; i++)
            {
                float stamp = timeline.Next(Step20Hz);
                Assert.True(stamp > previous);
                previous = stamp;
            }
            Assert.Equal(0, timeline.BadPairs);
        }

        [Fact]
        public void GuardForcesIncreaseEvenIfThePolicyMisbehaves()
        {
            // A step of zero makes the pure policy emit the same stamp forever.
            // The timeline must refuse to pass that to a live client: count the
            // fault, force the next representable float.
            SyntheticTimeline timeline = new();
            float a = timeline.Next(0.0);
            float b = timeline.Next(0.0);
            Assert.True(b > a);
            Assert.True(timeline.BadPairs > 0);
        }

        // ------------------------------------------------------------------
        // INCARNATIONS
        // ------------------------------------------------------------------

        [Fact]
        public void FreshTimelineStartsOneStepPastTheSeed()
        {
            SyntheticTimeline timeline = new();
            Assert.Equal(0.25f, timeline.Next(Step20Hz));
            Assert.Equal(0.3f, timeline.Next(Step20Hz), 6);
        }

        [Fact]
        public void ReseedResetsTheTimelineToMatchTheSeed()
        {
            // A recipient re-served the 1073 seed sees stamp 0.2 again; the
            // stream must restart just past it, not continue from minute nine -
            // stream and seed must never disagree about the epoch.
            SyntheticTimeline timeline = new();
            for (int i = 0; i < 10_000; i++)
            {
                timeline.Next(Step20Hz);
            }

            timeline.ResetIncarnation();
            Assert.Equal(0.25f, timeline.Next(Step20Hz));
        }

        [Fact]
        public void BadPairCounterSurvivesReseed()
        {
            // It is a lifetime fault counter. Resetting it on reconnect would
            // hide exactly the faults reconnects cause.
            SyntheticTimeline timeline = new();
            timeline.Next(0.0);
            timeline.Next(0.0);
            long faults = timeline.BadPairs;
            Assert.True(faults > 0);

            timeline.ResetIncarnation();
            Assert.Equal(faults, timeline.BadPairs);
        }

        [Fact]
        public void IssuedCountCountsLiveEmits()
        {
            SyntheticTimeline timeline = new();
            Assert.Equal(0, timeline.IssuedCount);
            timeline.Next(Step20Hz);
            timeline.Next(Step20Hz);
            Assert.Equal(2, timeline.IssuedCount);
            timeline.ResetIncarnation();
            Assert.Equal(0, timeline.IssuedCount);
        }
    }
}

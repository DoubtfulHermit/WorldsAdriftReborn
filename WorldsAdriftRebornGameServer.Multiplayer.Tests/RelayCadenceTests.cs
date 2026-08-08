using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The emit cadence. The numbers under test exist because the receiving
    /// client's interpolation delay is a FIXED 100 ms: 20 Hz is the rate at
    /// which one lost snapshot still leaves two samples inside that budget.
    /// </summary>
    public class RelayCadenceTests
    {
        // ------------------------------------------------------------------
        // THE RATE AND ITS BOUNDS
        // ------------------------------------------------------------------

        [Fact]
        public void DefaultIsTwentyHz()
        {
            Assert.Equal(20.0, RelayCadencePolicy.DefaultHz);
        }

        [Fact]
        public void UnsetOrGarbageFallsBackToDefault()
        {
            Assert.Equal(RelayCadencePolicy.DefaultHz, RelayCadencePolicy.HzFrom(null));
            Assert.Equal(RelayCadencePolicy.DefaultHz, RelayCadencePolicy.HzFrom(""));
            Assert.Equal(RelayCadencePolicy.DefaultHz, RelayCadencePolicy.HzFrom("  "));
            Assert.Equal(RelayCadencePolicy.DefaultHz, RelayCadencePolicy.HzFrom("fast"));
            Assert.Equal(RelayCadencePolicy.DefaultHz, RelayCadencePolicy.HzFrom("NaN"));
        }

        [Fact]
        public void ValidRatesParse()
        {
            Assert.Equal(25.0, RelayCadencePolicy.HzFrom("25"));
            Assert.Equal(17.5, RelayCadencePolicy.HzFrom("17.5"));
        }

        [Fact]
        public void RatesAreClamped()
        {
            Assert.Equal(RelayCadencePolicy.MinHz, RelayCadencePolicy.HzFrom("1"));
            Assert.Equal(RelayCadencePolicy.MaxHz, RelayCadencePolicy.HzFrom("300"));
        }

        [Fact]
        public void FloorKeepsOneLossInsideTheReceiversBudget()
        {
            // One lost emit at MinHz leaves a 2/MinHz gap; the receiver's queue
            // holds 5 slots x the emit interval. The floor must keep a single
            // loss from draining playback entirely - 15 Hz is 66.7 ms, and 5
            // slots at that interval is 333 ms of buffer against the 100 ms
            // delay. What the floor really guards is the STEP: below ~10 Hz the
            // per-sample stamp step exceeds the whole 100 ms budget.
            Assert.True(1.0 / RelayCadencePolicy.MinHz < 0.1, "one emit interval must stay under the client's 100 ms delay");
        }

        [Fact]
        public void IntervalAndStepAreDerivedFromTheRate()
        {
            Assert.Equal(TimeSpan.FromSeconds(0.05), RelayCadencePolicy.IntervalFor(20.0));
            Assert.Equal(0.05, RelayCadencePolicy.StepSecondsFor(20.0), 12);
            // Never hardcoded to one rate: an operator at 25 Hz gets 40 ms steps.
            Assert.Equal(0.04, RelayCadencePolicy.StepSecondsFor(25.0), 12);
        }

        [Fact]
        public void StatsWindowIsFiveSeconds()
        {
            Assert.Equal(TimeSpan.FromSeconds(5), RelayCadencePolicy.StatsInterval);
        }

        // ------------------------------------------------------------------
        // THE METRONOME
        // ------------------------------------------------------------------

        private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

        [Fact]
        public void FirstCallIsDue()
        {
            CadenceTimer timer = new(S(0.05));
            Assert.True(timer.Due(S(10)));
        }

        [Fact]
        public void NotDueAgainInsideTheInterval()
        {
            CadenceTimer timer = new(S(0.05));
            timer.Due(S(10));
            Assert.False(timer.Due(S(10.01)));
            Assert.False(timer.Due(S(10.049)));
        }

        [Fact]
        public void DueAgainAfterTheInterval()
        {
            CadenceTimer timer = new(S(0.05));
            timer.Due(S(10));
            Assert.True(timer.Due(S(10.05)));
        }

        [Fact]
        public void SchedulesOnTheGridSoJitterDoesNotDrift()
        {
            // The caller polls late by 10 ms every time; the timer must still
            // fire 20 times in a second, not 16 - it schedules nextDue from the
            // previous DUE time, not from when the caller happened to ask.
            CadenceTimer timer = new(S(0.05));
            int fired = 0;
            for (double t = 10.0; t < 11.0; t += 0.01)
            {
                if (timer.Due(S(t)))
                {
                    fired++;
                }
            }
            Assert.Equal(20, fired);
        }

        [Fact]
        public void StallDoesNotBurstCatchUp()
        {
            // A 500 ms main-loop stall owes ten ticks. Firing them back-to-back
            // would be exactly the clumping the cadence exists to remove: one
            // tick now, then back on the grid.
            CadenceTimer timer = new(S(0.05));
            timer.Due(S(10));

            Assert.True(timer.Due(S(10.5)));
            Assert.False(timer.Due(S(10.5001)));
            Assert.False(timer.Due(S(10.549)));
            Assert.True(timer.Due(S(10.55)));
        }

        [Fact]
        public void ZeroOrNegativeIntervalIsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CadenceTimer(TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CadenceTimer(S(-1)));
        }
    }
}

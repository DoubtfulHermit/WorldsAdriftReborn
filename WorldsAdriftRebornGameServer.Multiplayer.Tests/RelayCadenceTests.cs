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

        [Fact]
        public void Backpressure_is_per_recipient_hysteretic_and_leaves_healthy_peers_unchanged()
        {
            RecipientRelayPressure pressure = RecipientRelayPressure.Normal;
            Assert.Equal(TimeSpan.Zero, RelayBackpressurePolicy.MinimumInterval(pressure));

            pressure = RelayBackpressurePolicy.Next(pressure, 700);
            Assert.Equal(RecipientRelayPressure.Degraded, pressure);
            Assert.Equal(TimeSpan.FromMilliseconds(100),
                RelayBackpressurePolicy.MinimumInterval(pressure));

            // Do not flap around the 500 ms entry boundary.
            Assert.Equal(RecipientRelayPressure.Degraded,
                RelayBackpressurePolicy.Next(pressure, 400));
            pressure = RelayBackpressurePolicy.Next(pressure, 1800);
            Assert.Equal(RecipientRelayPressure.Severe, pressure);
            Assert.Equal(TimeSpan.FromMilliseconds(200),
                RelayBackpressurePolicy.MinimumInterval(pressure));
            Assert.Equal(RecipientRelayPressure.Degraded,
                RelayBackpressurePolicy.Next(pressure, 900));
            Assert.Equal(RecipientRelayPressure.Normal,
                RelayBackpressurePolicy.Next(pressure, 100));
        }

        [Fact]
        public void Backpressure_drops_only_superseding_samples_until_the_recipient_interval_is_due()
        {
            TimeSpan sent = TimeSpan.FromSeconds(10);
            Assert.False(RelayBackpressurePolicy.IsDue(
                sent + TimeSpan.FromMilliseconds(50), sent,
                RecipientRelayPressure.Degraded));
            Assert.True(RelayBackpressurePolicy.IsDue(
                sent + TimeSpan.FromMilliseconds(100), sent,
                RecipientRelayPressure.Degraded));
            Assert.True(RelayBackpressurePolicy.IsDue(
                sent + TimeSpan.FromMilliseconds(1), sent,
                RecipientRelayPressure.Normal));
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
        public void ASkippedIntervalIsCounted()
        {
            // The skip is the server-side name for a lost position: the window
            // it stretches is long enough to hold two of a sender's publishes,
            // and the older one is coalesced away where nobody sees it. It was
            // silent until now, which is why the only evidence of a slipping
            // cadence was a two-bot harness.
            CadenceTimer timer = new(S(0.05));
            timer.Due(S(10));
            Assert.Equal(0, timer.SkippedIntervals);

            // On the grid: no skip.
            Assert.True(timer.Due(S(10.05)));
            Assert.Equal(0, timer.SkippedIntervals);

            // Back 500 ms late: nine intervals went by unattended, counted once
            // as the one re-anchoring event it is.
            Assert.True(timer.Due(S(10.6)));
            Assert.Equal(1, timer.SkippedIntervals);

            // And it keeps counting, because the number that matters live is
            // whether it is still RISING.
            Assert.True(timer.Due(S(11.2)));
            Assert.Equal(2, timer.SkippedIntervals);
        }

        [Fact]
        public void ATickThatIsMerelyLateIsNotASkip()
        {
            // Late but inside the interval is the normal case on a loop that
            // turns on packet arrival. Counting those would bury the real
            // signal in noise.
            CadenceTimer timer = new(S(0.05));
            timer.Due(S(10));

            Assert.True(timer.Due(S(10.06)));
            Assert.True(timer.Due(S(10.11)));
            Assert.Equal(0, timer.SkippedIntervals);
        }

        [Fact]
        public void ZeroOrNegativeIntervalIsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CadenceTimer(TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CadenceTimer(S(-1)));
        }
    }
}

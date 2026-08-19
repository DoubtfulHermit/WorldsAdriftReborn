using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The soak gate's LEVEL check.
    ///
    /// The numbers here are not invented. They are the measured runs of
    /// 2026-08-19: the one that reported FLAT at 93.3% delivery with a median
    /// staleness of a whole emit interval, and the repeat runs on one unchanged
    /// tree that came in at 98.3% and 100% with medians on opposite sides of the
    /// emit grid. Every assertion below is "would this gate have told the truth
    /// about that run".
    /// </summary>
    public class SoakLevelPolicyTests
    {
        private static SoakLevelPolicy.SoakLevels Levels(
            double p50, double p95, double overstale, long matched, long sends) =>
            new(p50, p95, overstale, matched, sends);

        /// <summary>A repeat run on the unchanged tree: 100% delivered, no missed ticks.</summary>
        private static readonly SoakLevelPolicy.SoakLevels HealthyRun =
            Levels(p50: 0.28, p95: 45.77, overstale: 0.004, matched: 8643, sends: 8642);

        /// <summary>
        /// The run the old gate called FLAT: delivery 93.3%, and the MEDIAN
        /// sample over a whole emit interval - so more than half missed a tick.
        /// </summary>
        private static readonly SoakLevelPolicy.SoakLevels SteppedRun =
            Levels(p50: 50.4, p95: 55.6, overstale: 0.52, matched: 20153, sends: 21600);

        private static readonly SoakLevelPolicy.SoakLevelBudget Budget =
            SoakLevelPolicy.SoakLevelBudget.Default;

        // ------------------------------------------------------------------
        // THE ABSOLUTE CHECK
        // ------------------------------------------------------------------

        [Fact]
        public void AHealthyRunIsWithinBudget()
        {
            SoakLevelPolicy.SoakLevelVerdict verdict =
                SoakLevelPolicy.JudgeAbsolute(HealthyRun, Budget);

            Assert.True(verdict.WithinBudget);
            Assert.Empty(verdict.Breaches);
        }

        [Fact]
        public void TheOtherMeasuredHealthyRunIsAlsoWithinBudget()
        {
            // 98.3% delivered with the same code, hours apart. The floor has to
            // sit below the honest run-to-run spread or the gate cries wolf and
            // gets ignored, which is how a gate stops being one.
            SoakLevelPolicy.SoakLevelVerdict verdict = SoakLevelPolicy.JudgeAbsolute(
                Levels(p50: 0.29, p95: 46.07, overstale: 0.02, matched: 8491, sends: 8642),
                Budget);

            Assert.True(verdict.WithinBudget);
        }

        [Fact]
        public void TheSteppedRunFailsOnBothNumbersItMoved()
        {
            SoakLevelPolicy.SoakLevelVerdict verdict =
                SoakLevelPolicy.JudgeAbsolute(SteppedRun, Budget);

            Assert.False(verdict.WithinBudget);
            Assert.Contains(verdict.Breaches, b => b.Contains("missing ticks"));
            Assert.Contains(verdict.Breaches, b => b.Contains("delivery"));
        }

        [Fact]
        public void DeliveryAloneIsEnoughToFail()
        {
            // Phase luck can hold the percentiles low while emit ticks are being
            // missed, so delivery must stand on its own.
            SoakLevelPolicy.SoakLevelVerdict verdict = SoakLevelPolicy.JudgeAbsolute(
                Levels(p50: 0.3, p95: 15.0, overstale: 0.0, matched: 20153, sends: 21600),
                Budget);

            Assert.False(verdict.WithinBudget);
            Assert.Single(verdict.Breaches);
            Assert.Contains("delivery", verdict.Breaches[0]);
        }

        [Fact]
        public void MissedTicksAloneAreEnoughToFail()
        {
            // The converse: a relay can hold on to every sample and still
            // deliver each one a tick late.
            SoakLevelPolicy.SoakLevelVerdict verdict = SoakLevelPolicy.JudgeAbsolute(
                Levels(p50: 55.0, p95: 60.0, overstale: 0.40, matched: 21600, sends: 21600),
                Budget);

            Assert.False(verdict.WithinBudget);
            Assert.Single(verdict.Breaches);
            Assert.Contains("missing ticks", verdict.Breaches[0]);
        }

        [Fact]
        public void ThePercentilesThemselvesAreNeverJudged()
        {
            // Two runs with IDENTICAL cadence health and medians 45 ms apart -
            // which is what the phase between publish and emit grids does on
            // this harness with no code change at all. Both must pass, or the
            // gate is a coin toss.
            SoakLevelPolicy.SoakLevels low =
                Levels(p50: 0.24, p95: 46.0, overstale: 0.01, matched: 8642, sends: 8642);
            SoakLevelPolicy.SoakLevels high =
                Levels(p50: 45.75, p95: 46.0, overstale: 0.01, matched: 8642, sends: 8642);

            Assert.True(SoakLevelPolicy.JudgeAbsolute(low, Budget).WithinBudget);
            Assert.True(SoakLevelPolicy.JudgeAbsolute(high, Budget).WithinBudget);
        }

        [Fact]
        public void ARunThatMeasuredNothingIsNotALevelFailure()
        {
            // "Nothing was relayed" already has its own verdict (NO DATA). A
            // level breach on top of it would name the wrong failure and send
            // the next reader hunting a performance problem that never existed.
            SoakLevelPolicy.SoakLevelVerdict verdict = SoakLevelPolicy.JudgeAbsolute(
                Levels(p50: double.NaN, p95: double.NaN, overstale: 0.0, matched: 0, sends: 21600),
                Budget);

            Assert.True(verdict.WithinBudget);
        }

        [Fact]
        public void ExactlyAtTheLimitsPasses()
        {
            // A limit is the last acceptable value, not the first bad one:
            // failing AT the stated threshold makes the printed threshold a lie.
            SoakLevelPolicy.SoakLevelVerdict verdict = SoakLevelPolicy.JudgeAbsolute(
                Levels(p50: 20.0, p95: 40.0, overstale: 0.05, matched: 9700, sends: 10000),
                Budget);

            Assert.True(verdict.WithinBudget);
        }

        [Fact]
        public void ZeroSendsIsNotADeliveryFailure()
        {
            // Delivery of nothing is undefined, not zero percent.
            SoakLevelPolicy.SoakLevelVerdict verdict = SoakLevelPolicy.JudgeAbsolute(
                Levels(p50: 10.0, p95: 20.0, overstale: 0.0, matched: 5, sends: 0), Budget);

            Assert.True(verdict.WithinBudget);
        }

        // ------------------------------------------------------------------
        // THE BASELINE CHECK - the other axis from the drift check
        // ------------------------------------------------------------------

        [Fact]
        public void TheSteppedRunFailsAgainstAHealthyBaseline()
        {
            SoakLevelPolicy.SoakLevelVerdict verdict = SoakLevelPolicy.JudgeAgainstBaseline(
                SteppedRun, HealthyRun, SoakLevelPolicy.SoakStepBudget.Default);

            Assert.False(verdict.WithinBudget);
            Assert.Contains(verdict.Breaches, b => b.Contains("stepped"));
            Assert.Contains(verdict.Breaches, b => b.Contains("delivery fell"));
        }

        [Fact]
        public void TheSameRunTwiceIsNotAStep()
        {
            SoakLevelPolicy.SoakLevelVerdict verdict = SoakLevelPolicy.JudgeAgainstBaseline(
                HealthyRun, HealthyRun, SoakLevelPolicy.SoakStepBudget.Default);

            Assert.True(verdict.WithinBudget);
        }

        [Fact]
        public void GettingBetterIsNeverAFailure()
        {
            // A gate that reddens on an improvement teaches its reader to ignore
            // it. Only degradation is a breach.
            SoakLevelPolicy.SoakLevelVerdict verdict = SoakLevelPolicy.JudgeAgainstBaseline(
                HealthyRun, SteppedRun, SoakLevelPolicy.SoakStepBudget.Default);

            Assert.True(verdict.WithinBudget);
            Assert.Empty(verdict.Breaches);
        }

        [Fact]
        public void TheMeasuredRunToRunSpreadIsInsideTheStepBudget()
        {
            // 100% and 98.3% on one unchanged tree. The step budget must absorb
            // that or every second clean run is a false alarm.
            SoakLevelPolicy.SoakLevelVerdict verdict = SoakLevelPolicy.JudgeAgainstBaseline(
                Levels(p50: 0.29, p95: 46.07, overstale: 0.02, matched: 8491, sends: 8642),
                HealthyRun,
                SoakLevelPolicy.SoakStepBudget.Default);

            Assert.True(verdict.WithinBudget);
        }

        [Fact]
        public void ACostThatStaysInsideTheAbsoluteLimitsIsStillSeen()
        {
            // The honest-cost case: content that takes delivery from 100% to
            // 97.5% breaks no absolute limit, and that is exactly the change
            // that should be argued about rather than absorbed silently.
            SoakLevelPolicy.SoakLevels afterContent =
                Levels(p50: 12.0, p95: 46.0, overstale: 0.03, matched: 9750, sends: 10000);

            Assert.True(SoakLevelPolicy.JudgeAbsolute(afterContent, Budget).WithinBudget);

            SoakLevelPolicy.SoakLevelVerdict step = SoakLevelPolicy.JudgeAgainstBaseline(
                afterContent,
                Levels(p50: 12.0, p95: 46.0, overstale: 0.01, matched: 10000, sends: 10000),
                SoakLevelPolicy.SoakStepBudget.Default);

            Assert.False(step.WithinBudget);
            Assert.Contains("delivery fell", step.Breaches[0]);
        }

        [Fact]
        public void AMissingOrEmptyBaselineNeverFailsARun()
        {
            // A fresh checkout has no recorded baseline, and a baseline that
            // measured nothing proves nothing. Neither may turn a run red - the
            // absolute check is what covers those cases.
            SoakLevelPolicy.SoakLevelVerdict verdict = SoakLevelPolicy.JudgeAgainstBaseline(
                SteppedRun, Levels(0, 0, 0, 0, 0), SoakLevelPolicy.SoakStepBudget.Default);

            Assert.True(verdict.WithinBudget);
        }

        // ------------------------------------------------------------------
        // THE KNOBS
        // ------------------------------------------------------------------

        [Fact]
        public void AGarbageOverrideKeepsTheDefault()
        {
            Assert.Equal(20.0, SoakLevelPolicy.ThresholdFrom(null, 20.0));
            Assert.Equal(20.0, SoakLevelPolicy.ThresholdFrom("", 20.0));
            Assert.Equal(20.0, SoakLevelPolicy.ThresholdFrom("soon", 20.0));
            Assert.Equal(20.0, SoakLevelPolicy.ThresholdFrom("NaN", 20.0));
            Assert.Equal(20.0, SoakLevelPolicy.ThresholdFrom("-5", 20.0));
            Assert.Equal(35.5, SoakLevelPolicy.ThresholdFrom("35.5", 20.0));
        }

        [Fact]
        public void AnOverrideIsReadInvariantCulture()
        {
            // The maintainer's locale writes decimals with a comma; a gate that
            // silently read "35,5" as 355 would be no gate at all.
            Assert.Equal(20.0, SoakLevelPolicy.ThresholdFrom("35,5", 20.0));
        }
    }
}

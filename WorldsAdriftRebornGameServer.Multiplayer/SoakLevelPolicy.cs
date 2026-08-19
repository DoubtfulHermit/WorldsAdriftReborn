using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The LEVEL half of the relay soak verdict.
    ///
    /// WHY IT EXISTS. The soak's original verdict asks one question - "is
    /// staleness worse at the END of this run than it was at the START" - in two
    /// forms, an end-minus-start drift and a least-squares trend. That question
    /// catches the pathology it was built for (a queue that ages while its rate
    /// stays flat) and it must keep being asked. But it is blind along the other
    /// axis: a run that begins bad and STAYS bad is perfectly flat. On
    /// 2026-08-19 a soak reported FLAT with delivery at 93.3% and a median
    /// staleness of a full emit interval, next to runs of the same harness at
    /// 100% and sub-millisecond. Both passed. Content lands as a step, so a gate
    /// that can only see slopes cannot gate content.
    ///
    /// SO THIS ASKS THE OTHER QUESTION, twice:
    ///   - <see cref="JudgeAbsolute"/>: is the level defensible ON ITS OWN,
    ///     against limits derived from the emitter's own contract? No history
    ///     needed, so it works on the first run in a fresh checkout.
    ///   - <see cref="JudgeAgainstBaseline"/>: is the level worse than the
    ///     RECORDED one by more than a tolerance? The baseline is a committed
    ///     file, so a regression cannot quietly become the new normal - moving
    ///     it is a diff a reviewer sees.
    ///
    /// WHAT IT DELIBERATELY DOES NOT GATE ON: the staleness percentiles
    /// themselves. Where inside one emit interval a delivered sample lands is
    /// decided by the phase between the bots' publish grid and the emitter's
    /// grid, and that phase is set at join time. Measured repeatedly on one
    /// unchanged tree, the same code put the overall median at 0.28 ms in one
    /// run and tens of milliseconds in the next, with the two bots on opposite
    /// sides of the split. A percentile ceiling would have been a coin toss
    /// wearing a threshold's clothes. The two gated numbers are chosen because
    /// no phase can move them: a sample that waited longer than a whole emit
    /// period missed a tick, and a published sample that never arrived at all
    /// was coalesced away because one emit window held two of that sender's
    /// publishes. Both describe the emitter failing to give a published position
    /// a slot of its own, whatever the phase.
    ///
    /// Pure: no clock, no files, no environment. The reader that loads a
    /// baseline file and the harness that prints the verdict live in the bot.
    /// </summary>
    public static class SoakLevelPolicy
    {
        /// <summary>
        /// A finished soak reduced to the numbers a step change moves.
        /// Percentiles are carried for the report, not for judgement.
        /// </summary>
        public readonly record struct SoakLevels(
            double StalenessP50Ms,
            double StalenessP95Ms,
            double OverstaleShare,
            long Matched,
            long TotalSends)
        {
            /// <summary>
            /// Share of published movement samples that reached the other bot
            /// with their identity intact, as a percentage.
            ///
            /// WHY THIS IS PHASE-INVARIANT. The bots publish at 18 Hz and the
            /// relay emits at 20 Hz, so every published sample has an emit slot
            /// of its own with two slots a second to spare. A sample that never
            /// arrives was therefore COALESCED AWAY - a second sample from the
            /// same sender reached the same emit window - which cannot happen
            /// while every window is 50 ms and every publish 55.6 ms apart. Lost
            /// delivery is therefore counted in the units a player feels: motion
            /// that was published and never rendered on the other client.
            /// </summary>
            public double DeliveredPercent =>
                TotalSends > 0 ? 100.0 * Matched / TotalSends : 0.0;

            /// <summary><see cref="OverstaleShare"/> as a percentage, for messages.</summary>
            public double OverstalePercent => 100.0 * OverstaleShare;
        }

        /// <summary>
        /// Limits a run must sit under on its own merits.
        /// </summary>
        public readonly record struct SoakLevelBudget(
            double OverstaleSharePercentCeiling,
            double DeliveredFloorPercent)
        {
            /// <summary>
            /// OverstaleShare ceiling 5%: the emitter's contract is that a
            /// sample waits at most until its next tick, so the share that
            /// waited longer than a whole period should be zero. Five points of
            /// slack absorb host scheduling on a shared desktop without letting
            /// through the observed failure, where the MEDIAN sample was over
            /// the line.
            ///
            /// Delivered floor 97%: the healthy figure is 100%, and eleven repeat
            /// runs across two trees came in between 98.3% and 100% - so the
            /// floor sits below the measured spread and well above the 93.3% that
            /// went unnoticed. Losing three points of a player's movement samples
            /// is already visible motion, not a rounding artefact.
            ///
            /// Both are flat numbers rather than cadence-derived: they are
            /// SHARES, and the share of samples that miss their tick should not
            /// grow just because the tick got slower.
            /// </summary>
            public static readonly SoakLevelBudget Default = new(5.0, 97.0);
        }

        /// <summary>
        /// How much worse than the recorded baseline a run may be before it is
        /// called a step change.
        ///
        /// Both budgets are deliberately tighter than the absolute limits: the
        /// absolute check states what the relay may NEVER do, and this one
        /// states what THIS world on THIS harness has been shown to do. The
        /// second is the one that catches a cost that is real but still inside
        /// the contract - the "409 more entities cost us two points" case, which
        /// deserves to be seen and argued about rather than absorbed.
        /// </summary>
        public readonly record struct SoakStepBudget(
            double OverstalePointsStep,
            double DeliveredDropPoints)
        {
            public static readonly SoakStepBudget Default = new(3.0, 2.0);
        }

        /// <summary>
        /// The outcome of one level check. Breaches are human sentences, in the
        /// order they were tested, so the harness can print them verbatim - a
        /// gate that fails without saying which number moved sends the next
        /// reader back to the CSV.
        /// </summary>
        public sealed record SoakLevelVerdict(bool WithinBudget, IReadOnlyList<string> Breaches);

        private static readonly IReadOnlyList<string> NoBreaches = Array.Empty<string>();

        /// <summary>
        /// Judge a run against absolute limits. A run with no matched samples is
        /// NOT judged here - "nothing was relayed" is the harness's own NO DATA
        /// verdict, and calling that a level breach would report the wrong
        /// failure and send the next reader hunting a performance problem that
        /// never existed.
        /// </summary>
        public static SoakLevelVerdict JudgeAbsolute(SoakLevels levels, SoakLevelBudget budget)
        {
            if (levels.Matched <= 0)
            {
                return new SoakLevelVerdict(true, NoBreaches);
            }

            List<string> breaches = new();

            if (levels.OverstalePercent > budget.OverstaleSharePercentCeiling)
            {
                breaches.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0:0.#}% of delivered samples waited longer than a whole emit interval "
                    + "(ceiling {1:0.#}%): the cadence is missing ticks",
                    levels.OverstalePercent, budget.OverstaleSharePercentCeiling));
            }

            if (levels.TotalSends > 0 && levels.DeliveredPercent < budget.DeliveredFloorPercent)
            {
                breaches.Add(string.Format(CultureInfo.InvariantCulture,
                    "delivery {0:0.#}% is under the {1:0.#}% floor "
                    + "({2} of {3} published samples shared an emit window with a newer one and were dropped)",
                    levels.DeliveredPercent, budget.DeliveredFloorPercent,
                    levels.TotalSends - levels.Matched, levels.TotalSends));
            }

            return new SoakLevelVerdict(breaches.Count == 0, breaches);
        }

        /// <summary>
        /// Judge a run against a recorded baseline. Only DEGRADATION fails: a
        /// run better than the baseline is a pass, not an "unexpected change",
        /// because the honest response to a genuine improvement must never be to
        /// make the gate red.
        /// </summary>
        public static SoakLevelVerdict JudgeAgainstBaseline(
            SoakLevels current, SoakLevels baseline, SoakStepBudget step)
        {
            if (current.Matched <= 0 || baseline.Matched <= 0)
            {
                return new SoakLevelVerdict(true, NoBreaches);
            }

            List<string> breaches = new();

            double overstaleStep = current.OverstalePercent - baseline.OverstalePercent;
            if (overstaleStep > step.OverstalePointsStep)
            {
                breaches.Add(string.Format(CultureInfo.InvariantCulture,
                    "missed-tick share stepped {0:+0.#;-0.#;0} point(s) above the recorded baseline "
                    + "({1:0.#}% -> {2:0.#}%; step budget {3:0.#} point(s))",
                    overstaleStep, baseline.OverstalePercent, current.OverstalePercent,
                    step.OverstalePointsStep));
            }

            double deliveryDrop = baseline.DeliveredPercent - current.DeliveredPercent;
            if (deliveryDrop > step.DeliveredDropPoints)
            {
                breaches.Add(string.Format(CultureInfo.InvariantCulture,
                    "delivery fell {0:0.#} point(s) below the recorded baseline "
                    + "({1:0.#}% -> {2:0.#}%; step budget {3:0.#} point(s))",
                    deliveryDrop, baseline.DeliveredPercent, current.DeliveredPercent,
                    step.DeliveredDropPoints));
            }

            return new SoakLevelVerdict(breaches.Count == 0, breaches);
        }

        /// <summary>
        /// A threshold override from an environment string: invariant-culture,
        /// non-negative, and the fallback for anything unset or unparsable. Same
        /// contract as <see cref="RelayCadencePolicy.HzFrom"/> and for the same
        /// reason - a mistyped knob must not decide a gate by accident.
        /// </summary>
        public static double ThresholdFrom(string? env, double fallback)
        {
            if (string.IsNullOrWhiteSpace(env)
                || !double.TryParse(env, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.IsNaN(value)
                || value < 0.0)
            {
                return fallback;
            }

            return value;
        }
    }
}

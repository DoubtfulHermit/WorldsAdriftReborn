using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Simulation
{
    /// <summary>
    /// The diagnostic-only coupling score.
    ///
    /// <para>
    /// THESE WEIGHTS ARE NOT CALIBRATED. Nobody has measured a message rate, a
    /// physics contact rate or a migration cost to produce them. They are an
    /// ordinal ranking written down so that "the helm edge matters more than the
    /// proximity edge" is a number a panel can sort by - and nothing else. Do not
    /// read a pressure of 0.75 as three quarters of anything. Do not gate any
    /// behaviour on this value; the moment something does, these invented numbers
    /// become load-bearing and the first real measurement becomes a regression.
    /// </para>
    ///
    /// <para>
    /// The formula is the handover's: strength x latency sensitivity x activity.
    /// Activity is the only observed term, so an idle world scores exactly zero and
    /// the score only moves when the world does.
    /// </para>
    /// </summary>
    public static class InteractionPressure
    {
        // Evenly spaced ordinal steps. Even spacing is itself an admission that we
        // have no evidence for any other spacing.
        public static double WeightOf(InteractionStrength strength) => strength switch
        {
            InteractionStrength.Weak => 0.25,
            InteractionStrength.Moderate => 0.50,
            InteractionStrength.Strong => 0.75,
            InteractionStrength.VeryStrong => 1.00,
            _ => throw new ArgumentOutOfRangeException(nameof(strength)),
        };

        public static double WeightOf(InteractionLatencySensitivity sensitivity) => sensitivity switch
        {
            InteractionLatencySensitivity.Low => 0.25,
            InteractionLatencySensitivity.Moderate => 0.50,
            InteractionLatencySensitivity.High => 0.75,
            InteractionLatencySensitivity.VeryHigh => 1.00,
            _ => throw new ArgumentOutOfRangeException(nameof(sensitivity)),
        };

        /// <summary>
        /// Idle is exactly 0, not a small number: an observed-but-quiescent edge
        /// must be visible in the graph while contributing nothing to pressure.
        /// That is what lets "active cross-domain edges" be a sum over everything
        /// rather than a filtered sum with a second definition of "active" to drift.
        /// </summary>
        public static double WeightOf(InteractionActivity activity) => activity switch
        {
            InteractionActivity.Idle => 0.00,
            InteractionActivity.Intermittent => 0.50,
            InteractionActivity.Active => 1.00,
            _ => throw new ArgumentOutOfRangeException(nameof(activity)),
        };

        /// <summary>
        /// pressure(edge) = strength x latencySensitivity x activityFactor, in [0,1].
        /// Rounded to four places so the value is byte-identical across runs and
        /// across the process boundary into the stats file - determinism is the
        /// property this model actually promises.
        /// </summary>
        public static double For(InteractionEdge edge) => Round(
            WeightOf(edge.Strength)
            * WeightOf(edge.LatencySensitivity)
            * WeightOf(edge.Activity));

        /// <summary>The shared rounding, so sums and singletons round the same way.</summary>
        public static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
    }
}

using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Materials
{
    /// <summary>
    /// The WAREBORN_SHIP_MASS hull-mass override semantics, shared by everything
    /// that touches hull mass. The flat 50 kg per-part formula that used to live
    /// beside it is retired: per-part mass is now typed and provenance-labelled
    /// in <see cref="ShipMassEvaluator"/>, and the one flight total is
    /// <see cref="ShipMassSnapshot.TotalFlightMassKg"/> - never a count times a
    /// constant.
    /// </summary>
    public static class ShipTotalMass
    {
        public const double MaxOverrideKg = 1_000_000.0;

        /// <summary>
        /// Existing WAREBORN_SHIP_MASS semantics: a valid positive finite value
        /// below one million replaces the HULL mass. Mounted parts remain separate
        /// 1121 masses and are added afterwards, exactly as the client does.
        /// </summary>
        public static double HullMassWithOverride(double derivedHullMassKg, string? overrideRaw)
        {
            double derived = double.IsFinite(derivedHullMassKg) && derivedHullMassKg > 0.0
                ? derivedHullMassKg
                : HullMassCalculator.ReferenceHullMassKg;
            // Keep the pre-existing float parser and boundary semantics exactly;
            // this environment knob originally fed the float-valued 1257 field.
            if (!string.IsNullOrWhiteSpace(overrideRaw)
                && float.TryParse(overrideRaw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                && float.IsFinite(parsed) && parsed > 0f && parsed < MaxOverrideKg)
            {
                return parsed;
            }
            return derived;
        }
    }
}

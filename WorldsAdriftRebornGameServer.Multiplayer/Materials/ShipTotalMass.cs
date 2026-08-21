using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Materials
{
    /// <summary>
    /// One mass policy shared by the 1257 seed, force flight and telemetry.
    /// Retail sums the hull's ParentingMassAdderState with every mounted part's
    /// OriginalMassState. We serve 50 kg per part, so force flight must include
    /// the same amount rather than accelerating a bare hull while the client
    /// displays a rigged ship's larger mass.
    /// </summary>
    public static class ShipTotalMass
    {
        public const double MountedPartMassKg = 50.0;
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

        public static double TotalFlightMassKg(
            double derivedHullMassKg, int mountedPartCount, string? overrideRaw = null)
        {
            int parts = mountedPartCount < 0 ? 0 : mountedPartCount;
            return HullMassWithOverride(derivedHullMassKg, overrideRaw)
                + (parts * MountedPartMassKg);
        }
    }
}

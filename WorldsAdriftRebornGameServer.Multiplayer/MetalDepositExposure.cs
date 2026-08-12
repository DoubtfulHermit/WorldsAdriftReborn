namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// WHEN a deposit's core - and anything lodged in it - counts as EXPOSED.
    ///
    /// RETAIL BEHAVIOUR (worldsadrift.fandom.com/wiki/Mining and /wiki/Atlas_Shard):
    /// you break the OUTER SHELL of a metal node with the salvage gauntlet; once
    /// enough shell is gone the CENTRE is exposed - scrap sticking out, and any atlas
    /// shard visible as a green crystal in the core. The shard can be taken (interact,
    /// default E) AS SOON AS IT IS EXPOSED. You do NOT have to finish the node, and
    /// players were advised NOT to: finishing it drops the shard loose, where it can
    /// roll off the island. So exposure - not destruction - is the pickable seam.
    ///
    /// WHERE THE THRESHOLD COMES FROM. Exposure in the shipped client is geometric:
    /// MetalDepositCrustFractured.SimulateShot removes the crust fragments within a
    /// 0.2-0.3 m blast radius of each contact point (MetalDepositCrustFractured.cs:9,
    /// 38-63), so the core shows through where you have been shooting. The server
    /// cannot evaluate that - it has no meshes - but the client publishes an
    /// EQUIVALENT cue off a number the server DOES author: the core's own damage
    /// model. MetalDepositCoreVisuals.HealthPct feeds
    /// WeightedRendererSelectorList.Weight, and UpdateDamageModel picks
    ///     ModelVariant = round((1 - weight) * (variants - 1))
    /// (WeightedRendererSelectorList.cs:97-101). The core's authored damage set is a
    /// three-step Low/Med progression (MetalDepositCoreVisuals.CoreFracturePhases +
    /// OnVariantChanged's case 1 / case 2, MetalDepositCoreVisuals.cs:8-12, 84-95), so
    /// the FIRST cracked variant - the visible "the core has opened up" moment, with
    /// its Play_Harvest_Scrap_Core_Crack_Low cue - lands at
    ///     round((1 - h) * 2) >= 1  <=>  h <= 0.5.
    /// So HALF HEALTH is the client's own "the core is showing" moment, and that is
    /// the threshold this module encodes. It is a reconstruction of a geometric fact
    /// from a numeric one, not a measured retail constant - hence its own module,
    /// its own tests, and its own env knob.
    ///
    /// Pure: no ENet, no Improbable types, no game install.
    /// </summary>
    public static class MetalDepositExposure
    {
        /// <summary>
        /// The core-health FRACTION at or below which the core reads as exposed.
        /// 0.5 - the first cracked damage variant, see the class remarks.
        /// </summary>
        public const double DefaultExposureHealthFraction = 0.5;

        /// <summary>
        /// The exposure fraction from <c>WAREBORN_DEPOSIT_EXPOSE_AT</c> (a health
        /// fraction in (0, 1]), or <see cref="DefaultExposureHealthFraction"/>.
        ///
        /// Clamped to (0, 1]: a fraction of 0 or less would mean "exposed only when
        /// the core is already destroyed" (which the destroy path handles anyway) and
        /// anything above 1 would mean "exposed before the first shot", which would
        /// hand out a shard nobody mined. A garbled value falls back to the default
        /// rather than crashing the mining loop.
        /// </summary>
        public static double ExposureHealthFraction(string? env)
        {
            if (!string.IsNullOrWhiteSpace(env)
                && double.TryParse(env.Trim(), System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out double f)
                && f > 0.0 && f <= 1.0)
            {
                return f;
            }
            return DefaultExposureHealthFraction;
        }

        /// <summary>
        /// How many salvage shots on a deposit that empties in
        /// <paramref name="shotsToDeplete"/> are needed before the core reads as
        /// exposed, at <paramref name="healthFraction"/> remaining health.
        ///
        /// Health after n shots is (1 - n/shotsToDeplete), so exposure needs
        ///     1 - n/shotsToDeplete &lt;= healthFraction
        ///     n &gt;= shotsToDeplete * (1 - healthFraction)
        /// i.e. the CEILING of that, and never fewer than one shot: a deposit nobody
        /// has hit is never exposed, whatever the fraction rounds to. Never more than
        /// <paramref name="shotsToDeplete"/> either - the shot that empties the core
        /// exposes it by definition.
        /// </summary>
        public static int ShotsToExpose(int shotsToDeplete, double healthFraction)
        {
            if (shotsToDeplete < 1)
            {
                return 1;
            }
            double needed = shotsToDeplete * (1.0 - healthFraction);
            int shots = (int)System.Math.Ceiling(needed - 1e-9);
            if (shots < 1)
            {
                shots = 1;
            }
            return shots > shotsToDeplete ? shotsToDeplete : shots;
        }

        /// <summary>
        /// Whether a deposit that has taken <paramref name="hits"/> salvage shots and
        /// empties in <paramref name="shotsToDeplete"/> reads as EXPOSED at
        /// <paramref name="healthFraction"/>. Monotone: once true for a hit count it
        /// is true for every larger one, so the caller's "first time this became
        /// true" edge is a real once-only transition.
        /// </summary>
        public static bool IsExposed(int hits, int shotsToDeplete, double healthFraction) =>
            hits >= ShotsToExpose(shotsToDeplete, healthFraction);

        /// <summary>
        /// <see cref="IsExposed(int,int,double)"/> at the configured fraction, read
        /// from the environment. The one call the wire glue makes.
        /// </summary>
        public static bool IsExposed(int hits, int shotsToDeplete) =>
            IsExposed(hits, shotsToDeplete,
                ExposureHealthFraction(
                    System.Environment.GetEnvironmentVariable("WAREBORN_DEPOSIT_EXPOSE_AT")));
    }
}

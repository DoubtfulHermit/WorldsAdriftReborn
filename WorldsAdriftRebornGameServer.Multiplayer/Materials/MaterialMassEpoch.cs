using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Materials
{
    /// <summary>
    /// WHICH PATCH ERA'S MASS TABLE WE SHIP, and the mechanical guard that stops a
    /// future reader importing a row out of a different one.
    ///
    /// ==================================================================
    /// THE TRAP. READ THIS BEFORE CHANGING ANY NUMBER IN
    /// <see cref="MaterialCatalog"/>.
    ///
    /// Worlds Adrift rebalanced material weights at least once, and the community
    /// data that survives straddles the rebalance. AT LEAST TWO COMPLETE,
    /// SELF-CONSISTENT per-material weight tables exist, and they are NOT competing
    /// measurements of one truth - they are DIFFERENT PATCH EPOCHS, each of which
    /// was correct when it was measured. Their material ORDERINGS genuinely differ:
    /// the final era puts gold above tungsten, the calculator era puts tungsten
    /// above gold.
    ///
    /// So the rule is not "find the best number per material". The rule is:
    ///
    ///     PICK AN EPOCH AND BE INTERNALLY CONSISTENT.
    ///     NEVER MIX ROWS ACROSS EPOCHS.
    ///
    /// Averaging or cherry-picking produces a material ranking NO VERSION OF THE
    /// GAME EVER HAD, which is worse than either table on its own.
    ///
    /// THE SPECIFIC MISTAKE THE NEXT READER WILL MAKE. The research note
    /// docs/research/findings-material-mass.md section 2.5 back-solves the deleted
    /// cannon sheet and recovers TUNGSTEN = 0.74, correcting that sheet's 0.80. That
    /// is an excellent result AND IT BELONGS TO THE CALCULATOR ERA. It says nothing
    /// about the final era, which independently publishes 0.70. Importing 0.74 into
    /// the table below would leave 22 final-era rows and one calculator-era row.
    /// <see cref="ForeignEpochMasses"/> lists 0.74 explicitly so that mistake fails a
    /// test instead of shipping.
    /// ==================================================================
    /// </summary>
    public static class MaterialMassEpoch
    {
        /// <summary>
        /// The epoch <see cref="MaterialCatalog"/> ships, named so a test and a
        /// reader can both state it. "Final era" = the last balance pass the game
        /// received before shutdown.
        /// </summary>
        public const string ShippedEpoch = "final-era";

        /// <summary>
        /// Why this epoch and not the other, in the order the reasons actually carry
        /// weight. Reason 1 is the decisive one and the other two only reinforce it.
        /// </summary>
        public const string ShippedEpochRationale =
            "1. RIGHT QUANTITY: it is PANEL weight-per-unit, and HullMassCalculator "
            + "computes hull mass from hull cells and panels. The calculator-era table "
            + "is ENGINE-COMPONENT weight-per-unit, a different component class that is "
            + "additionally tier-scaled by wpuFactors [1, 0.875, 0.7777, 0.7083]. "
            + "2. ONLY COMPLETE SET: the only table carrying orthite, epilar and eternium. "
            + "3. LATEST: matches the Update 29.4 resilience data we hold.";

        /// <summary>
        /// The final-era table, all 23 retail materials, in kilograms per unit.
        ///
        /// RECOVERED / WIKI, and corroborated by two independent artefacts that agree
        /// row-for-row with no residual:
        ///  * the Worlds Adrift wiki Metal and Wood pages (Wayback 2018/2019), and
        ///  * <c>sciencesheet.xls</c> from the Worlds-Adrift-Engine-Science repository,
        ///    whose <c>weight</c> column is these fifteen metals exactly.
        ///    Archived at docs/research/world-data/external/wa-community-2026-08-20b/
        ///    workbooks/engine-science-sciencesheet-xls/.
        ///
        /// The wiki's own caveat, verbatim, and it is the reason this is the right
        /// table for a HULL: "All of the following data is a specific weight per unit
        /// FOR PANELS. Each component is believed to have a different weight per unit,
        /// but the order of weight remains the same."
        ///
        /// This is NOT real-world density and must not be "corrected" towards physics:
        /// retail put steel above iron and silver above lead, which real densities do
        /// not.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, double> FinalEraMassPerUnitKg =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                // Metals, light to heavy.
                ["aluminium"] = 0.33,
                ["titanium"] = 0.35,
                ["tin"] = 0.38,
                ["iron"] = 0.39,
                ["bronze"] = 0.42,
                ["nickel"] = 0.43,
                ["orthite"] = 0.43,
                ["epilar"] = 0.46,
                ["steel"] = 0.50,
                ["eternium"] = 0.50,
                ["copper"] = 0.55,
                ["lead"] = 0.56,
                ["silver"] = 0.66,
                ["tungsten"] = 0.70,
                ["gold"] = 0.73,

                // Woods, light to heavy.
                ["cedar"] = 0.13,
                ["hemlock"] = 0.15,
                ["chestnut"] = 0.17,
                ["elm"] = 0.18,
                ["birch"] = 0.20,
                ["ash"] = 0.22,
                ["oak"] = 0.23,
                ["palm"] = 0.25,
            };

        /// <summary>
        /// Values that are attested for a material in SOME OTHER epoch and must
        /// therefore never appear in <see cref="MaterialCatalog"/>. Every entry here
        /// is a real, sourced number - that is exactly what makes it dangerous, since
        /// a reader who finds it will have a citation for it.
        ///
        /// Only values that DIFFER from the final era are listed. Bronze is 0.42 in
        /// both epochs, so bronze carries no foreign value and cannot be a signal.
        ///
        /// Sources, all COMMUNITY-MEASURED:
        ///  * calculator era - the WAEngenius "WEIGHT" column, the "Large Panel Kg"
        ///    column divided by 40 (the same numbers x40, not independent), the
        ///    "Metal Chart" WEIGHT column and Fureniku's "Unit Weight" column. Four
        ///    artefacts, one table.
        ///  * tungsten 0.74 - findings-material-mass.md section 2.5, back-solved from
        ///    the recovered cannon sheet. A correction to the CALCULATOR-era 0.80,
        ///    NOT to the final-era 0.70. This is the single easiest mistake to make.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, IReadOnlyList<double>> ForeignEpochMasses =
            new Dictionary<string, IReadOnlyList<double>>(StringComparer.OrdinalIgnoreCase)
            {
                ["aluminium"] = new[] { 0.26 },
                ["titanium"] = new[] { 0.30 },
                ["tin"] = new[] { 0.34 },
                ["iron"] = new[] { 0.38 },
                ["nickel"] = new[] { 0.46 },
                ["steel"] = new[] { 0.40 },
                ["copper"] = new[] { 0.50 },
                ["lead"] = new[] { 0.60 },
                ["silver"] = new[] { 0.55 },
                // 0.80 = the sheets' figure; 0.74 = the cannon back-solve that CORRECTS
                // it. Both are calculator era. The final era says 0.70.
                ["tungsten"] = new[] { 0.80, 0.74 },
                ["gold"] = new[] { 0.69 },

                ["cedar"] = new[] { 0.15 },
                ["hemlock"] = new[] { 0.17 },
                ["chestnut"] = new[] { 0.19 },
                ["elm"] = new[] { 0.22 },
                ["birch"] = new[] { 0.24 },
                ["ash"] = new[] { 0.26 },
                ["oak"] = new[] { 0.28 },
                ["palm"] = new[] { 0.31 },
            };

        /// <summary>
        /// Tolerance for comparing a shipped mass against a table value. The tables
        /// are published to two decimals, so anything within half a thousandth is the
        /// same number and anything outside it is a deliberate edit.
        /// </summary>
        public const double MassToleranceKg = 0.0005;

        /// <summary>
        /// Every way the shipped catalogue currently departs from a single, internally
        /// consistent epoch. EMPTY is the only healthy result.
        ///
        /// Two independent checks, because they fail differently:
        ///  * a retail material whose shipped mass is not the final-era value, and
        ///  * a retail material whose shipped mass IS a known value from another epoch,
        ///    which is reported separately and loudly because it means someone
        ///    imported a real, citable number out of the wrong table.
        ///
        /// This project's own additions (cobalt, aurium) are skipped: retail never had
        /// them, so no epoch has an opinion and their masses are honestly CHOSEN.
        /// </summary>
        public static IReadOnlyList<string> Violations()
        {
            List<string> problems = new List<string>();

            foreach (ShipMaterial material in MaterialCatalog.Materials.Where(m => m.IsRetail))
            {
                if (!FinalEraMassPerUnitKg.TryGetValue(material.Id, out double expected))
                {
                    problems.Add(material.Id + ": retail material missing from the "
                        + ShippedEpoch + " table - add it, or mark it non-retail");
                    continue;
                }

                double actual = material.MassPerUnitKg;

                if (ForeignEpochMasses.TryGetValue(material.Id, out IReadOnlyList<double>? foreign)
                    && foreign.Any(f => Math.Abs(actual - f) <= MassToleranceKg))
                {
                    problems.Add(material.Id + ": shipped mass " + actual.ToString("0.00")
                        + " kg/unit is a value from ANOTHER PATCH EPOCH. The "
                        + ShippedEpoch + " value is " + expected.ToString("0.00")
                        + ". Never mix rows across epochs - see MaterialMassEpoch remarks.");
                    continue;
                }

                if (Math.Abs(actual - expected) > MassToleranceKg)
                {
                    problems.Add(material.Id + ": shipped mass " + actual.ToString("0.000")
                        + " kg/unit does not match the " + ShippedEpoch + " value "
                        + expected.ToString("0.00") + ". A change here is a claim about "
                        + "retail and needs a source.");
                }
            }

            foreach (string id in FinalEraMassPerUnitKg.Keys)
            {
                if (MaterialCatalog.Find(id) == null)
                {
                    problems.Add(id + ": in the " + ShippedEpoch
                        + " table but absent from MaterialCatalog");
                }
            }

            return problems;
        }
    }
}

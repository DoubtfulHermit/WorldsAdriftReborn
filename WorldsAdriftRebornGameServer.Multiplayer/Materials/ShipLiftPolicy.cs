using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Materials
{
    /// <summary>
    /// THE LIFT SEED, and the invariant that couples it to the mass table.
    ///
    /// WHY THIS TYPE EXISTS, and it is a test-coverage reason rather than a design
    /// one. The value below used to be a bare <c>1000000f</c> literal inline in
    /// <c>ComponentsSerializer</c>'s <c>1258 ShipLiftState</c> branch. That assembly
    /// has NO TEST PROJECT, so the number was guarded by nothing at all: changing it
    /// to <c>1000f</c> - which grounds every ship in the live world, because the
    /// legacy 2-cell hull already masses 1071 kg - passed the entire suite, 5,422
    /// tests, in silence. That was found by deliberately breaking it.
    ///
    /// A decision that cannot be unit-tested is a decision that will be undone by
    /// accident. So the number lives here, in Multiplayer, where
    /// <c>ShipLiftPolicyTests</c> can assert the property that actually matters:
    /// **the seed must stay far above the heaviest hull anyone can build.**
    ///
    /// ==================================================================
    /// WHAT THE SEED IS, honestly labelled.
    ///
    /// WAREBORN TUNING - not recovered, and deliberately not realistic. The client
    /// computes <c>IsOverloaded = totalMass &gt; TotalLift * AtlasMultiplier</c>
    /// (<c>ShipLiftVisualizer</c>), and blocks vertical input with the OSD "Ship
    /// weighs more than its atlas sky core can lift."
    /// (<c>ShipControlsBehaviour.UpdateVertical</c>). Our Harmony prefix
    /// <c>EndOfTheWorld_Patch.cs</c> pins <c>AtlasMultiplier</c> to <c>1f</c>, so
    /// <c>TotalLift</c> is exactly this seed. Against per-material hull masses in the
    /// hundreds of kilograms that leaves <c>IsOverloaded</c> false by three orders of
    /// magnitude, which is the point: **lift is not currently a limiting factor, and
    /// this seed is what makes that true.**
    ///
    /// RETAIL'S REAL NUMBER IS ABOUT A THOUSAND TIMES SMALLER - a bare sky core lifts
    /// 1000 kg (RECOVERED; see <see cref="MaterialCatalog.BaseSkyCoreLiftKg"/> and
    /// <see cref="MaterialCatalog.SkyCoreLiftKg"/>). Swapping this seed for that
    /// recovered value is roadmap item F2 and is a REAL BALANCE DECISION ABOUT LIVE
    /// SHIPS, not a fidelity cleanup: the legacy hull would be overloaded the instant
    /// it changed. Core internals are not modelled either, so lift above the 1000 kg
    /// base cannot currently be earned. Do not make lift "correct" as a side effect
    /// of some other change.
    /// ==================================================================
    /// </summary>
    public static class ShipLiftPolicy
    {
        /// <summary>
        /// Kilograms of lift served on <c>1258 ShipLiftState.totalLift</c> for a built
        /// hull. WAREBORN TUNING. See the type remarks before changing it, and read
        /// roadmap F2 first.
        /// </summary>
        public const double SeededTotalLiftKg = 1_000_000.0;

        /// <summary>
        /// The margin the seed must keep over <see cref="PessimisticHullMassKg"/>, as a
        /// multiple.
        ///
        /// CHOSEN at 10x, and calibrated rather than picked: the pessimistic hull below
        /// is a deliberately absurd 32-cell solid-gold ship massing 58,400 kg, against
        /// which the shipped seed leaves **17.1x**. A REAL ship is three orders of
        /// magnitude safer - the 1071 kg legacy hull sits at 934x - and that is the
        /// number the second test asserts.
        ///
        /// 10x is the honest threshold for the absurd case: loose enough that it will
        /// not fire on a legitimate tuning change, tight enough to catch the failure
        /// this guard was written for. A dropped decimal point 1e6 -> 1e5 falls to
        /// 1.7x and fails; 1e6 -> 1e3, which grounds every ship in the world, falls to
        /// 0.017x and fails loudly.
        ///
        /// Do NOT raise this to make a lift change pass. If the seed genuinely needs to
        /// come down - roadmap F2 - the hull masses have to be re-checked against it
        /// first, because that is a balance decision about live ships.
        /// </summary>
        public const double RequiredLiftMarginOverHeaviestHull = 10.0;

        /// <summary>
        /// The heaviest hull to hold the seed against: solid gold, the densest
        /// material in the catalogue, at a cell and deck count far beyond anything the
        /// live world holds. CHOSEN as a deliberately pessimistic bound - if the seed
        /// clears this it clears every real ship.
        /// </summary>
        public const int PessimisticHullCells = 32;

        /// <summary>The deck count of that same pessimistic hull.</summary>
        public const int PessimisticHullDecks = 32;

        /// <summary>
        /// The mass of that pessimistic hull, in kilograms, from the shipped material
        /// table. Moves if and only if the mass table or the hull-cell scale moves,
        /// which is exactly the coupling this policy exists to watch.
        /// </summary>
        public static double PessimisticHullMassKg() =>
            HullMassCalculator.HullMassKg(
                new HullMaterials(woodId: null, woodQuality: 1, metalId: HeaviestMaterialId(), metalQuality: 1),
                PessimisticHullCells,
                PessimisticHullDecks);

        /// <summary>The densest material in the catalogue, whatever it currently is.</summary>
        public static string HeaviestMaterialId()
        {
            string heaviest = MaterialCatalog.LegacyMetalId;
            double best = -1.0;
            foreach (ShipMaterial material in MaterialCatalog.Materials)
            {
                if (material.MassPerUnitKg > best)
                {
                    best = material.MassPerUnitKg;
                    heaviest = material.Id;
                }
            }
            return heaviest;
        }

        /// <summary>
        /// How many times the seeded lift exceeds the pessimistic hull. Above
        /// <see cref="RequiredLiftMarginOverHeaviestHull"/> means no ship can be
        /// overloaded and vertical input can never be blocked.
        /// </summary>
        public static double LiftMargin()
        {
            double mass = PessimisticHullMassKg();
            return mass > 0.0 ? SeededTotalLiftKg / mass : double.PositiveInfinity;
        }

        /// <summary>
        /// Whether a hull of this mass would have its vertical input blocked by the
        /// client, given what we seed. Mirrors
        /// <c>ShipLiftVisualizer.IsOverloaded</c> with <c>AtlasMultiplier</c> pinned to
        /// 1 by <c>EndOfTheWorld_Patch</c>.
        /// </summary>
        public static bool WouldBeOverloaded(double hullMassKg) =>
            double.IsFinite(hullMassKg) && hullMassKg > SeededTotalLiftKg;
    }
}

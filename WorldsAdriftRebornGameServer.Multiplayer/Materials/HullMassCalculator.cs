using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;

namespace WorldsAdriftRebornGameServer.Multiplayer.Materials
{
    /// <summary>
    /// HOW HEAVY A SHIP IS, in retail's own kilograms, from what it is made of and
    /// how big it is.
    ///
    /// WHY KILOGRAMS AND NOT AN ABSTRACT NUMBER. Retail's ship model was a MASS
    /// BUDGET AGAINST A LIFT BUDGET, both in kg, and the shipped client still
    /// enforces it: <c>ShipLiftVisualizer.IsOverloaded</c> is
    /// <c>totalMass &gt; TotalLift</c>, and <c>ShipControlsBehaviour.UpdateVertical</c>
    /// DROPS the pilot's vertical input and prints "Ship weighs more than its atlas
    /// sky core can lift." when it trips (VERIFIED, acs/ShipControlsBehaviour.cs
    /// :283 - the literal passed to OSDMessage.SendMessage).
    /// A bare sky core lifts 1000 kg (RECOVERED, twice: this repo's own
    /// itemData.json description and the wiki). So a mass expressed in the same
    /// kilograms plugs straight into a rule the client already has, and none of that
    /// mechanic has to be invented.
    ///
    /// THE UNIT PROBLEM, stated honestly. Retail's published per-material masses are
    /// "specific weight per unit for PANELS" and the wiki says outright that other
    /// components had different per-unit weights that it did not publish. So the
    /// per-material RATIOS are recovered and trustworthy; the absolute scale for a
    /// HULL FRAME is not. <see cref="UnitsPerHullCell"/> is therefore the one
    /// CHOSEN number here, and it is chosen by calibration rather than taste - see
    /// its remarks.
    /// </summary>
    public static class HullMassCalculator
    {
        /// <summary>
        /// How many material "units" one hull cell of frame costs.
        ///
        /// CHOSEN, by calibration against two fixed points we did not choose:
        ///
        ///  * This server has always published a flat 800 kg for every ship
        ///    (ComponentsSerializer's ParentingMassAdderState, env
        ///    WAREBORN_SHIP_MASS). Landing a stock one-cell IRON hull near that keeps
        ///    every existing ship flying with the mass it already has, so this change
        ///    is not a silent rebalance of the live world.
        ///  * A bare sky core lifts 1000 kg (RECOVERED).
        ///
        /// At iron's recovered 0.39 kg/unit, 2000 units per cell gives 780 kg - just
        /// under the old flat 800 and comfortably under the 1000 kg budget. The same
        /// cell in birch (0.20) is 400 kg, in aluminium (0.33) 660 kg, in gold (0.73)
        /// 1460 kg - which is over budget, and correctly so: nobody should be able to
        /// fly a solid-gold hull on a stock core.
        ///
        /// This is the single number to turn if ships feel too heavy or too light. It
        /// scales everything uniformly and changes no material's RELATIVE standing.
        /// </summary>
        public const double UnitsPerHullCell = 2000.0;

        /// <summary>
        /// A deck's cost in the same units. CHOSEN at a quarter of a hull cell: a
        /// deck is a floor plate, not a structural frame, and retail gave it a base
        /// HP of 75 against a panel's 60 (RECOVERED from itemData.json metadata),
        /// i.e. the same order of substance, not the same order as a whole cell.
        /// </summary>
        public const double UnitsPerDeck = 500.0;

        /// <summary>
        /// The share of a hull frame that is METAL fittings rather than timber, when
        /// the ship has both. CHOSEN: retail's hull rows cost mostly frame material
        /// with metal for the joins and braces, and 20% keeps a wooden ship
        /// recognisably wooden in mass while still making the choice of metal matter.
        /// A hull with only one material pays 100% in that material, so this constant
        /// never silently invents substance that is not there.
        /// </summary>
        public const double MetalShareOfMixedHull = 0.20;

        /// <summary>
        /// Quality is FREE. RECOVERED, and load-bearing: the wiki states "Using
        /// higher quality materials will give a higher statistic boost to the part
        /// you are making, WITHOUT ANY ADDITIONAL COST OF WEIGHT." That single clause
        /// is why Q10 aluminium was the most sought-after material in the game, and
        /// getting it wrong would invert the whole economy. So quality appears
        /// nowhere in this file.
        /// </summary>
        public const bool QualityAffectsMass = false;

        /// <summary>
        /// The kilograms of a hull's own structure: its cells and decks in the
        /// materials it was built from. Mounted parts are NOT included - they are
        /// separate entities with their own 1121 masses, and the client sums them
        /// itself.
        ///
        /// Never negative, never NaN, and never zero for a real hull: a mass of zero
        /// would read to the client as a weightless ship.
        /// </summary>
        public static double HullMassKg(HullMaterials materials, int cellCount, int deckCount)
        {
            HullMaterials effective = (materials ?? HullMaterials.Legacy).OrLegacy();

            // A malformed plan must not produce a weightless or negative ship.
            int cells = cellCount < 1 ? 1 : cellCount;
            int decks = deckCount < 0 ? 0 : deckCount;

            double frameUnits = cells * UnitsPerHullCell;
            double deckUnits = decks * UnitsPerDeck;
            double totalUnits = frameUnits + deckUnits;

            ShipMaterial? wood = effective.Wood;
            ShipMaterial? metal = effective.Metal;

            double kgPerUnit;
            if (wood != null && metal != null)
            {
                kgPerUnit = wood.MassPerUnitKg * (1.0 - MetalShareOfMixedHull)
                    + metal.MassPerUnitKg * MetalShareOfMixedHull;
            }
            else if (wood != null)
            {
                kgPerUnit = wood.MassPerUnitKg;
            }
            else if (metal != null)
            {
                kgPerUnit = metal.MassPerUnitKg;
            }
            else
            {
                // OrLegacy guarantees this is unreachable; be explicit anyway.
                kgPerUnit = MaterialCatalog.Find(MaterialCatalog.LegacyWoodId)!.MassPerUnitKg;
            }

            double mass = totalUnits * kgPerUnit;
            return double.IsFinite(mass) && mass > 0.0 ? mass : UnitsPerHullCell * kgPerUnit;
        }

        /// <summary>
        /// The same, measured straight off a decoded hull plan. <see cref="ShipHullMetrics"/>
        /// already counts the cells and decks; this is the seam where geometry and
        /// material meet, so a longer ship in the same wood is genuinely heavier.
        /// </summary>
        public static double HullMassKg(HullMaterials materials, ShipHullMetrics metrics) =>
            HullMassKg(materials, metrics.CellCount, metrics.DeckCount);

        /// <summary>
        /// How a hull's mass changes what it can DO, as a multiplier on the flight
        /// tuning's acceleration, turn rate and climb rate.
        ///
        /// SHAPE, not magnitude, is the recovered part. The only published speed
        /// model is a community calculator's
        /// <c>speed = 50 * sqrt(2 * power / weight)</c> - WEAK evidence, one source,
        /// preserved in the community snapshot's WAEngenius formulas - but it agrees
        /// with the game's own repeated advice that "power to weight ratio is king"
        /// and that heavier ships are slower AND less manoeuvrable. An inverse
        /// SQUARE ROOT of mass is therefore used rather than an inverse linear law,
        /// which would make a heavy ship unplayable.
        ///
        /// The reference mass is the flat 800 kg this server published for every ship
        /// until now, so a ship of that mass gets a multiplier of exactly 1.0 and
        /// flies EXACTLY as it does today. That is what makes this safe to ship: the
        /// change is zero for the average hull and graded either side of it.
        ///
        /// Clamped to [0.5, 1.6] so neither a featherweight cedar skiff nor an
        /// absurd gold barge leaves the range the control-point stream can carry.
        /// </summary>
        public const double ReferenceHullMassKg = 800.0;

        /// <summary>Lower clamp on the agility multiplier.</summary>
        public const double MinAgility = 0.5;

        /// <summary>Upper clamp on the agility multiplier.</summary>
        public const double MaxAgility = 1.6;

        public static double AgilityScale(double hullMassKg)
        {
            if (!double.IsFinite(hullMassKg) || hullMassKg <= 0.0)
            {
                return 1.0;
            }
            double scale = Math.Sqrt(ReferenceHullMassKg / hullMassKg);
            return scale < MinAgility ? MinAgility : (scale > MaxAgility ? MaxAgility : scale);
        }
    }
}

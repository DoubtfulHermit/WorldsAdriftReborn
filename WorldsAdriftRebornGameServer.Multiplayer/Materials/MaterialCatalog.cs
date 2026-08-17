using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Materials
{
    /// <summary>
    /// THE material table: every metal and wood a ship can be made of, with the
    /// properties that decide how heavy and how tough the result is.
    ///
    /// WHY THIS EXISTS. The shipped client is deliberately stat-blind - it knows a
    /// material only as <c>RawMaterial { materialTypeId, quality, category, meta }</c>
    /// and resolves the id to a tint colour. <c>acs/MaterialDefinition.cs</c> is its
    /// ENTIRE notion of a material: name, id, wood/metal, three colours. No mass, no
    /// durability, no conductivity. Every number that made copper different from
    /// aluminium lived on the SpatialOS GSIM worker, which is dead. This type is the
    /// reconstruction, and it is the ONLY place a material number lives.
    ///
    /// ==================================================================
    /// EVIDENCE GRADING. Three tiers, and every row below says which it is.
    ///
    ///  1. RECOVERED - a number retail itself published.
    ///     * <see cref="ShipMaterial.MassPerUnitKg"/>: the Worlds Adrift wiki Metal
    ///       and Wood pages, Alpha 6 / 2019, via the Wayback Machine
    ///       (web.archive.org/web/2019id_/https://worldsadrift.gamepedia.com/Metal
    ///       and /Wood). The wiki's own caveat, verbatim: "All of the following data
    ///       is a specific weight per unit for panels. Each component is believed to
    ///       have a different weight per unit, but the order of weight remains the
    ///       same."
    ///     * <see cref="ShipMaterial.AtlasLiftPerQualityKg"/>: the wiki Atlas Core
    ///       page's lift table (credited to Demodraco#0118). Twelve metals. This is
    ///       the ONLY surviving numeric per-material stat table, and it doubles as
    ///       retail's own conductivity ranking.
    ///     * The 1000 kg base sky-core lift is corroborated twice over: the wiki and
    ///       this repo's own copy of retail's item catalogue ("Can lift 1000kg of
    ///       weight").
    ///
    ///  2. MEASURED - a number the community measured in the live game. Strong, but
    ///     it is player testing rather than Bossa data, and the decompile outranks it
    ///     wherever both speak. Snapshot, with checksums and a manifest, at
    ///     docs/research/world-data/external/wa-community-2026-08-16/.
    ///     * <see cref="ShipMaterial.Durability"/>: wing-science, "Casing / Health"
    ///       (Closed Beta 0.1.3.3, by Gouki). Twelve metals, normalised so the best
    ///       performer is 100%.
    ///     * <see cref="ShipMaterial.HeatResistance"/>: engine-science, MECHANICAL
    ///       INTERNALS "Overheat Limit" material effectiveness. Twelve metals.
    ///       NOTE, and this cost some thought: the COMBUSTION-internals overheat
    ///       column is NOT heat resistance - it ranks gold, tin and lead top, which
    ///       contradicts every item description. That column measures heat
    ///       DISSIPATION (the conductors carry heat away; the wiki calls tin the
    ///       standard engine-casing cooler). The MECHANICAL column ranks tungsten
    ///       top and tin bottom, which is exactly what the descriptions say, so that
    ///       is the one used here.
    ///
    ///  3. CHOSEN - our number, no source. Hardness and StressResistance for every
    ///     material, and every axis for the materials no table covers. Authored to be
    ///     faithful to the recovered descriptions and nothing more is claimed.
    ///
    /// PATCH ERAS, do not mix them. At least three mass tables exist: this one
    /// (Alpha 6, the final balance and the only one covering orthite/epilar/eternium),
    /// an earlier one in the Steam Comprehensive Guide, and a third in the WAEngenius
    /// workbook. Their VALUES differ; their ORDERING agrees, and the woods order
    /// identically in all three (cedar &lt; hemlock &lt; chestnut &lt; elm &lt; birch &lt; ash &lt;
    /// oak &lt; palm). Alpha 6 is used because this repo's item descriptions are the
    /// Alpha 6 descriptions.
    ///
    /// The wiki's mass numbers are NOT real-world densities and must not be
    /// "corrected" towards physics: retail put steel above iron and silver above
    /// lead, which real densities do not.
    /// ==================================================================
    ///
    /// CASE. Lookups are case-insensitive on purpose: itemData.json spells ids
    /// lowercase ("iron") but the island catalogue spells the same metal Title-case
    /// ("Iron"), and both feed this table.
    /// </summary>
    public static class MaterialCatalog
    {
        /// <summary>
        /// Gold's table-topping 8.5 kg of sky-core lift per quality point - the
        /// normaliser that turns the RECOVERED lift table into a 0..1 conductivity.
        /// </summary>
        public const double BestAtlasLiftPerQualityKg = 8.5;

        /// <summary>
        /// A bare atlas sky core lifts this many kilograms. RECOVERED twice: the
        /// item description in this repo's itemData.json ("Can lift 1000kg of
        /// weight") and the wiki's Atlas Core page.
        /// </summary>
        public const double BaseSkyCoreLiftKg = 1000.0;

        // ------------------------------------------------------------------
        // Columns: mass = RECOVERED wiki Alpha 6; lift = RECOVERED wiki Atlas Core;
        // dur = MEASURED wing-science casing health; heat = MEASURED engine-science
        // mechanical-internals overheat; hard/stress = CHOSEN.
        // ------------------------------------------------------------------
        private static readonly ShipMaterial[] All =
        {
            // --- Metals, rarity 0 -------------------------------------------
            Metal("iron", "Iron", 0, mass: 0.39, lift: 3.0, hard: 0.45, dur: 0.750, stress: 0.45, heat: 0.667,
                "Reasonably tough and heat resistant given its modest weight."),
            Metal("lead", "Lead", 0, mass: 0.56, lift: 2.0, hard: 0.20, dur: 0.972, stress: 0.70, heat: 0.333,
                "Durable and strong but too heavy for many uses."),
            Metal("bronze", "Bronze", 0, mass: 0.42, lift: 2.5, hard: 0.45, dur: 0.694, stress: 0.80, heat: 0.500,
                "Capable of withstanding a lot of stress for its medium weight."),

            // --- Metals, rarity 1 -------------------------------------------
            Metal("tin", "Tin", 1, mass: 0.38, lift: 4.5, hard: 0.10, dur: 0.722, stress: 0.20, heat: 0.250,
                "Lightweight but weak, soft and susceptible to heat."),
            // orthite: retail's own invention. Mass IS recovered (the wiki lists it);
            // no lift/durability/overheat table covers it, so those are CHOSEN from
            // "moderately conductive, stress resistant, but weak to heat".
            Metal("orthite", "Orthite", 1, mass: 0.43, lift: 5.0, hard: 0.40, dur: 0.50, stress: 0.75, heat: 0.15,
                "Moderately conductive, stress resistant, but weak to heat.", liftChosen: true, statsChosen: true),
            Metal("steel", "Steel", 1, mass: 0.50, lift: 1.5, hard: 0.90, dur: 0.778, stress: 0.70, heat: 0.750,
                "Very hard and high performance metal for its weight."),
            Metal("copper", "Copper", 1, mass: 0.55, lift: 7.5, hard: 0.30, dur: 0.667, stress: 0.45, heat: 0.583,
                "A great conductor but otherwise unexceptional."),

            // --- Metals, rarity 2 -------------------------------------------
            Metal("titanium", "Titanium", 2, mass: 0.35, lift: 1.0, hard: 0.85, dur: 0.722, stress: 0.75, heat: 0.750,
                "Very light for such a hard metal but a bad conductor."),
            Metal("nickel", "Nickel", 2, mass: 0.43, lift: 4.0, hard: 0.55, dur: 0.889, stress: 0.60, heat: 0.833,
                "Versatile thanks to its medium weight and a few weaknesses."),
            // epilar: retail's own invention; mass recovered, rest CHOSEN.
            Metal("epilar", "Epilar", 2, mass: 0.46, lift: 3.0, hard: 0.25, dur: 0.85, stress: 0.90, heat: 0.50,
                "Durable and resistant to stress but not really hard.", liftChosen: true, statsChosen: true),
            Metal("silver", "Silver", 2, mass: 0.66, lift: 8.0, hard: 0.50, dur: 0.778, stress: 0.50, heat: 0.500,
                "A jack of all trades but master of only conductivity."),

            // --- Metals, rarity 3 -------------------------------------------
            Metal("aluminium", "Aluminium", 3, mass: 0.33, lift: 6.0, hard: 0.40, dur: 0.694, stress: 0.55, heat: 0.333,
                "Extremely light without compromising too much on strength"),
            Metal("gold", "Gold", 3, mass: 0.73, lift: 8.5, hard: 0.15, dur: 0.833, stress: 0.30, heat: 0.583,
                "Dense yet malleable with extremely high conductivity."),
            // eternium: retail's own invention; mass recovered, rest CHOSEN.
            Metal("eternium", "Eternium", 3, mass: 0.50, lift: 2.0, hard: 0.95, dur: 0.90, stress: 0.20, heat: 0.95,
                "Hard, durable, heat resisantant but poor under stress.", liftChosen: true, statsChosen: true),
            Metal("tungsten", "Tungsten", 3, mass: 0.70, lift: 3.5, hard: 0.95, dur: 1.000, stress: 0.85, heat: 1.000,
                "Unparalleled resistance to the elements but very heavy."),

            // ------------------------------------------------------------------
            // NOT RETAIL. cobalt and aurium are this project's own additions (branch
            // data/recipe-catalogue), so every placed ore node yields something
            // craftable. NO source covers them: every number is CHOSEN. cobalt is
            // authored as a tough, heat-resistant mid-weight; aurium as a conductive
            // heavy. IsRetail is false so a test - and a reader - can tell.
            // ------------------------------------------------------------------
            Metal("cobalt", "Cobalt", 2, mass: 0.45, lift: 2.5, hard: 0.70, dur: 0.70, stress: 0.60, heat: 0.75,
                description: "", retail: false, massChosen: true, liftChosen: true, statsChosen: true),
            Metal("aurium", "Aurium", 3, mass: 0.60, lift: 6.5, hard: 0.60, dur: 0.60, stress: 0.60, heat: 0.60,
                description: "", retail: false, massChosen: true, liftChosen: true, statsChosen: true),

            // ------------------------------------------------------------------
            // The 8 retail woods. Retail gave woods NO rarity (null in itemData.json)
            // and no wood appears in the Atlas Core lift table - a wood is never a
            // core internal - so lift, and therefore conductivity, is 0. Timber is an
            // insulator, and that is the physically right answer as well as the
            // recovered one. Masses are RECOVERED (wiki Alpha 6); the four 0..1 axes
            // are CHOSEN from the descriptions, since no community table measured
            // woods.
            // ------------------------------------------------------------------
            Wood("cedar", "Cedar Wood", mass: 0.13, hard: 0.05, dur: 0.10, stress: 0.15, heat: 0.10,
                "Extremely light and soft, cannot withstand much damage."),
            Wood("hemlock", "Hemlock Wood", mass: 0.15, hard: 0.20, dur: 0.40, stress: 0.30, heat: 0.15,
                "Very light but durable enough to be used in certain scenarios."),
            Wood("chestnut", "Chestnut Wood", mass: 0.17, hard: 0.30, dur: 0.45, stress: 0.45, heat: 0.25,
                "Lightweight but nevertheless versatile timber."),
            Wood("elm", "Elm Wood", mass: 0.18, hard: 0.40, dur: 0.45, stress: 0.50, heat: 0.35,
                "Medium weight with no outstanding strengths or vulnerabilities."),
            Wood("birch", "Birch Wood", mass: 0.20, hard: 0.25, dur: 0.60, stress: 0.60, heat: 0.40,
                "Soft but otherwise high performance for it's weight."),
            Wood("ash", "Ash Wood", mass: 0.22, hard: 0.50, dur: 0.60, stress: 0.90, heat: 0.40,
                "Extremely flexible all-rounder but heavy for timber."),
            Wood("oak", "Oak Wood", mass: 0.23, hard: 0.60, dur: 0.80, stress: 0.65, heat: 0.50,
                "Durable and versatile but heavy for wood."),
            Wood("palm", "Palm Wood", mass: 0.25, hard: 0.65, dur: 0.85, stress: 0.75, heat: 0.60,
                "The heaviest but also most useful wood."),
        };

        private static ShipMaterial Metal(string id, string name, int rarity, double mass, double lift,
            double hard, double dur, double stress, double heat, string description,
            bool massChosen = false, bool liftChosen = false, bool statsChosen = false, bool retail = true) =>
            new ShipMaterial(id, name, MaterialCategory.Metal, rarity, mass, lift,
                hard, dur, stress, heat, massChosen, liftChosen, retail, description);

        private static ShipMaterial Wood(string id, string name, double mass,
            double hard, double dur, double stress, double heat, string description) =>
            new ShipMaterial(id, name, MaterialCategory.Wood, null, mass, atlasLiftPerQualityKg: 0.0,
                hardness: hard, durability: dur, stressResistance: stress, heatResistance: heat,
                massIsChosen: false, liftIsChosen: false, isRetail: true, description: description);

        private static readonly Dictionary<string, ShipMaterial> ById =
            All.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);

        /// <summary>Every known material.</summary>
        public static IReadOnlyList<ShipMaterial> Materials => All;

        /// <summary>The metals only.</summary>
        public static IEnumerable<ShipMaterial> Metals => All.Where(m => m.IsMetal);

        /// <summary>The woods only.</summary>
        public static IEnumerable<ShipMaterial> Woods => All.Where(m => m.IsWood);

        /// <summary>
        /// The material with this id, or null. Case-insensitive, so both the
        /// lowercase itemData id ("iron") and the Title-case island-catalogue name
        /// ("Iron") resolve. Null/whitespace returns null rather than throwing - a
        /// malformed save must never take the server down.
        /// </summary>
        public static ShipMaterial? Find(string? itemTypeId)
        {
            if (string.IsNullOrWhiteSpace(itemTypeId))
            {
                return null;
            }
            return ById.TryGetValue(itemTypeId.Trim(), out ShipMaterial? found) ? found : null;
        }

        /// <summary>Whether this item id names a craftable ship material at all.</summary>
        public static bool IsMaterial(string? itemTypeId) => Find(itemTypeId) != null;

        /// <summary>
        /// The "Metal"/"Wood" family for an item id, or null when the id is not a
        /// ship material. This is the <c>RawMaterial.category</c> the client needs.
        /// </summary>
        public static string? CategoryOf(string? itemTypeId) => Find(itemTypeId)?.Category;

        /// <summary>
        /// Sky-core lift, in kilograms, for a core whose conductive internals are
        /// made of this metal at this quality.
        ///
        /// RECOVERED FORMULA. The wiki's Atlas Core table gives, per metal, a
        /// kg-per-quality-level rate plus the Q1 and Q10 endpoints. Solving those
        /// endpoints gives <c>lift = 1000 + rate * (10 + quality)</c>, and that one
        /// expression reproduces EVERY published row exactly: gold (rate 8.5) is
        /// 1093.5 at Q1 and 1170 at Q10; titanium (rate 1) is 1011 and 1020; copper
        /// (rate 7.5) is 1082.5 and 1150. A formula that hits twelve materials at
        /// both endpoints is recovered, not fitted.
        ///
        /// A non-metal, or an unknown id, gets the bare core: no internals, no bonus.
        /// </summary>
        public static double SkyCoreLiftKg(string? metalId, int quality)
        {
            ShipMaterial? metal = Find(metalId);
            if (metal == null || !metal.IsMetal)
            {
                return BaseSkyCoreLiftKg;
            }
            int q = quality < 1 ? 1 : (quality > 10 ? 10 : quality);
            return BaseSkyCoreLiftKg + metal.AtlasLiftPerQualityKg * (10 + q);
        }

        /// <summary>
        /// Whether an item SATISFIES a requirement written as either a family
        /// ("Metal", "Wood", "Wood/Metal") or one concrete id ("iron").
        ///
        /// This is the rule that turns "a recipe that wants metal" into "iron OR
        /// copper OR aluminium OR ...". It mirrors the client's own slot rule
        /// (<c>InventoryItemManager.IsSameMaterialType</c>, VERIFIED): category
        /// equality, or exact id equality, or either family satisfying the
        /// "Wood/Metal" pseudo-category. All comparisons are case-insensitive.
        ///
        /// A requirement naming something that is not a material at all (fuel,
        /// atlasShard) still works: it falls through to exact-id equality, so a
        /// concrete non-material requirement stays exactly as strict as it was.
        /// </summary>
        public static bool Satisfies(string? requirement, string? itemTypeId)
        {
            if (string.IsNullOrWhiteSpace(requirement) || string.IsNullOrWhiteSpace(itemTypeId))
            {
                return false;
            }

            string req = requirement.Trim();
            string item = itemTypeId.Trim();

            if (string.Equals(req, item, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string? category = CategoryOf(item);
            if (category == null)
            {
                return false;
            }

            if (string.Equals(req, category, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(req, MaterialCategory.WoodOrMetal, StringComparison.OrdinalIgnoreCase)
                && (category == MaterialCategory.Metal || category == MaterialCategory.Wood);
        }

        /// <summary>
        /// Whether a requirement string names a FAMILY rather than one material -
        /// i.e. whether the player gets a choice.
        /// </summary>
        public static bool IsFamily(string? requirement)
        {
            if (string.IsNullOrWhiteSpace(requirement))
            {
                return false;
            }
            string req = requirement.Trim();
            return string.Equals(req, MaterialCategory.Metal, StringComparison.OrdinalIgnoreCase)
                || string.Equals(req, MaterialCategory.Wood, StringComparison.OrdinalIgnoreCase)
                || string.Equals(req, MaterialCategory.WoodOrMetal, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        // The legacy defaults. EVERY existing ship, deck and part in the live
        // world was built before materials were recorded, and the server hardcoded
        // exactly these two ids (Deck.MaterialTypeId = Trees.WoodType = "birch";
        // ShipPartSalvagePolicy.ConcreteMaterial maps "Metal" -> "iron"). So a
        // record with no material is not "unknown", it is KNOWN to be one of these
        // - which is what makes the migration lossless rather than a guess.
        // ------------------------------------------------------------------

        /// <summary>What an unmarked legacy WOODEN part is made of: birch.</summary>
        public const string LegacyWoodId = "birch";

        /// <summary>What an unmarked legacy METAL part is made of: iron.</summary>
        public const string LegacyMetalId = "iron";

        /// <summary>
        /// The material an unmarked legacy record should be treated as, given the
        /// family its recipe asked for. Never null: an unrecognised family falls
        /// back to wood, because the starter ship is wooden.
        /// </summary>
        public static ShipMaterial LegacyDefaultFor(string? category)
        {
            bool metal = string.Equals(category, MaterialCategory.Metal, StringComparison.OrdinalIgnoreCase);
            return Find(metal ? LegacyMetalId : LegacyWoodId)!;
        }
    }
}

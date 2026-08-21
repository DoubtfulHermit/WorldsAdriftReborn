namespace WorldsAdriftRebornGameServer.Multiplayer.Materials
{
    /// <summary>
    /// The two material families the CLIENT branches on. These exact strings go on
    /// the wire as <c>RawMaterial.category</c> and the client switches on them:
    /// <c>PartGraphicsVariationByMaterial.GetPrefabFromMaterial</c> throws on
    /// anything that is not "Wood" or "Metal", and <c>ShipPanel</c> concatenates
    /// them into a PhysX material name ("Wood (Panel)" / "Metal (Panel)").
    /// VERIFIED in the decompile; do not invent a third value for a ship part.
    /// </summary>
    public static class MaterialCategory
    {
        public const string Metal = "Metal";
        public const string Wood = "Wood";
        public const string Fuel = "Fuel";

        /// <summary>
        /// The client's own pseudo-category for a slot that takes EITHER family
        /// (<c>InventoryItemManager.IsSameMaterialType</c>, VERIFIED). Retail
        /// recipes use it for slots like "Nails".
        /// </summary>
        public const string WoodOrMetal = "Wood/Metal";
    }

    /// <summary>
    /// One craftable material and everything the server needs to know about it.
    ///
    /// PROVENANCE. Read this before changing a number; each field says where it
    /// came from and the table in <see cref="MaterialCatalog"/> repeats the grading
    /// per row.
    ///
    /// * <see cref="Id"/>, <see cref="DisplayName"/>, <see cref="Category"/>,
    ///   <see cref="Rarity"/>, <see cref="Description"/> - RECOVERED from retail's
    ///   own item catalogue, which ships in this repo at
    ///   <c>WorldsAdriftRebornGameServer/Game/Items/Config/itemData.json</c>.
    ///
    /// * <see cref="MassPerUnitKg"/> - RECOVERED. This is retail's published
    ///   per-unit mass table, in retail's own kilograms. Source: the Worlds Adrift
    ///   wiki Metal and Wood pages as archived in 2019 (see the class remarks on
    ///   <see cref="MaterialCatalog"/> for the exact URLs). It is NOT real-world
    ///   density - retail's balance pass put steel above iron and silver above
    ///   lead, which real densities do not - so do not "correct" it towards
    ///   physics. The wiki's own caveat is quoted in the catalogue.
    ///
    /// * <see cref="AtlasLiftPerQualityKg"/> - RECOVERED for the twelve metals the
    ///   wiki's Atlas Core table covers; CHOSEN for retail's three invented metals
    ///   and this project's two additions. This is the single surviving NUMERIC
    ///   per-material stat table, and it is the game's own ranking of conductivity.
    ///
    /// * <see cref="Conductivity"/> - DERIVED from
    ///   <see cref="AtlasLiftPerQualityKg"/>, so for those twelve metals it is
    ///   recovered rather than invented. Retail spent conductivity on THERMAL and
    ///   LIFT behaviour ("Cooling Factor", atlas lift), not electricity - see the
    ///   findings document on the lightning question before wiring it to anything.
    ///
    /// * <see cref="Hardness"/> and <see cref="StressResistance"/> - CHOSEN. The
    ///   AXES are recovered (every one of the 24 retail descriptions is a sentence
    ///   about this exact set, and the dev blog names "strength (resilience),
    ///   colour, conductivity, heat-dissipation"), but no source measures these two
    ///   per material. They are authored to be faithful to the recovered
    ///   descriptions, and nothing more is claimed for them.
    ///
    /// * <see cref="Durability"/> and <see cref="HeatResistance"/> -
    ///   COMMUNITY-MEASURED for the twelve metals, CHOSEN elsewhere.
    ///   CORRECTED 2026-08-20: these two were documented here as CHOSEN and they are
    ///   not. Every one of the twelve metal values is an EXACT match, 12 of 12, to a
    ///   measured column - Durability to wing-science "Casing / Health" (Closed Beta
    ///   0.1.3.3, by Gouki) and HeatResistance to engine-science MECHANICAL
    ///   INTERNALS "Overheat Limit". <see cref="MaterialCatalog"/> has always graded
    ///   them correctly, so the two files contradicted each other and this one was
    ///   the wrong one. Labelling measured data as invented is the same class of
    ///   provenance error as the reverse: it invites a future reader to overwrite
    ///   real numbers with taste. The woods, retail's three invented metals and this
    ///   project's two additions genuinely are CHOSEN on both axes - no table covers
    ///   them - and the per-row grading in <see cref="MaterialCatalog"/> says which
    ///   is which.
    /// </summary>
    public sealed class ShipMaterial
    {
        public ShipMaterial(
            string id,
            string displayName,
            string category,
            int? rarity,
            double massPerUnitKg,
            double atlasLiftPerQualityKg,
            double hardness,
            double durability,
            double stressResistance,
            double heatResistance,
            bool massIsChosen,
            bool liftIsChosen,
            bool isRetail,
            string description)
        {
            Id = id;
            DisplayName = displayName;
            Category = category;
            Rarity = rarity;
            MassPerUnitKg = massPerUnitKg;
            AtlasLiftPerQualityKg = atlasLiftPerQualityKg;
            Hardness = hardness;
            Durability = durability;
            StressResistance = stressResistance;
            HeatResistance = heatResistance;
            MassIsChosen = massIsChosen;
            LiftIsChosen = liftIsChosen;
            IsRetail = isRetail;
            Description = description;
        }

        /// <summary>
        /// The lowercase itemData.json <c>itemTypeID</c> - "iron", "birch". This is
        /// what goes on the wire as <c>RawMaterial.materialTypeId</c>; the client
        /// resolves it by name through <c>MaterialManager</c>. RECOVERED.
        /// </summary>
        public string Id { get; }

        /// <summary>Title-case display name, matching itemData.json <c>name</c>. RECOVERED.</summary>
        public string DisplayName { get; }

        /// <summary>"Metal" or "Wood". RECOVERED.</summary>
        public string Category { get; }

        /// <summary>0..3 for metals; null for woods (retail did not tier woods). RECOVERED.</summary>
        public int? Rarity { get; }

        /// <summary>
        /// Retail's own per-unit mass in kilograms, as published for PANELS. The
        /// wiki's caveat, verbatim: "All of the following data is a specific weight
        /// per unit for panels. Each component is believed to have a different
        /// weight per unit, but the order of weight remains the same." So the
        /// RATIOS are the trustworthy part and this server uses it as a per-unit
        /// mass coefficient. RECOVERED (Alpha 6).
        /// </summary>
        public double MassPerUnitKg { get; }

        /// <summary>
        /// Kilograms of sky-core lift added per point of material quality when this
        /// metal is used in the core's conductive internals. RECOVERED for twelve
        /// metals; zero for woods (a wood is never a core internal).
        /// </summary>
        public double AtlasLiftPerQualityKg { get; }

        /// <summary>
        /// 0..1 conductivity, DERIVED from <see cref="AtlasLiftPerQualityKg"/>
        /// against gold's table-topping 8.5 kg/level. This makes the ordering
        /// retail's own rather than ours: gold 1.00, silver 0.94, copper 0.88,
        /// aluminium 0.71, ... titanium 0.12 - which is exactly what the item
        /// descriptions say ("extremely high conductivity", "master of only
        /// conductivity", "a great conductor", "a bad conductor").
        /// </summary>
        public double Conductivity => AtlasLiftPerQualityKg / MaterialCatalog.BestAtlasLiftPerQualityKg;

        /// <summary>0..1. CHOSEN, faithful to the recovered description.</summary>
        public double Hardness { get; }

        /// <summary>
        /// 0..1. COMMUNITY-MEASURED for the twelve metals wing-science covers - its
        /// "Casing / Health" column, normalised so the best performer is 1.0, and
        /// reproduced here 12 of 12 exactly. CHOSEN for the woods, for orthite,
        /// epilar and eternium, and for cobalt and aurium.
        /// </summary>
        public double Durability { get; }

        /// <summary>0..1. CHOSEN, faithful to the recovered description.</summary>
        public double StressResistance { get; }

        /// <summary>
        /// 0..1. COMMUNITY-MEASURED for the twelve metals engine-science covers - its
        /// MECHANICAL INTERNALS "Overheat Limit" material effectiveness, 12 of 12
        /// exactly. Deliberately NOT the combustion-internals overheat column, which
        /// measures heat DISSIPATION and ranks gold, tin and lead top; see the
        /// <see cref="MaterialCatalog"/> remarks. CHOSEN for the woods, for orthite,
        /// epilar and eternium, and for cobalt and aurium.
        /// </summary>
        public double HeatResistance { get; }

        /// <summary>True when the mass had to be invented (retail never published one).</summary>
        public bool MassIsChosen { get; }

        /// <summary>True when the atlas-lift rate had to be invented.</summary>
        public bool LiftIsChosen { get; }

        /// <summary>False for materials this project added that retail never had (cobalt, aurium).</summary>
        public bool IsRetail { get; }

        /// <summary>Retail's own flavour text, verbatim. RECOVERED. Empty for our additions.</summary>
        public string Description { get; }

        public bool IsMetal => Category == MaterialCategory.Metal;

        public bool IsWood => Category == MaterialCategory.Wood;

        public override string ToString() =>
            Id + " (" + Category + ", " + MassPerUnitKg.ToString("0.00") + " kg/unit)";
    }
}

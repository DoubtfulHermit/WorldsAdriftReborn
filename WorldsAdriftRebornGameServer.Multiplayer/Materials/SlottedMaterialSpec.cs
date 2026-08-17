using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Materials
{
    /// <summary>
    /// One entry of the 1099 <c>SalvageAndRepairState.originalMaterials</c> list, in
    /// ENGINE-FREE primitives. The game handler maps it straight into a gencode
    /// <c>Bossa.Travellers.Craftingstation.SlottedMaterial</c>.
    ///
    /// The wire shape, VERIFIED against gencode:
    /// <code>
    /// SlottedMaterial { 1: int index; 2: RawMaterial rawMaterial;
    ///                   3: int amount;  4: RawMaterial customizationMaterial (optional) }
    /// RawMaterial     { 1: string materialTypeId; 2: int quality;
    ///                   3: string category;       4: map&lt;string,string&gt; meta }
    /// </code>
    /// This type carries the three fields the client actually reads; the optional
    /// customization material (a paint pigment) is always absent for a hull.
    /// </summary>
    public readonly struct SlottedMaterialSpec
    {
        public SlottedMaterialSpec(int index, string materialTypeId, int quality, string category, int amount)
        {
            Index = index;
            MaterialTypeId = materialTypeId;
            Quality = quality;
            Category = category;
            Amount = amount;
        }

        /// <summary>The positional slot index. Retail's parts are read positionally.</summary>
        public int Index { get; }

        /// <summary>A REAL itemData id ("birch"). Resolved by name through MaterialManager.</summary>
        public string MaterialTypeId { get; }

        /// <summary>1..10.</summary>
        public int Quality { get; }

        /// <summary>"Wood" or "Metal". The client throws on any other value for a ship part.</summary>
        public string Category { get; }

        /// <summary>How much went in. MUST be &gt; 0: the salvage helper sums amounts.</summary>
        public int Amount { get; }
    }

    /// <summary>
    /// WHAT A SHIP PUBLISHES ABOUT ITS OWN SUBSTANCE - the 1099 material list for a
    /// built hull, and for a deck.
    ///
    /// WHY THIS EXISTS. The live client logs, repeatedly:
    ///
    ///   [ERROR] [ComponentMaterialColors] No wooden or metal materials found for
    ///           ShipFrame 283/Generated
    ///     at ComponentMaterialColors.SetMaterials()
    ///     at MeshGenerator+&lt;GenerateShipMesh&gt;c__AnonStorey0
    ///
    /// The chain is fully VERIFIED in the decompile:
    /// <c>CustomShipFrameVisualizer.cs:52</c> passes
    /// <c>_salvageAndRepairState.OriginalMaterials</c> into
    /// <c>MeshGenerator.GenerateShipMesh(plan, 2f, beamMaterials)</c>, which at
    /// <c>MeshGenerator.cs:166-169</c> hands it to
    /// <c>ComponentMaterialColors.SetMaterialColors</c>. That method buckets the
    /// list into woods and metals and, at <c>ComponentMaterialColors.cs:173-177</c>,
    /// logs exactly that error and returns when BOTH buckets are empty. The server
    /// has always sent the hull an EMPTY list, so the ship's beams are never tinted.
    ///
    /// THE OLD REASON FOR THE EMPTY LIST IS DISPROVEN. The serializer's comment says
    /// an invented id "WOULD NRE ComponentMaterialColors". It would not:
    /// <c>MaterialManager.MaterialDefinitionFromName</c>
    /// (acs/MaterialManager.cs:109-118, VERIFIED) logs ErrorOnce and returns
    /// <c>fallbackDefinition</c>, which is non-null, has a populated
    /// <c>paintColorSets[0]</c> and a materialType of metal - so an unknown name is
    /// a magenta tint, not a crash. We send real ids regardless.
    /// </summary>
    public static class HullMaterialPublication
    {
        /// <summary>
        /// The 1099 list for a built hull. Wood first, then metal, because
        /// <c>SetMaterials</c> takes the FIRST entry's family as the component's
        /// DOMINANT material and a ship frame reads as wooden unless it is all
        /// metal - a wooden hull with iron fittings should tint as wood with a metal
        /// accent, which is precisely the branch at
        /// <c>ComponentMaterialColors.cs:185-196</c>.
        ///
        /// Never returns an empty list for a hull that has any material at all, and
        /// callers should pass <c>materials.OrLegacy()</c> so an old hull publishes
        /// the birch/iron it has always implicitly been.
        /// </summary>
        public static IReadOnlyList<SlottedMaterialSpec> ForHull(HullMaterials materials)
        {
            var list = new List<SlottedMaterialSpec>(2);
            if (materials == null)
            {
                return list;
            }

            int index = 0;
            ShipMaterial? wood = materials.Wood;
            if (wood != null)
            {
                list.Add(new SlottedMaterialSpec(
                    index++, wood.Id, materials.WoodQuality, MaterialCategory.Wood, amount: 1));
            }

            ShipMaterial? metal = materials.Metal;
            if (metal != null)
            {
                list.Add(new SlottedMaterialSpec(
                    index, metal.Id, materials.MetalQuality, MaterialCategory.Metal, amount: 1));
            }

            return list;
        }

        /// <summary>
        /// The 1099 list for a DECK. A deck must carry EXACTLY ONE entry and it must
        /// be first: <c>ShipDeckVisualizer.OnEnable</c> reads
        /// <c>OriginalMaterials[0].rawMaterial.category</c> (VERIFIED,
        /// ShipDeckVisualizer.cs:60) to pick the wooden vs metal deck prototype, and
        /// an empty list is an IndexOutOfRangeException in OnEnable - the deck never
        /// builds and the player falls through the floor.
        ///
        /// A deck follows its hull: a metal-framed ship gets metal decking. When the
        /// hull is wooden (the usual case) the deck is that wood, which preserves
        /// today's behaviour exactly for every existing ship.
        /// </summary>
        public static SlottedMaterialSpec ForDeck(HullMaterials materials)
        {
            HullMaterials effective = (materials ?? HullMaterials.Legacy).OrLegacy();

            ShipMaterial? wood = effective.Wood;
            if (wood != null)
            {
                return new SlottedMaterialSpec(0, wood.Id, effective.WoodQuality, MaterialCategory.Wood, 1);
            }

            ShipMaterial? metal = effective.Metal;
            if (metal != null)
            {
                return new SlottedMaterialSpec(0, metal.Id, effective.MetalQuality, MaterialCategory.Metal, 1);
            }

            // Unreachable after OrLegacy, but a deck with no material would drop a
            // player through the world, so the fallback is explicit rather than a
            // null-reference away.
            return new SlottedMaterialSpec(
                0, MaterialCatalog.LegacyWoodId, 1, MaterialCategory.Wood, 1);
        }
    }
}

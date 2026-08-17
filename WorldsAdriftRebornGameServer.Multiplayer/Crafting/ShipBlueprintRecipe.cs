using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Crafting
{
    /// <summary>
    /// One required material on a blueprint schematic row, in ENGINE-FREE primitives.
    /// The game handler maps this into a gencode
    /// <c>Bossa.Travellers.Craftingstation.ShipBlueprintMaterial</c> whose
    /// <c>requiredMaterial.rawMaterial</c> the unmodified client renders: it looks the
    /// icon/name up by <see cref="MaterialTypeId"/> via
    /// <c>InventoryItemManager.LookupItem</c> and, for a "Wood"/"Metal"
    /// <see cref="Category"/>, prints "Q{quality}+ {name}"
    /// (acs/ShipBlueprintMaterialUI.cs:47,80-86). So <see cref="MaterialTypeId"/> MUST
    /// be a real itemData.json itemTypeID or the row shows the raw id and a blank icon.
    /// </summary>
    public sealed class MaterialRequirement
    {
        public MaterialRequirement(string materialTypeId, string category, int quality, int amount,
            bool acceptsAnyInCategory = true)
        {
            MaterialTypeId = materialTypeId;
            Category = category;
            Quality = quality;
            Amount = amount;
            AcceptsAnyInCategory = acceptsAnyInCategory;
        }

        /// <summary>
        /// itemData.json itemTypeID, e.g. "birch"/"iron". Drives icon + name.
        ///
        /// When <see cref="AcceptsAnyInCategory"/> is set this is the DISPLAY
        /// EXEMPLAR, not the only acceptable fill: the row still has to name a real
        /// item id because the client resolves the icon through
        /// <c>InventoryItemManager.LookupItem(materialTypeId)</c>
        /// (acs/ShipBlueprintMaterialUI.cs:47,89-92) and would otherwise draw a
        /// blank slot. VERIFIED: that UI does no accept-filtering of its own - it
        /// only renders - so the server is free to accept a wider set than the
        /// exemplar it displays.
        /// </summary>
        public string MaterialTypeId { get; }

        /// <summary>"Wood"/"Metal"/... - a "Wood"/"Metal" category prints the Q{quality}+ prefix.</summary>
        public string Category { get; }

        /// <summary>
        /// Whether ANY material of <see cref="Category"/> fills this slot (the
        /// retail behaviour: a hull slot wanted "Q3+ Metal", not specifically iron),
        /// or only <see cref="MaterialTypeId"/> exactly.
        ///
        /// Default TRUE. The strict form stays reachable for a slot that genuinely
        /// wants one substance - an atlas shard is not "any metal".
        /// </summary>
        public bool AcceptsAnyInCategory { get; }

        /// <summary>Minimum quality shown as "Q{quality}+".</summary>
        public int Quality { get; }

        /// <summary>How many are required. Shown as the denominator of "0/{amount}".</summary>
        public int Amount { get; }
    }

    /// <summary>
    /// One schematic row of a ship blueprint (the frame, a deck, ...), engine-free.
    /// The handler maps it into a gencode <c>ShipBlueprintSchematic</c>. The client's
    /// <c>ShipBlueprintSchematicUI</c> renders the row even when
    /// <c>LookupSchematic(schematicId)</c> returns null - it falls back to a title of
    /// "{schematicId} x{nodeIds.Count}" and "Unknown Component" material rows that still
    /// show icons/amounts (acs/ShipBlueprintSchematicUI.cs:72-90). "shipFrame" and
    /// "deck01" are the two ids the client treats as non-disableable
    /// (acs/ShipBlueprintSchematicUI.cs:58), so they are always present and enabled.
    /// </summary>
    public sealed class SchematicRow
    {
        public SchematicRow(string schematicId, int nodeCount, bool isEnabled,
            int craftingTime, IReadOnlyList<MaterialRequirement> materials)
        {
            SchematicId = schematicId;
            NodeCount = nodeCount;
            IsEnabled = isEnabled;
            CraftingTime = craftingTime;
            Materials = materials;
        }

        /// <summary>Row id, e.g. "shipFrame" / "deck01". Drives title + non-disableable flag.</summary>
        public string SchematicId { get; }

        /// <summary>How many hull nodes this row represents; printed as "x{NodeCount}".</summary>
        public int NodeCount { get; }

        /// <summary>Whether the row is enabled (mandatory rows are always enabled).</summary>
        public bool IsEnabled { get; }

        /// <summary>Per-row crafting time (seconds); the whole-blueprint time is the recipe's.</summary>
        public int CraftingTime { get; }

        /// <summary>The material requirements shown as slots in the row.</summary>
        public IReadOnlyList<MaterialRequirement> Materials { get; }
    }

    /// <summary>
    /// A ship blueprint's expanded bill of materials: the schematic rows the shipyard's
    /// 1271 <c>ShipBlueprintCraftingState.schematics</c> is populated with when the
    /// player selects the blueprint, plus the whole-blueprint crafting time. Engine-free
    /// so the whole recipe is unit-tested on Linux with no install.
    ///
    /// ======================================================================
    /// TEST RECIPE - NOT THE ORIGINAL WORLDS ADRIFT NUMBERS.
    /// The original per-hull expansion lived on the dead GSim server and is NOT in the
    /// client, so <see cref="TestMakeshiftShip"/> AUTHORS a small, deliberately cheap
    /// bill (a few birch + a few iron - materials the client already resolves from
    /// itemData.json) purely so the cost panel is non-empty and easy to test. Swap in
    /// the real rows/amounts here when a live/private-fork capture provides them; this
    /// is the single place to edit.
    /// ======================================================================
    /// </summary>
    public sealed class ShipBlueprintRecipe
    {
        public ShipBlueprintRecipe(int craftingTime, IReadOnlyList<SchematicRow> rows)
        {
            CraftingTime = craftingTime;
            Rows = rows;
        }

        /// <summary>Whole-blueprint crafting time in seconds (the 1271.craftingTime).</summary>
        public int CraftingTime { get; }

        /// <summary>The schematic rows, in display order.</summary>
        public IReadOnlyList<SchematicRow> Rows { get; }

        /// <summary>The client's two non-disableable, always-present schematic ids.</summary>
        public const string ShipFrameSchematicId = "shipFrame";
        public const string Deck01SchematicId = "deck01";

        /// <summary>
        /// The conservative TEST bill for every Phase-1 blueprint: a mandatory
        /// <c>shipFrame</c> row costing a little birch and a mandatory <c>deck01</c> row
        /// costing a little iron. Amounts are intentionally 1-3 so the loop is trivial to
        /// test. NOT the original recipe - see the class remarks.
        /// </summary>
        public static ShipBlueprintRecipe TestMakeshiftShip()
        {
            var frame = new SchematicRow(
                ShipFrameSchematicId, nodeCount: 1, isEnabled: true, craftingTime: 5,
                materials: new[]
                {
                    // "birch" / "Wood" -> icon woods/Wood_Birch. quality:0 so any
                    // harvested birch matches (trees grant Q0) - test recipe, easy to fill.
                    new MaterialRequirement("birch", "Wood", quality: 0, amount: 3),
                });

            var deck = new SchematicRow(
                Deck01SchematicId, nodeCount: 1, isEnabled: true, craftingTime: 5,
                materials: new[]
                {
                    // "iron" / "Metal" -> icon metals/Metal_Iron. quality:0 so any
                    // harvested iron matches (deposits may grant Q0) - test recipe.
                    new MaterialRequirement("iron", "Metal", quality: 0, amount: 2),
                });

            return new ShipBlueprintRecipe(craftingTime: 10, rows: new[] { frame, deck });
        }
    }
}

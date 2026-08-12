using Bossa.Travellers.Craftingstation;
using Bossa.Travellers.Materials;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>
    /// Thin glue that turns an engine-free <see cref="ShipBlueprintRecipe"/> (unit-tested
    /// on Linux) into the exact gencode
    /// <c>Improbable.Collections.List&lt;ShipBlueprintSchematic&gt;</c> the client reads
    /// from 1271 <c>ShipBlueprintCraftingState.schematics</c>. No policy lives here - the
    /// numbers and rows are all in the recipe; this only translates primitives into the
    /// byte-exact gencode structs.
    ///
    /// SHAPES (verified against gencode):
    ///   ShipBlueprintSchematic(string schematicId, Option&lt;string&gt; schematicOwnerName,
    ///     List&lt;ShipBlueprintMaterial&gt; materials, bool isEnabled, List&lt;uint&gt; nodeIds,
    ///     Option&lt;ShipBlueprintError&gt; error, int gsimSchematicIdHash, int craftingTime)
    ///   ShipBlueprintMaterial(SlottedMaterial requiredMaterial,
    ///     List&lt;SlottedMaterial&gt; actualMaterials, int equivalentMaterialAmount)
    ///   SlottedMaterial(int index, RawMaterial rawMaterial, int amount,
    ///     Option&lt;RawMaterial&gt; customizationMaterial)
    ///   RawMaterial(string materialTypeId, int quality, string category, Map&lt;string,string&gt; meta)
    ///
    /// Phase 1: actualMaterials is empty and equivalentMaterialAmount is 0 - nothing is
    /// loaded yet, so every row's progress reads "0/{required}". Material loading is
    /// Phase 2.
    /// </summary>
    internal static class ShipBlueprintSchematicMapper
    {
        /// <summary>
        /// Phase 2: map a LIVE build (with reserved materials) into the 1271 schematics.
        /// Same shape as <see cref="ToSchematics(ShipBlueprintRecipe)"/>, but each
        /// material row now carries its <c>actualMaterials</c> (one SlottedMaterial per
        /// reserved inventory item) and the real <c>equivalentMaterialAmount</c>, so the
        /// UI's per-slot progress reads "{loaded}/{required}" and the slot fills as the
        /// player drags items in. Row enabled flags come from the build, so a disabled
        /// optional row is reflected too.
        /// </summary>
        public static Improbable.Collections.List<ShipBlueprintSchematic> ToSchematics(ShipBlueprintBuild build)
        {
            var schematics = new Improbable.Collections.List<ShipBlueprintSchematic>();
            foreach (SchematicRowBuild row in build.Rows)
            {
                var materials = new Improbable.Collections.List<ShipBlueprintMaterial>();
                for (int m = 0; m < row.Slots.Count; m++)
                {
                    MaterialSlot slot = row.Slots[m];
                    MaterialRequirement req = slot.Required;

                    RawMaterial requiredRaw = new RawMaterial(
                        req.MaterialTypeId, req.Quality, req.Category,
                        new Improbable.Collections.Map<string, string>());
                    SlottedMaterial required = new SlottedMaterial(
                        m, requiredRaw, req.Amount, new Improbable.Collections.Option<RawMaterial>());

                    // actualMaterials: one SlottedMaterial per reserved item. Exact-type
                    // matching guarantees the loaded item shares the requirement's category,
                    // so the client resolves the same icon/name.
                    var actual = new Improbable.Collections.List<SlottedMaterial>();
                    var loaded = slot.Loaded;
                    for (int i = 0; i < loaded.Count; i++)
                    {
                        RawMaterial loadedRaw = new RawMaterial(
                            loaded[i].ItemTypeId, loaded[i].Quality, req.Category,
                            new Improbable.Collections.Map<string, string>());
                        actual.Add(new SlottedMaterial(
                            i, loadedRaw, loaded[i].Amount, new Improbable.Collections.Option<RawMaterial>()));
                    }

                    materials.Add(new ShipBlueprintMaterial(required, actual, slot.EquivalentAmount));
                }

                var nodeIds = new Improbable.Collections.List<uint>();
                for (int n = 0; n < row.NodeCount; n++)
                {
                    nodeIds.Add((uint)n);
                }

                schematics.Add(new ShipBlueprintSchematic(
                    row.SchematicId,
                    new Improbable.Collections.Option<string>(),
                    materials,
                    row.IsEnabled,
                    nodeIds,
                    new Improbable.Collections.Option<ShipBlueprintError>(),
                    0,
                    row.CraftingTime));
            }
            return schematics;
        }

        public static Improbable.Collections.List<ShipBlueprintSchematic> ToSchematics(ShipBlueprintRecipe recipe)
        {
            var schematics = new Improbable.Collections.List<ShipBlueprintSchematic>();
            foreach (SchematicRow row in recipe.Rows)
            {
                var materials = new Improbable.Collections.List<ShipBlueprintMaterial>();
                for (int m = 0; m < row.Materials.Count; m++)
                {
                    MaterialRequirement req = row.Materials[m];

                    RawMaterial rawMaterial = new RawMaterial(
                        req.MaterialTypeId, req.Quality, req.Category,
                        new Improbable.Collections.Map<string, string>());

                    // requiredMaterial: index is the material slot within the row; amount
                    // is the required count the UI prints as the denominator.
                    SlottedMaterial required = new SlottedMaterial(
                        m, rawMaterial, req.Amount, new Improbable.Collections.Option<RawMaterial>());

                    materials.Add(new ShipBlueprintMaterial(
                        required, new Improbable.Collections.List<SlottedMaterial>(), 0));
                }

                // nodeIds.Count drives the "x{n}" the client prints after the row title.
                var nodeIds = new Improbable.Collections.List<uint>();
                for (int n = 0; n < row.NodeCount; n++)
                {
                    nodeIds.Add((uint)n);
                }

                schematics.Add(new ShipBlueprintSchematic(
                    row.SchematicId,
                    new Improbable.Collections.Option<string>(),
                    materials,
                    row.IsEnabled,
                    nodeIds,
                    new Improbable.Collections.Option<ShipBlueprintError>(),
                    0,
                    row.CraftingTime));
            }
            return schematics;
        }
    }
}

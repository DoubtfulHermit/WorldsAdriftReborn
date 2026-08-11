using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crafting
{
    /// <summary>
    /// The Phase-1 cost bill: selecting a blueprint must populate 1271 with a NON-EMPTY
    /// set of schematic rows, or the shipyard UI shows no material cost (the symptom
    /// being fixed). This asserts the authored TEST recipe's shape - the exact material
    /// rows the <c>ShipBlueprintSchematicMapper</c> hands to 1271. Numbers here are a
    /// deliberate placeholder; see <see cref="ShipBlueprintRecipe"/>.
    /// </summary>
    public class ShipBlueprintRecipeTests
    {
        [Fact]
        public void Test_recipe_is_not_empty()
        {
            // An empty recipe => empty 1271.schematics => the "no cost" bug. Guard it.
            ShipBlueprintRecipe recipe = ShipBlueprintRecipe.TestMakeshiftShip();
            Assert.NotEmpty(recipe.Rows);
        }

        [Fact]
        public void Test_recipe_has_the_two_mandatory_rows()
        {
            // shipFrame and deck01 are the client's non-disableable rows
            // (acs/ShipBlueprintSchematicUI.cs:58); both must be present.
            ShipBlueprintRecipe recipe = ShipBlueprintRecipe.TestMakeshiftShip();
            List<string> ids = recipe.Rows.Select(r => r.SchematicId).ToList();
            Assert.Contains(ShipBlueprintRecipe.ShipFrameSchematicId, ids);
            Assert.Contains(ShipBlueprintRecipe.Deck01SchematicId, ids);
        }

        [Fact]
        public void Mandatory_rows_are_enabled_and_have_a_node()
        {
            // isEnabled true (the client force-enables them anyway) and nodeCount >= 1 so
            // the row prints "x{n}" and iterates its materials.
            ShipBlueprintRecipe recipe = ShipBlueprintRecipe.TestMakeshiftShip();
            foreach (SchematicRow row in recipe.Rows)
            {
                Assert.True(row.IsEnabled);
                Assert.True(row.NodeCount >= 1);
                Assert.NotEmpty(row.Materials);
            }
        }

        [Fact]
        public void Every_material_is_a_client_known_id_in_a_low_test_amount()
        {
            // Materials MUST be ids the client resolves from itemData.json (icon + name),
            // and amounts stay 1-3 per the "easy to test" preference. Inventing an id the
            // client can't resolve would show a raw string and blank icon.
            var knownIds = new HashSet<string> { "birch", "iron" };
            ShipBlueprintRecipe recipe = ShipBlueprintRecipe.TestMakeshiftShip();
            foreach (SchematicRow row in recipe.Rows)
            {
                foreach (MaterialRequirement mat in row.Materials)
                {
                    Assert.Contains(mat.MaterialTypeId, knownIds);
                    Assert.InRange(mat.Amount, 1, 3);
                    Assert.True(mat.Quality >= 1);
                    Assert.False(string.IsNullOrEmpty(mat.Category));
                }
            }
        }

        [Fact]
        public void Frame_costs_birch_and_deck_costs_iron()
        {
            // The authored bill: shipFrame -> birch (Wood), deck01 -> iron (Metal).
            ShipBlueprintRecipe recipe = ShipBlueprintRecipe.TestMakeshiftShip();
            SchematicRow frame = recipe.Rows.Single(r => r.SchematicId == ShipBlueprintRecipe.ShipFrameSchematicId);
            SchematicRow deck = recipe.Rows.Single(r => r.SchematicId == ShipBlueprintRecipe.Deck01SchematicId);
            Assert.Equal("birch", frame.Materials.Single().MaterialTypeId);
            Assert.Equal("Wood", frame.Materials.Single().Category);
            Assert.Equal("iron", deck.Materials.Single().MaterialTypeId);
            Assert.Equal("Metal", deck.Materials.Single().Category);
        }

        [Fact]
        public void Recipe_declares_a_positive_crafting_time()
        {
            // craftingTime feeds the "time to craft" text; a sane positive value.
            ShipBlueprintRecipe recipe = ShipBlueprintRecipe.TestMakeshiftShip();
            Assert.True(recipe.CraftingTime > 0);
        }
    }
}

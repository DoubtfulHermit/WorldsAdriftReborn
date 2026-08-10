using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crafting
{
    /// <summary>
    /// The heart of personal crafting: does the bag satisfy a recipe, what does
    /// one craft consume, and what does it grant. These run natively - no game
    /// install, no wire - which is the whole point of CraftingPolicy being pure.
    /// </summary>
    public class CraftingPolicyTests
    {
        // A tiny stand-in item database: footprints and categories for the types
        // the recipes below use, mirroring the real itemData.json values.
        private static readonly Dictionary<string, ItemFootprint> Sizes = new()
        {
            ["iron"] = new ItemFootprint(3, 2),
            ["steel"] = new ItemFootprint(3, 2),
            ["bronze"] = new ItemFootprint(3, 2),
            ["birch"] = new ItemFootprint(3, 2),
            ["oak"] = new ItemFootprint(3, 2),
            ["fuel"] = new ItemFootprint(2, 2),
            ["torch"] = new ItemFootprint(1, 4),
            ["glider"] = new ItemFootprint(3, 4),
            ["guitar"] = new ItemFootprint(6, 2),
        };

        private static readonly Dictionary<string, string> Categories = new()
        {
            ["iron"] = "Metal",
            ["steel"] = "Metal",
            ["bronze"] = "Metal",
            ["birch"] = "Wood",
            ["oak"] = "Wood",
            ["fuel"] = "Fuel",
            ["torch"] = "",
            ["glider"] = "Equipment",
            ["guitar"] = "Instrument",
        };

        private static bool Footprints(string itemTypeId, out ItemFootprint footprint)
        {
            return Sizes.TryGetValue(itemTypeId, out footprint);
        }

        private static bool CategoryLookup(string itemTypeId, out string category)
        {
            return Categories.TryGetValue(itemTypeId, out category!);
        }

        private static InventoryItem Mat(int itemId, string type, int amount, int x, int y)
        {
            return new InventoryItem(
                itemId, type, amount, InventoryItem.NotWorn, InventoryItem.NoSlot,
                x, y, false, InventoryItem.NoSlot, 0, 0, false,
                new Dictionary<string, string>(), null);
        }

        private static CraftingRequirement Req(int id, string name, int amount) =>
            new CraftingRequirement { Id = id, Name = name, AmountRequired = amount };

        private static SchematicRecord Recipe(string id, string output, int amountToCraft, params CraftingRequirement[] requirements) =>
            new SchematicRecord
            {
                SchematicId = id,
                ItemType = output,
                AmountToCraft = amountToCraft,
                Category = "Personal",
                CraftingRequirements = new List<CraftingRequirement>(requirements),
            };

        // A monotonic id source that also records how many times it was asked.
        private sealed class IdSource
        {
            private int _next;
            public int Calls { get; private set; }
            public IdSource(int start) { _next = start; }
            public int Next() { Calls++; return _next++; }
        }

        [Fact]
        public void Crafts_when_requirements_are_met_consuming_materials_and_granting_output()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "birch", 5, 0, 0));   // Wood
            model.Add(Mat(11, "iron", 5, 3, 0));    // Metal

            // glider: Wood x3 + Metal x2 -> glider x1
            SchematicRecord recipe = Recipe("glider", "glider", 1,
                Req(0, "Wood", 3), Req(1, "Metal", 2));

            IdSource ids = new IdSource(5000);

            CraftOutcome outcome = CraftingPolicy.TryCraft(
                recipe, model, CategoryLookup, ids.Next, 0, new Dictionary<string, string>(), 0, Footprints);

            Assert.True(outcome.Ok, outcome.Reason);
            Assert.Equal(5000, outcome.OutputItemId);

            // Materials drawn down: birch 5->2, iron 5->3.
            Assert.Equal(2, model.ById(10)!.Amount);
            Assert.Equal(3, model.ById(11)!.Amount);

            // The output landed, of the right type and amount.
            InventoryItem? output = model.ById(5000);
            Assert.NotNull(output);
            Assert.Equal("glider", output!.ItemTypeId);
            Assert.Equal(1, output.Amount);
        }

        [Fact]
        public void Consumes_a_stack_entirely_and_removes_it_when_exactly_used_up()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "birch", 3, 0, 0));
            model.Add(Mat(11, "iron", 2, 3, 0));

            SchematicRecord recipe = Recipe("glider", "glider", 1, Req(0, "Wood", 3), Req(1, "Metal", 2));

            CraftOutcome outcome = CraftingPolicy.TryCraft(
                recipe, model, CategoryLookup, new IdSource(1).Next, 0, new Dictionary<string, string>(), 0, Footprints);

            Assert.True(outcome.Ok, outcome.Reason);
            // Both material stacks fully consumed and gone.
            Assert.Null(model.ById(10));
            Assert.Null(model.ById(11));
        }

        [Fact]
        public void Draws_one_requirement_across_several_matching_stacks()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "iron", 3, 0, 0));
            model.Add(Mat(11, "steel", 4, 3, 0)); // also Metal

            // Needs 5 Metal; no single stack has it, but iron(3) + steel(4) do.
            SchematicRecord recipe = Recipe("bar", "torch", 1, Req(0, "Metal", 5));

            CraftOutcome outcome = CraftingPolicy.TryCraft(
                recipe, model, CategoryLookup, new IdSource(1).Next, 0, new Dictionary<string, string>(), 0, Footprints);

            Assert.True(outcome.Ok, outcome.Reason);
            // 5 taken: iron 3 fully, then steel 2 of 4.
            Assert.Null(model.ById(10));
            Assert.Equal(2, model.ById(11)!.Amount);
        }

        [Fact]
        public void A_material_shared_by_two_requirements_is_spent_only_once()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "iron", 4, 0, 0)); // one pool of 4 Metal

            // Two Metal requirements totalling 5 - the single pool of 4 is short,
            // so the craft must be refused rather than double-counting the stack.
            SchematicRecord recipe = Recipe("wing", "torch", 1, Req(0, "Metal", 3), Req(1, "Metal", 2));

            CraftOutcome outcome = CraftingPolicy.TryCraft(
                recipe, model, CategoryLookup, new IdSource(1).Next, 0, new Dictionary<string, string>(), 0, Footprints);

            Assert.False(outcome.Ok);
            // Bag untouched.
            Assert.Equal(4, model.ById(10)!.Amount);
        }

        [Fact]
        public void Two_requirements_sharing_a_pool_that_is_big_enough_both_draw_from_it()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "iron", 5, 0, 0));

            SchematicRecord recipe = Recipe("wing", "torch", 1, Req(0, "Metal", 3), Req(1, "Metal", 2));

            CraftOutcome outcome = CraftingPolicy.TryCraft(
                recipe, model, CategoryLookup, new IdSource(1).Next, 0, new Dictionary<string, string>(), 0, Footprints);

            Assert.True(outcome.Ok, outcome.Reason);
            Assert.Null(model.ById(10)); // all 5 consumed
        }

        [Fact]
        public void Refuses_and_leaves_the_bag_untouched_when_a_requirement_is_short()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "birch", 5, 0, 0));
            model.Add(Mat(11, "iron", 1, 3, 0)); // only 1 Metal, need 2

            SchematicRecord recipe = Recipe("glider", "glider", 1, Req(0, "Wood", 3), Req(1, "Metal", 2));

            CraftOutcome outcome = CraftingPolicy.TryCraft(
                recipe, model, CategoryLookup, new IdSource(1).Next, 0, new Dictionary<string, string>(), 0, Footprints);

            Assert.False(outcome.Ok);
            Assert.Contains("Metal", outcome.Reason);
            // Nothing consumed, no output, even though Wood alone was satisfiable.
            Assert.Equal(5, model.ById(10)!.Amount);
            Assert.Equal(1, model.ById(11)!.Amount);
            Assert.Equal(2, model.Items.Count);
        }

        [Fact]
        public void Does_not_spend_an_item_id_when_the_craft_is_rejected()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "iron", 1, 0, 0));

            SchematicRecord recipe = Recipe("glider", "glider", 1, Req(0, "Metal", 5));
            IdSource ids = new IdSource(9000);

            CraftOutcome outcome = CraftingPolicy.TryCraft(
                recipe, model, CategoryLookup, ids.Next, 0, new Dictionary<string, string>(), 0, Footprints);

            Assert.False(outcome.Ok);
            Assert.Equal(0, ids.Calls); // the id factory was never called
        }

        [Fact]
        public void An_itemTypeId_requirement_matches_that_exact_material_only()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "birch", 2, 0, 0));
            model.Add(Mat(11, "oak", 2, 3, 0));

            // Requirement names the concrete type "birch", so oak must not count.
            SchematicRecord recipe = Recipe("t", "torch", 1, Req(0, "birch", 3));

            CraftOutcome outcome = CraftingPolicy.TryCraft(
                recipe, model, CategoryLookup, new IdSource(1).Next, 0, new Dictionary<string, string>(), 0, Footprints);

            Assert.False(outcome.Ok); // only 2 birch, oak does not help
            Assert.Equal(2, model.ById(11)!.Amount);
        }

        [Fact]
        public void Wood_or_metal_satisfies_the_special_wood_slash_metal_requirement()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "birch", 1, 0, 0)); // Wood
            model.Add(Mat(11, "iron", 1, 3, 0));  // Metal

            SchematicRecord recipe = Recipe("t", "torch", 1, Req(0, "Wood/Metal", 2));

            CraftOutcome outcome = CraftingPolicy.TryCraft(
                recipe, model, CategoryLookup, new IdSource(1).Next, 0, new Dictionary<string, string>(), 0, Footprints);

            Assert.True(outcome.Ok, outcome.Reason);
        }

        [Fact]
        public void Grants_the_recipe_amount_to_craft()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "birch", 4, 0, 0));

            SchematicRecord recipe = Recipe("t", "torch", 3, Req(0, "Wood", 2));

            CraftOutcome outcome = CraftingPolicy.TryCraft(
                recipe, model, CategoryLookup, new IdSource(1).Next, 0, new Dictionary<string, string>(), 0, Footprints);

            Assert.True(outcome.Ok, outcome.Reason);
            Assert.Equal(3, model.ById(outcome.OutputItemId)!.Amount);
        }

        [Fact]
        public void Fails_cleanly_for_an_output_type_the_database_does_not_know()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "birch", 4, 0, 0));

            SchematicRecord recipe = Recipe("t", "nonexistent_item", 1, Req(0, "Wood", 2));

            CraftOutcome outcome = CraftingPolicy.TryCraft(
                recipe, model, CategoryLookup, new IdSource(1).Next, 0, new Dictionary<string, string>(), 0, Footprints);

            Assert.False(outcome.Ok);
            // The material was NOT consumed, because the transaction is atomic.
            Assert.Equal(4, model.ById(10)!.Amount);
        }

        [Fact]
        public void Matches_follows_the_client_rule()
        {
            Assert.True(CraftingPolicy.Matches("Metal", "iron", "Metal"));   // category
            Assert.True(CraftingPolicy.Matches("iron", "iron", "Metal"));    // itemTypeId
            Assert.True(CraftingPolicy.Matches("Wood/Metal", "iron", "Metal"));
            Assert.True(CraftingPolicy.Matches("Wood/Metal", "birch", "Wood"));
            Assert.False(CraftingPolicy.Matches("Wood/Metal", "fuel", "Fuel"));
            Assert.False(CraftingPolicy.Matches("Metal", "birch", "Wood"));
            Assert.False(CraftingPolicy.Matches("", "iron", "Metal"));
        }

        [Fact]
        public void AvailableFor_sums_matching_stacks_only()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "iron", 3, 0, 0));
            model.Add(Mat(11, "steel", 4, 3, 0));
            model.Add(Mat(12, "birch", 9, 6, 0));

            Assert.Equal(7, CraftingPolicy.AvailableFor(model, CategoryLookup, "Metal"));
            Assert.Equal(9, CraftingPolicy.AvailableFor(model, CategoryLookup, "Wood"));
            Assert.Equal(3, CraftingPolicy.AvailableFor(model, CategoryLookup, "iron"));
        }

        [Fact]
        public void CanCraft_reports_without_mutating()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "birch", 3, 0, 0));
            model.Add(Mat(11, "iron", 2, 3, 0));

            SchematicRecord recipe = Recipe("glider", "glider", 1, Req(0, "Wood", 3), Req(1, "Metal", 2));

            Assert.True(CraftingPolicy.CanCraft(recipe, model, CategoryLookup, out _));
            // Still there - CanCraft worked on a copy.
            Assert.Equal(3, model.ById(10)!.Amount);
            Assert.Equal(2, model.ById(11)!.Amount);
        }
    }
}

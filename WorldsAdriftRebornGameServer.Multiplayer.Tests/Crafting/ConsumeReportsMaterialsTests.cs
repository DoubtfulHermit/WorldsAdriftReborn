using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crafting
{
    /// <summary>
    /// The consumed-materials report of the station-craft transaction: the
    /// TryConsumeOnly overload must say exactly WHICH stacks it drew down,
    /// because that list is what the handler refunds if the deferred world-spawn
    /// then fails - the "refund + log" half of "no craft may eat materials it
    /// cannot show". A wrong list refunds the wrong materials, which is the same
    /// bug wearing a different hat.
    /// </summary>
    public class ConsumeReportsMaterialsTests
    {
        private static readonly Dictionary<string, string> Categories = new()
        {
            ["iron"] = "Metal",
            ["steel"] = "Metal",
            ["birch"] = "Wood",
            ["fuel"] = "Fuel",
        };

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

        private static SchematicRecord Recipe(params CraftingRequirement[] requirements) =>
            new()
            {
                SchematicId = "atlasSkyCore",
                ItemType = "skyCore",
                Category = "CraftingStation",
                CraftingRequirements = new List<CraftingRequirement>(requirements),
            };

        [Fact]
        public void Reports_exactly_what_one_craft_drew_down()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "birch", 10, 0, 0));
            model.Add(Mat(11, "iron", 5, 3, 0));

            SchematicRecord recipe = Recipe(
                new CraftingRequirement { Id = 0, Name = "Wood", AmountRequired = 8 },
                new CraftingRequirement { Id = 1, Name = "Metal", AmountRequired = 2 });

            Assert.True(CraftingPolicy.TryConsumeOnly(recipe, model, CategoryLookup,
                out string reason, out IReadOnlyList<ConsumedMaterial> consumed), reason);

            // The report IS the refund: same types, same amounts.
            Assert.Equal(2, consumed.Count);
            Assert.Contains(new ConsumedMaterial("birch", 8), consumed);
            Assert.Contains(new ConsumedMaterial("iron", 2), consumed);
            // And it matches what actually left the bag.
            Assert.Equal(2, model.ById(10)!.Amount);
            Assert.Equal(3, model.ById(11)!.Amount);
        }

        [Fact]
        public void A_requirement_spanning_two_stacks_reports_both_draws()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "iron", 3, 0, 0));
            model.Add(Mat(11, "steel", 4, 3, 0));

            SchematicRecord recipe = Recipe(
                new CraftingRequirement { Id = 0, Name = "Metal", AmountRequired = 5 });

            Assert.True(CraftingPolicy.TryConsumeOnly(recipe, model, CategoryLookup,
                out _, out IReadOnlyList<ConsumedMaterial> consumed));

            // 3 iron (whole stack) + 2 steel: refunding this list restores the player
            // to the exact material mix they paid, not a lump of one type.
            Assert.Equal(2, consumed.Count);
            Assert.Contains(new ConsumedMaterial("iron", 3), consumed);
            Assert.Contains(new ConsumedMaterial("steel", 2), consumed);
        }

        [Fact]
        public void A_rejected_consume_reports_nothing()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "iron", 1, 0, 0));

            SchematicRecord recipe = Recipe(
                new CraftingRequirement { Id = 0, Name = "Metal", AmountRequired = 2 });

            Assert.False(CraftingPolicy.TryConsumeOnly(recipe, model, CategoryLookup,
                out string reason, out IReadOnlyList<ConsumedMaterial> consumed));

            Assert.NotEqual(string.Empty, reason);
            // Nothing left the bag, so there must be nothing to refund.
            Assert.Empty(consumed);
            Assert.Equal(1, model.ById(10)!.Amount);
        }

        [Fact]
        public void The_report_free_overload_still_behaves_identically()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Mat(10, "birch", 10, 0, 0));

            SchematicRecord recipe = Recipe(
                new CraftingRequirement { Id = 0, Name = "Wood", AmountRequired = 8 });

            Assert.True(CraftingPolicy.TryConsumeOnly(recipe, model, CategoryLookup, out string reason), reason);
            Assert.Equal(2, model.ById(10)!.Amount);
        }
    }
}

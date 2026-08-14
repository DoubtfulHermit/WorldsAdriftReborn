using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public sealed class ShipPartSalvagePolicyTests
    {
        [Fact]
        public void Only_an_owned_part_on_the_ship_actually_docked_in_the_yard_is_salvageable()
        {
            Assert.Equal(ShipPartSalvageReject.Accept,
                ShipPartSalvagePolicy.Evaluate(true, 20, 30, 20, true, true));
            Assert.Equal(ShipPartSalvageReject.ShipNotDocked,
                ShipPartSalvagePolicy.Evaluate(true, 20, 0, 0, true, true));
            Assert.Equal(ShipPartSalvageReject.DockMismatch,
                ShipPartSalvagePolicy.Evaluate(true, 20, 30, 21, true, true));
            Assert.Equal(ShipPartSalvageReject.NotShipyardOwner,
                ShipPartSalvagePolicy.Evaluate(true, 20, 30, 20, false, true));
        }

        [Fact]
        public void Refunds_aggregate_duplicate_recipe_materials_and_resolve_categories()
        {
            var recipe = new SchematicRecord
            {
                CraftingRequirements = new List<CraftingRequirement>
                {
                    new() { Name = "iron", AmountRequired = 2 },
                    new() { Name = "IRON", AmountRequired = 3 },
                    new() { Name = "Wood", AmountRequired = 4 },
                    new() { Name = "Fuel", AmountRequired = 1 },
                }
            };
            IReadOnlyList<ShipPartSalvageRefund> refunds = ShipPartSalvagePolicy.Refunds(recipe);
            Assert.Contains(refunds, x => x.ItemTypeId.Equals("iron", StringComparison.OrdinalIgnoreCase) && x.Amount == 5);
            Assert.Contains(refunds, x => x.ItemTypeId == "birch" && x.Amount == 4);
            Assert.Contains(refunds, x => x.ItemTypeId == "fuel" && x.Amount == 1);
            Assert.Equal(3, refunds.Count);
        }
    }
}

using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public sealed class ShipPartSalvagePolicyTests
    {
        [Fact]
        public void Only_a_crafted_part_inside_the_players_shipyard_is_salvageable()
        {
            Assert.Equal(ShipPartSalvageReject.Accept,
                ShipPartSalvagePolicy.Evaluate(true, true, true));
            Assert.Equal(ShipPartSalvageReject.NotCraftedPart,
                ShipPartSalvagePolicy.Evaluate(false, true, true));
            Assert.Equal(ShipPartSalvageReject.OutsideOwnedShipyard,
                ShipPartSalvagePolicy.Evaluate(true, false, true));
            Assert.Equal(ShipPartSalvageReject.UnknownRecipe,
                ShipPartSalvagePolicy.Evaluate(true, true, false));
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

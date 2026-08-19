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
                ShipPartSalvagePolicy.Evaluate(true, true, true, false));
            Assert.Equal(ShipPartSalvageReject.NotCraftedPart,
                ShipPartSalvagePolicy.Evaluate(false, true, true, false));
            Assert.Equal(ShipPartSalvageReject.OutsideOwnedShipyard,
                ShipPartSalvagePolicy.Evaluate(true, false, true, false));
            Assert.Equal(ShipPartSalvageReject.UnknownRecipe,
                ShipPartSalvagePolicy.Evaluate(true, true, false, false));
        }

        /// <summary>
        /// A ship storage container that still holds something is NOT dismantled.
        /// Salvaging destroys the entity and its inventory goes with it, and this
        /// server has nowhere to spill the contents - so without this the salvage
        /// beam is a silent item deleter, and silent is the operative word: the
        /// player sees a successful salvage and a full refund.
        /// </summary>
        [Fact]
        public void A_container_that_still_holds_something_is_refused()
        {
            Assert.Equal(ShipPartSalvageReject.ContainerNotEmpty,
                ShipPartSalvagePolicy.Evaluate(true, true, true, containerHoldsItems: true));
        }

        /// <summary>
        /// An emptied container salvages exactly like any other part, so "empty it
        /// first" is a real answer the player can act on rather than a dead end.
        /// </summary>
        [Fact]
        public void An_emptied_container_salvages_normally()
        {
            Assert.Equal(ShipPartSalvageReject.Accept,
                ShipPartSalvagePolicy.Evaluate(true, true, true, containerHoldsItems: false));
        }

        /// <summary>
        /// The container check runs LAST. A player shooting a full trunk from outside
        /// their shipyard is told the thing they can act on first - where they are
        /// standing - not a second reason that would still be there afterwards.
        /// </summary>
        [Fact]
        public void The_container_refusal_never_masks_a_more_basic_one()
        {
            Assert.Equal(ShipPartSalvageReject.OutsideOwnedShipyard,
                ShipPartSalvagePolicy.Evaluate(true, false, true, containerHoldsItems: true));
            Assert.Equal(ShipPartSalvageReject.NotCraftedPart,
                ShipPartSalvagePolicy.Evaluate(false, true, true, containerHoldsItems: true));
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

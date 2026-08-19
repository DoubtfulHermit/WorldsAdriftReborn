using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Loot
{
    /// <summary>
    /// TAKING LOOT OUT OF A CHEST.
    ///
    /// The two properties worth breaking a build over:
    ///
    ///   * <b>ids are renumbered.</b> Item ids are per-inventory and both allocators
    ///     start at the same floor, so a chest and a player routinely both hold an
    ///     item numbered 1104. Carrying the id across collides, and the client keys
    ///     its slot lookup on (EntityId, ItemId, ItemType, IsSplitItem) and inserts
    ///     with the INDEXER - a repeated id silently overwrites, one of the two items
    ///     vanishes, and RemoveByItemId then deletes BOTH.
    ///   * <b>a refused move changes nothing.</b> Destination first, source second.
    ///     Remove-then-fail-to-place deletes a player's loot.
    /// </summary>
    public class CrossInventoryPolicyTests
    {
        private static readonly Dictionary<string, ItemFootprint> Sizes = new()
        {
            ["small"] = new ItemFootprint(1, 1),
            ["wide"] = new ItemFootprint(4, 2),
            ["tall"] = new ItemFootprint(2, 4),
        };

        private static bool Footprints(string itemTypeId, out ItemFootprint footprint) =>
            Sizes.TryGetValue(itemTypeId, out footprint);

        private static InventoryItem Item(int id, string type, int x, int y) =>
            new InventoryItem(id, type, 1, InventoryItem.NotWorn, InventoryItem.NoSlot,
                x, y, false, InventoryItem.NoSlot, 0, 0, false,
                new Dictionary<string, string>(), null);

        private static InventoryModel Chest()
        {
            InventoryModel model = new InventoryModel(10, 6, false, 0);
            model.Add(Item(1104, "wide", 0, 0));
            model.Add(Item(1105, "small", 0, 2));
            return model;
        }

        private static InventoryModel Player() => new InventoryModel(10, 18, true, 3);

        [Fact]
        public void TakingAnItemMovesItAndGivesItTheDestinationsOwnId()
        {
            InventoryModel chest = Chest();
            InventoryModel bag = Player();
            bag.Add(Item(1104, "small", 9, 17));   // deliberately the SAME id

            CrossMoveOutcome outcome = CrossInventoryPolicy.TryMove(
                chest, bag, sourceItemId: 1104, destinationItemId: 5000,
                x: 0, y: 0, rotate: false, Footprints);

            Assert.Equal(CrossMoveOutcome.Moved, outcome);
            Assert.Null(chest.ById(1104));
            Assert.NotNull(bag.ById(5000));
            Assert.Equal("wide", bag.ById(5000)!.ItemTypeId);

            // The pre-existing 1104 in the bag is untouched, which is the whole point.
            Assert.NotNull(bag.ById(1104));
            Assert.Equal("small", bag.ById(1104)!.ItemTypeId);
        }

        [Fact]
        public void EverythingButThePositionAndIdSurvivesTheMove()
        {
            InventoryModel chest = new InventoryModel(10, 6, false, 0);
            Dictionary<string, string> meta = new() { ["colour"] = "rust", ["health"] = "40" };
            chest.Add(new InventoryItem(1104, "small", 7, InventoryItem.NotWorn, InventoryItem.NoSlot,
                0, 0, false, InventoryItem.NoSlot, 0, 6, false, meta, 3));

            InventoryModel bag = Player();
            Assert.Equal(CrossMoveOutcome.Moved, CrossInventoryPolicy.TryMove(
                chest, bag, 1104, 9000, 2, 2, rotate: false, Footprints));

            InventoryItem moved = bag.ById(9000)!;
            Assert.Equal(7, moved.Amount);
            Assert.Equal(6, moved.Quality);
            Assert.Equal(3, moved.Rarity);
            // Meta is the ONLY place colours and item health live; dropping it strips
            // every dyed garment in the game.
            Assert.Equal("rust", moved.Meta["colour"]);
            Assert.Equal("40", moved.Meta["health"]);
        }

        [Fact]
        public void ARefusedMoveLeavesBothInventoriesExactlyAsTheyWere()
        {
            InventoryModel chest = Chest();
            InventoryModel bag = new InventoryModel(2, 2, false, 0);   // far too small

            CrossMoveOutcome outcome = CrossInventoryPolicy.TryMove(
                chest, bag, 1104, 5000, 0, 0, rotate: false, Footprints);

            Assert.Equal(CrossMoveOutcome.NoRoom, outcome);
            Assert.NotNull(chest.ById(1104));      // still in the chest
            Assert.Empty(bag.Items);               // and nowhere else
        }

        [Fact]
        public void AMoveOntoAnOccupiedCellIsRefused()
        {
            InventoryModel chest = Chest();
            InventoryModel bag = Player();
            bag.Add(Item(2000, "wide", 0, 0));

            Assert.Equal(CrossMoveOutcome.NoRoom, CrossInventoryPolicy.TryMove(
                chest, bag, 1104, 5000, 1, 0, rotate: false, Footprints));
            Assert.NotNull(chest.ById(1104));
        }

        [Fact]
        public void RotationIsHonouredWhenItIsWhatMakesTheItemFit()
        {
            InventoryModel chest = new InventoryModel(10, 6, false, 0);
            chest.Add(Item(1104, "wide", 0, 0));            // 4x2
            InventoryModel narrow = new InventoryModel(2, 4, false, 0);

            Assert.Equal(CrossMoveOutcome.NoRoom, CrossInventoryPolicy.TryMove(
                chest, narrow, 1104, 7000, 0, 0, rotate: false, Footprints));

            Assert.Equal(CrossMoveOutcome.Moved, CrossInventoryPolicy.TryMove(
                chest, narrow, 1104, 7000, 0, 0, rotate: true, Footprints));
            Assert.True(narrow.ById(7000)!.Rotated);
        }

        [Fact]
        public void AHotbarAssignmentDoesNotTravelWithTheItem()
        {
            InventoryModel bag = Player();
            bag.Add(new InventoryItem(1104, "small", 1, InventoryItem.NotWorn, InventoryItem.NoSlot,
                0, 0, false, 5, 0, 0, false, new Dictionary<string, string>(), null));

            InventoryModel chest = new InventoryModel(10, 6, false, 0);
            Assert.Equal(CrossMoveOutcome.Moved, CrossInventoryPolicy.TryMove(
                bag, chest, 1104, 3000, 0, 0, rotate: false, Footprints));

            // A chest has no hotbar; an item that kept slot 5 would give it one.
            Assert.Equal(InventoryItem.NoSlot, chest.ById(3000)!.HotBarSlotNum);
        }

        [Fact]
        public void UnknownItemsUnknownTypesAndSelfMovesAreNamedRefusals()
        {
            InventoryModel chest = Chest();
            InventoryModel bag = Player();

            Assert.Equal(CrossMoveOutcome.UnknownItem,
                CrossInventoryPolicy.TryMove(chest, bag, 4242, 1, 0, 0, false, Footprints));
            Assert.Equal(CrossMoveOutcome.SameInventory,
                CrossInventoryPolicy.TryMove(chest, chest, 1104, 1, 0, 0, false, Footprints));

            InventoryModel odd = new InventoryModel(10, 6, false, 0);
            odd.Add(Item(1104, "not-a-real-type", 0, 0));
            Assert.Equal(CrossMoveOutcome.UnknownItemType,
                CrossInventoryPolicy.TryMove(odd, bag, 1104, 1, 0, 0, false, Footprints));
        }

        [Fact]
        public void AWornGarmentIsNotInAGridAndCannotBeDraggedIntoAChest()
        {
            InventoryModel bag = Player();
            bag.Add(new InventoryItem(1104, "small", 1, "Chest", InventoryItem.NoSlot,
                0, 0, false, InventoryItem.NoSlot, 0, 0, false,
                new Dictionary<string, string>(), null));

            Assert.Equal(CrossMoveOutcome.NotInGrid, CrossInventoryPolicy.TryMove(
                bag, new InventoryModel(10, 6, false, 0), 1104, 1, 0, 0, false, Footprints));
        }

        [Fact]
        public void TakeAllEmptiesTheChest()
        {
            InventoryModel chest = Chest();
            InventoryModel bag = Player();
            int next = 5000;

            int moved = CrossInventoryPolicy.MoveAll(chest, bag, () => next++, Footprints);

            Assert.Equal(2, moved);
            Assert.Empty(chest.Items);
            Assert.Equal(2, bag.Items.Count);
            Assert.Equal(2, bag.Items.Select(i => i.ItemId).Distinct().Count());
        }

        [Fact]
        public void TakeAllMovesWhatFitsAndLeavesTheRest()
        {
            InventoryModel chest = Chest();                    // a 4x2 and a 1x1
            InventoryModel tiny = new InventoryModel(1, 1, false, 0);
            int next = 5000;

            int moved = CrossInventoryPolicy.MoveAll(chest, tiny, () => next++, Footprints);

            // The 1x1 fits, the 4x2 does not - and a full destination must not stop
            // the walk, or a big item at the front of the list blocks everything.
            Assert.Equal(1, moved);
            Assert.Single(chest.Items);
            Assert.Equal("wide", chest.Items[0].ItemTypeId);
        }

        [Fact]
        public void TakeAllBurnsNoIdOnAnItemItCouldNotPlace()
        {
            InventoryModel chest = Chest();
            InventoryModel tiny = new InventoryModel(1, 1, false, 0);
            int issued = 0;

            CrossInventoryPolicy.MoveAll(chest, tiny, () => { issued++; return 5000 + issued; }, Footprints);

            Assert.Equal(1, issued);
        }
    }
}

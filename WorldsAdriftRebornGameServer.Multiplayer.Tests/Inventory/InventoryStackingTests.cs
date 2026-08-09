using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Inventory
{
    /// <summary>
    /// Covers InventoryPolicy.TryStackInto - the merge step a repeated harvest
    /// grant needs so the grid does not fill with duplicate material piles.
    /// </summary>
    public class InventoryStackingTests
    {
        private const int StackMax = 99;

        private static InventoryItem Iron(int itemId, int amount, int x, int y, int quality = 0)
        {
            return new InventoryItem(
                itemId, "iron", amount, InventoryItem.NotWorn, InventoryItem.NoSlot,
                x, y, false, InventoryItem.NoSlot, 0, quality, false,
                new Dictionary<string, string>(), null);
        }

        [Fact]
        public void Merges_into_an_existing_stack_of_the_same_type()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(Iron(2000, 12, 0, 0));

            InventoryItem? merged = InventoryPolicy.TryStackInto(model, "iron", 12, quality: 0, StackMax);

            Assert.NotNull(merged);
            Assert.Equal(2000, merged!.ItemId);
            Assert.Equal(24, merged.Amount);
            // No new row was created.
            Assert.Single(model.Items);
            Assert.Equal(24, model.ById(2000)!.Amount);
        }

        [Fact]
        public void Returns_null_when_there_is_no_matching_stack()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(Iron(2000, 12, 0, 0));

            // Different type: no merge target, caller must place a new item.
            Assert.Null(InventoryPolicy.TryStackInto(model, "birch", 1, 0, StackMax));
        }

        [Fact]
        public void Never_merges_across_different_quality()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(Iron(2000, 12, 0, 0, quality: 0));

            // A quality-5 nugget must not fold into the quality-0 pile.
            Assert.Null(InventoryPolicy.TryStackInto(model, "iron", 8, quality: 5, StackMax));
            Assert.Equal(12, model.ById(2000)!.Amount);
        }

        [Fact]
        public void Never_overflows_the_stack_max()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(Iron(2000, 95, 0, 0));

            // 95 + 12 > 99: this stack cannot swallow the whole amount, so no
            // merge and no partial split.
            Assert.Null(InventoryPolicy.TryStackInto(model, "iron", 12, 0, StackMax));
            Assert.Equal(95, model.ById(2000)!.Amount);
        }

        [Fact]
        public void Fills_a_stack_exactly_to_the_max()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(Iron(2000, 90, 0, 0));

            InventoryItem? merged = InventoryPolicy.TryStackInto(model, "iron", 9, 0, StackMax);

            Assert.NotNull(merged);
            Assert.Equal(99, merged!.Amount);
        }

        [Fact]
        public void Picks_a_stack_that_has_room_over_one_that_does_not()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(Iron(2000, 95, 0, 0)); // full-ish, cannot take 12
            model.Add(Iron(2001, 10, 3, 0)); // has room

            InventoryItem? merged = InventoryPolicy.TryStackInto(model, "iron", 12, 0, StackMax);

            Assert.NotNull(merged);
            Assert.Equal(2001, merged!.ItemId);
            Assert.Equal(22, merged.Amount);
            Assert.Equal(95, model.ById(2000)!.Amount);
        }

        [Fact]
        public void An_unstackable_type_never_merges()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(Iron(2000, 1, 0, 0));

            // stackMax <= 1 is the "not stackable / stacksize unset" case: always
            // a fresh row, never a merge.
            Assert.Null(InventoryPolicy.TryStackInto(model, "iron", 1, 0, stackMax: 1));
            Assert.Null(InventoryPolicy.TryStackInto(model, "iron", 1, 0, stackMax: -1));
        }

        [Fact]
        public void Never_merges_into_a_worn_item()
        {
            InventoryModel model = InventoryTestData.Grid();
            InventoryItem worn = Iron(2000, 1, 0, 0) with { SlotType = "Body" };
            model.Add(worn);

            Assert.Null(InventoryPolicy.TryStackInto(model, "iron", 12, 0, StackMax));
        }
    }
}

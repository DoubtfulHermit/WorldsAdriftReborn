using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Inventory
{
    public class ItemIdAllocatorTests
    {
        [Fact]
        public void Ids_start_above_every_id_the_seed_already_uses()
        {
            // The seed hands out 1 to 4 and 1101 to 1103, and the stash 6 to 30.
            // A colliding id is SILENT on the client - one of the two items just
            // stops existing - so the floor is the only thing preventing it.
            Assert.Equal(1104, ItemIdAllocator.Floor);
            Assert.Equal(1104, new ItemIdAllocator().Next());
        }

        [Fact]
        public void Ids_are_never_handed_out_twice()
        {
            ItemIdAllocator allocator = new();

            Assert.Equal(1104, allocator.Next());
            Assert.Equal(1105, allocator.Next());
            Assert.Equal(1106, allocator.Next());
        }

        [Fact]
        public void A_reserved_id_is_skipped()
        {
            ItemIdAllocator allocator = new();

            allocator.Reserve(2000);

            Assert.Equal(2001, allocator.Next());
        }

        [Fact]
        public void Reserving_an_id_below_the_floor_changes_nothing()
        {
            ItemIdAllocator allocator = new();

            allocator.Reserve(4);
            allocator.Reserve(1103);

            Assert.Equal(1104, allocator.Next());
        }

        [Fact]
        public void Reserving_is_idempotent()
        {
            ItemIdAllocator allocator = new();

            allocator.Reserve(2000);
            allocator.Reserve(2000);
            allocator.Reserve(1500);

            Assert.Equal(2001, allocator.Next());
        }

        [Fact]
        public void An_allocator_built_for_a_model_never_collides_with_it()
        {
            // The restore path. A fresh allocator starting at the floor would
            // reach a restored id after enough grants and quietly eat the item.
            InventoryModel model = InventoryTestData.Seeded();
            model.Add(InventoryTestData.Item(3000, "iron", 5, 10));

            ItemIdAllocator allocator = ItemIdAllocator.For(model);

            int next = allocator.Next();

            Assert.Equal(3001, next);
            Assert.Null(model.ById(next));
        }

        [Fact]
        public void Peek_does_not_consume()
        {
            ItemIdAllocator allocator = new();

            Assert.Equal(allocator.Peek, allocator.Peek);
            Assert.Equal(allocator.Peek, allocator.Next());
        }
    }
}

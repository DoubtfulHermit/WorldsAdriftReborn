using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Inventory
{
    public class WearableInvariantsTests
    {
        private static Dictionary<string, string> WithHealth(string value)
        {
            return new Dictionary<string, string> { [WearableInvariants.TotalHealthKey] = value };
        }

        [Fact]
        public void The_three_arrays_are_always_the_same_length()
        {
            // A shorter `active` list is an IndexOutOfRangeException per FRAME on
            // the client, not once.
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "torso_poncho", slotType: "Body", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1201, "head_devhat", slotType: "Head", meta: WithHealth("50")));

            WearableArrays arrays = WearableInvariants.For(model);

            Assert.True(arrays.IsConsistent);
            Assert.Equal(2, arrays.Count);
        }

        [Fact]
        public void Two_garments_in_different_slots_both_survive()
        {
            // The old hand-written 1280 write used a single-element list, which
            // is why equipping a second wearable replaced the first.
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "torso_poncho", slotType: "Body", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1201, "head_devhat", slotType: "Head", meta: WithHealth("100")));

            WearableArrays arrays = WearableInvariants.For(model);

            Assert.Contains(1200, arrays.ItemIds);
            Assert.Contains(1201, arrays.ItemIds);
        }

        [Fact]
        public void An_item_that_is_not_worn_is_not_in_the_arrays()
        {
            InventoryModel model = InventoryTestData.Seeded();

            Assert.Equal(0, WearableInvariants.For(model).Count);
        }

        [Fact]
        public void A_worn_item_the_client_would_never_register_is_left_out()
        {
            // GearWearablesVisualizer registers only items with a parseable
            // meta["totalHealth"] of at least 0.01. An id in the array that it
            // never registered is a KeyNotFoundException per frame.
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "torso_poncho", slotType: "Body"));
            model.Add(InventoryTestData.Item(1201, "head_devhat", slotType: "Head", meta: WithHealth("nonsense")));
            model.Add(InventoryTestData.Item(1202, "head_devhat", slotType: "Face", meta: WithHealth("0")));

            Assert.Equal(0, WearableInvariants.For(model).Count);
        }

        [Fact]
        public void Total_health_is_parsed_invariantly()
        {
            // A server running under a comma-decimal locale must not read
            // "100.5" as 1005 or fail to read it at all.
            InventoryItem item = InventoryTestData.Item(1200, "torso_poncho", slotType: "Body", meta: WithHealth("100.5"));

            Assert.Equal(100.5f, WearableInvariants.TotalHealthOf(item));
        }

        [Fact]
        public void Every_id_in_the_arrays_is_worn_in_the_inventory()
        {
            // The invariant the client enforces with an exception: the worn set
            // comes from 1081 and the durability from 1280, and they must agree.
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "torso_poncho", slotType: "Body", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1201, "iron", meta: WithHealth("100")));

            foreach (int id in WearableInvariants.For(model).ItemIds)
            {
                Assert.True(model.ById(id)!.IsWorn);
            }
        }

        [Fact]
        public void Everything_in_the_arrays_starts_active()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "torso_poncho", slotType: "Body", meta: WithHealth("100")));

            Assert.All(WearableInvariants.For(model).Active, Assert.True);
        }
    }
}

using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
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
        public void A_worn_deployable_with_valid_health_is_still_left_out()
        {
            // THE PLACE-A-SHIPYARD CRASH. A shipyard is marked equippable /
            // characterSlot="Utility" with meta totalHealth="100" in itemData.json -
            // byte-identical in shape to the glider - so once it occupies the Utility
            // slot it passes IsWorn AND the totalHealth gate. But the client's
            // GearWearablesVisualizer has no rig UtilityItem for a placed structure, so
            // its id in 1280.itemIds is never registered and
            // UpdateActiveWornItemsHealths throws _utilityIdToUtility[id] every frame.
            // It must be excluded here even though it is a genuine worn item present in
            // 1081 (the mismatch is with the rig, not the inventory).
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1300, "shipyard", slotType: "Utility", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1301, "assemblyStation", slotType: "Utility", meta: WithHealth("100")));

            WearableArrays arrays = WearableInvariants.For(model);

            Assert.Equal(0, arrays.Count);
            Assert.DoesNotContain(1300, arrays.ItemIds);
            Assert.DoesNotContain(1301, arrays.ItemIds);
        }

        [Fact]
        public void A_genuine_worn_utility_alongside_a_worn_deployable_survives_alone()
        {
            // The glider (a real rig UtilityItem) worn next to a shipyard: only the
            // glider may reach 1280. Proves the exclusion is deployable-scoped, not a
            // blanket "drop everything in the Utility slot".
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1101, "glider", slotType: "Utility", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1300, "shipyard", slotType: "Utility", meta: WithHealth("100")));

            WearableArrays arrays = WearableInvariants.For(model);

            Assert.Contains(1101, arrays.ItemIds);
            Assert.DoesNotContain(1300, arrays.ItemIds);
        }

        [Fact]
        public void After_a_consumed_utility_no_active_id_is_absent_from_the_inventory()
        {
            // THE INVARIANT THE CLIENT ENFORCES WITH A PER-FRAME THROW. Every active
            // id 1280 serves must resolve to an item the served 1081 inventory still
            // holds worn - otherwise GearWearablesVisualizer cannot register it and
            // UpdateActiveWornItemsHealths throws. Model the place-and-consume: a worn
            // glider plus a shipyard that is then placed (removed). After the removal,
            // the surviving 1280 arrays are re-derived and must reference only items
            // still present in the inventory.
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1101, "glider", slotType: "Utility", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1300, "shipyard", slotType: "Utility", meta: WithHealth("100")));

            // The shipyard is placed and consumed authoritatively (ItemPlacingState_Handler
            // -> model.Remove -> InventoryPush re-derives 1280 from this same model).
            model.Remove(1300);

            WearableArrays arrays = WearableInvariants.For(model);

            for (int i = 0; i < arrays.Count; i++)
            {
                InventoryItem? backing = model.ById(arrays.ItemIds[i]);
                Assert.NotNull(backing);          // no active id absent from inventory
                Assert.True(backing!.IsWorn);     // and it is genuinely worn in 1081
                Assert.False(arrays.Active[i] && Deployables.IsDeployable(backing.ItemTypeId));
            }
            // The shipyard never appears; the glider does.
            Assert.DoesNotContain(1300, arrays.ItemIds);
            Assert.Contains(1101, arrays.ItemIds);
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

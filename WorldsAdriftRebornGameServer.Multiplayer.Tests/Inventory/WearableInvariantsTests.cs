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
            // the client, not once. Two genuine worn utilities (Utility + UtilityFeet).
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1101, "glider", slotType: "Utility", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1201, "boots_grav", slotType: "UtilityFeet", meta: WithHealth("50")));

            WearableArrays arrays = WearableInvariants.For(model);

            Assert.True(arrays.IsConsistent);
            Assert.Equal(2, arrays.Count);
        }

        [Fact]
        public void Two_utilities_in_different_slots_both_survive()
        {
            // The old hand-written 1280 write used a single-element list, which
            // is why equipping a second utility replaced the first.
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1101, "glider", slotType: "Utility", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1201, "helm_grav", slotType: "UtilityHead", meta: WithHealth("100")));

            WearableArrays arrays = WearableInvariants.For(model);

            Assert.Contains(1101, arrays.ItemIds);
            Assert.Contains(1201, arrays.ItemIds);
        }

        [Fact]
        public void An_item_that_is_not_worn_is_not_in_the_arrays()
        {
            InventoryModel model = InventoryTestData.Seeded();

            Assert.Equal(0, WearableInvariants.For(model).Count);
        }

        [Fact]
        public void A_worn_utility_without_usable_health_is_left_out()
        {
            // GearWearablesVisualizer registers only items with a parseable
            // meta["totalHealth"] of at least 0.01. An id in the array that it
            // never registered is a KeyNotFoundException per frame. All three are in
            // a real utility slot but fail the health gate (missing / unparseable / 0).
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "glider", slotType: "Utility"));
            model.Add(InventoryTestData.Item(1201, "glider", slotType: "UtilityHead", meta: WithHealth("nonsense")));
            model.Add(InventoryTestData.Item(1202, "glider", slotType: "UtilityFeet", meta: WithHealth("0")));

            Assert.Equal(0, WearableInvariants.For(model).Count);
        }

        [Fact]
        public void A_worn_tool_slot_item_is_left_out()
        {
            // THE LOAD-IN FLOOD that survived the deployable-only rule. A pistol/torch
            // is characterSlot="Tool"+totalHealth="100" in itemData.json, so worn it
            // passes IsWorn and the health gate. But CharacterSlotType.Tool is NOT one of
            // CharacterCustomisationVisualizer._slotTypeToUtilityIds (Utility/UtilityHead/
            // UtilityFeet/UtilityHand), so AddUtilityItem is never called for it and the
            // client builds no UtilityItem - its id in 1280 is a KeyNotFoundException
            // every frame (3,106 throws in one load-in log).
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1400, "pistol", slotType: "Tool", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1401, "torch", slotType: "Tool", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1402, "guitar", slotType: "Tool", meta: WithHealth("100")));

            WearableArrays arrays = WearableInvariants.For(model);

            Assert.Equal(0, arrays.Count);
            Assert.DoesNotContain(1400, arrays.ItemIds);
            Assert.DoesNotContain(1401, arrays.ItemIds);
            Assert.DoesNotContain(1402, arrays.ItemIds);
        }

        [Fact]
        public void A_worn_garment_with_health_is_left_out()
        {
            // A Head/Body/Feet/Face item is a cosmetic - AddCosmetic never routes it
            // through AddUtilityItem - so even carrying a totalHealth meta it is not a
            // UtilityItem and must not appear in 1280.
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1500, "torso_poncho", slotType: "Body", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1501, "head_devhat", slotType: "Head", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1502, "face_scar", slotType: "Face", meta: WithHealth("100")));

            Assert.Equal(0, WearableInvariants.For(model).Count);
        }

        [Fact]
        public void A_worn_deployable_with_valid_health_is_still_left_out()
        {
            // THE PLACE-A-SHIPYARD CRASH. A shipyard is marked equippable /
            // characterSlot="Utility" with meta totalHealth="100" in itemData.json -
            // byte-identical in shape to the glider - so it PASSES the utility-slot test
            // and the health gate. But it is a placed structure with no customisation
            // prefab, so the client's CreateItem returns null and no UtilityItem is
            // built - a KeyNotFoundException per frame. Every placeable is in the
            // Deployables table, so IsDeployable excludes it.
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1300, "shipyard", slotType: "Utility", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1301, "assemblyStation", slotType: "Utility", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1302, "barrel", slotType: "Utility", meta: WithHealth("25")));
            model.Add(InventoryTestData.Item(1303, "campFire", slotType: "Utility", meta: WithHealth("100")));

            WearableArrays arrays = WearableInvariants.For(model);

            Assert.Equal(0, arrays.Count);
        }

        [Fact]
        public void A_genuine_worn_utility_alongside_tools_and_deployables_survives_alone()
        {
            // The glider (a real rig UtilityItem in the Utility slot) worn next to a
            // held tool and a deployable: only the glider may reach 1280. Proves the
            // rule is "rig-registerable utility", not "anything worn with health".
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1101, "glider", slotType: "Utility", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1400, "pistol", slotType: "Tool", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1300, "shipyard", slotType: "Utility", meta: WithHealth("100")));

            WearableArrays arrays = WearableInvariants.For(model);

            Assert.Equal(new[] { 1101 }, arrays.ItemIds);
        }

        [Fact]
        public void Realistic_loadout_yields_only_rig_registerable_utilities()
        {
            // THE INVARIANT: for any inventory + equipment state, every id 1280 marks
            // active is one the client's RegisterWearables would add to
            // _utilityIdToUtility - i.e. an item worn in a utility slot that the rig
            // builds a UtilityItem for (not a held tool, not a garment, not a placed
            // structure). A realistic loadout: glider equipped (Utility), scanner and
            // pistol held (Tool), a deployable sitting in the bag (not worn).
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1101, "glider", slotType: "Utility", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1400, "pistol", slotType: "Tool", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1401, "torch", slotType: "Tool", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1600, "shipyard", meta: WithHealth("100"))); // in the bag, slotType "None"
            model.Add(InventoryTestData.Item(1700, "iron"));                              // a plain material

            WearableArrays arrays = WearableInvariants.For(model);

            // Only the glider - the sole rig-registerable worn utility.
            Assert.Equal(new[] { 1101 }, arrays.ItemIds);

            // The hard invariant, stated directly: every active id resolves to a worn
            // item in a registerable utility slot that the client is NOT excluded from
            // building (i.e. not a deployable).
            for (int i = 0; i < arrays.Count; i++)
            {
                InventoryItem backing = model.ById(arrays.ItemIds[i])!;
                Assert.True(backing.IsWorn);
                Assert.Contains(backing.SlotType, new[] { "Utility", "UtilityHead", "UtilityFeet", "UtilityHand" });
                Assert.False(Deployables.IsDeployable(backing.ItemTypeId));
            }
        }

        [Fact]
        public void After_a_consumed_utility_no_active_id_is_absent_from_the_inventory()
        {
            // Every active id 1280 serves must resolve to an item the served 1081
            // inventory still holds worn. Model place-and-consume: a worn glider plus a
            // shipyard that is then placed (removed). After removal the re-derived 1280
            // references only items still present.
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1101, "glider", slotType: "Utility", meta: WithHealth("100")));
            model.Add(InventoryTestData.Item(1300, "shipyard", slotType: "Utility", meta: WithHealth("100")));

            model.Remove(1300);

            WearableArrays arrays = WearableInvariants.For(model);

            for (int i = 0; i < arrays.Count; i++)
            {
                InventoryItem? backing = model.ById(arrays.ItemIds[i]);
                Assert.NotNull(backing);
                Assert.True(backing!.IsWorn);
                Assert.False(Deployables.IsDeployable(backing.ItemTypeId));
            }
            Assert.DoesNotContain(1300, arrays.ItemIds);
            Assert.Contains(1101, arrays.ItemIds);
        }

        [Fact]
        public void Total_health_is_parsed_invariantly()
        {
            // A server running under a comma-decimal locale must not read
            // "100.5" as 1005 or fail to read it at all.
            InventoryItem item = InventoryTestData.Item(1200, "glider", slotType: "Utility", meta: WithHealth("100.5"));

            Assert.Equal(100.5f, WearableInvariants.TotalHealthOf(item));
        }

        [Fact]
        public void Every_id_in_the_arrays_is_worn_in_the_inventory()
        {
            // The invariant the client enforces with an exception: the worn set
            // comes from 1081 and the durability from 1280, and they must agree.
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1101, "glider", slotType: "Utility", meta: WithHealth("100")));
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
            model.Add(InventoryTestData.Item(1101, "glider", slotType: "Utility", meta: WithHealth("100")));

            Assert.All(WearableInvariants.For(model).Active, Assert.True);
        }
    }
}

using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Inventory
{
    public class InventoryPolicyTests
    {
        [Fact]
        public void The_seeded_inventory_is_safe_to_put_on_the_wire()
        {
            IReadOnlyList<string> problems = InventoryPolicy.ValidateForWire(
                InventoryTestData.Seeded(), InventoryTestData.Footprints);

            Assert.Empty(problems);
        }

        [Fact]
        public void SlotType_is_case_sensitive()
        {
            // Enum.Parse with no TryParse and no try/catch, and the throw lands
            // after the panel's lookup table has been cleared: "none" blanks the
            // entire inventory.
            Assert.True(InventoryPolicy.IsLegalSlotType("None"));
            Assert.True(InventoryPolicy.IsLegalSlotType("Head"));
            Assert.False(InventoryPolicy.IsLegalSlotType("none"));
            Assert.False(InventoryPolicy.IsLegalSlotType("Torso"));
            Assert.False(InventoryPolicy.IsLegalSlotType(null));
        }

        [Fact]
        public void A_bad_slot_type_is_reported_rather_than_shipped()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "iron", slotType: "torso"));

            Assert.Contains(
                InventoryPolicy.ValidateForWire(model, InventoryTestData.Footprints),
                p => p.Contains("blanks the whole panel"));
        }

        [Fact]
        public void An_unknown_item_type_is_reported()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "unobtanium"));

            Assert.Contains(
                InventoryPolicy.ValidateForWire(model, InventoryTestData.Footprints),
                p => p.Contains("unknown itemTypeId"));
        }

        [Fact]
        public void Overlapping_items_are_reported()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "iron", 0, 0));
            model.Add(InventoryTestData.Item(1201, "birch", 1, 1));

            Assert.Contains(
                InventoryPolicy.ValidateForWire(model, InventoryTestData.Footprints),
                p => p.Contains("overlaps"));
        }

        [Fact]
        public void An_out_of_bounds_item_is_reported()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "iron", 9, 0));

            Assert.Contains(
                InventoryPolicy.ValidateForWire(model, InventoryTestData.Footprints),
                p => p.Contains("out of bounds"));
        }

        [Fact]
        public void A_worn_item_is_exempt_from_the_grid_rules()
        {
            // Worn items are excluded from the grid entirely and their x/y are
            // ignored, so stale coordinates on a garment must not be an error.
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "iron", 0, 0));
            model.Add(InventoryTestData.Item(1201, "torso_poncho", 0, 0, slotType: "Body"));

            Assert.Empty(InventoryPolicy.ValidateForWire(model, InventoryTestData.Footprints));
        }

        [Fact]
        public void A_hotbar_slot_of_eight_or_more_is_reported()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "iron", hotBarSlot: 8));

            Assert.Contains(
                InventoryPolicy.ValidateForWire(model, InventoryTestData.Footprints),
                p => p.Contains("hotbar slot 8"));
        }

        [Fact]
        public void A_move_into_a_free_cell_is_accepted()
        {
            InventoryModel model = InventoryTestData.Seeded();

            Assert.True(InventoryPolicy.TryMove(model, 1101, 5, 10, false, InventoryTestData.Footprints));

            InventoryItem moved = model.ById(1101)!;
            Assert.Equal(5, moved.X);
            Assert.Equal(10, moved.Y);
        }

        [Fact]
        public void A_move_onto_another_item_is_refused_and_changes_nothing()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "iron", 0, 0));
            model.Add(InventoryTestData.Item(1201, "birch", 5, 5));

            Assert.False(InventoryPolicy.TryMove(model, 1201, 1, 0, false, InventoryTestData.Footprints));

            InventoryItem unmoved = model.ById(1201)!;
            Assert.Equal(5, unmoved.X);
            Assert.Equal(5, unmoved.Y);
        }

        [Fact]
        public void A_move_off_the_edge_is_refused()
        {
            InventoryModel model = InventoryTestData.Seeded();

            Assert.False(InventoryPolicy.TryMove(model, 1101, 8, 0, false, InventoryTestData.Footprints));
        }

        [Fact]
        public void An_item_may_be_moved_onto_the_cells_it_already_occupies()
        {
            // The item must not block itself, which is the bug you get by
            // testing the destination against every rectangle including its own.
            InventoryModel model = InventoryTestData.Seeded();

            // The glider is 3x4 at (0,0) and the only thing that could block a
            // move back to (0,0) is the glider itself.
            Assert.True(InventoryPolicy.TryMove(model, 1101, 0, 0, false, InventoryTestData.Footprints));
        }

        [Fact]
        public void A_rotated_move_uses_the_swapped_footprint()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "iron", 0, 0));

            // iron is 3x2; rotated it is 2x3, so x=8 fits where x=8 unrotated
            // would not.
            Assert.False(InventoryPolicy.TryMove(model, 1200, 8, 0, false, InventoryTestData.Footprints));
            Assert.True(InventoryPolicy.TryMove(model, 1200, 8, 0, true, InventoryTestData.Footprints));
            Assert.True(model.ById(1200)!.Rotated);
        }

        [Fact]
        public void Moving_a_worn_item_into_the_grid_takes_it_off_the_body()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "torso_poncho", 0, 0, slotType: "Body"));

            Assert.True(InventoryPolicy.TryMove(model, 1200, 4, 4, false, InventoryTestData.Footprints));
            Assert.Equal(InventoryItem.NotWorn, model.ById(1200)!.SlotType);
        }

        [Fact]
        public void Hotbar_slots_zero_to_three_are_refused()
        {
            // They are the gauntlet shells; InteractAgentObserver hardcodes them
            // and never reads the inventory for those slots.
            InventoryModel model = InventoryTestData.Seeded();

            for (int slot = 0; slot < 4; slot++)
            {
                Assert.False(InventoryPolicy.TryAssignToHotBar(model, 1101, slot));
            }

            Assert.False(InventoryPolicy.TryRemoveFromHotBar(model, 0));
        }

        [Fact]
        public void Assigning_to_a_hotbar_slot_evicts_the_previous_occupant()
        {
            InventoryModel model = InventoryTestData.Seeded();

            Assert.True(InventoryPolicy.TryAssignToHotBar(model, 1101, 4));
            Assert.True(InventoryPolicy.TryAssignToHotBar(model, 1102, 4));

            Assert.Equal(InventoryItem.NoSlot, model.ById(1101)!.HotBarSlotNum);
            Assert.Equal(4, model.ById(1102)!.HotBarSlotNum);
        }

        [Fact]
        public void A_hotbar_assignment_leaves_the_item_where_it_is_in_the_grid()
        {
            // Hotbar membership is orthogonal to grid position: the item still
            // occupies its cells.
            InventoryModel model = InventoryTestData.Seeded();

            InventoryPolicy.TryAssignToHotBar(model, 1103, 5);

            InventoryItem item = model.ById(1103)!;
            Assert.Equal(3, item.X);
            Assert.Equal(0, item.Y);
        }

        [Fact]
        public void Removing_from_an_empty_hotbar_slot_is_refused()
        {
            Assert.False(InventoryPolicy.TryRemoveFromHotBar(InventoryTestData.Seeded(), 7));
        }

        [Fact]
        public void Equipping_a_second_garment_in_one_slot_unequips_the_first()
        {
            // The old handler wrote a single-element 1280 array by hand, which
            // is why a second wearable replaced the first instead of joining it.
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "torso_poncho", 0, 0));
            model.Add(InventoryTestData.Item(1201, "head_devhat", 4, 0));

            Assert.True(InventoryPolicy.TryEquip(model, 1200, "Body"));
            Assert.True(InventoryPolicy.TryEquip(model, 1201, "Head"));

            Assert.Equal("Body", model.ById(1200)!.SlotType);
            Assert.Equal("Head", model.ById(1201)!.SlotType);
        }

        [Fact]
        public void Equipping_into_an_occupied_slot_displaces_the_occupant()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "head_devhat", 0, 0));
            model.Add(InventoryTestData.Item(1201, "head_devhat", 4, 0));

            InventoryPolicy.TryEquip(model, 1200, "Head");
            InventoryPolicy.TryEquip(model, 1201, "Head");

            Assert.Equal(InventoryItem.NotWorn, model.ById(1200)!.SlotType);
            Assert.Equal("Head", model.ById(1201)!.SlotType);
        }

        [Fact]
        public void Equipping_to_None_is_refused()
        {
            InventoryModel model = InventoryTestData.Seeded();

            Assert.False(InventoryPolicy.TryEquip(model, 1102, InventoryItem.NotWorn));
            Assert.False(InventoryPolicy.TryEquip(model, 1102, "Torso"));
        }

        [Fact]
        public void Unequipping_finds_a_free_cell_when_the_old_one_was_taken()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "torso_poncho", 0, 0, slotType: "Body"));
            model.Add(InventoryTestData.Item(1201, "iron", 0, 0));

            Assert.True(InventoryPolicy.TryUnequip(model, 1200, InventoryTestData.Footprints));

            InventoryItem unequipped = model.ById(1200)!;
            Assert.Equal(InventoryItem.NotWorn, unequipped.SlotType);
            Assert.Empty(InventoryPolicy.ValidateForWire(model, InventoryTestData.Footprints));
        }

        [Fact]
        public void Unequipping_with_no_room_anywhere_is_refused()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "torso_poncho", 0, 0, slotType: "Body"));

            // 9x17 in a 10x18 grid leaves only a one-cell column and a one-cell
            // row, which no 2x2 garment can fit into.
            model.Add(InventoryTestData.Item(1201, "huge", 0, 0));

            Assert.False(InventoryPolicy.TryUnequip(model, 1200, InventoryTestData.Footprints));
            Assert.Equal("Body", model.ById(1200)!.SlotType);
        }

        [Fact]
        public void Unequipping_something_that_is_not_worn_is_refused()
        {
            Assert.False(InventoryPolicy.TryUnequip(InventoryTestData.Seeded(), 1101, InventoryTestData.Footprints));
        }

        [Fact]
        public void A_grant_lands_in_the_first_free_cell_and_is_wire_safe()
        {
            InventoryModel model = InventoryTestData.Seeded();

            InventoryItem? granted = InventoryPolicy.TryGrant(
                model, 1200, "iron", 12, 5, new Dictionary<string, string>(), null, InventoryTestData.Footprints);

            Assert.NotNull(granted);
            Assert.Equal(InventoryItem.NotWorn, granted!.SlotType);
            Assert.Equal(0, granted.TimeToBuild);
            Assert.Equal(InventoryItem.NoSlot, granted.HotBarSlotNum);
            Assert.NotNull(granted.Meta);
            Assert.Empty(InventoryPolicy.ValidateForWire(model, InventoryTestData.Footprints));
        }

        [Fact]
        public void A_grant_of_an_unknown_item_type_is_refused()
        {
            InventoryModel model = InventoryTestData.Seeded();

            Assert.Null(InventoryPolicy.TryGrant(
                model, 1200, "unobtanium", 1, 0, new Dictionary<string, string>(), null, InventoryTestData.Footprints));
        }

        [Fact]
        public void A_grant_that_reuses_an_existing_item_id_is_refused()
        {
            InventoryModel model = InventoryTestData.Seeded();

            Assert.Null(InventoryPolicy.TryGrant(
                model, 1101, "iron", 1, 0, new Dictionary<string, string>(), null, InventoryTestData.Footprints));
        }

        [Fact]
        public void A_grant_into_a_full_inventory_is_refused_rather_than_placed_out_of_bounds()
        {
            InventoryModel model = InventoryTestData.Grid();
            model.Add(InventoryTestData.Item(1200, "huge", 0, 0));

            Assert.Null(InventoryPolicy.TryGrant(
                model, 1202, "iron", 1, 0, new Dictionary<string, string>(), null, InventoryTestData.Footprints));
        }
    }
}

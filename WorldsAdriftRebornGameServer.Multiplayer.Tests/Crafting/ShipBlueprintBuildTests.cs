using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crafting
{
    /// <summary>
    /// The Phase 2 material-loading + craft state machine: add/return/autofill/return-all,
    /// enable/disable, and the craft gate + consume. All pure - no engine, no client.
    ///
    /// The bill under test is <see cref="ShipBlueprintRecipe.TestMakeshiftShip"/>:
    /// row 0 shipFrame needs 3 birch (Q1), row 1 deck01 needs 2 iron (Q1); both mandatory.
    /// </summary>
    public class ShipBlueprintBuildTests
    {
        private const int Frame = 0;   // shipFrame row
        private const int Deck = 1;    // deck01 row
        private const int Slot0 = 0;

        private static ShipBlueprintBuild NewBuild() =>
            new ShipBlueprintBuild("Makeshift Ship", ShipBlueprintRecipe.TestMakeshiftShip());

        private static InventoryItem Material(int itemId, string type, int amount, int quality, int x = 0, int y = 0)
        {
            return new InventoryItem(
                itemId, type, amount, InventoryItem.NotWorn, InventoryItem.NoSlot,
                x, y, false, InventoryItem.NoSlot, 0, quality, false,
                new Dictionary<string, string>(), null);
        }

        // ---- MaterialMatch -------------------------------------------------

        [Fact]
        public void Match_accepts_exact_type_and_sufficient_quality()
        {
            MaterialRequirement req = new MaterialRequirement("birch", "Wood", quality: 1, amount: 3);
            Assert.True(MaterialMatch.Matches(req, "birch", 1));
            Assert.True(MaterialMatch.Matches(req, "birch", 2));   // higher quality is fine
            Assert.True(MaterialMatch.Matches(req, "BIRCH", 1));   // type is case-insensitive
        }

        [Fact]
        public void Match_rejects_wrong_type_or_too_low_quality()
        {
            MaterialRequirement req = new MaterialRequirement("birch", "Wood", quality: 2, amount: 3);
            Assert.False(MaterialMatch.Matches(req, "iron", 5));   // wrong type
            Assert.False(MaterialMatch.Matches(req, "birch", 1));  // quality below the floor
        }

        // ---- AddItem -------------------------------------------------------

        [Fact]
        public void Add_reserves_the_item_out_of_inventory_and_fills_the_slot()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(5000, "birch", amount: 3, quality: 1));

            AddItemOutcome outcome = ShipBlueprintTransaction.AddItem(build, inv, Frame, Slot0, 5000);

            Assert.Equal(AddItemOutcome.Added, outcome);
            Assert.Null(inv.ById(5000));                                  // gone from inventory
            Assert.Equal(3, build.SlotAt(Frame, Slot0)!.EquivalentAmount); // now in the slot
            Assert.True(build.SlotAt(Frame, Slot0)!.IsSatisfied);
        }

        [Fact]
        public void Add_rejects_a_mismatched_material_and_leaves_inventory_intact()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(5001, "iron", amount: 5, quality: 1));       // iron into the birch frame slot

            AddItemOutcome outcome = ShipBlueprintTransaction.AddItem(build, inv, Frame, Slot0, 5001);

            Assert.Equal(AddItemOutcome.Mismatch, outcome);
            Assert.NotNull(inv.ById(5001));                              // untouched
            Assert.Equal(0, build.SlotAt(Frame, Slot0)!.EquivalentAmount);
        }

        [Fact]
        public void Add_rejects_an_item_the_player_does_not_have()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();

            Assert.Equal(AddItemOutcome.ItemNotFound,
                ShipBlueprintTransaction.AddItem(build, inv, Frame, Slot0, 9999));
        }

        [Fact]
        public void Add_rejects_a_slot_that_does_not_exist()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(5002, "birch", amount: 3, quality: 1));

            Assert.Equal(AddItemOutcome.NoSuchSlot,
                ShipBlueprintTransaction.AddItem(build, inv, schematicSlotIndex: 7, materialSlotIndex: 0, itemId: 5002));
            Assert.NotNull(inv.ById(5002));
        }

        [Fact]
        public void Add_rejects_once_the_slot_is_satisfied()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(5003, "birch", amount: 3, quality: 1, x: 0, y: 0));
            inv.Add(Material(5004, "birch", amount: 3, quality: 1, x: 3, y: 0));

            Assert.Equal(AddItemOutcome.Added,
                ShipBlueprintTransaction.AddItem(build, inv, Frame, Slot0, 5003));
            // Slot already reads 3/3; a second birch is refused.
            Assert.Equal(AddItemOutcome.SlotFull,
                ShipBlueprintTransaction.AddItem(build, inv, Frame, Slot0, 5004));
            Assert.NotNull(inv.ById(5004));
        }

        // ---- ReturnItem ----------------------------------------------------

        [Fact]
        public void Return_puts_the_exact_item_back_and_empties_the_slot()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(5100, "birch", amount: 3, quality: 1));
            ShipBlueprintTransaction.AddItem(build, inv, Frame, Slot0, 5100);

            ReturnItemOutcome outcome = ShipBlueprintTransaction.ReturnItem(build, inv, Frame, Slot0);

            Assert.Equal(ReturnItemOutcome.Returned, outcome);
            Assert.NotNull(inv.ById(5100));                               // back in inventory
            Assert.Equal(3, inv.ById(5100)!.Amount);
            Assert.Equal(0, build.SlotAt(Frame, Slot0)!.EquivalentAmount); // slot empty
        }

        [Fact]
        public void Return_on_an_empty_slot_reports_nothing_to_return()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();

            Assert.Equal(ReturnItemOutcome.NothingToReturn,
                ShipBlueprintTransaction.ReturnItem(build, inv, Frame, Slot0));
        }

        // ---- AutoFill / ReturnAll -----------------------------------------

        [Fact]
        public void Autofill_fills_every_enabled_row_from_inventory()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(5200, "birch", amount: 3, quality: 1, x: 0, y: 0));
            inv.Add(Material(5201, "iron", amount: 2, quality: 1, x: 3, y: 0));

            int loaded = ShipBlueprintTransaction.AutoFill(build, inv);

            Assert.Equal(2, loaded);
            Assert.True(build.SlotAt(Frame, Slot0)!.IsSatisfied);
            Assert.True(build.SlotAt(Deck, Slot0)!.IsSatisfied);
            Assert.Empty(inv.Items);                                      // both consumed from inventory
            Assert.True(build.AllEnabledRowsFilled());
        }

        [Fact]
        public void Autofill_pulls_multiple_stacks_until_a_slot_is_satisfied()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            // Frame needs 3 birch; only 1-unit stacks are available.
            inv.Add(Material(5300, "birch", amount: 1, quality: 1, x: 0, y: 0));
            inv.Add(Material(5301, "birch", amount: 1, quality: 1, x: 2, y: 0));
            inv.Add(Material(5302, "birch", amount: 1, quality: 1, x: 4, y: 0));
            inv.Add(Material(5303, "birch", amount: 1, quality: 1, x: 6, y: 0)); // spare

            ShipBlueprintTransaction.AutoFill(build, inv);

            Assert.True(build.SlotAt(Frame, Slot0)!.IsSatisfied);        // 3 pulled
            Assert.Equal(3, build.SlotAt(Frame, Slot0)!.EquivalentAmount);
            Assert.Single(inv.Items);                                    // the 4th birch stays
        }

        [Fact]
        public void Autofill_never_touches_worn_or_stashed_items()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            // A worn "birch" (contrived) and a stashed one must both be ignored.
            inv.Add(new InventoryItem(5400, "birch", 3, "Chest", InventoryItem.NoSlot,
                0, 0, false, InventoryItem.NoSlot, 0, 1, false, new Dictionary<string, string>(), null));
            inv.Add(new InventoryItem(5401, "birch", 3, InventoryItem.NotWorn, InventoryItem.NoSlot,
                3, 0, false, InventoryItem.NoSlot, 0, 1, true /*lockbox*/, new Dictionary<string, string>(), null));

            int loaded = ShipBlueprintTransaction.AutoFill(build, inv);

            Assert.Equal(0, loaded);
            Assert.False(build.SlotAt(Frame, Slot0)!.IsSatisfied);
            Assert.Equal(2, inv.Items.Count);                            // both untouched
        }

        [Fact]
        public void ReturnAll_empties_every_slot_back_into_inventory()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(5500, "birch", amount: 3, quality: 1, x: 0, y: 0));
            inv.Add(Material(5501, "iron", amount: 2, quality: 1, x: 3, y: 0));
            ShipBlueprintTransaction.AutoFill(build, inv);
            Assert.Empty(inv.Items);

            int returned = ShipBlueprintTransaction.ReturnAll(build, inv);

            Assert.Equal(2, returned);
            Assert.Equal(2, inv.Items.Count);
            Assert.Equal(0, build.SlotAt(Frame, Slot0)!.EquivalentAmount);
            Assert.Equal(0, build.SlotAt(Deck, Slot0)!.EquivalentAmount);
        }

        // ---- SetEnabled ----------------------------------------------------

        [Fact]
        public void Mandatory_rows_cannot_be_disabled()
        {
            ShipBlueprintBuild build = NewBuild();
            Assert.True(build.RowAt(Frame)!.IsMandatory);
            Assert.False(build.RowAt(Frame)!.SetEnabled(false));         // refused
            Assert.True(build.RowAt(Frame)!.IsEnabled);                  // still on
        }

        [Fact]
        public void Optional_rows_toggle()
        {
            // A blueprint with one optional row proves the enable path works.
            SchematicRow optional = new SchematicRow("mast01", nodeCount: 1, isEnabled: true,
                craftingTime: 5, materials: new[]
                {
                    new MaterialRequirement("cloth", "Fabric", quality: 1, amount: 1),
                });
            ShipBlueprintRecipe recipe = new ShipBlueprintRecipe(craftingTime: 10, rows: new[] { optional });
            ShipBlueprintBuild build = new ShipBlueprintBuild("Rigged Ship", recipe);

            Assert.False(build.RowAt(0)!.IsMandatory);
            Assert.True(build.RowAt(0)!.SetEnabled(false));
            Assert.False(build.RowAt(0)!.IsEnabled);
            Assert.True(build.RowAt(0)!.SetEnabled(true));
            Assert.True(build.RowAt(0)!.IsEnabled);
        }

        // ---- StartCraft gate + consume ------------------------------------

        [Fact]
        public void Craft_is_blocked_when_a_row_is_not_filled()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(5600, "birch", amount: 3, quality: 1));     // only the frame
            ShipBlueprintTransaction.AddItem(build, inv, Frame, Slot0, 5600);

            Assert.Equal(StartCraftOutcome.MissingMaterials, ShipBlueprintTransaction.StartCraft(build));
            Assert.False(build.IsCrafting);
        }

        [Fact]
        public void Craft_starts_when_every_enabled_row_is_filled()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(5700, "birch", amount: 3, quality: 1, x: 0, y: 0));
            inv.Add(Material(5701, "iron", amount: 2, quality: 1, x: 3, y: 0));
            ShipBlueprintTransaction.AutoFill(build, inv);

            Assert.Equal(StartCraftOutcome.Started, ShipBlueprintTransaction.StartCraft(build));
            Assert.True(build.IsCrafting);
        }

        [Fact]
        public void Crafting_consumes_the_materials_for_real_no_return()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(5800, "birch", amount: 3, quality: 1, x: 0, y: 0));
            inv.Add(Material(5801, "iron", amount: 2, quality: 1, x: 3, y: 0));
            ShipBlueprintTransaction.AutoFill(build, inv);
            ShipBlueprintTransaction.StartCraft(build);

            // Reserved items are already out of inventory, and now they are non-returnable.
            Assert.Empty(inv.Items);
            Assert.Equal(0, ShipBlueprintTransaction.ReturnAll(build, inv));
            Assert.Equal(ReturnItemOutcome.WhileCrafting,
                ShipBlueprintTransaction.ReturnItem(build, inv, Frame, Slot0));
            Assert.Empty(inv.Items);

            // Completion clears the loaded materials.
            List<InventoryItem> cleared = build.DrainAllLoaded();
            Assert.Equal(2, cleared.Count);
        }

        [Fact]
        public void Craft_is_refused_when_the_shipyard_already_has_a_docked_ship()
        {
            // ONE SHIP PER SHIPYARD: even a fully-filled, buildable blueprint is refused
            // when the target yard already holds a built/docked ship, and nothing is
            // consumed (IsCrafting stays false so the materials remain returnable).
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(6000, "birch", amount: 3, quality: 1, x: 0, y: 0));
            inv.Add(Material(6001, "iron", amount: 2, quality: 1, x: 3, y: 0));
            ShipBlueprintTransaction.AutoFill(build, inv);

            Assert.Equal(StartCraftOutcome.ShipyardOccupied,
                ShipBlueprintTransaction.StartCraft(build, shipyardOccupied: true));
            Assert.False(build.IsCrafting);

            // Once the yard is freed, the very same build starts.
            Assert.Equal(StartCraftOutcome.Started,
                ShipBlueprintTransaction.StartCraft(build, shipyardOccupied: false));
            Assert.True(build.IsCrafting);
        }

        [Fact]
        public void Occupied_yard_is_checked_before_materials_so_a_bad_build_is_not_the_reason_reported()
        {
            // An occupied yard trumps even a missing-materials build: the player is told
            // the yard is full, not that they are short materials.
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(6100, "birch", amount: 3, quality: 1));     // only the frame
            ShipBlueprintTransaction.AddItem(build, inv, Frame, Slot0, 6100);

            Assert.Equal(StartCraftOutcome.ShipyardOccupied,
                ShipBlueprintTransaction.StartCraft(build, shipyardOccupied: true));
            Assert.False(build.IsCrafting);
        }

        // ---- Overfill consume (material-loss fix F1) ----------------------

        [Fact]
        public void Craft_completion_consumes_only_the_requirement_and_returns_the_overfill()
        {
            // REPRO of the material-loss P1: a 30-stack dropped into a slot needing 3.
            // FAIL-BEFORE: completion did build.DrainAllLoaded() and discarded ALL 30,
            // losing 27. PASS-AFTER: ConsumeForCraftReturningOverfill consumes 3, returns 27.
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(7000, "birch", amount: 30, quality: 1, x: 0, y: 0)); // frame needs 3
            inv.Add(Material(7001, "iron", amount: 2, quality: 1, x: 3, y: 0));   // deck needs 2 exactly
            ShipBlueprintTransaction.AddItem(build, inv, Frame, Slot0, 7000);
            ShipBlueprintTransaction.AddItem(build, inv, Deck, Slot0, 7001);
            ShipBlueprintTransaction.StartCraft(build);

            List<InventoryItem> overfill = build.ConsumeForCraftReturningOverfill();

            // Only the birch overfill comes back: 30 loaded - 3 required = 27, same id.
            Assert.Single(overfill);
            Assert.Equal(7000, overfill[0].ItemId);
            Assert.Equal(27, overfill[0].Amount);
            Assert.Equal(0, overfill[0].X);   // returns to the exact grid cell it left
            Assert.Equal(0, overfill[0].Y);
            // Slots are emptied; the exact requirement (3 birch + 2 iron) was consumed.
            Assert.Equal(0, build.SlotAt(Frame, Slot0)!.EquivalentAmount);
            Assert.Equal(0, build.SlotAt(Deck, Slot0)!.EquivalentAmount);
        }

        [Fact]
        public void Craft_completion_of_an_exactly_filled_build_returns_nothing()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(7100, "birch", amount: 3, quality: 1, x: 0, y: 0));
            inv.Add(Material(7101, "iron", amount: 2, quality: 1, x: 3, y: 0));
            ShipBlueprintTransaction.AutoFill(build, inv);
            ShipBlueprintTransaction.StartCraft(build);

            List<InventoryItem> overfill = build.ConsumeForCraftReturningOverfill();

            Assert.Empty(overfill);   // nothing over the requirement, nothing lost
        }

        [Fact]
        public void Craft_completion_returns_a_whole_extra_stack_kept_beyond_the_requirement()
        {
            // Frame needs 3 birch; two 2-stacks are loaded (4 total). Consume 3 (all of the
            // first, 1 of the second), return 1 of the second - the straddling item split.
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(7200, "birch", amount: 2, quality: 1, x: 0, y: 0));
            inv.Add(Material(7201, "birch", amount: 2, quality: 1, x: 2, y: 0));
            inv.Add(Material(7202, "iron", amount: 2, quality: 1, x: 4, y: 0));
            ShipBlueprintTransaction.AddItem(build, inv, Frame, Slot0, 7200);
            ShipBlueprintTransaction.AddItem(build, inv, Frame, Slot0, 7201);
            ShipBlueprintTransaction.AddItem(build, inv, Deck, Slot0, 7202);
            ShipBlueprintTransaction.StartCraft(build);

            List<InventoryItem> overfill = build.ConsumeForCraftReturningOverfill();

            Assert.Single(overfill);
            Assert.Equal(7201, overfill[0].ItemId);   // the straddling stack's remainder
            Assert.Equal(1, overfill[0].Amount);
        }

        [Fact]
        public void Slot_consume_up_to_zero_returns_everything_untouched()
        {
            // The disabled-row case: consume 0 -> the whole load is returned intact.
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(7300, "birch", amount: 5, quality: 1, x: 0, y: 0));
            ShipBlueprintTransaction.AddItem(build, inv, Frame, Slot0, 7300);

            List<InventoryItem> returned = build.SlotAt(Frame, Slot0)!.ConsumeUpTo(0);

            Assert.Single(returned);
            Assert.Equal(5, returned[0].Amount);
            Assert.Equal(0, build.SlotAt(Frame, Slot0)!.EquivalentAmount);
        }

        [Fact]
        public void Teardown_returns_the_full_overfilled_stack_with_no_loss()
        {
            // The F2/F3 seam at the pure level: ReturnAll (what DrainBuildBackToInventory
            // and the disconnect teardown call) gives back the WHOLE reserved stack,
            // overfill included - a build torn down before craft loses nothing.
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(7400, "birch", amount: 30, quality: 1, x: 0, y: 0));
            ShipBlueprintTransaction.AddItem(build, inv, Frame, Slot0, 7400);
            Assert.Null(inv.ById(7400));   // reserved out of the grid

            int returned = ShipBlueprintTransaction.ReturnAll(build, inv);

            Assert.Equal(1, returned);
            Assert.NotNull(inv.ById(7400));
            Assert.Equal(30, inv.ById(7400)!.Amount);   // all 30 back, none lost
        }

        [Fact]
        public void No_material_transaction_is_accepted_while_crafting()
        {
            ShipBlueprintBuild build = NewBuild();
            InventoryModel inv = InventoryModel.DefaultGrid();
            inv.Add(Material(5900, "birch", amount: 3, quality: 1, x: 0, y: 0));
            inv.Add(Material(5901, "iron", amount: 2, quality: 1, x: 3, y: 0));
            ShipBlueprintTransaction.AutoFill(build, inv);
            ShipBlueprintTransaction.StartCraft(build);

            inv.Add(Material(5902, "birch", amount: 3, quality: 1, x: 6, y: 0));
            Assert.Equal(AddItemOutcome.WhileCrafting,
                ShipBlueprintTransaction.AddItem(build, inv, Frame, Slot0, 5902));
            Assert.Equal(0, ShipBlueprintTransaction.AutoFill(build, inv));
            Assert.Equal(StartCraftOutcome.AlreadyCrafting, ShipBlueprintTransaction.StartCraft(build));
        }
    }
}

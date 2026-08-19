using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Inventory
{
    /// <summary>
    /// THE BELT SEPARATOR ROW, from every direction an item can arrive at it.
    ///
    /// This file exists because the belt was, until now, a number the server
    /// carried and never used. The client turns <c>beltRow</c> into a full-width
    /// row of blocker cells and refuses every drag that touches it
    /// (<c>InventorySpaceChecker.AddBlockerRows</c> / <c>IsItemBlocked</c>); the
    /// server placed items across that row all day and nothing anywhere said a
    /// word. The result was three symptoms at once - a gap you could drop into,
    /// a drag ghost that stopped following the mouse, and a refused placement -
    /// which read as a rendering fault.
    ///
    /// So every one of these tests is written to FAIL if the blocked row stops
    /// being honoured, not merely to describe what the code now does.
    /// </summary>
    public class InventoryBeltTests
    {
        // ---- where the divider is -------------------------------------------------

        [Fact]
        public void The_separator_sits_one_row_above_the_bottom_three()
        {
            // Retail carved the belt out of the existing grid: "the lower three
            // (four if you count the spacer) grid rows were converted into the
            // new belt area". So for an 18-tall grid the belt is rows 15-17 and
            // the divider is row 14.
            Assert.Equal(14, InventoryModel.SeparatorRowFor(18));
            Assert.Equal(6, InventoryModel.SeparatorRowFor(10));
            Assert.Equal(3, InventoryModel.BeltRows);
        }

        [Fact]
        public void The_separator_is_always_inside_the_grid()
        {
            // beltRow >= height throws inside InventorySpaceChecker's
            // constructor, which runs inside InventoryVisualiser.OnEnable - so a
            // grid too short for a belt would abort the checkout that draws it
            // rather than merely look wrong.
            for (int height = 1; height <= 20; height++)
            {
                int row = InventoryModel.SeparatorRowFor(height);

                Assert.InRange(row, 0, height - 1);
            }
        }

        [Fact]
        public void A_belted_grid_is_given_the_separator_row_whatever_the_caller_asked_for()
        {
            // THE ORIGINAL BUG: 3 was written where the client wanted an index,
            // because the belt is three rows tall. A count is not an index.
            InventoryModel model = new InventoryModel(10, 18, true, 3);

            Assert.Equal(14, model.BeltRow);
            Assert.Equal(14, model.BlockedRow);
        }

        [Fact]
        public void The_stock_player_grid_blocks_row_fourteen()
        {
            InventoryModel model = InventoryModel.DefaultGrid();

            Assert.Equal(10, model.Width);
            Assert.Equal(18, model.Height);
            Assert.True(model.HasBelt);
            Assert.Equal(14, model.BlockedRow);
        }

        [Fact]
        public void A_grid_with_no_belt_blocks_nothing()
        {
            // A chest must not inherit a divider from the player's grid; the
            // client never blocks a row when hasBelt is false.
            InventoryModel chest = new InventoryModel(8, 6, false, 3);

            Assert.Equal(InventoryGeometry.NoBlockedRow, chest.BlockedRow);
            Assert.False(InventoryGeometry.CrossesBlockedRow(2, 4, chest.BlockedRow));
        }

        [Fact]
        public void A_copy_keeps_the_same_divider()
        {
            InventoryModel copy = InventoryModel.DefaultGrid().Copy();

            Assert.Equal(14, copy.BlockedRow);
        }

        // ---- the predicate --------------------------------------------------------

        [Theory]
        [InlineData(12, 2, false)] // rows 12-13, clear above the divider
        [InlineData(13, 2, true)]  // rows 13-14, its bottom edge lands on it
        [InlineData(14, 1, true)]  // exactly on it
        [InlineData(14, 4, true)]  // starts on it and runs into the belt
        [InlineData(15, 2, false)] // rows 15-16, on the belt itself, which is fine
        [InlineData(0, 18, true)]  // the whole column
        public void CrossesBlockedRow_answers_for_a_rectangle_not_just_its_origin(int y, int h, bool expected)
        {
            Assert.Equal(expected, InventoryGeometry.CrossesBlockedRow(y, h, 14));
        }

        [Fact]
        public void A_zero_height_item_never_touches_the_divider()
        {
            // The four gauntlet shells are 0x0 at (-1,-1) and must stay legal.
            Assert.False(InventoryGeometry.CrossesBlockedRow(14, 0, 14));
            Assert.False(InventoryGeometry.CrossesBlockedRow(-1, 0, 14));
        }

        // ---- geometry -------------------------------------------------------------

        [Fact]
        public void Fits_refuses_an_empty_cell_that_is_on_the_divider()
        {
            // Nothing is in the way. The refusal is the belt and only the belt.
            Assert.False(InventoryGeometry.Fits(0, 14, 3, 2, 10, 18, Array.Empty<GridRect>(), 14));
            Assert.False(InventoryGeometry.Fits(0, 13, 3, 2, 10, 18, Array.Empty<GridRect>(), 14));
            Assert.True(InventoryGeometry.Fits(0, 12, 3, 2, 10, 18, Array.Empty<GridRect>(), 14));
            Assert.True(InventoryGeometry.Fits(0, 15, 3, 2, 10, 18, Array.Empty<GridRect>(), 14));
        }

        [Fact]
        public void FirstFree_steps_over_the_divider_rather_than_stopping_at_it()
        {
            // Rows 0-13 taken. The next free 3x2 must be on the belt at row 15,
            // NOT at row 14 - and the search must not give up either.
            GridRect[] occupied = { new GridRect(1, 0, 0, 10, 14) };

            Assert.Equal((0, 15), InventoryGeometry.FirstFree(3, 2, 10, 18, occupied, 14));
        }

        [Fact]
        public void FirstFree_fills_a_whole_grid_without_ever_landing_on_the_divider()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            int id = 100;

            while (InventoryPolicy.TryGrant(model, id, "iron", 1, 0,
                       new Dictionary<string, string>(), null, InventoryTestData.Footprints) != null)
            {
                id++;
            }

            Assert.NotEmpty(model.Items);

            foreach (InventoryItem item in model.Items)
            {
                Assert.False(InventoryGeometry.CrossesBlockedRow(item.Y, 2, model.BlockedRow),
                    "a grant landed on the belt separator at y=" + item.Y);
            }
        }

        // ---- the mutation paths ---------------------------------------------------

        [Fact]
        public void TryMove_refuses_the_divider_and_leaves_the_item_where_it_was()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(InventoryTestData.Item(200, "iron", 0, 0));

            Assert.False(InventoryPolicy.TryMove(model, 200, 0, 14, false, InventoryTestData.Footprints));
            Assert.False(InventoryPolicy.TryMove(model, 200, 0, 13, false, InventoryTestData.Footprints));

            InventoryItem? after = model.ById(200);
            Assert.NotNull(after);
            Assert.Equal(0, after!.X);
            Assert.Equal(0, after.Y);
        }

        [Fact]
        public void TryMove_still_allows_the_belt_itself()
        {
            // The belt is storage, not a wall. Refusing it would take three rows
            // off every player.
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(InventoryTestData.Item(200, "iron", 0, 0));

            Assert.True(InventoryPolicy.TryMove(model, 200, 0, 16, false, InventoryTestData.Footprints));
            Assert.Equal(16, model.ById(200)!.Y);
        }

        [Fact]
        public void TryMove_refuses_a_rotated_item_that_would_reach_the_divider()
        {
            // Rotation changes the height, so the divider check has to run on the
            // oriented rectangle and not the catalogue one.
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(InventoryTestData.Item(200, "iron", 0, 0));

            // iron is 3x2; rotated it is 2x3, so y=12 covers rows 12-14.
            Assert.False(InventoryPolicy.TryMove(model, 200, 0, 12, true, InventoryTestData.Footprints));
            Assert.True(InventoryPolicy.TryMove(model, 200, 0, 11, true, InventoryTestData.Footprints));
        }

        [Fact]
        public void TryUnequip_does_not_park_a_garment_on_the_divider()
        {
            InventoryModel model = InventoryModel.DefaultGrid();

            // Worn, and its stale coordinates point straight at the divider.
            model.Add(InventoryTestData.Item(300, "torso_poncho", 0, 14, slotType: "Body"));

            Assert.True(InventoryPolicy.TryUnequip(model, 300, InventoryTestData.Footprints));

            InventoryItem? placed = model.ById(300);
            Assert.NotNull(placed);
            Assert.False(InventoryGeometry.CrossesBlockedRow(placed!.Y, 2, model.BlockedRow));
        }

        [Fact]
        public void ValidateForWire_reports_an_item_sitting_on_the_divider()
        {
            // This is the state a pre-fix database row restores into, and it is
            // the state that punches a hole in the client's divider.
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(InventoryTestData.Item(400, "iron", 0, 13));

            IReadOnlyList<string> problems =
                InventoryPolicy.ValidateForWire(model, InventoryTestData.Footprints);

            Assert.Contains(problems, p => p.Contains("belt separator row"));
        }

        [Fact]
        public void ValidateForWire_is_quiet_about_an_item_on_the_belt()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(InventoryTestData.Item(400, "iron", 0, 15));

            Assert.Empty(InventoryPolicy.ValidateForWire(model, InventoryTestData.Footprints));
        }

        [Fact]
        public void The_seeded_starter_kit_clears_the_divider()
        {
            // The glider is 3x4 at (0,0) and spans rows 0-3. Under the old
            // beltRow of 3 it overwrote the blockers under it on every refresh -
            // the divider leaked from the very first login.
            InventoryModel model = InventoryTestData.Seeded();

            Assert.Empty(InventoryPolicy.ValidateForWire(model, InventoryTestData.Footprints));
        }

        // ---- restore --------------------------------------------------------------

        [Fact]
        public void A_stored_payload_written_before_the_fix_restores_with_the_divider_corrected()
        {
            // Every character saved so far carries BeltRow 3. Nothing else can
            // correct it: the client reads the grid exactly once at checkout, so
            // a stale 3 would outlive the fix for as long as the row survives.
            string legacy = "{\"Version\":1,\"Width\":10,\"Height\":18,\"HasBelt\":true,\"BeltRow\":3,\"Items\":[]}";

            InventoryModel? restored = InventorySnapshot.Read(legacy);

            Assert.NotNull(restored);
            Assert.Equal(14, restored!.BeltRow);
        }

        [Fact]
        public void A_restore_keeps_items_that_the_old_divider_let_them_place()
        {
            // Correcting the geometry must not cost anybody an item. The row
            // moves; what is already in the grid stays exactly where it is, and
            // ValidateForWire is what tells us about it.
            InventoryModel before = InventoryModel.DefaultGrid();
            before.Add(InventoryTestData.Item(500, "iron", 0, 2));
            before.Add(InventoryTestData.Item(501, "iron", 0, 15));

            InventoryModel? after = InventorySnapshot.Read(InventorySnapshot.Write(before));

            Assert.NotNull(after);
            Assert.Equal(2, after!.Items.Count);
            Assert.Equal(2, after.ById(500)!.Y);
            Assert.Equal(15, after.ById(501)!.Y);
        }

        // ---- cross-inventory ------------------------------------------------------

        [Fact]
        public void A_cross_inventory_drop_onto_the_divider_is_refused()
        {
            InventoryModel chest = new InventoryModel(10, 6, false, 0);
            InventoryModel player = InventoryModel.DefaultGrid();
            chest.Add(InventoryTestData.Item(700, "iron", 0, 0));

            int next = 900;

            Assert.Equal(
                CrossMoveOutcome.NoRoom,
                CrossInventoryPolicy.TryMove(chest, player, 700, () => next++, 0, 14, false,
                    InventoryTestData.Footprints));

            // Refused, so nothing left the chest.
            Assert.NotNull(chest.ById(700));
            Assert.Empty(player.Items);
        }

        [Fact]
        public void MoveAll_into_a_belted_grid_never_uses_the_divider()
        {
            InventoryModel chest = new InventoryModel(10, 6, false, 0);
            InventoryModel player = InventoryModel.DefaultGrid();

            // Fill everything above the divider so MoveAll has to reach past it:
            // the first cell it meets going down is the divider itself.
            player.Add(InventoryTestData.Item(600, "backpackFiller", 0, 0));

            for (int i = 0; i < 3; i++)
            {
                chest.Add(InventoryTestData.Item(700 + i, "iron", 3 * i, 0));
            }

            int next = 900;
            int moved = CrossInventoryPolicy.MoveAll(chest, player, () => next++, InventoryTestData.Footprints);

            Assert.True(moved > 0);

            foreach (InventoryItem item in player.Items)
            {
                if (item.ItemTypeId != "iron") continue;

                Assert.False(InventoryGeometry.CrossesBlockedRow(item.Y, 2, player.BlockedRow),
                    "MoveAll parked an item on the belt separator at y=" + item.Y);
            }
        }
    }
}

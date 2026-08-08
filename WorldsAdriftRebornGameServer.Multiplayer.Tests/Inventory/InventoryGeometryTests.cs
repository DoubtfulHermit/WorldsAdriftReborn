using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Inventory
{
    /// <summary>
    /// The grid rules. Each of these corresponds to a client failure that is
    /// either silent or unattributable, which is why they are asserted here and
    /// not left to be noticed in game.
    /// </summary>
    public class InventoryGeometryTests
    {
        [Fact]
        public void A_rectangle_inside_the_grid_is_in_bounds()
        {
            Assert.True(InventoryGeometry.InBounds(0, 0, 3, 4, 10, 18));
            Assert.True(InventoryGeometry.InBounds(7, 14, 3, 4, 10, 18));
        }

        [Fact]
        public void A_rectangle_that_runs_off_the_edge_is_out_of_bounds()
        {
            // The client throws IndexOutOfRangeException and aborts the panel
            // refresh half-drawn, so this is the difference between "the item is
            // in the wrong place" and "the inventory is broken".
            Assert.False(InventoryGeometry.InBounds(8, 14, 3, 4, 10, 18));
            Assert.False(InventoryGeometry.InBounds(7, 15, 3, 4, 10, 18));
            Assert.False(InventoryGeometry.InBounds(-1, 0, 3, 4, 10, 18));
            Assert.False(InventoryGeometry.InBounds(0, -1, 3, 4, 10, 18));
        }

        [Fact]
        public void The_unplaced_sentinel_is_legal_only_for_a_zero_area_item()
        {
            // (-1,-1) is where the four gauntlet shells sit. A non-zero item
            // there throws on the client.
            Assert.True(InventoryGeometry.InBounds(-1, -1, 0, 0, 10, 18));
            Assert.False(InventoryGeometry.InBounds(-1, -1, 2, 2, 10, 18));
        }

        [Fact]
        public void Touching_rectangles_do_not_overlap()
        {
            Assert.False(InventoryGeometry.Overlaps(0, 0, 2, 2, 2, 0, 2, 2));
            Assert.False(InventoryGeometry.Overlaps(0, 0, 2, 2, 0, 2, 2, 2));
        }

        [Fact]
        public void Rectangles_sharing_one_cell_overlap()
        {
            // The client renders this without complaint, one icon on top of the
            // other, so nothing but the server will ever catch it.
            Assert.True(InventoryGeometry.Overlaps(0, 0, 2, 2, 1, 1, 2, 2));
        }

        [Fact]
        public void A_zero_area_item_never_blocks_anything()
        {
            Assert.False(InventoryGeometry.Overlaps(0, 0, 0, 0, 0, 0, 2, 2));
        }

        [Fact]
        public void Fits_refuses_a_cell_that_is_already_taken()
        {
            GridRect[] occupied = { new GridRect(1, 0, 0, 3, 4) };

            Assert.False(InventoryGeometry.Fits(1, 1, 2, 2, 10, 18, occupied));
            Assert.True(InventoryGeometry.Fits(3, 0, 2, 2, 10, 18, occupied));
        }

        [Fact]
        public void FirstFree_scans_rows_before_columns()
        {
            GridRect[] occupied = { new GridRect(1, 0, 0, 3, 4) };

            Assert.Equal((3, 0), InventoryGeometry.FirstFree(2, 2, 10, 18, occupied));
        }

        [Fact]
        public void FirstFree_returns_null_when_nothing_fits()
        {
            GridRect[] occupied = { new GridRect(1, 0, 0, 10, 18) };

            Assert.Null(InventoryGeometry.FirstFree(1, 1, 10, 18, occupied));
        }

        [Fact]
        public void FirstFree_places_a_zero_area_item_nowhere()
        {
            Assert.Equal((InventoryGeometry.Unplaced, InventoryGeometry.Unplaced),
                InventoryGeometry.FirstFree(0, 0, 10, 18, Array.Empty<GridRect>()));
        }
    }
}

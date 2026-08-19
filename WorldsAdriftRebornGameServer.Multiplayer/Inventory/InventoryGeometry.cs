namespace WorldsAdriftRebornGameServer.Multiplayer.Inventory
{
    /// <summary>
    /// Where a rectangle may sit in the grid. Nothing here knows what an item
    /// is; it takes occupied rectangles and answers questions about space.
    ///
    /// This exists as its own module because the client does ZERO validation of
    /// server-supplied coordinates and fails in three different unhelpful ways:
    /// out of bounds throws IndexOutOfRangeException and aborts the refresh
    /// half-drawn, overlapping renders overlapping with no error at all, and an
    /// item on the belt row silently stops that column's belt blocking. The
    /// server is the only thing that can refuse a bad placement, so the refusal
    /// had better be tested.
    ///
    /// Origin is top-left and y grows DOWNWARD, which is the client's
    /// convention; getting that backwards puts everything in the wrong half of
    /// the panel and looks like a rendering bug.
    /// </summary>
    public static class InventoryGeometry
    {
        /// <summary>
        /// (-1,-1) is the sentinel the four gauntlet shells sit at. It is safe
        /// ONLY for a 0x0 item; a non-zero item there throws on the client.
        /// </summary>
        public const int Unplaced = -1;

        /// <summary>
        /// Passed as <c>blockedRow</c> by a grid that has no belt - a chest, a
        /// ship trunk - and so has no row reserved by the client.
        /// </summary>
        public const int NoBlockedRow = -1;

        /// <summary>
        /// Whether a rectangle of height <paramref name="h"/> starting at row
        /// <paramref name="y"/> touches the belt separator row.
        ///
        /// THIS IS THE ROW THE CLIENT REFUSES TO DROP ON, and it is the whole
        /// reason this concept exists on the server at all.
        /// <c>InventorySpaceChecker</c> fills row <c>beltRow</c> with
        /// <c>IsBlocker</c> cells at construction and <c>IsItemBlocked</c> - the
        /// ONLY placement predicate the client has - rejects any drag whose
        /// rectangle covers one of them.
        ///
        /// The server must refuse the same row, and not merely to agree about
        /// one strip of cells. <c>InventorySpaceChecker.AddItem</c> writes the
        /// item's own slot data over every cell it covers, blockers included, so
        /// ONE server-placed item straddling this row deletes the blockers under
        /// it and that stretch of the divider stops blocking - for that session,
        /// for those columns only. The player then finds a gap they can drop into
        /// in the middle of the divider, and a neighbouring column that still
        /// refuses. Both halves of that look like a rendering fault rather than a
        /// coordinate one.
        /// </summary>
        public static bool CrossesBlockedRow(int y, int h, int blockedRow)
        {
            if (blockedRow < 0 || h <= 0)
            {
                // A zero-area item covers no cells, so it cannot land on the
                // divider however its coordinates read.
                return false;
            }

            return y <= blockedRow && blockedRow < y + h;
        }

        /// <summary>Whether a w x h rectangle at (x,y) lies wholly inside a width x height grid.</summary>
        public static bool InBounds(int x, int y, int w, int h, int width, int height)
        {
            if (w <= 0 || h <= 0)
            {
                // A zero-area item occupies nothing, so the only question is
                // whether it claims to be somewhere. The gauntlets' (-1,-1) is
                // the intended answer for "nowhere".
                return (x == Unplaced && y == Unplaced) || (x >= 0 && y >= 0 && x <= width && y <= height);
            }

            return x >= 0 && y >= 0 && x + w <= width && y + h <= height;
        }

        /// <summary>Whether two rectangles share any cell.</summary>
        public static bool Overlaps(
            int ax, int ay, int aw, int ah,
            int bx, int by, int bw, int bh)
        {
            if (aw <= 0 || ah <= 0 || bw <= 0 || bh <= 0)
            {
                return false;
            }

            return ax < bx + bw && bx < ax + aw && ay < by + bh && by < ay + ah;
        }

        /// <summary>
        /// Whether a w x h rectangle at (x,y) fits: inside the grid, clear of the
        /// belt separator row, and clear of every rectangle in
        /// <paramref name="occupied"/>.
        ///
        /// <paramref name="blockedRow"/> has no default on purpose. Every caller
        /// has a grid in its hand and must say which row that grid reserves;
        /// a defaulted parameter is exactly how the belt got forgotten the first
        /// time. Pass <see cref="NoBlockedRow"/> for a grid with no belt.
        /// </summary>
        public static bool Fits(
            int x, int y, int w, int h,
            int width, int height,
            IReadOnlyList<GridRect> occupied,
            int blockedRow)
        {
            if (!InBounds(x, y, w, h, width, height))
            {
                return false;
            }

            if (w <= 0 || h <= 0)
            {
                return true;
            }

            if (CrossesBlockedRow(y, h, blockedRow))
            {
                return false;
            }

            for (int i = 0; i < occupied.Count; i++)
            {
                GridRect other = occupied[i];

                if (Overlaps(x, y, w, h, other.X, other.Y, other.Width, other.Height))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The first free spot for a w x h rectangle, scanning rows top to bottom
        /// and columns left to right, or null when the grid is full.
        ///
        /// Row-major because that is the reading order of the panel: an item
        /// granted by the server should appear where a player's eye already is,
        /// not in whichever cell a hash happened to land on.
        /// </summary>
        public static (int X, int Y)? FirstFree(
            int w, int h,
            int width, int height,
            IReadOnlyList<GridRect> occupied,
            int blockedRow)
        {
            if (w <= 0 || h <= 0)
            {
                return (Unplaced, Unplaced);
            }

            for (int y = 0; y + h <= height; y++)
            {
                for (int x = 0; x + w <= width; x++)
                {
                    if (Fits(x, y, w, h, width, height, occupied, blockedRow))
                    {
                        return (x, y);
                    }
                }
            }

            return null;
        }
    }

    /// <summary>One occupied rectangle, tagged with whose it is so a move can ignore itself.</summary>
    public readonly record struct GridRect(int ItemId, int X, int Y, int Width, int Height);
}

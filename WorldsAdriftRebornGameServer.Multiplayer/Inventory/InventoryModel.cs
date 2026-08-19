namespace WorldsAdriftRebornGameServer.Multiplayer.Inventory
{
    /// <summary>
    /// One player's inventory container: the grid it lives in, and the items in
    /// it. The server's copy of the truth, from which every 1081 push is derived.
    ///
    /// Mutable, and mutated in place. That is deliberate rather than lazy: there
    /// must be exactly ONE object per inventory, because the failure this whole
    /// workstream exists to fix is a second copy being built from a stale read
    /// and then pushed. Handing out fresh instances would invite exactly that
    /// again.
    ///
    /// The grid dimensions are here but must be treated as fixed after the first
    /// serve: the client reads width/height/hasBelt/beltRow EXACTLY ONCE, at
    /// InventoryVisualiser.OnEnable, and LoadInventory never calls Setup. A later
    /// 1081 carrying different dimensions is silently ignored until the entity is
    /// checked out again, so a resize is a lie the server tells itself.
    /// </summary>
    public sealed class InventoryModel
    {
        private readonly List<InventoryItem> items = new();

        public InventoryModel(int width, int height, bool hasBelt, int beltRow)
        {
            Width = width;
            Height = height;
            HasBelt = hasBelt;

            // The separator row is NOT a free parameter, however much the wire
            // field looks like one. Retail's belt was the bottom three rows of
            // the same grid, with the divider immediately above them - "the
            // lower three (four if you count the spacer) grid rows were
            // converted into the new belt area" - and the client turns beltRow
            // into a full-width blocker row at exactly that INDEX, counted down
            // from the top. Any other value blocks a row in the middle of the
            // backpack and leaves the belt unmarked.
            //
            // A caller's belief is overridden rather than trusted because there
            // is no visible tell when it is wrong: the client reads beltRow once
            // at checkout and never complains, so a stale 3 restored from a
            // database row would quietly outlive every fix. Corrected here, at
            // the one constructor every path goes through - DefaultGrid,
            // InventorySnapshot.Read, Copy, BindContainer - it cannot be
            // bypassed. beltRow is still honoured for a beltless grid, where the
            // client ignores it entirely.
            BeltRow = hasBelt ? SeparatorRowFor(height) : beltRow;
        }

        /// <summary>
        /// How many rows at the BOTTOM of the grid are the belt. Community
        /// record and the client agree: the belt was carved out of the existing
        /// inventory, three rows plus a one-row spacer above them, all in the
        /// one container.
        /// </summary>
        public const int BeltRows = 3;

        /// <summary>
        /// The index of the divider row for a grid this tall - the row the
        /// client blocks, one above the belt.
        ///
        /// Clamped into the grid because <c>beltRow &gt;= height</c> throws
        /// inside <c>InventorySpaceChecker</c>'s constructor, which runs inside
        /// <c>InventoryVisualiser.OnEnable</c>: a grid too short for a belt would
        /// not merely misdraw, it would abort the checkout that draws it.
        /// </summary>
        public static int SeparatorRowFor(int height)
        {
            int row = height - BeltRows - 1;

            return row < 0 ? 0 : (row >= height ? height - 1 : row);
        }

        /// <summary>The stock player grid: 10 wide, 18 tall, belt on the bottom three rows.</summary>
        public static InventoryModel DefaultGrid() => new InventoryModel(10, 18, true, SeparatorRowFor(18));

        public int Width { get; }

        public int Height { get; }

        public bool HasBelt { get; }

        public int BeltRow { get; }

        /// <summary>
        /// The row no item may occupy, or <see cref="InventoryGeometry.NoBlockedRow"/>
        /// when this grid has no belt. Everything that places or moves an item
        /// asks this rather than reading <see cref="BeltRow"/>, so a beltless
        /// chest cannot accidentally inherit a divider.
        /// </summary>
        public int BlockedRow => HasBelt ? BeltRow : InventoryGeometry.NoBlockedRow;

        /// <summary>The items, in wire order.</summary>
        public IReadOnlyList<InventoryItem> Items => items;

        /// <summary>
        /// How many hotbar slots exist. Slots 0-3 are the fixed gauntlets and
        /// 4-7 are user-assignable; the client displays 4-7 as keys 5-8, and a
        /// slotIndex of 8 or more is logged as an error and the item dropped.
        /// </summary>
        public const int HotBarSlots = 8;

        /// <summary>The first hotbar slot a player may assign to. 0-3 are the gauntlets.</summary>
        public const int FirstAssignableHotBarSlot = 4;

        /// <summary>Replaces the whole item list, e.g. after loading from the database.</summary>
        public void Reset(IEnumerable<InventoryItem> replacement)
        {
            items.Clear();
            items.AddRange(replacement);
        }

        /// <summary>The item with this id, or null.</summary>
        public InventoryItem? ById(int itemId)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].ItemId == itemId)
                {
                    return items[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Adds an item. Refuses a duplicate id rather than appending it: the
        /// client keys its slot lookup on (EntityId, ItemId, ItemType,
        /// IsSplitItem) and inserts with the INDEXER, so a repeated id silently
        /// overwrites and one of the two items simply vanishes from the panel
        /// with no error anywhere.
        /// </summary>
        public bool Add(InventoryItem item)
        {
            if (ById(item.ItemId) != null)
            {
                return false;
            }

            items.Add(item);
            return true;
        }

        /// <summary>Removes an item by id. False if it was not there.</summary>
        public bool Remove(int itemId)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].ItemId == itemId)
                {
                    items.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Swaps one item for a modified copy of itself, in place, keeping wire
        /// order. Order matters more than it looks: the client rebuilds its whole
        /// local model from this list, and a reordering makes every icon flicker.
        /// </summary>
        public bool Replace(InventoryItem item)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].ItemId == item.ItemId)
                {
                    items[i] = item;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The rectangles currently occupying the grid, ignoring the item with
        /// <paramref name="exceptItemId"/> so a move can be tested against
        /// everything except itself.
        ///
        /// Worn and stashed items are skipped: neither is in the grid, and
        /// counting them would block cells that are actually empty.
        /// </summary>
        public IReadOnlyList<GridRect> OccupiedRects(ItemFootprintLookup footprints, int exceptItemId = int.MinValue)
        {
            List<GridRect> rects = new();

            foreach (InventoryItem item in items)
            {
                if (item.ItemId == exceptItemId || item.IsWorn || item.IsStashed)
                {
                    continue;
                }

                if (!footprints(item.ItemTypeId, out ItemFootprint footprint))
                {
                    continue;
                }

                ItemFootprint oriented = item.Oriented(footprint);

                if (oriented.Width <= 0 || oriented.Height <= 0)
                {
                    continue;
                }

                rects.Add(new GridRect(item.ItemId, item.X, item.Y, oriented.Width, oriented.Height));
            }

            return rects;
        }

        /// <summary>The item occupying a hotbar slot, or null.</summary>
        public InventoryItem? OnHotBar(int slotIndex)
        {
            foreach (InventoryItem item in items)
            {
                if (item.HotBarSlotNum == slotIndex)
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>A deep-enough copy for a caller that wants a snapshot it can hold.</summary>
        public InventoryModel Copy()
        {
            InventoryModel copy = new InventoryModel(Width, Height, HasBelt, BeltRow);
            copy.items.AddRange(items);
            return copy;
        }
    }
}

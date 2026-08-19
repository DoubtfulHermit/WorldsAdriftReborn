namespace WorldsAdriftRebornGameServer.Multiplayer.Inventory
{
    /// <summary>Why a cross-inventory move did or did not happen.</summary>
    public enum CrossMoveOutcome
    {
        /// <summary>The item left one inventory and arrived in the other.</summary>
        Moved,

        /// <summary>Source and destination are the same inventory - use TryMove.</summary>
        SameInventory,

        /// <summary>No item with that id in the source.</summary>
        UnknownItem,

        /// <summary>A worn garment or a stash tile. Neither lives in a grid.</summary>
        NotInGrid,

        /// <summary>The item database has never heard of this item type.</summary>
        UnknownItemType,

        /// <summary>The destination has no room at the requested position.</summary>
        NoRoom,
    }

    /// <summary>
    /// MOVING AN ITEM BETWEEN TWO INVENTORIES - taking loot out of a chest, and
    /// putting something back in.
    ///
    /// THE EVENT THIS SERVES, AND WHY IT WAS REFUSED FOR SO LONG. The client sends
    /// <c>CrossInventoryMoveItem{srcInventoryEntityId, destInventoryEntityId,
    /// srcItemId, xPos, yPos, rotate, isLockboxItem}</c> on its OWN 1082
    /// (<c>InventoryModificationBehaviour.RequestCrossInventoryMoveItem</c>), and
    /// <c>InventoryModificationState_Handler</c> refused it with a one-line reason
    /// that was completely correct at the time: <i>"no second inventory exists
    /// yet"</i>. A loot container IS that second inventory, and until one existed
    /// there was nothing to implement.
    ///
    /// THE ID RENUMBERING IS THE WHOLE TRICK, and getting it wrong is silent. Item
    /// ids are PER-INVENTORY - every <c>InventoryModel</c> has its own
    /// <see cref="ItemIdAllocator"/> from the same floor of 1104 - so a chest and a
    /// player routinely both hold an item numbered 1104. Carrying the source id
    /// across would therefore collide with an existing destination item, and the
    /// client keys its slot lookup on (EntityId, ItemId, ItemType, IsSplitItem) and
    /// inserts with the INDEXER: a repeated id silently OVERWRITES, one of the two
    /// items vanishes from the panel, and <c>RemoveByItemId</c> then deletes BOTH.
    /// So the destination issues a fresh id and the item keeps everything else -
    /// type, amount, quality, meta and rarity. Meta in particular is not optional:
    /// it is the only place colours and item health live.
    ///
    /// ORDER OF OPERATIONS: the destination is checked and the item ADDED before the
    /// source is touched, so a refused move leaves both sides exactly as they were.
    /// The alternative - remove, then fail to place - deletes a player's loot.
    ///
    /// Pure: geometry and records. The caller owns both models, both allocators, the
    /// footprint lookup and the two 1081 pushes.
    /// </summary>
    public static class CrossInventoryPolicy
    {
        /// <summary>
        /// Moves one item from <paramref name="source"/> into
        /// <paramref name="destination"/> at (<paramref name="x"/>,
        /// <paramref name="y"/>), giving it a fresh destination id.
        ///
        /// <paramref name="nextDestinationItemId"/> is a FACTORY and is called at
        /// most once, after every refusal has already been decided. Taking a bare
        /// int here would burn an id on every rejected drag - which is the latent
        /// leak <c>InventoryPolicy.TryStackInto</c> documents closing on the grant
        /// path, and a chest is dragged from far more often than it is granted into.
        /// </summary>
        public static CrossMoveOutcome TryMove(
            InventoryModel source,
            InventoryModel destination,
            int sourceItemId,
            Func<int> nextDestinationItemId,
            int x,
            int y,
            bool rotate,
            ItemFootprintLookup footprints)
        {
            if (nextDestinationItemId == null) throw new ArgumentNullException(nameof(nextDestinationItemId));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (footprints == null) throw new ArgumentNullException(nameof(footprints));

            if (ReferenceEquals(source, destination))
            {
                return CrossMoveOutcome.SameInventory;
            }

            InventoryItem? item = source.ById(sourceItemId);
            if (item == null)
            {
                return CrossMoveOutcome.UnknownItem;
            }

            if (item.IsWorn || item.IsStashed)
            {
                // A worn garment is excluded from the grid entirely and a stash item
                // lives in a fixed tile list, so neither has coordinates a
                // cross-inventory move could honour.
                return CrossMoveOutcome.NotInGrid;
            }

            if (!footprints(item.ItemTypeId, out ItemFootprint footprint))
            {
                return CrossMoveOutcome.UnknownItemType;
            }

            ItemFootprint oriented = rotate
                ? new ItemFootprint(footprint.Height, footprint.Width)
                : footprint;

            if (!InventoryGeometry.Fits(
                    x, y, oriented.Width, oriented.Height,
                    destination.Width, destination.Height,
                    destination.OccupiedRects(footprints)))
            {
                return CrossMoveOutcome.NoRoom;
            }

            InventoryItem arriving = item with
            {
                ItemId = nextDestinationItemId(),
                X = x,
                Y = y,
                Rotated = rotate,
                // A hotbar assignment belongs to the inventory it was made in. An
                // item dragged into a chest that kept slot 5 would give the chest a
                // hotbar, and one dragged out of a chest has never had one.
                HotBarSlotNum = InventoryItem.NoSlot,
            };

            if (!destination.Add(arriving))
            {
                return CrossMoveOutcome.NoRoom;
            }

            // Only now. See the order-of-operations note in the type remarks.
            source.Remove(sourceItemId);
            return CrossMoveOutcome.Moved;
        }

        /// <summary>
        /// Empties <paramref name="source"/> into <paramref name="destination"/>,
        /// item by item, into the first free spot for each - the "take all" button.
        /// Returns how many moved; the rest stay put, which is what a player wants
        /// when their bag fills up halfway through.
        ///
        /// <paramref name="nextDestinationItemId"/> is called once per item that is
        /// actually going to be placed.
        /// </summary>
        public static int MoveAll(
            InventoryModel source,
            InventoryModel destination,
            Func<int> nextDestinationItemId,
            ItemFootprintLookup footprints)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (nextDestinationItemId == null) throw new ArgumentNullException(nameof(nextDestinationItemId));
            if (footprints == null) throw new ArgumentNullException(nameof(footprints));

            if (ReferenceEquals(source, destination))
            {
                return 0;
            }

            // A snapshot, because the move mutates source.Items underneath us.
            List<InventoryItem> candidates = new(source.Items);
            int moved = 0;

            foreach (InventoryItem item in candidates)
            {
                if (item.IsWorn || item.IsStashed) continue;
                if (!footprints(item.ItemTypeId, out ItemFootprint footprint)) continue;

                (int X, int Y)? spot = InventoryGeometry.FirstFree(
                    footprint.Width, footprint.Height,
                    destination.Width, destination.Height,
                    destination.OccupiedRects(footprints));

                if (spot == null)
                {
                    // Full. Keep going rather than stopping: a 1x1 may still fit
                    // where a 4x3 did not, and a player who asked for "all" would
                    // rather have most of it.
                    continue;
                }

                if (TryMove(source, destination, item.ItemId, nextDestinationItemId,
                        spot.Value.X, spot.Value.Y, rotate: false, footprints)
                    == CrossMoveOutcome.Moved)
                {
                    moved++;
                }
            }

            return moved;
        }
    }
}

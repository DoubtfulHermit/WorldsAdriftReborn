namespace WorldsAdriftRebornGameServer.Multiplayer.Inventory
{
    /// <summary>
    /// Hands out item ids that cannot collide with anything already in an
    /// inventory.
    ///
    /// There was no allocator at all before this, and a collision is silent and
    /// destructive rather than noisy: the client keys its slot lookup on
    /// (EntityId, ItemId, ItemType, IsSplitItem) and inserts with the INDEXER,
    /// so granting an item whose id is already in use makes one of the two
    /// simply disappear from the panel, and a later RemoveByItemId deletes BOTH.
    /// Nothing logs.
    ///
    /// <see cref="Floor"/> is 1104 because the seeded items run 1 to 4 (the
    /// gauntlet shells) and 1101 to 1103 (glider, poncho, dev hat), and the
    /// stash items occupy 6 to 30. Starting above all of them means a fresh
    /// server with an empty database still never issues a colliding id, which
    /// matters because that is exactly the case nobody tests by hand.
    ///
    /// Ids are allocated PER INVENTORY, not globally: the client's key includes
    /// the entity id, so two players may both hold item 1200 without any
    /// interaction. A global counter would work too, but it would have to be
    /// persisted or it would restart at the floor after a reboot and collide
    /// with everything already loaded from the database.
    /// </summary>
    public sealed class ItemIdAllocator
    {
        /// <summary>
        /// The lowest id this allocator will ever issue. Below it are the ids the
        /// seed and the stash already use; the game server's own comment reserves
        /// "the first 100 itemIds for client logic", which the seed then ignores
        /// by handing out 1101-1103, so the floor is set from what is actually
        /// there rather than from the comment.
        /// </summary>
        public const int Floor = 1104;

        private int next = Floor;

        /// <summary>The id <see cref="Next"/> would return.</summary>
        public int Peek => next;

        /// <summary>
        /// Notes that an id is taken, so it is never handed out. Idempotent, and
        /// safe to call with ids below the floor (they are already excluded).
        ///
        /// Call this for every id loaded from the database. Skipping it is the
        /// bug this class exists to prevent: a restored inventory holding 1200
        /// and a fresh allocator starting at 1104 will reach 1200 after 96
        /// grants and then quietly eat the restored item.
        /// </summary>
        public void Reserve(int itemId)
        {
            if (itemId >= next)
            {
                next = itemId + 1;
            }
        }

        /// <summary>Reserves every id in an inventory in one call.</summary>
        public void ReserveAll(IEnumerable<InventoryItem> items)
        {
            foreach (InventoryItem item in items)
            {
                Reserve(item.ItemId);
            }
        }

        /// <summary>The next unused id. Monotonic; never reused within a process.</summary>
        public int Next()
        {
            int allocated = next;
            next = allocated + 1;
            return allocated;
        }

        /// <summary>
        /// An allocator already primed for an existing inventory. The only
        /// correct way to build one for a model that came out of storage.
        /// </summary>
        public static ItemIdAllocator For(InventoryModel model)
        {
            ItemIdAllocator allocator = new ItemIdAllocator();
            allocator.ReserveAll(model.Items);
            return allocator;
        }
    }
}

using WorldsAdriftRebornGameServer.Multiplayer.Inventory;

namespace WorldsAdriftRebornGameServer.Game.Inventory
{
    /// <summary>
    /// The server's inventories: who owns which, what is in them, and when they
    /// are written to the database.
    ///
    /// A static, like Players and Appearances, because there is exactly one
    /// server. The state itself lives in the pure InventoryStore next door, so
    /// what is here is only the wiring the pure project is not allowed to know
    /// about: the game's default item list, and Postgres.
    ///
    /// THE ORDER OF EVENTS THIS IS BUILT AROUND, because it is not the obvious
    /// one. An entity is checked out - and its 1081 seeded - BEFORE the client
    /// publishes 1088, which is the only packet carrying the character uid. So
    /// every player necessarily begins the session on a volatile session key and
    /// is REBOUND onto their durable character key a moment later, at which
    /// point their stored inventory is loaded and pushed. That is why the push
    /// seam is called from the identity path and not only from mutations.
    /// </summary>
    internal static class InventoryService
    {
        private static readonly InventoryStore Store = new InventoryStore();
        private static readonly InventoryPersistence Persistence = new InventoryPersistence();

        /// <summary>
        /// Says once, at start-up, whether anything a player does tonight will
        /// still be there tomorrow. Silence here is how "it does not save" gets
        /// discovered three sessions late.
        /// </summary>
        internal static void ReportPersistenceState()
        {
            if (Persistence.Enabled)
            {
                Console.WriteLine("[info] inventory persistence is ON (Postgres).");
            }
            else
            {
                Console.WriteLine("[warning] inventory persistence is OFF (" + Persistence.DisabledReason
                    + "). Inventories will work for the length of a session and then be lost.");
            }
        }

        /// <summary>
        /// This entity's inventory, creating it under a session key if the
        /// entity has never been seen.
        ///
        /// This is what the 1081 seed calls, and it is why a second interest
        /// request no longer resets anybody: the seed reads the store instead of
        /// rebuilding the defaults, exactly as the 1088 branch already reads
        /// Appearances.
        /// </summary>
        internal static InventoryModel ForEntity( long entityId )
        {
            return Store.Bind(entityId, Store.KeyOf(entityId) ?? InventoryKey.ForSession(entityId), InventoryWire.DefaultModel);
        }

        /// <summary>The key an entity's inventory is filed under, or null.</summary>
        internal static InventoryKey? KeyOf( long entityId ) => Store.KeyOf(entityId);

        /// <summary>
        /// The next never-used item id for this entity's inventory. The only
        /// legitimate source of an item id for a grant.
        /// </summary>
        internal static int NextItemId( long entityId )
        {
            ItemIdAllocator? allocator = Store.AllocatorForEntity(entityId);

            if (allocator != null)
            {
                return allocator.Next();
            }

            // Binding first is what primes the allocator with the seeded ids;
            // asking for an id before the inventory exists otherwise hands out
            // the floor while the seed is about to claim it.
            ForEntity(entityId);

            return Store.AllocatorForEntity(entityId)!.Next();
        }

        /// <summary>
        /// Puts a new item in a player's inventory and pushes it. Returns the id
        /// it was given, or null when the item type is unknown or there is no
        /// room.
        ///
        /// THE ENTRY POINT FOR ANY FUTURE GRANT - harvest yield, loot, an admin
        /// command. It is here, in the container, rather than in whatever
        /// produced the item, because "which id" and "where does it go" are
        /// properties of the inventory and getting either wrong is silent: a
        /// reused id makes an existing item vanish, and an out-of-bounds
        /// placement throws on the client mid-refresh.
        ///
        /// Nothing calls it yet. The harvest transaction is a separate
        /// workstream, and this deliberately does not reach for it.
        /// </summary>
        internal static int? Grant(
            long entityId,
            string itemTypeId,
            int amount = 1,
            int quality = 0,
            IReadOnlyDictionary<string, string>? meta = null,
            int? rarity = null )
        {
            InventoryModel model = ForEntity(entityId);

            InventoryItem? granted = InventoryPolicy.TryGrant(
                model,
                NextItemId(entityId),
                itemTypeId,
                amount,
                quality,
                meta ?? new Dictionary<string, string>(),
                rarity,
                InventoryWire.Footprints);

            if (granted == null)
            {
                Console.WriteLine("[warning] could not grant '" + itemTypeId + "' to entity " + entityId
                    + " (unknown item type, or no free space).");
                return null;
            }

            InventoryPush.Push(entityId, "granted " + amount + "x " + itemTypeId);

            return granted.ItemId;
        }

        /// <summary>
        /// Binds an entity to its character identity, loads whatever the
        /// database holds for that character, and reports whether the key it
        /// ended up on is durable.
        ///
        /// Called from the 1088 handler, which is the only place the uid appears.
        /// It is safe to call repeatedly: rebinding to the same key is a no-op,
        /// and the load only happens the first time the character key is seen.
        /// </summary>
        internal static bool BindIdentity( long entityId, IReadOnlyDictionary<string, string> customisation )
        {
            InventoryKey key = CharacterIdentity.KeyFor(entityId, customisation);
            InventoryKey? previous = Store.KeyOf(entityId);

            if (previous.HasValue && previous.Value.Equals(key))
            {
                return key.IsDurable;
            }

            if (!key.IsDurable)
            {
                // The uid did not arrive. Loud, because this is the difference
                // between a persistent game and one that quietly forgets
                // everything, and it is not visible in game at all.
                Console.WriteLine("[warning] entity " + entityId + " published no usable character uid ("
                    + CharacterIdentity.CharacterDataKey + " missing or not a GUID). Its inventory stays on the "
                    + "session key " + key + " and WILL NOT BE SAVED.");

                Store.Bind(entityId, key, InventoryWire.DefaultModel);
                return false;
            }

            bool alreadyResident = Store.ForKey(key) != null;

            Store.Rebind(entityId, key, InventoryWire.DefaultModel);

            if (!alreadyResident)
            {
                InventoryModel? stored = Persistence.Load(key);

                if (stored != null)
                {
                    Store.Load(key, stored.Items);
                    Console.WriteLine("[info] restored " + stored.Items.Count + " item(s) for " + key
                        + " (entity " + entityId + ").");
                }
                else
                {
                    Console.WriteLine("[info] no stored inventory for " + key
                        + " (entity " + entityId + "); keeping this session's contents.");
                }
            }

            return true;
        }

        /// <summary>
        /// Writes an entity's inventory if it is on a durable key. Called after
        /// every mutation, from the push seam, so there is no separate
        /// "remember to save" step anybody can forget.
        /// </summary>
        internal static void Save( long entityId )
        {
            InventoryKey? key = Store.KeyOf(entityId);
            InventoryModel? model = Store.ForEntity(entityId);

            if (!key.HasValue || model == null)
            {
                return;
            }

            Persistence.Save(key.Value, model);
        }

        /// <summary>
        /// Saves and then drops an entity's inventory when its player leaves.
        ///
        /// The save has to come first and it has to be unconditional: a player
        /// who disconnects mid-drag has had a push, so the last mutation is
        /// already written, but a player whose last action was a grant from some
        /// future server-side path may not have. Saving twice costs a
        /// millisecond; saving never costs the session.
        /// </summary>
        internal static void Forget( long entityId )
        {
            Save(entityId);
            Store.Forget(entityId);
        }
    }
}

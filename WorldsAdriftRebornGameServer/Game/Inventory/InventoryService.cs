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
        /// <paramref name="push"/> exists for ONE caller shape: a single harvest
        /// hit that grants several materials at once. 1081 is a full-state
        /// component - the whole list is re-sent and persisted on every push - so
        /// a tree paying wood, fibre and berries would otherwise cost three full
        /// sends and three database writes for one swing. Such a caller passes
        /// false and pushes once at the end. It MUST push: an inventory the client
        /// is never told about is an item the player does not have, and the client
        /// clears its own waiting flag on nothing else.
        /// </summary>
        internal static int? Grant(
            long entityId,
            string itemTypeId,
            int amount = 1,
            int quality = 0,
            IReadOnlyDictionary<string, string>? meta = null,
            int? rarity = null,
            bool push = true )
        {
            InventoryModel model = ForEntity(entityId);

            // Stack onto an existing pile of the same material first. Without this
            // a repeated harvest - a 12-section tree, a node hit again - adds a
            // fresh grid row every time and fills the inventory with duplicate
            // piles. Only stackable types (stacksize > 1) merge; everything else
            // falls through to a new item exactly as before. No id is consumed on
            // the merge path, which also closes a latent id leak in the old code
            // (it allocated an id even when the grant then failed).
            InventoryItem? granted = InventoryPolicy.TryStackInto(
                model, itemTypeId, amount, quality, InventoryWire.StackMaxOf(itemTypeId));

            if (granted == null)
            {
                granted = InventoryPolicy.TryGrant(
                    model,
                    NextItemId(entityId),
                    itemTypeId,
                    amount,
                    quality,
                    meta ?? new Dictionary<string, string>(),
                    rarity,
                    InventoryWire.Footprints);
            }

            if (granted == null)
            {
                Console.WriteLine("[warning] could not grant '" + itemTypeId + "' to entity " + entityId
                    + " (unknown item type, or no free space).");
                return null;
            }

            if (push)
            {
                InventoryPush.Push(entityId, "granted " + amount + "x " + itemTypeId);
            }

            return granted.ItemId;
        }

        /// <summary>
        /// Binds a NON-PLAYER entity - a loot container - to its own inventory, with
        /// its own grid and its own contents, and returns how many of
        /// <paramref name="drops"/> actually fitted.
        ///
        /// WHY THIS IS NOT <see cref="ForEntity"/>. ForEntity's create-factory is
        /// <c>InventoryWire.DefaultModel</c>, the player starter kit. It is the right
        /// default for the entity kind that had an inventory when it was written -
        /// players - and exactly the wrong one for a chest, which would open onto a
        /// set of gauntlets in a 10x18 belt grid. The grid dimensions matter as much
        /// as the contents: the client reads width/height/hasBelt/beltRow EXACTLY
        /// ONCE, at OnEnable, so they are a property of checkout and no later push
        /// can correct them.
        ///
        /// The factory runs at most once per key (InventoryStore.Bind guarantees it),
        /// so calling this twice on the same container cannot re-roll it or duplicate
        /// its contents. Item ids come from the container's OWN allocator, which is
        /// primed by Bind - container item ids and player item ids are independent
        /// number lines and must be, because the two inventories are separate
        /// entities on the wire.
        ///
        /// No push: the caller is the 1081 SEED path, which serialises the model
        /// itself. Pushing here would send a component update for an entity the peer
        /// has not finished checking out.
        ///
        /// <para><paramref name="sourceTier"/> IS STAMPED ONTO EVERY DROP, and it is
        /// the whole reason salvaging pays the right quality. A Tonking Puck is 45
        /// aluminium at quality 6, 5 or 10 depending on which tier of island it came
        /// off, and once it is in a player's bag the ONLY thing that still knows
        /// which is this stamp. It rides the free-form <c>meta</c> dictionary that is
        /// already persisted and already survives a cross-inventory move (the move
        /// copies the record), so it needs no schema change and no wire change.</para>
        /// </summary>
        internal static int BindContainer(
            long entityId,
            int width,
            int height,
            bool hasBelt,
            int beltRow,
            IReadOnlyList<Multiplayer.Loot.LootDrop> drops,
            int? sourceTier = null )
        {
            int placed = 0;

            Dictionary<string, string> stamp = sourceTier.HasValue
                ? new Dictionary<string, string>
                {
                    [ScrapSalvagePolicy.SourceTierMetaKey] =
                        sourceTier.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }
                : new Dictionary<string, string>();

            Store.Bind(entityId, InventoryKey.ForSession(entityId), () =>
            {
                InventoryModel model = new InventoryModel(width, height, hasBelt, beltRow);
                ItemIdAllocator allocator = ItemIdAllocator.For(model);

                foreach (Multiplayer.Loot.LootDrop drop in drops)
                {
                    InventoryItem? item = InventoryPolicy.TryGrant(
                        model,
                        allocator.Next(),
                        drop.ItemTypeId,
                        drop.Amount,
                        drop.Quality,
                        // A fresh dictionary per item: Meta is stored by reference on
                        // the record and later mutated per item elsewhere, so sharing
                        // one instance across a chest would make every item in it
                        // change together.
                        new Dictionary<string, string>(stamp),
                        rarity: null,
                        InventoryWire.Footprints);

                    if (item != null) placed++;
                }

                return model;
            });

            return placed;
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

                // The live inventory the entity is now bound to. After Rebind this
                // is never null; the fallback only guards a future refactor. Its
                // count is the OTHER half of the wipe decision - a stored payload
                // is allowed to replace it only when doing so cannot destroy data.
                int currentCount = Store.ForKey(key)?.Items.Count ?? 0;

                if (InventoryLoadPolicy.ShouldApplyStored(currentCount, stored?.Items))
                {
                    Store.Load(key, stored!.Items);
                    Console.WriteLine("[info] restored " + stored!.Items.Count + " item(s) for " + key
                        + " (entity " + entityId + ").");
                }
                else if (stored == null)
                {
                    // No row, an unreadable database, or an unparseable payload -
                    // the persistence layer collapses all three to null on
                    // purpose. Keep the session's contents; a transient database
                    // error must never present as a wipe.
                    Console.WriteLine("[info] no stored inventory for " + key
                        + " (entity " + entityId + "); keeping this session's contents.");
                }
                else
                {
                    // The row parsed but is empty while this session is not. An
                    // empty row is indistinguishable from a truncated one, so it
                    // is treated as suspect rather than authoritative: the live
                    // inventory is kept and the next save overwrites the empty
                    // row with it. See InventoryLoadPolicy for the asymmetry.
                    Console.WriteLine("[warning] stored inventory for " + key + " (entity " + entityId
                        + ") is empty but this session holds " + currentCount
                        + " item(s); keeping the session's contents rather than wiping them.");
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

namespace WorldsAdriftRebornGameServer.Multiplayer.Inventory
{
    /// <summary>
    /// The one place a player's inventory lives while the server is running,
    /// plus the entity-to-key binding that says whose it is.
    ///
    /// SHAPED LIKE AppearanceStore, KEYED NOTHING LIKE IT. AppearanceStore is a
    /// Dictionary&lt;long, ...&gt; on entity id, and that is correct for what it
    /// does - it is a within-session mirror cache. Copying that key here would
    /// be fatal rather than untidy: EntityIdAllocator never reuses an entity id,
    /// so an entityId-keyed inventory is a brand new empty inventory every
    /// single session, and the persistence would appear to work right up until
    /// somebody relogged. The key is therefore an <see cref="InventoryKey"/>,
    /// which knows whether it is durable.
    ///
    /// The binding is separate from the storage because the two facts arrive at
    /// different times. An entity is checked out (and its 1081 seeded) before
    /// the client publishes its 1088, which is where the character uid comes
    /// from. So an entity starts bound to a session key and is REBOUND to a
    /// durable one the moment identity arrives - see <see cref="Rebind"/>.
    ///
    /// Pure storage: it validates ownership of nothing and talks to no database.
    /// The caller does both, which is what keeps this project free of ENet, game
    /// types and Npgsql.
    /// </summary>
    public sealed class InventoryStore
    {
        private readonly Dictionary<InventoryKey, InventoryModel> byKey = new();
        private readonly Dictionary<InventoryKey, ItemIdAllocator> allocators = new();
        private readonly Dictionary<long, InventoryKey> keyByEntity = new();

        /// <summary>Number of inventories held.</summary>
        public int Count => byKey.Count;

        /// <summary>The key an entity's inventory is filed under, or null if unbound.</summary>
        public InventoryKey? KeyOf(long entityId)
        {
            return keyByEntity.TryGetValue(entityId, out InventoryKey key) ? key : (InventoryKey?)null;
        }

        /// <summary>
        /// Binds an entity to a key, creating the inventory from
        /// <paramref name="create"/> if this key has never been seen.
        ///
        /// <paramref name="create"/> is a factory rather than a model because
        /// the default contents come from the game's item database, which this
        /// project cannot name. It is called at most once per key, so a caller
        /// cannot accidentally reset a live inventory by binding twice.
        /// </summary>
        public InventoryModel Bind(long entityId, InventoryKey key, Func<InventoryModel> create)
        {
            keyByEntity[entityId] = key;

            if (!byKey.TryGetValue(key, out InventoryModel? model))
            {
                model = create();
                byKey[key] = model;
                allocators[key] = ItemIdAllocator.For(model);
            }

            return model;
        }

        /// <summary>
        /// Moves an entity from whatever key it is on to a new one, carrying the
        /// inventory across only when the destination has none yet.
        ///
        /// This is the identity-arrived path, and the asymmetry is the whole
        /// point. Rebinding a session key onto a character key that ALREADY has
        /// a stored inventory must keep the stored one - that is the relog, and
        /// carrying the session's freshly seeded default across would overwrite
        /// everything the player owns with a starter kit. Rebinding onto a
        /// character key with nothing under it carries the session inventory
        /// over, which is the first-ever login and loses nothing.
        ///
        /// Returns the model the entity is now on.
        /// </summary>
        public InventoryModel Rebind(long entityId, InventoryKey key, Func<InventoryModel> create)
        {
            InventoryKey? previous = KeyOf(entityId);

            if (previous.HasValue && previous.Value.Equals(key))
            {
                return Bind(entityId, key, create);
            }

            if (!byKey.ContainsKey(key)
                && previous.HasValue
                && byKey.TryGetValue(previous.Value, out InventoryModel? carried))
            {
                byKey[key] = carried;
                allocators[key] = allocators.TryGetValue(previous.Value, out ItemIdAllocator? allocator)
                    ? allocator
                    : ItemIdAllocator.For(carried);
            }

            if (previous.HasValue && !previous.Value.Equals(key) && !IsBoundByAnyOther(entityId, previous.Value))
            {
                byKey.Remove(previous.Value);
                allocators.Remove(previous.Value);
            }

            keyByEntity[entityId] = key;

            return Bind(entityId, key, create);
        }

        /// <summary>
        /// Replaces the contents of the inventory under a key - the load-from-
        /// database path.
        ///
        /// The MODEL OBJECT is kept and its items replaced, never swapped for a
        /// new instance, because callers hold the model. Swapping it is how a
        /// mutation lands on an object nobody is going to push.
        /// </summary>
        public bool Load(InventoryKey key, IEnumerable<InventoryItem> items)
        {
            if (!byKey.TryGetValue(key, out InventoryModel? model))
            {
                return false;
            }

            model.Reset(items);
            allocators[key] = ItemIdAllocator.For(model);
            return true;
        }

        /// <summary>The inventory an entity is bound to, or null when it has none.</summary>
        public InventoryModel? ForEntity(long entityId)
        {
            InventoryKey? key = KeyOf(entityId);

            return key.HasValue && byKey.TryGetValue(key.Value, out InventoryModel? model) ? model : null;
        }

        /// <summary>The inventory under a key, or null.</summary>
        public InventoryModel? ForKey(InventoryKey key)
        {
            return byKey.TryGetValue(key, out InventoryModel? model) ? model : null;
        }

        /// <summary>
        /// The id allocator for an entity's inventory, primed with every id
        /// already in it. Null when the entity has no inventory.
        /// </summary>
        public ItemIdAllocator? AllocatorForEntity(long entityId)
        {
            InventoryKey? key = KeyOf(entityId);

            return key.HasValue && allocators.TryGetValue(key.Value, out ItemIdAllocator? allocator) ? allocator : null;
        }

        /// <summary>
        /// Drops an entity's binding when its player disconnects, and the
        /// inventory too if no other entity is on that key.
        ///
        /// Dropping the inventory is safe ONLY because a durable one has already
        /// been written to the database by then - the caller's job, and the
        /// reason this returns the key rather than silently forgetting it.
        /// </summary>
        public InventoryKey? Forget(long entityId)
        {
            InventoryKey? key = KeyOf(entityId);

            if (!key.HasValue)
            {
                return null;
            }

            keyByEntity.Remove(entityId);

            if (!IsBoundByAnyOther(entityId, key.Value))
            {
                byKey.Remove(key.Value);
                allocators.Remove(key.Value);
            }

            return key;
        }

        private bool IsBoundByAnyOther(long entityId, InventoryKey key)
        {
            foreach (KeyValuePair<long, InventoryKey> entry in keyByEntity)
            {
                if (entry.Key != entityId && entry.Value.Equals(key))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

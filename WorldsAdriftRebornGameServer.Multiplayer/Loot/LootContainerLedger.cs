namespace WorldsAdriftRebornGameServer.Multiplayer.Loot
{
    /// <summary>
    /// WHICH ENTITIES ARE LOOT CONTAINERS, and what tier of loot each holds.
    ///
    /// Populated at the same spawn seam every other resource uses
    /// (<c>WorldResourceActivation.Activate</c>), and consulted by three places that
    /// otherwise have no way to tell a chest from a rock:
    ///
    ///   * the <c>1210 InteractiveState</c> serve, which must offer the
    ///     <c>Inventory</c> verb on a container and nothing else on a nugget;
    ///   * the <c>1081 InventoryState</c> serve, which must bind a CONTAINER grid
    ///     rather than falling through to <c>InventoryWire.DefaultModel</c> - that
    ///     fallback is the player starter kit, so without this a chest would open
    ///     onto a set of gauntlets;
    ///   * the interact echo, which turns a player's <c>1211 InteractWithObject</c>
    ///     into the <c>Interact</c> event on the container's own 1210 that actually
    ///     opens the panel.
    ///
    /// Process-global and static, mirroring <see cref="DatabankLedger"/> and
    /// <c>NodeRegistry</c>: a container is a fixed world fact, not per-player state,
    /// so one registration serves every player who opens it.
    ///
    /// THE TIER IS STORED, NOT LOOKED UP AT OPEN TIME. Contents must be identical
    /// for every peer that opens the same chest and across a re-checkout, so the
    /// input to the roll has to be a property of the container, not of whoever is
    /// standing next to it. See <see cref="LootTable"/>.
    /// </summary>
    public static class LootContainerLedger
    {
        private readonly struct Entry
        {
            internal Entry(string key, int tier)
            {
                Key = key;
                Tier = tier;
            }

            internal string Key { get; }
            internal int Tier { get; }
        }

        private static readonly Dictionary<long, Entry> ByEntity = new();

        /// <summary>
        /// Registers a placed container and the island tier that decides its
        /// contents. Idempotent: a second joiner walking the same spawn step gets
        /// false and changes nothing, so a chest cannot be re-rolled by someone
        /// arriving late.
        /// </summary>
        public static bool Register(long entityId, string key, int tier)
        {
            if (ByEntity.ContainsKey(entityId))
            {
                return false;
            }
            ByEntity[entityId] = new Entry(key ?? "", tier);
            return true;
        }

        /// <summary>True if this entity is a registered loot container.</summary>
        public static bool IsContainer(long entityId) => ByEntity.ContainsKey(entityId);

        /// <summary>This container's registration key, or null.</summary>
        public static string? KeyOf(long entityId) =>
            ByEntity.TryGetValue(entityId, out Entry entry) ? entry.Key : null;

        /// <summary>This container's island tier, or null.</summary>
        public static int? TierOf(long entityId) =>
            ByEntity.TryGetValue(entityId, out Entry entry) ? entry.Tier : (int?)null;

        /// <summary>
        /// Everything in this container, or an empty list when the entity is not a
        /// container. The single question the 1081 serve asks.
        /// </summary>
        public static IReadOnlyList<LootDrop> ContentsOf(long entityId)
        {
            if (!ByEntity.TryGetValue(entityId, out Entry entry))
            {
                return System.Array.Empty<LootDrop>();
            }
            return LootTable.Roll(entry.Key, entry.Tier);
        }

        /// <summary>How many containers are registered.</summary>
        public static int Count => ByEntity.Count;

        /// <summary>Clears the ledger. Tests only - a running server never does this.</summary>
        public static void Reset() => ByEntity.Clear();
    }
}

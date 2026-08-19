namespace WorldsAdriftRebornGameServer.Multiplayer.Loot
{
    /// <summary>One rolled item: what it is, how many, and at what quality.</summary>
    public readonly record struct LootDrop(string ItemTypeId, int Amount, int Quality);

    /// <summary>
    /// WHAT IS IN A GIVEN CONTAINER - decided once, from its key, forever.
    ///
    /// THE ROLL IS DETERMINISTIC, AND THAT IS NOT AN OPTIMISATION. Retail rolled
    /// loot on the GSim worker and stored the result, replicating it to clients as
    /// a filled <c>IslandLootState.pendingItems</c>. This server has no such store
    /// yet (that is Phase 2 of docs/plans/loot-containers.md), so the contents have
    /// to be REDERIVABLE instead. If they were random:
    ///
    ///   * two players standing at the same chest would see different loot, because
    ///     each peer's 1081 is serialised independently at checkout;
    ///   * a re-checkout - walk away, walk back - would reroll the same chest, so a
    ///     player could farm one container by pacing;
    ///   * a server restart would silently redistribute the world.
    ///
    /// Hashing the container's registration key gives one answer per container per
    /// tier, on every peer, on every serve, across restarts, with no state at all.
    /// It is the same trick <see cref="Resources.SurfacePlacementGenerator"/> uses
    /// to order placements without an RNG, and the same FNV-1a the fauna policies
    /// use. When Phase 2 adds the stored <c>opened</c>/<c>spawningTime</c> ledger,
    /// this becomes the ROLL and the ledger becomes the override - the roll does
    /// not go away.
    ///
    /// WHAT IS RECOVERED HERE: nothing but membership, and that comes from
    /// <see cref="LootScrapTable"/>. Retail's loot table did not ship; a census of
    /// the whole decompile for <c>lootTable|dropTable|dropChance|dropWeight</c>
    /// returns zero hits, and the nineteen tuning fields on
    /// <c>1244 LootablePerAreaDataState</c> are gone with it.
    ///
    /// WHAT IS INVENTED, all of it labelled WAREBORN TUNING below: how many items a
    /// container holds, and the fact that every eligible item is equally likely.
    /// The one structural clue retail left about weighting is that
    /// <c>IslandLootSpawnerState.*BaseBudget</c> is a <c>float</c> PER CONTAINER -
    /// so retail rolled a value budget, not a fixed item count. A budget model
    /// needs per-item values that also did not ship, so a flat count over a
    /// tier-filtered pool is the honest reduction: it invents one number instead of
    /// a hundred and thirty-three.
    ///
    /// Pure: arithmetic and the embedded table. No I/O beyond that embed.
    /// </summary>
    public static class LootTable
    {
        /// <summary>WAREBORN TUNING. The fewest items a container ever holds.</summary>
        public const int MinItems = 2;

        /// <summary>
        /// WAREBORN TUNING. The most. Five 2x2-ish pieces of scrap is a satisfying
        /// find that still fits a container grid alongside the largest 5x3 rows.
        /// </summary>
        public const int MaxItems = 5;

        /// <summary>
        /// Scrap is not stackable - no <c>scrapItem-*</c> row in itemData.json
        /// carries a <c>stacksize</c> - so every drop is one item. RECOVERED.
        /// </summary>
        public const int DropAmount = 1;

        /// <summary>
        /// Quality 0, the same value every other grant on this server uses. Retail's
        /// quality scale is 1-10 and applies to MATERIALS; a piece of scrap carries
        /// its quality in the <c>rewards</c> block it salvages into
        /// (<c>{"a": 80, "q": 6, "item": "titanium"}</c>), not on the scrap itself.
        /// </summary>
        public const int DropQuality = 0;

        /// <summary>
        /// Everything in this container, in grid order. Empty only when the tier has
        /// no eligible scrap at all, which cannot happen for tiers 1-4.
        /// </summary>
        public static IReadOnlyList<LootDrop> Roll(string? containerKey, int islandTier)
        {
            IReadOnlyList<LootScrapEntry> pool = LootScrapTable.ForTier(islandTier);
            if (pool.Count == 0 || string.IsNullOrEmpty(containerKey))
            {
                return System.Array.Empty<LootDrop>();
            }

            uint seed = Hash(containerKey!);
            int count = MinItems + (int)(seed % (uint)(MaxItems - MinItems + 1));

            List<LootDrop> drops = new(count);
            HashSet<string> taken = new(StringComparer.Ordinal);

            // Draw WITHOUT replacement: a chest holding three identical Tonking
            // Pucks reads as a bug even when it is not, and the pool is never
            // smaller than 32 so the walk always terminates well inside the guard.
            uint cursor = seed;
            int guard = 0;
            while (drops.Count < count && guard < count * 16)
            {
                guard++;
                cursor = Mix(cursor);
                LootScrapEntry entry = pool[(int)(cursor % (uint)pool.Count)];
                if (!taken.Add(entry.ItemTypeId))
                {
                    continue;
                }
                drops.Add(new LootDrop(entry.ItemTypeId, DropAmount, DropQuality));
            }

            return drops;
        }

        /// <summary>
        /// FNV-1a over the container key. The same hash and constants the fauna
        /// policies and the placement generator use, so "deterministic" means one
        /// thing across this assembly.
        /// </summary>
        internal static uint Hash(string value)
        {
            const uint OffsetBasis = 2166136261;
            const uint Prime = 16777619;

            uint hash = OffsetBasis;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= Prime;
            }
            return hash;
        }

        /// <summary>One more avalanche round, so successive draws do not correlate.</summary>
        private static uint Mix(uint value)
        {
            const uint Prime = 16777619;
            value ^= value >> 13;
            value *= Prime;
            value ^= value >> 15;
            return value;
        }
    }
}

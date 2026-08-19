using System.Reflection;

namespace WorldsAdriftRebornGameServer.Multiplayer.Loot
{
    /// <summary>
    /// One salvageable scrap item a loot container may hold: its real retail id,
    /// its grid footprint, and the island tiers its salvage rewards are keyed by.
    /// </summary>
    public sealed class LootScrapEntry
    {
        internal LootScrapEntry(string itemTypeId, int width, int height, IReadOnlyList<int> tiers)
        {
            ItemTypeId = itemTypeId;
            Width = width;
            Height = height;
            Tiers = tiers;
        }

        /// <summary>The retail id, e.g. <c>scrapItem-tonkingpuck</c>. RECOVERED.</summary>
        public string ItemTypeId { get; }

        /// <summary>Grid width in cells, from itemData.json. RECOVERED.</summary>
        public int Width { get; }

        /// <summary>Grid height in cells, from itemData.json. RECOVERED.</summary>
        public int Height { get; }

        /// <summary>
        /// The island tiers this item's <c>rewards</c> block is keyed by, ascending.
        /// RECOVERED - it is the reward key set with the ".1"/".2" second-yield
        /// suffix stripped.
        /// </summary>
        public IReadOnlyList<int> Tiers { get; }

        /// <summary>Grid cells this item occupies unrotated.</summary>
        public int Cells => Width * Height;
    }

    /// <summary>
    /// WHICH SCRAP MAY APPEAR IN A CONTAINER, AND ON WHICH TIER OF ISLAND.
    ///
    /// This is the recovered half of the loot table, and the only half that is
    /// recovered. Retail's loot roll ran on the GSim worker and did not ship: a
    /// census of the whole decompile for
    /// <c>lootTable|dropTable|dropChance|dropWeight|generateLoot|lootItem|lootReward</c>
    /// returns zero hits, and all nineteen tuning fields on
    /// <c>1244 LootablePerAreaDataState</c> are likewise gone. So nobody can say
    /// how MANY items a chest held or how LIKELY any one was - see
    /// <see cref="LootTable"/>, where those numbers are labelled WAREBORN TUNING.
    ///
    /// What IS recoverable is membership, and it is recoverable exactly:
    ///
    ///   * The 133 <c>scrapItem-*</c> rows of category <c>Salvage</c> in
    ///     <c>itemData.json</c> are real retail ids. Each row's <c>iconName</c>
    ///     matches an entry in the shipped icon atlas (250 <c>scrap items/*</c>
    ///     icons in docs/research/valid-icons.txt), and the decompiled client
    ///     handles the <c>scrapItem-</c> prefix at
    ///     <c>InventoryTooltipPopup.cs:113</c> and <c>ScannableData.cs:368</c>,
    ///     where it reads <c>Meta["title"]</c>/<c>Meta["description"]</c> - which
    ///     is precisely the <c>metadata</c> block shape those rows carry. That is
    ///     a two-way match between the decompile and the data, not a guess.
    ///
    ///   * Each row's <c>rewards</c> block is KEYED BY TIER:
    ///     <c>{"3": {"a": 80, "q": 6, "item": "titanium"}}</c>. So the data itself
    ///     says which island tier a given piece of scrap belongs to. Tier 1 has 41
    ///     items, tier 2 has 50, tier 3 has 32, tier 4 has 85.
    ///
    /// WHY SCRAP AND NOT SOMETHING ELSE. Scrap is the one item family with an
    /// independent structural link to loot: <c>acs/RuinLootPreprocessor.cs:33</c>
    /// sets the ruin pile's open sound to <c>"Play_Scrap_Open"</c>. Nothing else
    /// in the shipped client points at container contents at all.
    ///
    /// AND NOT SCHEMATICS. A schematic WAS a real inventory item - <c>itemTypeId
    /// == "schematics"</c>, with LEARN and SALVAGE tooltip actions - but every
    /// acquisition path visible in the shipped client runs through the knowledge
    /// tree, which is why <c>KnowledgeUseResponseType</c> lists
    /// <c>FullInventory</c> as a way to fail BUYING a node. Putting schematics in
    /// chests is not supported by any surviving artefact and is not done here.
    ///
    /// The table is an embedded, diffable, generated file
    /// (<c>tools/world-import/generate-loot-scrap-tiers.py</c>) rather than a read
    /// of <c>itemData.json</c>, because this assembly deliberately references
    /// nothing - that is what lets the roll be unit-tested with no game install.
    /// <c>LootScrapTableIntegrityTests</c> re-reads <c>itemData.json</c> and
    /// asserts the two still agree, so they cannot drift apart silently.
    /// </summary>
    public static class LootScrapTable
    {
        /// <summary>The lowest and highest island tier the table is keyed by.</summary>
        public const int MinTier = 1;
        public const int MaxTier = 4;

        internal const string ResourceName = "loot-scrap-tiers.txt";

        private static IReadOnlyList<LootScrapEntry>? _all;
        private static readonly Dictionary<int, IReadOnlyList<LootScrapEntry>> ByTier = new();

        /// <summary>Every salvageable scrap row, sorted by id.</summary>
        public static IReadOnlyList<LootScrapEntry> All => _all ??= Load();

        /// <summary>
        /// The scrap a tier-<paramref name="tier"/> island's containers may hold.
        /// An out-of-range tier is clamped rather than throwing: an island whose
        /// MapFile cell tier and survey tier disagree is a real, preserved
        /// condition on this world and it must not be able to empty a chest.
        /// </summary>
        public static IReadOnlyList<LootScrapEntry> ForTier(int tier)
        {
            int clamped = System.Math.Clamp(tier, MinTier, MaxTier);
            if (ByTier.TryGetValue(clamped, out IReadOnlyList<LootScrapEntry>? cached))
            {
                return cached;
            }

            List<LootScrapEntry> rows = new();
            foreach (LootScrapEntry entry in All)
            {
                for (int i = 0; i < entry.Tiers.Count; i++)
                {
                    if (entry.Tiers[i] == clamped)
                    {
                        rows.Add(entry);
                        break;
                    }
                }
            }

            ByTier[clamped] = rows;
            return rows;
        }

        /// <summary>The entry with this id, or null.</summary>
        public static LootScrapEntry? ById(string? itemTypeId)
        {
            if (string.IsNullOrEmpty(itemTypeId)) return null;
            foreach (LootScrapEntry entry in All)
            {
                if (string.Equals(entry.ItemTypeId, itemTypeId, StringComparison.Ordinal))
                {
                    return entry;
                }
            }
            return null;
        }

        /// <summary>Whether this id is one this table can put in a container.</summary>
        public static bool IsLootScrap(string? itemTypeId) => ById(itemTypeId) != null;

        private static IReadOnlyList<LootScrapEntry> Load()
        {
            Assembly asm = typeof(LootScrapTable).Assembly;
            string? resource = null;
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith(ResourceName, StringComparison.Ordinal))
                {
                    resource = name;
                    break;
                }
            }

            if (resource == null)
            {
                // Fail LOUD rather than returning an empty table. An empty table
                // is a world full of empty chests, which reads as "loot is broken"
                // and gives no clue that the build simply lost an embed.
                throw new System.IO.FileNotFoundException(
                    "embedded loot scrap table '" + ResourceName + "' not found in "
                    + asm.GetName().Name
                    + "; every loot container would be empty. Regenerate with "
                    + "tools/world-import/generate-loot-scrap-tiers.py and check the csproj embed.");
            }

            List<LootScrapEntry> rows = new();
            using System.IO.Stream stream = asm.GetManifestResourceStream(resource)!;
            using System.IO.StreamReader reader = new System.IO.StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] parts = line.Split('\t');
                if (parts.Length < 4) continue;

                string id = parts[0].Trim();
                if (!int.TryParse(parts[1], out int width)) continue;
                if (!int.TryParse(parts[2], out int height)) continue;

                List<int> tiers = new();
                foreach (string raw in parts[3].Split(','))
                {
                    if (int.TryParse(raw.Trim(), out int tier)) tiers.Add(tier);
                }
                if (tiers.Count == 0) continue;

                rows.Add(new LootScrapEntry(id, width, height, tiers));
            }

            return rows;
        }
    }
}

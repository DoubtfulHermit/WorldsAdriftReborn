using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Loot;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Loot
{
    /// <summary>
    /// THE EMBEDDED SCRAP TABLE AGAINST ITS SOURCE.
    ///
    /// <c>Loot/loot-scrap-tiers.txt</c> is a projection of
    /// <c>Game/Items/Config/itemData.json</c>, made by
    /// <c>tools/world-import/generate-loot-scrap-tiers.py</c> because the pure
    /// Multiplayer assembly cannot read that file. A projection with no check is a
    /// second source of truth waiting to drift: someone adds a scrap row, containers
    /// silently never contain it, and nothing fails.
    ///
    /// So this reads itemData.json off disk exactly as the game server does and
    /// asserts the two agree, row for row, tier for tier, footprint for footprint.
    /// If it fails, run the generator - do not edit the table.
    /// </summary>
    public class LootScrapTableIntegrityTests
    {
        private const string ScrapPrefix = "scrapItem-";
        private const string SalvageCategory = "Salvage";

        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string probe = Path.Combine(dir.FullName,
                    "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "itemData.json");
                if (File.Exists(probe)) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the repo root from " + AppContext.BaseDirectory);
        }

        private static JArray Items() => JArray.Parse(File.ReadAllText(Path.Combine(
            RepoRoot(), "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "itemData.json")));

        /// <summary>The tiers a row's rewards block is keyed by, ".1" suffixes folded in.</summary>
        private static List<int> TiersOf(JObject row)
        {
            SortedSet<int> tiers = new();
            if (row["rewards"] is JObject rewards)
            {
                foreach (JProperty reward in rewards.Properties())
                {
                    string head = reward.Name.Split('.')[0];
                    if (int.TryParse(head, out int tier)) tiers.Add(tier);
                }
            }
            return tiers.ToList();
        }

        private static Dictionary<string, JObject> SourceRows()
        {
            Dictionary<string, JObject> rows = new(StringComparer.Ordinal);
            foreach (JObject row in Items().OfType<JObject>())
            {
                string id = (string?)row["itemTypeID"] ?? "";
                if (!id.StartsWith(ScrapPrefix, StringComparison.Ordinal)) continue;
                if ((string?)row["category"] != SalvageCategory) continue;
                if (TiersOf(row).Count == 0) continue;
                rows[id] = row;
            }
            return rows;
        }

        [Fact]
        public void TheEmbeddedTableHoldsExactlyTheSalvageableScrapRows()
        {
            Dictionary<string, JObject> source = SourceRows();
            HashSet<string> embedded = new(LootScrapTable.All.Select(e => e.ItemTypeId), StringComparer.Ordinal);

            string[] missing = source.Keys.Where(k => !embedded.Contains(k)).OrderBy(k => k).ToArray();
            string[] extra = embedded.Where(k => !source.ContainsKey(k)).OrderBy(k => k).ToArray();

            Assert.True(missing.Length == 0,
                "itemData.json has scrap rows the embedded table does not: " + string.Join(", ", missing)
                + ". Run tools/world-import/generate-loot-scrap-tiers.py.");
            Assert.True(extra.Length == 0,
                "the embedded table has rows itemData.json does not: " + string.Join(", ", extra)
                + ". Run tools/world-import/generate-loot-scrap-tiers.py.");
        }

        [Fact]
        public void EveryEmbeddedRowCarriesTheSourceFootprintAndTiers()
        {
            Dictionary<string, JObject> source = SourceRows();

            foreach (LootScrapEntry entry in LootScrapTable.All)
            {
                JObject row = source[entry.ItemTypeId];
                Assert.Equal((int)row["width"]!, entry.Width);
                Assert.Equal((int)row["height"]!, entry.Height);
                Assert.Equal(TiersOf(row), entry.Tiers);
            }
        }

        [Fact]
        public void EveryRolledItemIsAGrantableCatalogueRow()
        {
            // The end-to-end version of the above: an itemTypeId the item database
            // has never heard of is an unguarded null dereference on the client
            // (InventoryItemManager.LookupItem), so a chest holding one is a crash,
            // not a cosmetic error.
            HashSet<string> catalogue = new(
                Items().Select(r => (string?)r["itemTypeID"]).Where(s => s != null)!,
                StringComparer.Ordinal);

            for (int tier = LootScrapTable.MinTier; tier <= LootScrapTable.MaxTier; tier++)
            {
                for (int i = 0; i < 60; i++)
                {
                    foreach (LootDrop drop in LootTable.Roll("loot-" + i, tier))
                    {
                        Assert.Contains(drop.ItemTypeId, catalogue);
                    }
                }
            }
        }

        [Fact]
        public void NoRolledItemIsASchematicOrAFoundersEntitlement()
        {
            // Schematics came from the knowledge tree, not from loot - every
            // acquisition path in the shipped client runs through a knowledge node
            // (KnowledgeUseResponseType.FullInventory is a way to fail BUYING one).
            // Putting them in chests is not supported by any surviving artefact, so
            // it must not happen by accident either. The Founder's Tome is an
            // entitlement and belongs to nobody who did not buy it.
            for (int tier = LootScrapTable.MinTier; tier <= LootScrapTable.MaxTier; tier++)
            {
                for (int i = 0; i < 60; i++)
                {
                    foreach (LootDrop drop in LootTable.Roll("loot-" + i, tier))
                    {
                        Assert.DoesNotContain("schematic", drop.ItemTypeId, StringComparison.OrdinalIgnoreCase);
                        Assert.DoesNotContain("Founder", drop.ItemTypeId, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
    }
}

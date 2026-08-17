using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The RECONSTRUCTED atlas reference data as SHIPPED in the config files, read from
    /// disk exactly as the game server loads it (ItemHelper / SchematicHelper). Proves
    /// the acquisition loop can actually complete end to end: the granted item is a real
    /// catalogue row (so InventoryService.Grant accepts it rather than rolling back), and
    /// the three Atlas recipes consume that same real id in the recovered counts with no
    /// dangling material reference. The retail values are lost (findings-atlas-refdata.md);
    /// these assertions lock in the documented reconstruction.
    /// </summary>
    public class AtlasRefdataIntegrityTests
    {
        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string probe = Path.Combine(dir.FullName,
                    "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "schematicData.json");
                if (File.Exists(probe))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the repo root from " + AppContext.BaseDirectory);
        }

        private static string ConfigPath(string file) => Path.Combine(
            RepoRoot(), "WorldsAdriftRebornGameServer", "Game", "Items", "Config", file);

        private static JArray Items() => JArray.Parse(File.ReadAllText(ConfigPath("itemData.json")));

        private static JObject Schematics() => JObject.Parse(File.ReadAllText(ConfigPath("schematicData.json")));

        private static JObject ItemRow(JArray items, string id) =>
            (JObject)items.First(r => (string?)r["itemTypeID"] == id);

        private static HashSet<string> ItemIds(JArray items) =>
            new HashSet<string>(items.Select(r => (string?)r["itemTypeID"]).Where(s => s != null)!);

        [Fact]
        public void The_atlasShard_row_exists_and_is_a_grantable_metal()
        {
            JArray items = Items();
            Assert.Contains("atlasShard", ItemIds(items));

            JObject shard = ItemRow(items, "atlasShard");
            // The id the pickup transaction grants and the recipes consume.
            Assert.Equal(AtlasShardCatalogue.ItemTypeId, (string?)shard["itemTypeID"]);
            Assert.Equal("Atlas Shard", (string?)shard["name"]);
            // Raw mined resource -> the Metal category (no stone/mineral category exists;
            // it is mined from a metal deposit exactly like iron). Reconstructed choice.
            Assert.Equal("Metal", (string?)shard["category"]);
            // A footprint is what makes it grantable (InventoryWire.Footprints reads w/h);
            // a small, valuable 2x2 fragment - smaller than iron's 3x2.
            Assert.True((int)shard["width"]! >= 1 && (int)shard["width"]! <= 2);
            Assert.True((int)shard["height"]! >= 1 && (int)shard["height"]! <= 2);
            // Rare: elevated rarity vs common metals (iron=0), and a low stack.
            Assert.True((int)shard["rarity"]! > 0);
            Assert.True((int)shard["stacksize"]! >= 1 && (int)shard["stacksize"]! < 99);
            // A real icon reference, never blank.
            Assert.False(string.IsNullOrWhiteSpace((string?)shard["iconName"]));
        }

        [Theory]
        [InlineData("atlasSkyCore", 1)]
        [InlineData("skyCoreAtlasEnhancer", 3)]
        [InlineData("atlasLifter", 2)]
        public void Each_atlas_recipe_consumes_the_real_shard_in_the_recovered_count(string recipeId, int expectedShards)
        {
            JObject schematics = Schematics();
            Assert.True(schematics[recipeId] is JObject, $"recipe '{recipeId}' is absent from the catalogue.");

            JArray reqs = (JArray)schematics[recipeId]!["craftingRequirements"]!;
            JObject shardSlot = (JObject)reqs.First(r => (string?)r["component"] == "Atlas Shards");

            // The shard slot must name the REAL item, not the atlashod placeholder or the
            // collapsed-family iron.
            Assert.Equal("atlasShard", (string?)shardSlot["name"]);
            Assert.NotEqual("scrapItem-atlashod", (string?)shardSlot["name"]);
            Assert.NotEqual("iron", (string?)shardSlot["name"]);
            // The recovered wiki count (1/3/2).
            Assert.Equal(expectedShards, (int)shardSlot["amountRequired"]!);
        }

        [Theory]
        [InlineData("atlasSkyCore")]
        [InlineData("skyCoreAtlasEnhancer")]
        [InlineData("atlasLifter")]
        public void Every_material_in_an_atlas_recipe_resolves_to_a_real_item_row(string recipeId)
        {
            HashSet<string> itemIds = ItemIds(Items());
            JArray reqs = (JArray)Schematics()[recipeId]!["craftingRequirements"]!;

            foreach (JToken req in reqs)
            {
                string? matId = (string?)req["name"];
                Assert.False(string.IsNullOrWhiteSpace(matId), $"{recipeId} has a blank material id.");
                Assert.True(itemIds.Contains(matId!),
                    $"{recipeId} references material '{matId}' with no itemData.json row (dangling id).");
            }
        }

        [Fact]
        public void The_atlashod_placeholder_is_gone_from_the_atlas_recipe_shard_slots()
        {
            JObject schematics = Schematics();
            foreach (string recipeId in new[] { "atlasSkyCore", "skyCoreAtlasEnhancer", "atlasLifter" })
            {
                JArray reqs = (JArray)schematics[recipeId]!["craftingRequirements"]!;
                foreach (JToken req in reqs)
                {
                    Assert.NotEqual("scrapItem-atlashod", (string?)req["name"]);
                }
            }
        }

        [Fact]
        public void The_recipe_outputs_are_themselves_real_catalogue_items()
        {
            // Sanity: the things you craft (the sky core family) are real rows too, so the
            // whole shard -> recipe -> item chain resolves.
            HashSet<string> itemIds = ItemIds(Items());
            Assert.Contains("atlasSkyCore", itemIds);
            Assert.Contains("skyCoreAtlasEnhancer", itemIds);
            Assert.Contains("atlasLifter", itemIds);
        }
    }
}

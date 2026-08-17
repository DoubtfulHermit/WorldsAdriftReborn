using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The FUEL reference data as SHIPPED in the config files, read from disk exactly
    /// as the game server loads it. Proves the gather->craft loop can complete: the
    /// item a SALVAGED fuel canister grants (<c>"fuel"</c>) is a real grantable
    /// Fuel-category row, and the combustion/light recipes consume that same real id in
    /// the slot that already carries the fuel-container icon (a placeholder correction,
    /// not an invention - the same class of fix as the atlas shard slot).
    ///
    /// The per-canister yield is NOT lost: 3 shots of 8/8/9 = 25 fuel is a recovered
    /// retail figure, pinned in <see cref="FuelCanisterYieldTests"/>. What remains
    /// unknown is the per-recipe fuel COUNT (how much fuel a torch really costs), which
    /// stays at the shipped amountRequired - see docs/research/findings-combustion-fuel.md §6.
    /// </summary>
    public class FuelRefdataIntegrityTests
    {
        /// <summary>The recipes whose fuel-icon slot must consume the real "fuel" item.</summary>
        public static readonly string[] FuelRecipes =
            { "torch", "hipLamp", "headTorch", "campFire", "stove", "lamp" };

        private const string FuelIcon = "misc craft materials/2x2_Fuel_container";

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
        public void The_fuel_row_exists_and_is_a_grantable_fuel_resource()
        {
            JArray items = Items();
            Assert.Contains("fuel", ItemIds(items));

            JObject fuel = ItemRow(items, "fuel");
            // The id the pickup transaction grants and the recipes consume.
            Assert.Equal(FuelPods.ItemTypeId, (string?)fuel["itemTypeID"]);
            Assert.Equal("Fuel", (string?)fuel["name"]);
            // Category "Fuel" is what ItemHelper treats as a resource (isResource), so
            // InventoryService.Grant accepts + stacks it.
            Assert.Equal("Fuel", (string?)fuel["category"]);
            // A footprint is what makes it grantable (InventoryWire reads w/h).
            Assert.True((int)fuel["width"]! >= 1);
            Assert.True((int)fuel["height"]! >= 1);
            // Stackable, so repeated pod pickups merge rather than flooding the grid.
            Assert.True((int)fuel["stacksize"]! > 1);
            Assert.False(string.IsNullOrWhiteSpace((string?)fuel["iconName"]));
        }

        [Theory]
        [InlineData("torch")]
        [InlineData("hipLamp")]
        [InlineData("headTorch")]
        [InlineData("campFire")]
        [InlineData("stove")]
        [InlineData("lamp")]
        public void Each_combustion_recipe_consumes_the_real_fuel_item_in_its_fuel_slot(string recipeId)
        {
            JObject schematics = Schematics();
            Assert.True(schematics[recipeId] is JObject, $"recipe '{recipeId}' is absent from the catalogue.");

            JArray reqs = (JArray)schematics[recipeId]!["craftingRequirements"]!;
            // The slot that carries the FUEL-CONTAINER icon is the fuel slot.
            JObject fuelSlot = (JObject)reqs.First(r => (string?)r["iconId"] == FuelIcon);

            // It must now name the REAL "fuel" item, not the old birch placeholder.
            Assert.Equal("fuel", (string?)fuelSlot["name"]);
            Assert.NotEqual("birch", (string?)fuelSlot["name"]);
            Assert.True((int)fuelSlot["amountRequired"]! >= 1);
        }

        [Theory]
        [InlineData("torch")]
        [InlineData("hipLamp")]
        [InlineData("headTorch")]
        [InlineData("campFire")]
        [InlineData("stove")]
        [InlineData("lamp")]
        public void Every_material_in_a_fuel_recipe_resolves_to_a_real_item_row(string recipeId)
        {
            HashSet<string> itemIds = ItemIds(Items());
            JArray reqs = (JArray)Schematics()[recipeId]!["craftingRequirements"]!;

            foreach (JToken req in reqs)
            {
                string? matId = (string?)req["name"];
                Assert.False(string.IsNullOrWhiteSpace(matId), $"{recipeId} has a blank material id.");
                // A requirement is EITHER one concrete item id OR a material FAMILY
                // ("Metal"/"Wood"/"Wood/Metal"), which is retail's own form and what
                // the client's crafting slot tests against
                // (InventoryItemManager.IsSameMaterialType, VERIFIED). A family has no
                // itemData row of its own by design; what must not dangle is a
                // requirement that is neither.
                Assert.True(itemIds.Contains(matId!) || MaterialCatalog.IsFamily(matId),
                    $"{recipeId} references material '{matId}' that is neither an " +
                    "itemData.json row nor a known material family (dangling id).");
            }
        }

        [Fact]
        public void No_fuel_icon_slot_anywhere_is_left_on_the_birch_placeholder()
        {
            JObject schematics = Schematics();
            foreach (JProperty recipe in schematics.Properties())
            {
                if (recipe.Value["craftingRequirements"] is not JArray reqs)
                {
                    continue;
                }
                foreach (JToken req in reqs)
                {
                    if ((string?)req["iconId"] == FuelIcon)
                    {
                        Assert.Equal("fuel", (string?)req["name"]);
                    }
                }
            }
        }

        [Fact]
        public void The_recipe_outputs_are_themselves_real_catalogue_items()
        {
            HashSet<string> itemIds = ItemIds(Items());
            foreach (string recipeId in FuelRecipes)
            {
                Assert.Contains(recipeId, itemIds);
            }
        }
    }
}

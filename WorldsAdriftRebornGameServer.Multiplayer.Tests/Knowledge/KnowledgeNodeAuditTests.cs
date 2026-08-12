using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Knowledge
{
    /// <summary>
    /// The node -> recipe -> station AUDIT. Each visible tree node must learn the recipe
    /// a player expects from that node's NAME, and that recipe's category must route it
    /// to the right UI:
    ///   CraftingStation -> the Assembly Station (ItemCraft tab),
    ///   Personal        -> the personal Crafting tab (MultitoolCraft),
    ///   Cooking         -> the Cooking tab,
    ///   Shipyard        -> the shipyard/ship-build flow.
    /// These lock in the faithful mapping after the grant-all revert AND the fact that
    /// a player who has unlocked the tree can actually build a functional ship.
    /// </summary>
    public class KnowledgeNodeAuditTests
    {
        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string probe = Path.Combine(
                    dir.FullName,
                    "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "schematicData.json");
                if (File.Exists(probe))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the repo root from " + AppContext.BaseDirectory);
        }

        private static JObject Catalogue()
        {
            string path = Path.Combine(
                RepoRoot(),
                "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "schematicData.json");
            return JObject.Parse(File.ReadAllText(path));
        }

        private static string CategoryOf(JObject cat, string recipeId)
        {
            Assert.True(cat[recipeId] is JObject, $"recipe '{recipeId}' is absent from the served catalogue.");
            return (string?)cat[recipeId]!["category"] ?? "";
        }

        [Theory]
        // --- Propulsion: engine + wing craft at the ASSEMBLY STATION ---------------
        [InlineData("EnginesRootSchematic", "proceduralEngineDefault", "CraftingStation")]
        [InlineData("WingsRootSchematic", "proceduralWingDefault", "CraftingStation")]
        // --- Weapons: the roots grant their projectile (personal Crafting tab) ------
        [InlineData("CannonsRootSchematic", "cannonball", "Personal")]
        [InlineData("SwivelGunRootSchematic", "swivelGunShell", "Personal")]
        [InlineData("PistolsRootSchematic", "pistol", "Personal")]
        [InlineData("PistolsSchematic2", "pistolBullets", "Personal")]
        // --- Shipbuilding baseline: hull basics at the ASSEMBLY STATION -------------
        [InlineData("Shipbuilding", "shipyard", "Personal")]     // the deployable you place
        [InlineData("Shipbuilding", "deck", "CraftingStation")]
        [InlineData("Shipbuilding", "helm", "CraftingStation")]
        [InlineData("Shipbuilding", "sail", "CraftingStation")]
        // --- Skyship Builder: structure at the ASSEMBLY STATION ---------------------
        [InlineData("Stairs", "stairs", "CraftingStation")]
        [InlineData("Medium Panel", "mediumPanel", "CraftingStation")]
        [InlineData("Crows Nest", "smallPanel", "CraftingStation")]
        // --- Atlas Engineer: the sky cores at the ASSEMBLY STATION ------------------
        [InlineData("Atlas Core Enhancer", "atlasSkyCore", "CraftingStation")]
        [InlineData("Atlas Core Enhancer", "skyCoreAtlasEnhancer", "CraftingStation")]
        [InlineData("Atlas Core Generator", "skyCoreGenerator", "CraftingStation")]
        [InlineData("Lifter", "atlasLifter", "Personal")]
        // --- Explorer: instruments + reviver ---------------------------------------
        [InlineData("Fuel Gauge", "fuelGauge", "CraftingStation")]
        [InlineData("Compass", "headingIndicator", "CraftingStation")]
        [InlineData("Makeshift Bandages", "personalReviver", "CraftingStation")]
        // --- Tradesman: furniture / storage ----------------------------------------
        [InlineData("Storage Container", "storageContainer", "CraftingStation")]
        [InlineData("Trunk", "trunk", "CraftingStation")]
        [InlineData("Metal Chair", "cupboard", "CraftingStation")]
        // --- Cooking: the cooked food + drink in the Cooking tab --------------------
        [InlineData("Thuntomite Steak", "thuntomiteSteak", "Cooking")]
        [InlineData("Manta Steak", "mantaSteak", "Cooking")]
        [InlineData("Manta Burger", "moonshine", "Cooking")]
        // --- Territory -------------------------------------------------------------
        [InlineData("Territory Control Tower", "territory_control_beacon", "Shipyard")]
        public void Node_learns_the_expected_recipe_in_the_expected_category(
            string nodeId, string expectedRecipe, string expectedCategory)
        {
            IReadOnlyList<string> learned = KnowledgeSpendPolicy.SchematicIdsFor(nodeId);

            Assert.Contains(expectedRecipe, learned);
            Assert.Equal(expectedCategory, CategoryOf(Catalogue(), expectedRecipe));
        }

        /// <summary>
        /// The headline of the whole change: a player who unlocked ENGINES sees the
        /// engine at the ASSEMBLY STATION (CraftingStation category).
        /// </summary>
        [Fact]
        public void Unlocking_Engines_puts_the_engine_at_the_assembly_station()
        {
            Assert.Contains("proceduralEngineDefault", KnowledgeSpendPolicy.SchematicIdsFor("EnginesRootSchematic"));
            Assert.Equal("CraftingStation", CategoryOf(Catalogue(), "proceduralEngineDefault"));
        }

        /// <summary>
        /// A player who has unlocked Shipbuilding + Engines + Wings + Atlas Engineer can
        /// build a FUNCTIONAL ship: deck, helm, sail, an engine, a wing and a sky core -
        /// none of them stranded with "nowhere to unlock".
        /// </summary>
        [Fact]
        public void The_functional_ship_baseline_is_reachable_from_the_owned_roots()
        {
            var owned = new[] { "Shipbuilding", "EnginesRootSchematic", "WingsRootSchematic", "Atlas Core Enhancer" };
            var learned = new HashSet<string>(owned.SelectMany(KnowledgeSpendPolicy.SchematicIdsFor));

            foreach (string part in new[]
            {
                "deck", "helm", "sail", "proceduralEngineDefault", "proceduralWingDefault", "atlasSkyCore",
            })
            {
                Assert.Contains(part, learned);
            }
        }

        /// <summary>
        /// After recategorisation the Assembly Station (CraftingStation) shows a RICH,
        /// GROUPED set - the basics/skyCore/structural/storage/decoration/instruments/
        /// power headers, plus the modular engine/wing - not just lamp + engine.
        /// </summary>
        [Fact]
        public void The_assembly_station_shows_a_rich_grouped_ship_part_set()
        {
            JObject cat = Catalogue();
            var stationItemTypes = new HashSet<string>();
            int stationCount = 0;
            foreach (KeyValuePair<string, JToken?> kv in cat)
            {
                if ((string?)kv.Value?["category"] == "CraftingStation")
                {
                    stationCount++;
                    string? itemType = (string?)kv.Value?["itemType"];
                    Assert.False(string.IsNullOrEmpty(itemType), $"{kv.Key}: CraftingStation part has no itemType header.");
                    stationItemTypes.Add(itemType!);
                }
            }

            // Far richer than the old "lamp + engine" bench.
            Assert.True(stationCount >= 30, $"expected a rich assembly station, got only {stationCount} parts.");

            // The WA-style category headers are all present.
            foreach (string header in new[]
            {
                "basics", "skyCore", "structural", "storage", "decoration", "instruments", "power",
            })
            {
                Assert.Contains(header, stationItemTypes);
            }
        }
    }
}

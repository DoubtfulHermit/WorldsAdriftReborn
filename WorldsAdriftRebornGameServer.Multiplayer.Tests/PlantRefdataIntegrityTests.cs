using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Gathering;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// THE PLANT reference data as SHIPPED, read off disk exactly as the game
    /// server loads it, in the same shape as
    /// <see cref="FuelRefdataIntegrityTests"/>.
    ///
    /// WHY THIS FILE IS THE IMPORTANT ONE FOR PLANT FIBRE. The yield tables next
    /// door prove the harvest RESOLVES three materials off one cut. They cannot
    /// prove the player gets them, because the grant is refused - silently, with a
    /// warning nobody reads - for any itemTypeId that is not a row in
    /// itemData.json. So a tree could register fibre, resolve fibre, and hand the
    /// player nothing, with the wood still landing so that nothing looked wrong.
    /// That is the exact failure shape the tree work fell into once already: green
    /// tests over a pure model whose output never reached production.
    ///
    /// These tests are the join. They read the REAL config the server serves as
    /// 1097 reference data and assert that what the harvest names is what the
    /// catalogue contains.
    /// </summary>
    public class PlantRefdataIntegrityTests
    {
        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string probe = Path.Combine(dir.FullName,
                    "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "itemData.json");
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

        private static JObject Row(JArray items, string id) =>
            (JObject)items.First(r => (string?)r["itemTypeID"] == id);

        [Fact]
        public void Everything_a_tree_yields_is_a_real_item_row()
        {
            // THE JOIN. If a yield names an id the catalogue does not carry, the
            // grant is refused and the player is quietly handed nothing.
            JArray items = Items();
            HashSet<string> ids = new(items
                .Select(r => (string?)r["itemTypeID"])
                .Where(s => s != null)!, StringComparer.Ordinal);

            HarvestYield yields = new();

            foreach (string wood in TreeSpecies.Woods)
            {
                TreeYield.RegisterSpecies(yields, wood);
            }

            foreach (string wood in TreeSpecies.Woods)
            {
                foreach (YieldGrant grant in yields.Resolve(wood, 1))
                {
                    Assert.Contains(grant.ItemTypeId, ids);
                }
            }
        }

        [Fact]
        public void Plant_fibre_is_stackable_or_a_tree_fills_the_grid()
        {
            // Every section of every tree pays fibre. Unstackable fibre would put a
            // fresh tile in the grid per section and fill a player's inventory on
            // one tree, which reads as the harvest being broken.
            JObject fibre = Row(Items(), TreeYield.PlantFiberItemTypeId);

            Assert.True((int?)fibre["stacksize"] > 1,
                "plantFiber must stack; it is paid once per felled section");
        }

        [Fact]
        public void Berries_are_stackable_too()
        {
            JObject berries = Row(Items(), TreeYield.DaccatBerriesItemTypeId);

            Assert.True((int?)berries["stacksize"] > 1,
                "daccatBerries must stack; they are paid once per felled section");
        }

        [Fact]
        public void The_plant_rows_carry_the_categories_retail_keyed_on()
        {
            // NOT cosmetic, and not a guess in either case.
            //
            // daccatBerries: the client's collect-SFX table is keyed on the item's
            // CATEGORY and contains the literal entry
            // { "daccatBerries", "PlantsVegetation" }
            // (acs/Travellers.UI.PlayerInventory/InventoryContents.cs:55, consumed at
            // :551 via changed.Key.category). A different category means the wrong
            // pickup sound.
            //
            // plantFiber: Bossa's own quest condition asks for it BY CATEGORY -
            // HaveItemByCategory{ itemCategory: "plantFiber" }
            // (docs/research/loop/data/quest-conditions.json:74).
            JArray items = Items();

            Assert.Equal(TreeYield.DaccatBerriesItemTypeId,
                (string?)Row(items, TreeYield.DaccatBerriesItemTypeId)["category"]);
            Assert.Equal(TreeYield.PlantFiberItemTypeId,
                (string?)Row(items, TreeYield.PlantFiberItemTypeId)["category"]);
        }

        [Fact]
        public void The_plant_rows_point_at_icons_the_client_actually_ships()
        {
            // A missing icon is a soft failure - InventoryIconManager falls back to
            // placeholder_icon and logs - but a wrong PATH is a permanent
            // placeholder, and these two are the icons the shipped census carries.
            JArray items = Items();

            Assert.Equal("materials/1x2_plantfiber",
                (string?)Row(items, TreeYield.PlantFiberItemTypeId)["iconName"]);
            Assert.Equal("foods/2x2_berries",
                (string?)Row(items, TreeYield.DaccatBerriesItemTypeId)["iconName"]);
        }

        [Fact]
        public void The_plant_row_footprints_match_their_icon_dimensions()
        {
            // itemData's convention is WxH in the icon prefix. A row whose grid does
            // not match its art places wrongly and can throw on the client mid-refresh.
            JArray items = Items();
            JObject fibre = Row(items, TreeYield.PlantFiberItemTypeId);
            JObject berries = Row(items, TreeYield.DaccatBerriesItemTypeId);

            Assert.Equal(1, (int?)fibre["width"]);
            Assert.Equal(2, (int?)fibre["height"]);
            Assert.Equal(2, (int?)berries["width"]);
            Assert.Equal(2, (int?)berries["height"]);
        }

        [Fact]
        public void Makeshift_cloth_consumes_the_plant_fibre_its_label_promises()
        {
            // THE LIE, pinned. The consumption key is `name` and the label the
            // player reads is `component`; this recipe said "Plant Fibers" and ate
            // iron. It is also the recipe Bossa's own tutorial drives
            // (quests.json:2124, itemIdToLookFor "clothMakeshift"), so it is the
            // first thing a new player would have found dishonest.
            JObject cloth = (JObject)Schematics()["clothMakeshift"]!;
            JArray requirements = (JArray)cloth["craftingRequirements"]!;
            JObject slot = (JObject)requirements.Single();

            Assert.Equal("Plant Fibers", (string?)slot["component"]);
            Assert.Equal(TreeYield.PlantFiberItemTypeId, (string?)slot["name"]);
        }

        [Fact]
        public void No_recipe_still_claims_plant_fibers_while_eating_something_else()
        {
            // The general form, so the next one of these is caught rather than
            // discovered. Any slot LABELLED as plant fibers must consume plantFiber.
            foreach (JProperty schematic in Schematics().Properties())
            {
                JToken? requirements = schematic.Value["craftingRequirements"];

                if (requirements is not JArray slots)
                {
                    continue;
                }

                foreach (JObject slot in slots.OfType<JObject>())
                {
                    string label = (string?)slot["component"] ?? "";

                    if (!label.Contains("Plant Fiber", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Assert.Equal(TreeYield.PlantFiberItemTypeId, (string?)slot["name"]);
                }
            }
        }
    }
}

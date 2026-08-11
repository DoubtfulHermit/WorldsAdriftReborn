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
    /// Overnight CRASH-SAFETY validator. The UNMODIFIED Unity client parses the served
    /// recipe catalogue (1097 schematicData.json) in full on reference-data load and
    /// looks recipes up again when it renders the crafting/schematics tabs and the
    /// knowledge-tree node info panels. Several of those parse/lookup sites THROW an
    /// UNCAUGHT exception on bad data and blank the whole panel. There is no way to
    /// click through the live UI (Wine ignores synthetic input), so correctness is
    /// asserted here against the client's EXACT constraints, recovered from the
    /// decompiled client (acs/, gencode/). Each constraint below cites the client
    /// file:line it protects.
    ///
    /// Valid CraftingCategory (acs/Travellers.UI.PlayerInventory/CraftingCategory.cs):
    ///   Shipyard=0, Personal=1, CraftingStation=2, Cooking=3, Clothing=4, None=5.
    /// Valid SchematicType (acs/SchematicType.cs): Fixed=0, Procedural=1, Ship=2.
    /// Valid SchematicsRarity (gencode/.../SchematicsRarity.cs): Tier1..Tier6 = 0..5.
    /// Valid CipherShipPartType (gencode/.../CipherShipPartType.cs):
    ///   Engine, Wing, Cannon, SwivelGun.
    /// </summary>
    public class ReferenceDataCrashSafetyTests
    {
        // acs/CharacterLearnedSchematicLibrary.cs:342 Enum.Parse(category) then :344
        // indexer into _schematicDataListByCraftingCategory whose keys are exactly the
        // five below (ctor :183-224). "None" PARSES but KeyNotFounds at :344, so it is
        // NOT valid here.
        private static readonly HashSet<string> ValidCategories = new()
        {
            "Shipyard", "Personal", "CraftingStation", "Cooking", "Clothing",
        };

        // acs/SchematicData.cs:208 Enum.Parse(SchematicsRarity) on a cipher slot's
        // "rarity" string; :211 Enum.Parse(CipherShipPartType) on "shipPartType".
        private static readonly HashSet<string> ValidRarityNames = new()
        {
            "Tier1", "Tier2", "Tier3", "Tier4", "Tier5", "Tier6",
        };

        private static readonly HashSet<string> ValidShipPartTypes = new()
        {
            "Engine", "Wing", "Cannon", "SwivelGun",
        };

        private const int MaxRarityInt = 5;      // Tier6 (acs/RarityHelper.cs:114 dict indexer)
        private const int MaxSchematicType = 2;  // Ship
        private const int MaxStatBars = 10;      // acs/SchematicsSubScreen.cs:331 bar array

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
            throw new DirectoryNotFoundException(
                "Could not locate the repo root (no WorldsAdriftRebornGameServer/Game/Items/Config/schematicData.json above " +
                AppContext.BaseDirectory + ").");
        }

        private static JObject Catalogue()
        {
            string path = Path.Combine(
                RepoRoot(),
                "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "schematicData.json");
            return JObject.Parse(File.ReadAllText(path));
        }

        private static JObject KnowledgeTree()
        {
            string path = Path.Combine(
                RepoRoot(),
                "WorldsAdriftRebornGameServer", "Game", "Knowledge", "Config", "knowledge-tree.json");
            return JObject.Parse(File.ReadAllText(path));
        }

        private static JArray ItemData()
        {
            string path = Path.Combine(
                RepoRoot(),
                "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "itemData.json");
            return JArray.Parse(File.ReadAllText(path));
        }

        // The exact set of textures the UNMODIFIED client can resolve, extracted from the
        // client's own resource catalogue (globalgamemanagers ResourceManager m_Container)
        // and saved to docs/research/valid-icons.txt. Each line is a catalogue path with the
        // leading "icons/" stripped and lowercased -- i.e. the value the client resolves via
        // Resources.Load("Icons/" + iconName). Resources.Load lookup is case-insensitive, so
        // membership is tested lowercased.
        private static HashSet<string> ValidIcons()
        {
            string path = Path.Combine(RepoRoot(), "docs", "research", "valid-icons.txt");
            return File.ReadAllLines(path)
                .Select(l => l.Trim().ToLowerInvariant())
                .Where(l => l.Length > 0)
                .ToHashSet();
        }

        public static IEnumerable<object[]> AllRecipes()
        {
            foreach (KeyValuePair<string, JToken?> kv in Catalogue())
            {
                yield return new object[] { kv.Key };
            }
        }

        /// <summary>
        /// Every catalogue record must survive the client's reference-data load and
        /// per-schematic render without an uncaught throw.
        /// </summary>
        [Theory]
        [MemberData(nameof(AllRecipes))]
        public void Recipe_is_crash_safe(string id)
        {
            JObject cat = Catalogue();
            JObject r = (JObject)cat[id]!;

            // --- category: non-null, exactly one of the 5 mapped names, case-sensitive.
            // Protects acs/CharacterLearnedSchematicLibrary.cs:342 (Enum.Parse) and :344
            // (dict indexer). Also acs/SchematicsReferenceStore.cs:36 ContainsKey(null).
            string? category = (string?)r["category"];
            Assert.True(category != null, $"{id}: category is null (ArgumentNullException at SchematicsReferenceStore.cs:36).");
            Assert.True(ValidCategories.Contains(category!),
                $"{id}: category '{category}' is not one of {string.Join('/', ValidCategories)} " +
                "(Enum.Parse ArgumentException / KeyNotFound at CharacterLearnedSchematicLibrary.cs:342/344).");

            // --- SchematicType: valid enum int. acs/SchematicData.cs:99.
            int schematicType = (int?)r["SchematicType"] ?? 0;
            Assert.InRange(schematicType, 0, MaxSchematicType);

            // --- rarity: <= Tier6. rarity>=6 -> KeyNotFound at RarityHelper.cs:114 via
            // SchematicsSubScreen.SetName. Negatives clamp (SchematicData.cs:179) so only
            // the upper bound bites.
            int rarity = (int?)r["rarity"] ?? 0;
            int rarityParsed = (int?)r["rarityParsed"] ?? 0;
            Assert.True(rarity <= MaxRarityInt, $"{id}: rarity {rarity} > {MaxRarityInt} (KeyNotFound at RarityHelper.cs:114).");
            Assert.True(rarityParsed <= MaxRarityInt, $"{id}: rarityParsed {rarityParsed} > {MaxRarityInt}.");

            // --- non-null string fields the render path dereferences.
            // title -> GetFormattedTitle().CapitaliseFirstLetter (SchematicData.cs:285),
            // description -> SetDescription / KnowledgeInfoPanel string.Format,
            // iconId -> SetNonProceduralIcon .Replace (SchematicsSubScreen.cs:265),
            // itemType -> hierarchy bucket key (CharacterLearnedSchematicLibrary.cs:343).
            foreach (string field in new[] { "title", "description", "iconId", "itemType" })
            {
                Assert.True(r[field] != null && r[field]!.Type != JTokenType.Null,
                    $"{id}: required field '{field}' is missing/null (NRE risk on render).");
            }

            // --- baseStats: recognised stats + hp bar must fit the prefab bar array
            // (acs/SchematicsSubScreen.cs:331 _schematicAttributeBars[num++], no bounds
            // check). Conservative cap at the StatsOrder maximum.
            JToken? baseStats = r["baseStats"];
            if (baseStats is JObject statsObj)
            {
                Assert.True(statsObj.Count <= MaxStatBars,
                    $"{id}: {statsObj.Count} baseStats exceeds {MaxStatBars} attribute bars (IndexOutOfRange at SchematicsSubScreen.cs:331).");
            }

            // --- cipherSlots: each map's "rarity"/"shipPartType" must be a valid enum
            // name, "stats" valid JSON. Uncaught Enum.Parse at SchematicData.cs:208/211.
            if (r["cipherSlots"] is JArray ciphers)
            {
                foreach (JToken slot in ciphers)
                {
                    if (slot is not JObject slotObj)
                    {
                        continue;
                    }
                    string? cr = (string?)slotObj["rarity"];
                    if (cr != null)
                    {
                        Assert.True(ValidRarityNames.Contains(cr),
                            $"{id}: cipher rarity '{cr}' invalid (Enum.Parse throw at SchematicData.cs:208).");
                    }
                    string? sp = (string?)slotObj["shipPartType"];
                    if (sp != null)
                    {
                        Assert.True(ValidShipPartTypes.Contains(sp),
                            $"{id}: cipher shipPartType '{sp}' invalid (Enum.Parse throw at SchematicData.cs:211).");
                    }
                }
            }
        }

        /// <summary>
        /// The Phase-0 starter ship-part set MUST be present in the served catalogue and
        /// crash-safe. The client learns/renders these as craftable ship parts, so a
        /// missing record (or a bad category/itemType) is either a silent no-show or an
        /// uncaught throw on the schematics/knowledge panels. These are the ids the ship
        /// build effort proves against; lamp + helm are the first proof targets.
        ///
        /// Categories are asserted against the client's routing (CraftingUI splits craft
        /// actions by CraftingCategory): functional MODULAR parts (engine/wing) are
        /// CraftingStation-category assembly-station schematics per SchematicData.cs's
        /// modular item types (engine/cannon/wing/swivelGun) and the assemblyStation
        /// ("construct ship parts and equipment"); helm/lamp/sail/storage are the
        /// bolt-on/utility parts the branch surfaces through the Shipyard flow. Every
        /// value below is also covered field-by-field by <see cref="Recipe_is_crash_safe"/>.
        /// </summary>
        [Theory]
        [InlineData("proceduralEngineDefault", "CraftingStation", "engine")]
        [InlineData("proceduralWingDefault", "Shipyard", "proceduralWing")]
        [InlineData("helm", "Shipyard", "helm")]
        [InlineData("lamp", "CraftingStation", "lamp")]
        [InlineData("sail", "Shipyard", "sail")]
        [InlineData("makeshiftStorage", "Personal", "makeshiftStorage")]
        [InlineData("storageContainer", "Shipyard", "storageContainer")]
        public void Starter_ship_part_is_served_and_correctly_categorised(string id, string category, string itemType)
        {
            JObject cat = Catalogue();
            Assert.True(cat[id] is JObject, $"{id}: starter ship-part record absent from the served catalogue.");
            JObject r = (JObject)cat[id]!;

            Assert.Equal(category, (string?)r["category"]);
            Assert.True(ValidCategories.Contains((string?)r["category"]!),
                $"{id}: category is not a client-parseable CraftingCategory.");
            Assert.Equal(itemType, (string?)r["itemType"]);
            Assert.False(string.IsNullOrEmpty((string?)r["itemType"]),
                $"{id}: itemType is empty (hierarchy bucket key at CharacterLearnedSchematicLibrary.cs:343).");
        }

        // CraftingCategory enum ints, from the class doc above
        // (acs/Travellers.UI.PlayerInventory/CraftingCategory.cs).
        private static readonly Dictionary<string, int> CategoryEnumInt = new()
        {
            ["Shipyard"] = 0,
            ["Personal"] = 1,
            ["CraftingStation"] = 2,
            ["Cooking"] = 3,
            ["Clothing"] = 4,
            ["None"] = 5,
        };

        /// <summary>
        /// A schematic's numeric "CraftingCategoryEnum" field MUST equal the enum value its
        /// "category" string maps to. The client itself IGNORES the numeric field and derives
        /// the category by Enum.Parse(category) (acs/SchematicData.cs:165, a get-only property
        /// Json.NET cannot set), so a stale numeric value never bit the client directly - but
        /// a schematic whose loaded category has no built slot in the tab it is shown in is
        /// exactly what NREs CraftingStationSchematicList.SelectSchematic
        /// (CategoryPressed(schematic.CraftingCategoryEnum) -> null -> deref, cs:365-385) and
        /// blanks the Crafting tab. Keeping the numeric field consistent with the string keeps
        /// the two views of a schematic's category from ever disagreeing - any server-side
        /// binning that trusts the number then agrees with the client that trusts the string.
        /// (Found live: lamp shipped category "CraftingStation" but CraftingCategoryEnum 0
        /// (Shipyard).)
        /// </summary>
        [Theory]
        [MemberData(nameof(AllRecipes))]
        public void Recipe_numeric_category_matches_its_category_string(string id)
        {
            JObject r = (JObject)Catalogue()[id]!;
            string? category = (string?)r["category"];
            Assert.NotNull(category);
            Assert.True(CategoryEnumInt.ContainsKey(category!),
                $"{id}: category '{category}' is not a known CraftingCategory name.");

            int? enumInt = (int?)r["CraftingCategoryEnum"];
            Assert.True(enumInt.HasValue, $"{id}: CraftingCategoryEnum is missing.");
            Assert.True(enumInt!.Value == CategoryEnumInt[category!],
                $"{id}: CraftingCategoryEnum {enumInt} disagrees with category '{category}' " +
                $"(expected {CategoryEnumInt[category!]}). The client reads the STRING; keep the number in sync.");
        }

        /// <summary>
        /// Every SCHEMATIC_FIXED knowledge node's baked schematicId MUST resolve in the
        /// served catalogue. KnowledgeInfoPanel.cs:92-93 does
        /// <c>LookupSchematic(node.schematicId).GetFormattedTitle()</c> with no null
        /// check when that node's info panel opens; a miss is an uncaught NRE that
        /// breaks the node popup. The served catalogue is a 53-recipe subset, so the
        /// weapon/utility fixed nodes (pistol, cannonball, moonshine, powerGenerator01,
        /// swivelGunShell, territory_control_beacon) need catalogue stubs.
        /// </summary>
        [Fact]
        public void Every_fixed_node_schematicId_resolves_in_catalogue()
        {
            JObject cat = Catalogue();
            JObject tree = KnowledgeTree();
            var missing = new List<string>();

            foreach (JToken node in (JArray)tree["nodes"]!)
            {
                if ((string?)node["nodeType"] != "SCHEMATIC_FIXED")
                {
                    continue;
                }
                string schId = (string?)node["schematicId"] ?? "";
                // Empty schematicId also NREs (LookupSchematic("") returns null).
                if (string.IsNullOrEmpty(schId) || cat[schId] == null)
                {
                    missing.Add($"{(string?)node["id"]} -> '{schId}'");
                }
            }

            Assert.True(missing.Count == 0,
                "SCHEMATIC_FIXED nodes whose schematicId is absent from the catalogue " +
                "(KnowledgeInfoPanel.cs:92 NRE on info-panel open): " + string.Join(", ", missing.Distinct()));
        }

        /// <summary>
        /// Node rarity drives RarityHelper.GetRarityColoursForButtonStates
        /// (KnowledgeInfoPanel.cs:94/103 -> RarityHelper.cs:114 dict indexer) for
        /// SCHEMATIC_FIXED / SCHEMATIC_RANDOM node info panels. rarity &gt;= 6 KeyNotFounds.
        /// </summary>
        [Fact]
        public void Every_node_rarity_is_within_enum_range()
        {
            JObject tree = KnowledgeTree();
            var bad = new List<string>();
            foreach (JToken node in (JArray)tree["nodes"]!)
            {
                int rarity = (int?)node["rarity"] ?? 0;
                if (rarity < 0 || rarity > MaxRarityInt)
                {
                    bad.Add($"{(string?)node["id"]}={rarity}");
                }
            }
            Assert.True(bad.Count == 0, "Node rarity out of Tier1..Tier6 range: " + string.Join(", ", bad));
        }

        /// <summary>
        /// Whatever the 1334 handler would learn on a node purchase must be crash-safe:
        /// SchematicIdFor(node) either resolves to a catalogue key (then that record is
        /// covered by <see cref="Recipe_is_crash_safe"/>) or it does not (the handler's
        /// only-learn-catalogue-ids guard drops it, so nothing unlearnable reaches the
        /// client). This asserts the SchematicAliases table never points at a
        /// non-existent recipe, which would be a silent dead unlock.
        /// </summary>
        [Fact]
        public void Every_alias_target_resolves_in_catalogue()
        {
            JObject cat = Catalogue();
            JObject tree = KnowledgeTree();
            var broken = new List<string>();

            foreach (JToken node in (JArray)tree["nodes"]!)
            {
                string nodeType = (string?)node["nodeType"] ?? "";
                bool learns = nodeType is "SCHEMATIC_LIST" or "SCHEMATIC_FIXED" or "SCHEMATIC_RANDOM";
                if (!learns)
                {
                    continue;
                }
                string nodeId = (string?)node["id"] ?? "";
                string learned = KnowledgeSpendPolicy.SchematicIdFor(nodeId);

                // A learned id that IS a catalogue key must be a real record (safe to
                // learn). A learned id that is NOT a catalogue key is dropped by the
                // handler guard, which is the intended no-op for un-recovered nodes.
                if (cat[learned] != null)
                {
                    Assert.True(cat[learned] is JObject, $"{nodeId}: learned id '{learned}' is not a record object.");
                }
                else if (nodeId != learned)
                {
                    // An alias was applied but its target is missing -> a mapping bug.
                    broken.Add($"{nodeId} -> '{learned}'");
                }
            }

            Assert.True(broken.Count == 0,
                "SchematicAliases entries whose target recipe is absent from the catalogue: " +
                string.Join(", ", broken));
        }

        /// <summary>
        /// Every schematic's iconId MUST name a real baked client texture. The client renders
        /// it via InventoryIconManager.GetIconTexture("Icons/" + iconId)
        /// (acs/InventoryIconManager.cs:44); a name the resource catalogue does not contain
        /// resolves to Icons/placeholder_icon -- the pink box -- instead of the intended art.
        /// Validated against the client's own icon catalogue (docs/research/valid-icons.txt).
        /// </summary>
        [Fact]
        public void Every_schematic_iconId_is_a_real_client_texture()
        {
            HashSet<string> valid = ValidIcons();
            var bad = new List<string>();
            foreach (KeyValuePair<string, JToken?> kv in Catalogue())
            {
                string? icon = (string?)kv.Value?["iconId"];
                if (icon == null || !valid.Contains(icon.Trim().ToLowerInvariant()))
                {
                    bad.Add($"{kv.Key} -> '{icon}'");
                }
            }
            Assert.True(bad.Count == 0,
                "Schematic iconIds that resolve to the pink placeholder_icon (not in the client's " +
                "icon catalogue): " + string.Join(", ", bad));
        }

        /// <summary>
        /// Every item's iconName MUST name a real baked client texture, same resolution path
        /// as above (InventoryIconManager.cs:44/64). An unknown name is the pink placeholder
        /// box in the inventory grid.
        /// </summary>
        [Fact]
        public void Every_item_iconName_is_a_real_client_texture()
        {
            HashSet<string> valid = ValidIcons();
            var bad = new List<string>();
            foreach (JToken entry in ItemData())
            {
                string? icon = (string?)entry["iconName"];
                if (icon == null || !valid.Contains(icon.Trim().ToLowerInvariant()))
                {
                    bad.Add($"{(string?)entry["itemTypeID"]} -> '{icon}'");
                }
            }
            Assert.True(bad.Count == 0,
                "Item iconNames that resolve to the pink placeholder_icon (not in the client's " +
                "icon catalogue): " + string.Join(", ", bad));
        }
    }
}

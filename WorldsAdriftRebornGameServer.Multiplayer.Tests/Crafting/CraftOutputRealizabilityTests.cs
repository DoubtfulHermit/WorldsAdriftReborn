using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crafting
{
    /// <summary>
    /// THE CLASS-KILLER for "crafted X, resources eaten, nothing appears": every
    /// recipe in the shipped schematicData.json must have a REALIZABLE output -
    /// something the player can actually SEE after the craft - or be on the
    /// explicit, documented blocked list (refused up front, so it can never eat
    /// materials).
    ///
    /// Realizable means, by category:
    ///   - Personal / Cooking (inventory grants): the output itemType exists in
    ///     itemData.json, so InventoryPolicy.TryGrant can place it. The handler
    ///     refuses unknown outputs BEFORE consuming, but a dangling id would make
    ///     the recipe silently uncraftable - this catches it at commit time.
    ///   - CraftingStation (loose world parts): the recipe has a
    ///     LoosePartCatalogue row AND that row's prefab is in the REAL extracted
    ///     client prefab census (ClientEntityPrefabs, the runtime copy) - the
    ///     client can only instantiate what it can load.
    ///   - Shipyard: the explicit blocked list. Not spawnable as a loose part
    ///     today; the station path refuses it pre-consume (no catalogue row) and
    ///     the category gate refuses it at both targets, so it costs nothing.
    ///
    /// A NEW recipe whose output dangles - a typo'd itemType, a station recipe
    /// with no catalogue row, a catalogue row with an unloadable prefab, or a
    /// whole new category nobody classified - fails here, at build time, instead
    /// of eating a player's materials in the live game.
    /// </summary>
    public class CraftOutputRealizabilityTests
    {
        // Categories whose output is granted to the INVENTORY (must exist in itemData.json).
        private static readonly HashSet<string> InventoryCategories =
            new(StringComparer.Ordinal) { "Personal", "Cooking" };

        // The category whose output is a LOOSE WORLD PART (must have a catalogue row + loadable prefab).
        private const string StationCategory = "CraftingStation";

        // Recipes that are DELIBERATELY not craftable today. Every entry must be
        // refused up front (no LoosePartCatalogue row -> the station handler
        // rejects before consuming; the category gate rejects it in both crafting
        // contexts), so being here means "blocked and honest", never "eats
        // materials". Shrink this list by implementing the recipe, not by
        // deleting the assert.
        private static readonly Dictionary<string, string> KnownBlocked = new(StringComparer.Ordinal)
        {
            ["territory_control_beacon"] = "Shipyard-category; the territory flow does not exist yet. "
                + "Refused pre-consume at every target (category gate + no catalogue row).",
        };

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

        private static JObject Schematics() => JObject.Parse(File.ReadAllText(ConfigPath("schematicData.json")));

        private static HashSet<string> ItemTypeIds()
        {
            JArray items = JArray.Parse(File.ReadAllText(ConfigPath("itemData.json")));
            return new HashSet<string>(
                items.Select(row => (string?)row["itemTypeID"]).Where(id => !string.IsNullOrEmpty(id))!,
                StringComparer.Ordinal);
        }

        [Fact]
        public void Every_recipe_output_is_realizable_or_explicitly_blocked()
        {
            JObject schematics = Schematics();
            HashSet<string> itemIds = ItemTypeIds();
            List<string> failures = new();

            foreach (KeyValuePair<string, JToken?> pair in schematics)
            {
                string schematicId = pair.Key;
                JToken recipe = pair.Value!;
                string category = (string?)recipe["category"] ?? "";
                string itemType = (string?)recipe["itemType"] ?? "";

                if (KnownBlocked.ContainsKey(schematicId))
                {
                    // Blocked-and-honest: it must NOT have a catalogue row (that is
                    // what guarantees the station path refuses it before consuming).
                    if (LoosePartCatalogue.IsLoosePart(schematicId))
                    {
                        failures.Add(schematicId + ": is on the KnownBlocked list but HAS a catalogue row -"
                            + " it would consume materials; unblock it properly or remove the row.");
                    }
                    continue;
                }

                if (InventoryCategories.Contains(category))
                {
                    if (!itemIds.Contains(itemType))
                    {
                        failures.Add(schematicId + " (" + category + "): output itemType '" + itemType
                            + "' is not in itemData.json - the craft would be refused (or worse, dangle).");
                    }
                    continue;
                }

                if (category == StationCategory)
                {
                    if (!LoosePartCatalogue.IsLoosePart(schematicId))
                    {
                        failures.Add(schematicId + " (CraftingStation): no LoosePartCatalogue row -"
                            + " the bench shows it but the craft is refused; add a row (with a"
                            + " census-verified prefab) or move it to KnownBlocked.");
                        continue;
                    }

                    LoosePartDefinition part = LoosePartCatalogue.ForSchematic(schematicId)!;
                    if (!ClientEntityPrefabs.CanResolve(part.PrefabName))
                    {
                        failures.Add(schematicId + " (CraftingStation): catalogue prefab '" + part.PrefabName
                            + "' is not in the client prefab census - the part would spawn INVISIBLE.");
                    }
                    continue;
                }

                failures.Add(schematicId + ": category '" + category + "' has no classified output path -"
                    + " decide whether it grants an item, spawns a part, or belongs on KnownBlocked.");
            }

            Assert.True(failures.Count == 0,
                "Recipes with unrealizable outputs (each one is a live 'materials eaten, nothing"
                + " appears' bug waiting for a player):\n  " + string.Join("\n  ", failures));
        }

        [Fact]
        public void Every_catalogue_part_resolves_through_the_RUNTIME_census()
        {
            // LoosePartTests pins the catalogue against the TEST-embedded census copy;
            // this pins it against the RUNTIME copy the live refusal gate actually
            // consults (ClientEntityPrefabs, embedded in the Multiplayer assembly), so
            // a packaging mistake there cannot silently turn the gate into
            // refuse-everything (or a stale copy into allow-anything).
            foreach (LoosePartDefinition part in LoosePartCatalogue.All)
            {
                Assert.True(ClientEntityPrefabs.CanResolve(part.PrefabName),
                    "Catalogue part '" + part.SchematicId + "' prefab '" + part.PrefabName
                    + "' does not resolve through the runtime census.");
            }
        }

        [Fact]
        public void Runtime_census_is_loaded_and_matches_the_test_census()
        {
            // The runtime census must have genuinely loaded (fail-closed means an
            // empty set refuses every craft - loud in play, but this catches it in CI)...
            Assert.True(ClientEntityPrefabs.All.Count > 100,
                "The runtime client prefab census failed to load (only "
                + ClientEntityPrefabs.All.Count + " names) - every station craft would be refused.");

            // ...and must be the SAME set the tests embed, so the two copies cannot drift.
            HashSet<string> testCopy = LoadTestCensus();
            HashSet<string> runtime = new(ClientEntityPrefabs.All, StringComparer.Ordinal);
            Assert.True(runtime.SetEquals(testCopy),
                "Ship/client-entity-prefabs.txt differs between the Multiplayer project (runtime)"
                + " and the Tests project (pin) - update both together.");
        }

        [Fact]
        public void Known_good_and_known_bad_prefabs_classify_correctly_at_runtime()
        {
            // The live bug's own prefab must resolve (CoreMain IS a real client asset -
            // the invisibility was the client-side load race, not the name)...
            Assert.True(ClientEntityPrefabs.CanResolve("CoreMain"));
            Assert.True(ClientEntityPrefabs.CanResolve("Helm01"));
            // ...case-insensitively, exactly as the client lower-cases bundle names...
            Assert.True(ClientEntityPrefabs.CanResolve("coremain"));
            // ...and the historical bad guess (the invisible lamp) must NOT resolve.
            Assert.False(ClientEntityPrefabs.CanResolve("Lamp"));
            Assert.False(ClientEntityPrefabs.CanResolve(null));
            Assert.False(ClientEntityPrefabs.CanResolve("  "));
        }

        private static HashSet<string> LoadTestCensus()
        {
            System.Reflection.Assembly asm = typeof(CraftOutputRealizabilityTests).Assembly;
            string name = asm.GetManifestResourceNames()
                .Single(n => n.EndsWith("client-entity-prefabs.txt", StringComparison.Ordinal));
            using Stream stream = asm.GetManifestResourceStream(name)!;
            using StreamReader reader = new(stream);
            HashSet<string> set = new(StringComparer.Ordinal);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    set.Add(trimmed.ToLowerInvariant());
                }
            }
            return set;
        }
    }
}

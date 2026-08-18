using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftServer.Admin
{
    /// <summary>
    /// Read-only, allowlisted projection of Bossa's preserved release MapFile.
    /// Geography is loaded once from the embedded research artifact; live ships
    /// and players remain a separate authoritative game-stats stream.
    ///
    /// Each island also carries what is actually seeded ON it - databanks, metal
    /// deposits by ore type, trees - joined from
    /// <see cref="IslandResourceInventoryCatalog"/> on the only key the MapFile
    /// has, its "&lt;workshopId&gt;.json" asset name. Those counts are counts of real
    /// entities, and each carries the provenance of the survey it came from so an
    /// inferred ore table can never be drawn as a recovered one.
    /// </summary>
    internal static class ReleaseWorldMap
    {
        private static readonly Lazy<string> Projected = new(BuildProjectedJson);

        internal static string Json => Projected.Value;

        private static string BuildProjectedJson()
        {
            Assembly assembly = typeof(ReleaseWorldMap).Assembly;
            string? resourceName = assembly.GetManifestResourceNames()
                .SingleOrDefault(name => name.EndsWith("wamap-islands.json",
                    StringComparison.Ordinal));
            if (resourceName == null)
                throw new InvalidOperationException("Embedded release world map is missing.");

            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("Embedded release world map could not be opened.");
            using StreamReader reader = new(stream);
            JObject source = JObject.Parse(reader.ReadToEnd());

            double edge = (double?)source["WorldInfo"]?["WorldEdgeLength"] ?? 0;
            double havenSeparatorX = (double?)source["Haven"]?["xOfVerticalSeparator"] ?? 0;
            if (edge <= 0 || havenSeparatorX <= 0
                || source["Islands"] is not JArray sourceIslands
                || source["Walls"] is not JArray sourceWalls
                || source["Biomes"] is not JArray sourceBiomes)
                throw new InvalidOperationException("Embedded release world map has an invalid shape.");

            JArray islands = new();
            foreach (JObject island in sourceIslands.OfType<JObject>())
            {
                string asset = (string?)island["Island"] ?? string.Empty;
                JObject projectedIsland = new()
                {
                    ["x"] = (double?)island["x"] ?? 0,
                    ["y"] = (double?)island["y"] ?? 0,
                    ["z"] = (double?)island["z"] ?? 0,
                    ["asset"] = asset,
                    ["haven"] = string.Equals(asset, "1431299145.json", StringComparison.Ordinal),
                };
                IslandResourceInventory? inventory =
                    IslandResourceInventoryCatalog.ByMapAsset(asset);
                if (inventory != null)
                    projectedIsland["inventory"] = ProjectInventory(inventory);
                islands.Add(projectedIsland);
            }

            // Bossa left two cells' District null. The runtime catalogue names them
            // "unassigned-t<type>-<n>", n being their rank when the null cells are
            // sorted by (z, x) - so the same rule is applied here, and the drawn
            // cell can be joined to the islands the catalogue put in it.
            var nullCells = sourceBiomes.OfType<JObject>()
                .Where(biome => (string?)biome["District"] == null)
                .OrderBy(biome => (double?)biome["z"] ?? 0)
                .ThenBy(biome => (double?)biome["x"] ?? 0)
                .ToList();

            JArray biomes = new();
            foreach (JObject biome in sourceBiomes.OfType<JObject>())
            {
                int type = (int?)biome["Type"] ?? 0;
                if (type is < 1 or > 4) continue;
                string? district = (string?)biome["District"];
                biomes.Add(new JObject
                {
                    ["x"] = (double?)biome["x"] ?? 0,
                    ["z"] = (double?)biome["z"] ?? 0,
                    ["type"] = type,
                    ["civilization"] = (int?)biome["Civ"] ?? 0,
                    // Preserve Bossa's two explicit nulls. Inventing E1/E2 or
                    // folding these cells into E3 would falsify the MapFile.
                    ["district"] = district == null ? JValue.CreateNull() : district,
                    ["authoredDistrict"] = district != null,
                    ["cellId"] = district
                        ?? $"unassigned-t{type}-{nullCells.IndexOf(biome) + 1}",
                });
            }

            JArray walls = new();
            foreach (JObject wall in sourceWalls.OfType<JObject>())
            {
                int type = (int?)wall["Type"] ?? -1;
                if (type is not (0 or 1 or 2 or 3 or 4 or 5)) continue;
                walls.Add(new JObject
                {
                    ["x1"] = (double?)wall["x1"] ?? 0,
                    ["z1"] = (double?)wall["z1"] ?? 0,
                    ["x2"] = (double?)wall["x2"] ?? 0,
                    ["z2"] = (double?)wall["z2"] ?? 0,
                    ["type"] = type,
                });
            }

            IslandResourceTotals totals = IslandResourceInventoryCatalog.Totals;
            JObject projected = new()
            {
                ["source"] = "preserved-release-mapfile",
                ["resourceTotals"] = new JObject
                {
                    ["islands"] = totals.Islands,
                    ["deposits"] = totals.Deposits,
                    ["databanks"] = totals.Databanks,
                    ["trees"] = totals.Trees,
                    ["woodedIslands"] = totals.WoodedIslands,
                    ["islandsWithRecoveredOres"] = totals.IslandsWithRecoveredOres,
                    ["islandsWithInferredOres"] = totals.IslandsWithInferredOres,
                    ["inferredDeposits"] = totals.InferredDeposits,
                },
                ["worldEdgeLength"] = edge,
                ["havenSeparatorX"] = havenSeparatorX,
                ["islands"] = islands,
                ["biomes"] = biomes,
                ["walls"] = walls,
            };

            using StringWriter output = new();
            using (JsonTextWriter writer = new(output))
            {
                writer.Formatting = Formatting.None;
                writer.StringEscapeHandling = StringEscapeHandling.EscapeHtml;
                projected.WriteTo(writer);
            }
            return output.ToString();
        }

        /// <summary>
        /// One island's seeded contents, in the compact shape the page draws from.
        ///
        /// Counts only. Nothing here is scaled, rounded or estimated: every number
        /// is the length of a list in the catalogue the game server itself seeds
        /// from. <c>oreSource</c> travels with the ore rows so the page can mark an
        /// inferred table wherever it shows one.
        /// </summary>
        private static JObject ProjectInventory(IslandResourceInventory inventory)
        {
            JArray ores = new();
            foreach (IslandOreTally ore in inventory.Ores)
                ores.Add(new JObject
                {
                    ["metal"] = ore.Metal,
                    ["quality"] = ore.Quality,
                    ["deposits"] = ore.Deposits,
                });

            return new JObject
            {
                ["name"] = inventory.DisplayName,
                ["islandId"] = inventory.IslandId.Value,
                ["cell"] = inventory.CellId,
                ["cellTier"] = inventory.CellTier,
                ["surveyTier"] = inventory.SurveyTier,
                ["culture"] = inventory.Culture,
                ["databanks"] = inventory.Databanks,
                ["deposits"] = inventory.Deposits,
                ["trees"] = inventory.Trees,
                ["woods"] = new JArray(inventory.TreeSpecies),
                ["fuelPods"] = inventory.FuelPods,
                ["lootContainers"] = inventory.LootContainers,
                ["revival"] = inventory.HasRevivalChamber,
                ["turrets"] = inventory.HasTurrets,
                ["dangerous"] = inventory.Dangerous,
                ["ores"] = ores,
                ["oreSource"] = inventory.MetalSource switch
                {
                    MetalTableSource.SurveyPve => "survey-pve",
                    MetalTableSource.SurveyPvp => "survey-pvp",
                    MetalTableSource.InferredTier => "inferred-tier",
                    _ => throw new InvalidOperationException(
                        $"Unhandled metal table source {inventory.MetalSource}."),
                },
                ["oresInferred"] = inventory.OresAreInferred,
            };
        }
    }
}

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
                {
                    projectedIsland["inventory"] = ProjectInventory(inventory);
                    projectedIsland["shell"] = ProjectShell(inventory);
                    projectedIsland["fauna"] = ProjectFauna(inventory);
                }
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
                ["faunaModel"] = ProjectFaunaModel(),
                ["worldEdgeLength"] = edge,
                ["havenSeparatorX"] = havenSeparatorX,
                ["islands"] = islands,
                ["biomes"] = biomes,
                ["walls"] = walls,
                ["cells"] = ProjectCellRollups(),
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
                // Wood carries its own provenance for the same reason ore does: 180
                // islands grow a species composed from the tier cohort rather than
                // one anybody recorded, and the page must be able to say so.
                ["woodSource"] = inventory.WoodSource switch
                {
                    WoodTableSource.Survey => "survey",
                    WoodTableSource.SurveyNone => "survey-none",
                    WoodTableSource.InferredTier => "inferred-tier",
                    _ => throw new InvalidOperationException(
                        $"Unhandled wood table source {inventory.WoodSource}."),
                },
                ["woodsInferred"] = inventory.WoodsAreInferred,
                ["workshopId"] = inventory.WorkshopId,
            };
        }

        /// <summary>
        /// The island's own preserved collision silhouette, as a flat
        /// <c>[x0,z0,x1,z1,...]</c> ring in island-local metres.
        ///
        /// It is projected FLAT and rounded to a decimetre on purpose: 254 rings
        /// of 16 points is the difference between a map that can draw the real
        /// coastline when you zoom in and one that can only ever draw a generic
        /// pin, and the flat form costs about a third of what an array of pairs
        /// would. A decimetre is far below one screen pixel at every zoom the map
        /// allows, so nothing visible is lost by rounding.
        ///
        /// The MapFile places islands by translation only - it carries x/y/z and
        /// no rotation - so world position is the island's origin plus this ring,
        /// with no orientation guesswork.
        /// </summary>
        private static JArray ProjectShell(IslandResourceInventory inventory)
        {
            JArray ring = new();
            foreach (IslandShellPoint point in inventory.Record.Shell)
            {
                ring.Add(Math.Round(point.X, 1));
                ring.Add(Math.Round(point.Z, 1));
            }
            return ring;
        }

        /// <summary>
        /// Every NUMBER the browser needs to evaluate the game server's own fauna
        /// movement, read from <see cref="IslandFaunaMapModel.Constants"/>.
        ///
        /// This is the whole reason the console can draw the wildlife moving
        /// smoothly off a three-second snapshot: the browser is not given
        /// positions, it is given the same closed form. Nothing here is a literal -
        /// every field is the game server's own constant - so retuning a manta's
        /// speed moves the map with it and cannot be forgotten. What the browser
        /// does restate is the SHAPE of the formulas, and
        /// AdminFaunaParityTests pins that against the C# at fixed timestamps.
        /// </summary>
        private static JObject ProjectFaunaModel()
        {
            FaunaMapConstants c = IslandFaunaMapModel.Constants;
            return new JObject
            {
                ["dayNightCycleSeconds"] = c.DayNightCycleSeconds,
                ["dayBeginsAtCycleFraction"] = c.DayBeginsAtCycleFraction,
                ["dayEndsAtCycleFraction"] = c.DayEndsAtCycleFraction,
                ["phaseTransitionFraction"] = c.PhaseTransitionFraction,
                ["jellyDayRadiusRatio"] = c.JellyDayRadiusRatio,
                ["jellyNightRadiusRatio"] = c.JellyNightRadiusRatio,
                ["jellySecondsPerRevolution"] = c.JellySecondsPerRevolution,
                ["walkableHeightFraction"] = c.IslandWalkableHeightFraction,
                ["mantaVerticalSpanRatio"] = c.MantaVerticalSpanRatio,
                ["mantaMetresPerSecond"] = c.MantaMetresPerSecond,
                ["mantaSchoolRadius"] = c.MantaSchoolRadiusMetres,
                ["mantaSchoolVerticalRadius"] = c.MantaSchoolVerticalRadiusMetres,
                ["jellyShoalRadius"] = c.JellyShoalRadiusMetres,
                ["jellyShoalVerticalRadius"] = c.JellyShoalVerticalRadiusMetres,
                ["weaveRadiansPerSecond"] = c.WeaveRadiansPerSecond,
                ["goldenAngleRadians"] = c.GoldenAngleRadians,
                ["goldenRatioFraction"] = c.GoldenRatioFraction,
                ["schoolsPerIsland"] = c.SchoolsPerIsland,
                // Ecology constants (v9). The per-island bloom parameters travel
                // in the LIVE feed - they depend on the game server's world seed,
                // which is that process's env and not this one's.
                ["mantaCirculationSigmaRatio"] = c.MantaCirculationSigmaRatio,
                ["jellyCirculationSigmaRatio"] = c.JellyCirculationSigmaRatio,
                ["mantaOrbitSpeed"] = c.MantaOrbitMetresPerSecond,
                ["jellyOrbitSpeed"] = c.JellyOrbitMetresPerSecond,
                ["maxGroupSpread"] = c.MaxGroupSpread,
                ["excursionRamp"] = c.ExcursionRampFraction,
                ["feedRadiusPinch"] = c.FeedRadiusPinch,
                ["diveBelowFloorFraction"] = c.DiveBelowFloorFraction,
                // The family's two lengths (Phase 5); the pairing itself travels
                // in the live feed, being seed-derived.
                ["calfTrailMetres"] = c.CalfTrailMetres,
                ["calfDropMetres"] = c.CalfDropMetres,
            };
        }

        /// <summary>
        /// One island's fauna geometry and its seeding plan, in ISLAND-LOCAL
        /// metres - the same frame the preserved coastline above is projected in,
        /// so a creature is always drawn in the right relationship to the rock
        /// under it.
        ///
        /// Everything shape-dependent is derived HERE by
        /// <see cref="IslandFaunaMapModel.MotionFor"/>, which calls the movement's
        /// own accessors; the browser is left only the part that depends on time.
        /// A half-diagonal computed twice is a half-diagonal that can differ.
        ///
        /// The counts are the SURVEY tier's, not the MapFile cell tier's, because
        /// that is the tier <c>IslandFaunaPolicy.PopulationFor</c> reads. On the
        /// islands where the two preserved tiers disagree the panel says so.
        /// </summary>
        private static JObject ProjectFauna(IslandResourceInventory inventory)
        {
            FaunaIslandMotion motion = IslandFaunaMapModel.MotionFor(inventory.Record.Envelope);
            FaunaIslandPopulation population =
                IslandFaunaMapModel.PopulationFor(inventory.SurveyTier);

            return new JObject
            {
                ["cx"] = Math.Round(motion.CentreX, 2),
                ["cy"] = Math.Round(motion.CentreY, 2),
                ["cz"] = Math.Round(motion.CentreZ, 2),
                ["minY"] = Math.Round(motion.MinY, 2),
                ["maxY"] = Math.Round(motion.MaxY, 2),
                ["halfHeight"] = Math.Round(motion.HalfHeightMetres, 3),
                ["mantaOrbitRadius"] = Math.Round(motion.MantaOrbitRadiusMetres, 3),
                // NOT ROUNDED, and that is a correctness rule rather than a
                // preference. Every other field here is an offset, so trimming it
                // costs a fixed millimetre. The lap time divides ELAPSED SECONDS,
                // so its error is multiplied by how long the server has been up: a
                // millisecond of rounding is a tenth of a metre after ten minutes
                // and nineteen metres after a day, which on a small island is the
                // far side of the orbit. A server that has been up for a week must
                // not draw its wildlife somewhere else.
                ["mantaLapSeconds"] = motion.MantaLapSeconds,
                ["jellyLateralRadius"] = Math.Round(motion.JellyLateralRadiusMetres, 3),
                ["manta"] = population.MantaRays,
                ["jelly"] = population.JellyFish,
                ["schools"] = population.Schools,
                ["mantaSchoolSize"] = population.MantaSchoolSize,
                ["jellyShoalSize"] = population.JellyShoalSize,
            };
        }

        /// <summary>
        /// Per-zone roll-ups, keyed by the same cell id the drawn tier cell
        /// carries, so clicking a zone answers with arithmetic done once in
        /// <see cref="IslandCellRollupCatalog"/> rather than re-summed in the
        /// browser while it paints.
        /// </summary>
        private static JObject ProjectCellRollups()
        {
            JObject cells = new();
            foreach (IslandCellRollup cell in IslandCellRollupCatalog.All)
            {
                JArray ores = new();
                foreach (IslandOreTally ore in cell.Ores)
                    ores.Add(new JObject
                    {
                        ["metal"] = ore.Metal,
                        ["quality"] = ore.Quality,
                        ["deposits"] = ore.Deposits,
                        // Weakened to inferred if ANY contributing island's table
                        // was composed - see IslandCellRollup for why.
                        ["inferred"] = ore.Provenance == ResourceProvenance.Inferred,
                    });

                cells[cell.CellId] = new JObject
                {
                    ["islands"] = cell.Islands,
                    ["databanks"] = cell.Databanks,
                    ["deposits"] = cell.Deposits,
                    ["trees"] = cell.Trees,
                    ["woodedIslands"] = cell.WoodedIslands,
                    ["islandsWithInferredOres"] = cell.IslandsWithInferredOres,
                    ["islandsWithRecoveredOres"] = cell.IslandsWithRecoveredOres,
                    ["inferredDeposits"] = cell.InferredDeposits,
                    ["woods"] = new JArray(cell.TreeSpecies),
                    ["ores"] = ores,
                };
            }
            return cells;
        }
    }
}

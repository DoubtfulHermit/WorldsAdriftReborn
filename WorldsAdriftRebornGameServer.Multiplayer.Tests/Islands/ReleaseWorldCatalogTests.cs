using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    public sealed class ReleaseWorldCatalogTests
    {
        [Fact]
        public void Complete_catalog_has_one_record_per_ordinary_release_asset()
        {
            Assert.Equal(254, ReleaseWorldCatalog.All.Count);
            Assert.Equal(254, ReleaseWorldCatalog.All.Select(x => x.Survey.WorkshopId).Distinct().Count());
            Assert.Equal(254, ReleaseWorldCatalog.All.Select(x => x.Definition.Id).Distinct().Count());
            Assert.Equal(254, ReleaseWorldCatalog.All.Select(x => x.Definition.WorldEntityKey).Distinct().Count());
            Assert.DoesNotContain(ReleaseWorldCatalog.All,
                x => x.Survey.WorkshopId == "1431299145");
            Assert.All(ReleaseWorldCatalog.All, record =>
            {
                Assert.Equal(16, record.Shell.Count);
                Assert.NotNull(IslandTerrainEnvelopes.ByIsland(record.Definition.Id));
                Assert.Equal(record.Survey.DatabankCount, record.Databanks.Count);
            });
        }

        /// <summary>
        /// The shell outline is the island's silhouette, so every point must be
        /// evidence: a measured surface sample, or a point on the chord between two
        /// of them. An empty angular bin once emitted a UNIT vector, putting a 1 m
        /// radius point between neighbours hundreds of metres out and pinching 12
        /// islands into spikes (83 points, worst 1 m against a real 599 m extent).
        /// The first repair reused a neighbour's RADIUS at the missing angle, which
        /// overshot the other way on long or concave islands - 66 points outside
        /// their own island, the worst by 383 m. Both failures are guarded here.
        /// </summary>
        [Fact]
        public void Every_shell_outline_point_stays_inside_its_own_island()
        {
            Assert.All(ReleaseWorldCatalog.All, record =>
            {
                IslandTerrainEnvelope envelope = record.Envelope;
                foreach (IslandShellPoint point in record.Shell)
                {
                    // No unit-vector fallback: that is the pinch signature.
                    Assert.True(Math.Sqrt(point.X * point.X + point.Z * point.Z) > 1.5,
                        record.Definition.Id + " has a degenerate outline point");
                    // Inside the measured footprint: that is the bulge signature.
                    Assert.InRange(point.X, envelope.MinX - 1, envelope.MaxX + 1);
                    Assert.InRange(point.Z, envelope.MinZ - 1, envelope.MaxZ + 1);
                }
            });
        }

        [Fact]
        public void Full_rollout_has_255_active_terrains_and_complete_cell_ownership()
        {
            IslandRegistry islands = IslandRegistry.CreateReleaseWorld("all");
            RegionRegistry regions = RegionRegistry.CreateReleaseWorld(islands, "all");
            Assert.Equal(255, islands.All.Count);
            Assert.Equal(21, regions.All.Count); // Haven plus the 20 exact MapFile cells.
            Assert.All(islands.All, island => Assert.NotNull(regions.ByIsland(island.Id)));
            Assert.Equal(255, regions.All.Sum(region => region.IslandIds.Count));
        }

        [Fact]
        public void District_rollout_is_exact_and_does_not_invent_null_district_names()
        {
            IReadOnlyList<ReleaseIslandRecord> b3 = ReleaseWorldRolloutPolicy.Select("B3");
            Assert.NotEmpty(b3);
            Assert.All(b3, island => Assert.Equal("B3", island.CellId));
            Assert.Empty(ReleaseWorldRolloutPolicy.Select("E1,E2"));
            Assert.Equal(2, ReleaseWorldCatalog.All.Select(x => x.CellId)
                .Where(id => id.StartsWith("unassigned-t4-", StringComparison.Ordinal))
                .Distinct().Count());
        }

        /// <summary>
        /// THIS TEST CHANGED DELIBERATELY: 354 -> 1930 deposits. Databanks are
        /// untouched at 1233 because those ARE an exact surveyed count for all 254
        /// islands; only the metal tables had a coverage gap. The density rule
        /// (ceil(LOD0 cells * 0.05)) is unchanged - it simply now applies to all
        /// 254 islands rather than to the 38 with a surveyed PvE table.
        /// </summary>
        [Fact]
        public void Resource_population_is_deterministic_and_every_island_has_metal()
        {
            Assert.Equal(1930, ReleaseWorldCatalog.All.Sum(x => x.Deposits.Count));
            Assert.Equal(1233, ReleaseWorldCatalog.All.Sum(x => x.Survey.DatabankCount));
            Assert.Equal(1233, ReleaseWorldCatalog.All.Sum(x => x.Databanks.Count));
            Assert.All(ReleaseWorldCatalog.All.SelectMany(x => x.Deposits), node =>
                Assert.Same(node, ReleaseWorldCatalog.DepositByKey(node.Key)));
            Assert.DoesNotContain(ReleaseWorldCatalog.All, island => island.Deposits.Count == 0);
        }

        /// <summary>
        /// The world-wide provenance split, stated as one number per rung so a
        /// silent reclassification cannot happen. 38 islands carry their own PvE
        /// survey; 23 more have no PvE table but WERE read on the PvP shard, which
        /// is still an observation of that island; the remaining 193 have neither
        /// and their metals are composed from their tier cohort by
        /// tools/world-import/metal_inference.py.
        ///
        /// The 216 empty PvE tables in the source survey are preserved verbatim -
        /// this asserts the inference sits BESIDE the evidence rather than
        /// overwriting it.
        /// </summary>
        [Fact]
        public void Every_island_states_the_provenance_of_its_metals()
        {
            ILookup<MetalTableSource, ReleaseIslandRecord> bySource =
                ReleaseWorldCatalog.All.ToLookup(island => island.Survey.MetalSource);

            Assert.Equal(38, bySource[MetalTableSource.SurveyPve].Count());
            Assert.Equal(23, bySource[MetalTableSource.SurveyPvp].Count());
            Assert.Equal(193, bySource[MetalTableSource.InferredTier].Count());
            Assert.Equal(254, bySource.Sum(group => group.Count()));

            // The raw survey is untouched: 38 PvE tables, 33 PvP tables, and the
            // 216 islands the survey left blank are still blank in PveMetals.
            Assert.Equal(38, ReleaseWorldCatalog.All.Count(x => x.Survey.PveMetals.Count > 0));
            Assert.Equal(33, ReleaseWorldCatalog.All.Count(x => x.Survey.PvpMetals.Count > 0));

            Assert.All(bySource[MetalTableSource.SurveyPve], island => Assert.Equal(
                island.Survey.PveMetals.Select(metal => (metal.Name, metal.Quality)),
                island.Survey.Metals.Select(metal => (metal.Name, metal.Quality))));
            Assert.All(bySource[MetalTableSource.SurveyPvp], island =>
            {
                Assert.Empty(island.Survey.PveMetals);
                Assert.Equal(island.Survey.PvpMetals.Select(metal => (metal.Name, metal.Quality)),
                    island.Survey.Metals.Select(metal => (metal.Name, metal.Quality)));
            });
            Assert.All(bySource[MetalTableSource.InferredTier], island =>
            {
                Assert.Empty(island.Survey.PveMetals);
                Assert.Empty(island.Survey.PvpMetals);
                Assert.NotEmpty(island.Survey.Metals);
            });
        }

        /// <summary>
        /// INDEPENDENT CONFIRMATION that the derived metal->tier ladder is real and
        /// not an artefact of thin sampling.
        ///
        /// Bossa's Update 31 patch notes - the release build this world IS, shipped
        /// 11 June 2019 - state two specific metal retierings by name: "Orthite has
        /// been made a T3 metal" and "Nickel has been made a T2 metal"
        /// (https://worldsadrift.fandom.com/wiki/Update_31). The ladder derived
        /// here reads Bossa's numbers straight back out of player observations that
        /// never mentioned a tier: Orthite is first seen at tier 3, Nickel at tier
        /// 2. Two independent artefacts, exact agreement.
        ///
        /// The same notes say "metal quality is more in line with the biome they
        /// spawn in", which is the stated cause of the tier quality bands, and
        /// "each island will only produce one quality variant of each metal", which
        /// is why one {name, quality} pair per metal is the right table shape.
        ///
        /// If a catalogue regeneration ever broke this agreement, the inference
        /// would have stopped tracking the only retail statement that can check it.
        /// </summary>
        [Fact]
        public void Derived_metal_tiers_agree_with_the_Update_31_patch_notes()
        {
            Dictionary<string, int> firstSeenAtTier = ReleaseWorldCatalog.All
                .SelectMany(island => island.Survey.PveMetals.Concat(island.Survey.PvpMetals),
                    (island, metal) => (island.Survey.Tier, metal.Name))
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Min(entry => entry.Tier),
                    StringComparer.OrdinalIgnoreCase);

            Assert.Equal(3, firstSeenAtTier["Orthite"]);
            Assert.Equal(2, firstSeenAtTier["Nickel"]);
            // "Iron ... found in every tier of the map" (fandom.com/wiki/Metal).
            Assert.Equal(1, firstSeenAtTier["Iron"]);
            // All 15 metals of the release build are represented, none invented.
            Assert.Equal(15, firstSeenAtTier.Count);
        }

        /// <summary>
        /// The inference must stay inside the envelope the survey actually
        /// measured. Both bounds are derived from the recorded observations, not
        /// chosen: no inferred island may carry a metal that was never seen at or
        /// below its tier, nor a quality outside that tier's observed range.
        ///
        /// The tier-4 quality floor is the strongest single measurement behind
        /// this: 280 recorded tier-4 observations and not one below quality 7.
        /// </summary>
        [Fact]
        public void Inferred_metals_stay_inside_the_envelope_the_survey_measured()
        {
            IReadOnlyList<(int Tier, SurveyedMetal Metal)> observed = ReleaseWorldCatalog.All
                .SelectMany(island => island.Survey.PveMetals.Concat(island.Survey.PvpMetals),
                    (island, metal) => (island.Survey.Tier, metal))
                .ToArray();
            Assert.Equal(405, observed.Count);

            Dictionary<string, int> firstSeenAtTier = observed
                .GroupBy(entry => entry.Metal.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Min(entry => entry.Tier),
                    StringComparer.OrdinalIgnoreCase);
            Dictionary<int, (int Low, int High)> band = observed
                .GroupBy(entry => entry.Tier)
                .ToDictionary(group => group.Key,
                    group => (group.Min(e => e.Metal.Quality), group.Max(e => e.Metal.Quality)));

            Assert.Equal((7, 10), band[4]);
            Assert.Equal((1, 4), band[1]);

            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All
                .Where(island => island.Survey.MetalsAreInferred))
            {
                foreach (SurveyedMetal metal in island.Survey.Metals)
                {
                    Assert.True(firstSeenAtTier[metal.Name] <= island.Survey.Tier,
                        island.Survey.WorkshopId + " infers " + metal.Name + " at tier "
                        + island.Survey.Tier + ", never observed above tier "
                        + firstSeenAtTier[metal.Name]);
                    (int low, int high) = band[island.Survey.Tier];
                    Assert.InRange(metal.Quality, low, high);
                }
                // A table with a repeated metal would make one deposit shadow another.
                Assert.Equal(island.Survey.Metals.Count, island.Survey.Metals
                    .Select(metal => metal.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            }
        }

        [Fact]
        public void Map_cell_and_community_survey_tier_disagreement_remains_visible()
        {
            ReleaseIslandRecord mismatch = Assert.Single(ReleaseWorldCatalog.All
                .Where(x => x.CellTier != x.Survey.Tier));
            Assert.Equal("1409387904", mismatch.Survey.WorkshopId);
            Assert.Equal("A4", mismatch.CellId);
            Assert.Equal(2, mismatch.CellTier);
            Assert.Equal(3, mismatch.Survey.Tier);
        }

        [Fact]
        public void Full_world_registry_contains_every_terrain_and_seeded_resource_once()
        {
            WorldEntityRegistry world = WorldEntities.Default(new EntityIdAllocator(),
                includeTree: false, includeMetal: false, includeDeck: false,
                includeStaticShip: false, includeFuelPods: false,
                releaseWorldDistricts: "all");

            Assert.Equal(255, world.Registrations.Count(entity =>
                entity.AssetName.EndsWith("@Island", StringComparison.Ordinal)));
            Assert.Equal(1930, world.Registrations.Count(entity =>
                entity.AssetName == MetalDeposits.AssetName));
            Assert.Equal(1233, world.Registrations.Count(entity =>
                entity.AssetName == Databanks.AssetName));
            // One atlas shard per deposit at the default rate. This is not a taste
            // call: Update 31 - the release build this world is - states "Metal
            // nodes now have 100% chance of spawning an Atlas Shard"
            // (https://worldsadrift.fandom.com/wiki/Update_31). Shards ride
            // deposits, so filling the 216 unsurveyed islands moved this number in
            // lockstep: 354 -> 1930 is the same invariant, not a new shard rate.
            Assert.Equal(1930, world.Registrations.Count(entity =>
                entity.AssetName == AtlasShardCatalogue.AssetName));
            Assert.Equal(world.Registrations.Count,
                world.Registrations.Select(entity => entity.Key).Distinct().Count());

            IslandRegistry islands = IslandRegistry.CreateReleaseWorld("all");
            RegionRegistry regions = RegionRegistry.CreateReleaseWorld(islands, "all");
            WorldDirectory directory = WorldDirectory.Build(world, islands, regions);
            Assert.Equal(world.Registrations.Count, directory.Entries.Count);
            Assert.All(directory.Entries.Where(entry =>
                    entry.Entity.Key != WorldEntities.GlobalEntityKey),
                entry => Assert.NotNull(entry.IslandId));
        }
    }
}

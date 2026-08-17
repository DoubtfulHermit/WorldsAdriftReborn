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

        [Fact]
        public void Surveyed_resource_population_is_deterministic_and_evidence_bounded()
        {
            Assert.Equal(354, ReleaseWorldCatalog.All.Sum(x => x.Deposits.Count));
            Assert.Equal(1233, ReleaseWorldCatalog.All.Sum(x => x.Survey.DatabankCount));
            Assert.Equal(1233, ReleaseWorldCatalog.All.Sum(x => x.Databanks.Count));
            Assert.All(ReleaseWorldCatalog.All.SelectMany(x => x.Deposits), node =>
                Assert.Same(node, ReleaseWorldCatalog.DepositByKey(node.Key)));
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
            Assert.Equal(354, world.Registrations.Count(entity =>
                entity.AssetName == MetalDeposits.AssetName));
            Assert.Equal(1233, world.Registrations.Count(entity =>
                entity.AssetName == Databanks.AssetName));
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

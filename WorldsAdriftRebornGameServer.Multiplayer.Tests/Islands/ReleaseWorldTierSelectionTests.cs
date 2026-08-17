using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// The Tier-1 Wilderness rollout: exactly the four surveyed tier-1 cells, and
    /// exactly the resources the preserved evidence records for them.
    /// </summary>
    public sealed class ReleaseWorldTierSelectionTests
    {
        private const string TierOneCells = "A2,A3,B2,B3";

        [Fact]
        public void Tier_one_is_exactly_46_islands_and_all_of_them_are_tier_one()
        {
            IReadOnlyList<ReleaseIslandRecord> tier1 = ReleaseWorldRolloutPolicy.Select("tier1");

            Assert.Equal(46, tier1.Count);
            Assert.All(tier1, island =>
            {
                Assert.Equal(1, island.CellTier);
                // The community survey and the MapFile agree for every tier-1 island;
                // the single known disagreement (Holy Ruins) is a Tier-2 cell.
                Assert.Equal(1, island.Survey.Tier);
            });
            Assert.Equal(new[] { "A2", "A3", "B2", "B3" },
                tier1.Select(island => island.CellId).Distinct().OrderBy(id => id, StringComparer.Ordinal));
        }

        /// <summary>
        /// Both halves of the equivalence, so it cannot silently break: the tier
        /// selector produces the four cells, AND those four cells contain nothing
        /// but tier-1 islands. A catalogue regeneration that moved an island
        /// between cells would fail here rather than quietly changing what
        /// "Wilderness" means in production.
        /// </summary>
        [Fact]
        public void Tier_one_selector_and_the_four_wilderness_cells_are_the_same_set()
        {
            IReadOnlyList<ReleaseIslandRecord> byTier = ReleaseWorldRolloutPolicy.Select("tier1");
            IReadOnlyList<ReleaseIslandRecord> byCell = ReleaseWorldRolloutPolicy.Select(TierOneCells);

            Assert.Equal(byCell.Select(island => island.Definition.Id),
                byTier.Select(island => island.Definition.Id));
            Assert.Equal(46, ReleaseWorldCatalog.All.Count(island => island.CellTier == 1));
        }

        [Theory]
        [InlineData("tier1")]
        [InlineData("TIER1")]
        [InlineData("t1")]
        [InlineData("wilderness")]
        [InlineData(" Wilderness ")]
        public void Every_tier_one_alias_selects_the_same_islands(string selector) =>
            Assert.Equal(ReleaseWorldRolloutPolicy.Select(TierOneCells).Select(x => x.Definition.Id),
                ReleaseWorldRolloutPolicy.Select(selector).Select(x => x.Definition.Id));

        [Fact]
        public void Tier_selectors_cover_the_whole_world_and_compose_with_cells()
        {
            int byTier = new[] { "tier1", "tier2", "tier3", "tier4" }
                .Sum(selector => ReleaseWorldRolloutPolicy.Select(selector).Count);
            Assert.Equal(254, byTier);

            IReadOnlyList<ReleaseIslandRecord> combined = ReleaseWorldRolloutPolicy.Select("tier1,C6");
            Assert.Equal(46 + ReleaseWorldRolloutPolicy.Select("C6").Count, combined.Count);
            Assert.Equal(combined.Count, combined.Select(x => x.Definition.Id).Distinct().Count());
        }

        /// <summary>
        /// No authored cell id may be readable as a tier selector, or naming a cell
        /// would silently enable a whole tier. Guards the parser, not the data.
        /// </summary>
        [Fact]
        public void No_cell_id_is_ambiguous_with_a_tier_selector()
        {
            Assert.All(ReleaseWorldCatalog.All.Select(island => island.CellId).Distinct(),
                cell => Assert.Null(ReleaseWorldRolloutPolicy.TierOf(cell)));
            Assert.Null(ReleaseWorldRolloutPolicy.TierOf("tier0"));
            Assert.Null(ReleaseWorldRolloutPolicy.TierOf("tier5"));
            Assert.Null(ReleaseWorldRolloutPolicy.TierOf("tier"));
            Assert.Null(ReleaseWorldRolloutPolicy.TierOf(null));
        }

        /// <summary>
        /// The Wilderness content budget, stated once. 42 of the 46 islands have NO
        /// metal at all: the final Cardinal survey recorded a PvE metal table for
        /// only 38 of the 254 ordinary islands, and an empty table is deliberately
        /// never backfilled with an invented population. If a later decision does
        /// backfill it, this test is where that decision becomes visible.
        /// </summary>
        [Fact]
        public void Tier_one_population_is_46_deposits_and_215_databanks_on_46_islands()
        {
            ReleaseWorldPopulation population = ReleaseWorldPopulationPolicy.For("tier1");

            Assert.Equal(46, population.Islands);
            Assert.Equal(47, population.Terrains); // 46 + Haven
            Assert.Equal(4, population.Cells);
            Assert.Equal(46, population.Deposits);
            Assert.Equal(215, population.Databanks);
            Assert.Equal(46, population.AtlasShards); // default rate: one per deposit
            Assert.Equal(42, population.IslandsWithoutMetal);
            Assert.Equal(12, population.IslandsWithRevivalChambers);
            Assert.Equal(14, population.IslandsWithTreeSpecies);
            Assert.Equal(46 + 46 + 215 + 46, population.ReleaseEntities);
        }

        [Fact]
        public void Atlas_rate_thins_shards_but_never_leaves_a_metal_island_without_one()
        {
            IReadOnlyList<ReleaseIslandRecord> tier1 = ReleaseWorldRolloutPolicy.Select("tier1");
            ReleaseWorldPopulation rare =
                ReleaseWorldPopulationPolicy.For(tier1, atlasShardsEnabled: true, oneInDeposits: 5);

            Assert.True(rare.AtlasShards < rare.Deposits);
            Assert.All(tier1.Where(island => island.Deposits.Count > 0), island =>
                Assert.True(ReleaseWorldPopulationPolicy.ShardCountFor(island, 5) >= 1,
                    island.Definition.Id + " has metal but no atlas shard"));
            Assert.Equal(0, ReleaseWorldPopulationPolicy
                .For(tier1, atlasShardsEnabled: false, oneInDeposits: 1).AtlasShards);
        }

        /// <summary>
        /// The registry-level statement of the same facts: every tier-1 deposit,
        /// databank and shard is registered exactly once, every shard's host
        /// deposit is registered BEFORE it (AtlasShardEntity resolves its host by
        /// key at registration and refuses an unbound one), and the world registry,
        /// island registry and region registry agree with zero unowned entities.
        /// </summary>
        [Fact]
        public void Tier_one_world_registry_is_complete_unique_and_fully_owned()
        {
            WorldEntityRegistry world = WorldEntities.Default(new EntityIdAllocator(),
                includeTree: false, includeMetal: false, includeDeck: false,
                includeStaticShip: false, includeFuelPods: false,
                releaseWorldDistricts: "tier1");

            Assert.Equal(47, world.Registrations.Count(entity =>
                entity.AssetName.EndsWith("@Island", StringComparison.Ordinal)));
            Assert.Equal(46, world.Registrations.Count(entity =>
                entity.AssetName == MetalDeposits.AssetName));
            Assert.Equal(215, world.Registrations.Count(entity =>
                entity.AssetName == Databanks.AssetName));
            Assert.Equal(46, world.Registrations.Count(entity =>
                entity.AssetName == AtlasShardCatalogue.AssetName));
            Assert.Equal(world.Registrations.Count,
                world.Registrations.Select(entity => entity.Key).Distinct(StringComparer.Ordinal).Count());

            List<string> order = world.Registrations.Select(entity => entity.Key).ToList();
            Assert.All(world.Registrations.Where(entity =>
                    entity.AssetName == AtlasShardCatalogue.AssetName), shard =>
            {
                string host = Assert.IsType<string>(AtlasShardCatalogue.HostKeyOf(shard.Key));
                Assert.True(order.IndexOf(host) >= 0 && order.IndexOf(host) < order.IndexOf(shard.Key),
                    shard.Key + " is registered before its host deposit " + host);
            });

            IslandRegistry islands = IslandRegistry.CreateReleaseWorld("tier1");
            RegionRegistry regions = RegionRegistry.CreateReleaseWorld(islands, "tier1");
            Assert.Equal(47, islands.All.Count);
            Assert.Equal(5, regions.All.Count); // Haven plus the four Wilderness cells.
            Assert.Equal(47, regions.All.Sum(region => region.IslandIds.Count));

            WorldDirectory directory = WorldDirectory.Build(world, islands, regions);
            Assert.Equal(world.Registrations.Count, directory.Entries.Count);
            Assert.Equal(directory.Entries.Count,
                directory.Entries.Select(entry => entry.Entity.Key).Distinct(StringComparer.Ordinal).Count());
            Assert.All(directory.Entries.Where(entry =>
                    entry.Entity.Key != WorldEntities.GlobalEntityKey),
                entry => Assert.NotNull(entry.IslandId));
        }

        /// <summary>
        /// Every tier-1 deposit resolves back to an authoritative harvest record
        /// through the SAME lookup the boot activation path uses
        /// (WorldResourceActivation -> MetalDeposits.ByKey). A deposit that
        /// registers but does not resolve here is the "the prefab renders but the
        /// resource is inert" failure, so this asserts production's own seam rather
        /// than re-reading the catalogue.
        /// </summary>
        [Fact]
        public void Every_tier_one_deposit_resolves_through_the_production_activation_lookup()
        {
            IReadOnlyList<ReleaseIslandRecord> tier1 = ReleaseWorldRolloutPolicy.Select("tier1");
            IReadOnlyList<MetalNode> deposits = tier1.SelectMany(island => island.Deposits).ToArray();

            Assert.Equal(46, deposits.Count);
            Assert.Equal(46, deposits.Select(node => node.Key).Distinct(StringComparer.Ordinal).Count());
            Assert.All(deposits, node =>
            {
                MetalNode resolved = Assert.IsType<MetalNode>(MetalDeposits.ByKey(node.Key));
                Assert.Same(node, resolved);
                Assert.True(resolved.IsDeposit);
                Assert.False(string.IsNullOrWhiteSpace(resolved.MetalType));
            });
        }

        [Fact]
        public void Every_tier_one_databank_key_is_island_scoped_and_unique()
        {
            IReadOnlyList<ReleaseIslandRecord> tier1 = ReleaseWorldRolloutPolicy.Select("tier1");
            List<string> keys = new();
            foreach (ReleaseIslandRecord island in tier1)
            {
                Assert.InRange(island.Databanks.Count, 3, 5);
                Assert.Equal(island.Survey.DatabankCount, island.Databanks.Count);
                for (int i = 0; i < island.Databanks.Count; i++)
                {
                    string key = Multiplayer.Resources.ReleaseWorldResources
                        .DatabankKeyFor(island, i);
                    Assert.Contains(island.Survey.WorkshopId, key, StringComparison.Ordinal);
                    keys.Add(key);
                }
            }
            Assert.Equal(215, keys.Count);
            Assert.Equal(215, keys.Distinct(StringComparer.Ordinal).Count());
        }
    }
}

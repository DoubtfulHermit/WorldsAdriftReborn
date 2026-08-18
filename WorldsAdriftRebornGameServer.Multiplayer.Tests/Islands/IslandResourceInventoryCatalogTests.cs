using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// The admin world map now states what is ON each island, so these tests pin
    /// the two things that could quietly turn that display into a lie: the counts
    /// have to be the same counts the game server seeds from, and an ore type the
    /// island was never surveyed for has to stay marked as inferred all the way
    /// down to the row a reader looks at.
    /// </summary>
    public sealed class IslandResourceInventoryCatalogTests
    {
        [Fact]
        public void Every_release_island_has_exactly_one_inventory()
        {
            Assert.Equal(ReleaseWorldCatalog.All.Count, IslandResourceInventoryCatalog.All.Count);
            Assert.Equal(254, IslandResourceInventoryCatalog.All.Count);
            Assert.Equal(
                IslandResourceInventoryCatalog.All.Count,
                IslandResourceInventoryCatalog.All.Select(record => record.WorkshopId).Distinct().Count());
        }

        [Fact]
        public void World_totals_are_1930_deposits_1233_databanks_and_3767_trees()
        {
            // The same numbers ReleaseWorldCatalogTests and ReleaseWorldTreeTests
            // pin on the catalogues themselves. Re-asserting them here is the point:
            // if the roll-up ever drifts from the source, this fails rather than the
            // panel showing a plausible wrong number.
            IslandResourceTotals totals = IslandResourceInventoryCatalog.Totals;
            Assert.Equal(254, totals.Islands);
            Assert.Equal(1930, totals.Deposits);
            Assert.Equal(1233, totals.Databanks);
            Assert.Equal(3767, totals.Trees);
            Assert.Equal(72, totals.WoodedIslands);
            Assert.Equal(ReleaseTreeCatalog.TotalTrees, totals.Trees);
        }

        [Fact]
        public void Each_islands_counts_are_the_catalogue_counts_not_a_recomputation()
        {
            foreach (IslandResourceInventory inventory in IslandResourceInventoryCatalog.All)
            {
                ReleaseIslandRecord record = ReleaseWorldCatalog.Require(inventory.IslandId);
                Assert.Equal(record.Deposits.Count, inventory.Deposits);
                Assert.Equal(record.Databanks.Count, inventory.Databanks);
                Assert.Equal(record.Survey.DatabankCount, inventory.Databanks);
                Assert.Equal(
                    ReleaseTreeCatalog.ForWorkshopId(inventory.WorkshopId)?.Points.Count ?? 0,
                    inventory.Trees);
            }
        }

        [Fact]
        public void Ore_tallies_account_for_every_deposit_on_the_island()
        {
            foreach (IslandResourceInventory inventory in IslandResourceInventoryCatalog.All)
            {
                Assert.Equal(inventory.Deposits, inventory.Ores.Sum(ore => ore.Deposits));
                foreach (IslandOreTally ore in inventory.Ores)
                {
                    Assert.True(ore.Deposits > 0);
                    Assert.InRange(ore.Quality, 1, 10);
                    Assert.False(string.IsNullOrWhiteSpace(ore.Metal));
                    // MetalNode lower-cases at load; the display layer must not be
                    // handed a mix of "Iron" and "iron" to de-duplicate.
                    Assert.Equal(ore.Metal.ToLowerInvariant(), ore.Metal);
                }
                Assert.Equal(
                    inventory.Ores.Select(ore => (ore.Metal, ore.Quality)).Distinct().Count(),
                    inventory.Ores.Count);
            }
        }

        [Fact]
        public void Ore_rows_are_ordered_richest_first_so_a_reader_can_stop_early()
        {
            foreach (IslandResourceInventory inventory in IslandResourceInventoryCatalog.All)
                for (int i = 1; i < inventory.Ores.Count; i++)
                    Assert.True(inventory.Ores[i - 1].Deposits >= inventory.Ores[i].Deposits);
        }

        [Fact]
        public void An_unsurveyed_islands_ore_is_marked_inferred_on_every_row()
        {
            // 193 of the 254 islands never had their metal read at all. If the tally
            // dropped the provenance, the panel would present a composed guess in
            // exactly the same type as a recovered survey.
            IslandResourceTotals totals = IslandResourceInventoryCatalog.Totals;
            Assert.Equal(193, totals.IslandsWithInferredOres);
            Assert.Equal(61, totals.IslandsWithRecoveredOres);
            Assert.Equal(totals.Islands, totals.IslandsWithInferredOres + totals.IslandsWithRecoveredOres);

            foreach (IslandResourceInventory inventory in IslandResourceInventoryCatalog.All)
            {
                ResourceProvenance expected = inventory.OresAreInferred
                    ? ResourceProvenance.Inferred
                    : ResourceProvenance.Recovered;
                Assert.Equal(expected, inventory.OreProvenance);
                Assert.All(inventory.Ores, ore => Assert.Equal(expected, ore.Provenance));
            }
        }

        [Fact]
        public void A_pvp_reading_counts_as_recovered_and_only_an_absent_survey_is_inferred()
        {
            // The ladder in tools/world-import/metal_inference.py: survey-pve, then
            // survey-pvp, then inferred-tier. Only the third rung is a composition.
            Assert.Equal(ResourceProvenance.Recovered,
                IslandResourceInventory.ProvenanceOf(MetalTableSource.SurveyPve));
            Assert.Equal(ResourceProvenance.Recovered,
                IslandResourceInventory.ProvenanceOf(MetalTableSource.SurveyPvp));
            Assert.Equal(ResourceProvenance.Inferred,
                IslandResourceInventory.ProvenanceOf(MetalTableSource.InferredTier));

            Assert.Equal(38, IslandResourceInventoryCatalog.All
                .Count(record => record.MetalSource == MetalTableSource.SurveyPve));
            Assert.Equal(23, IslandResourceInventoryCatalog.All
                .Count(record => record.MetalSource == MetalTableSource.SurveyPvp));
            Assert.Equal(193, IslandResourceInventoryCatalog.All
                .Count(record => record.MetalSource == MetalTableSource.InferredTier));

            foreach (MetalTableSource source in Enum.GetValues<MetalTableSource>())
                Assert.False(string.IsNullOrWhiteSpace(IslandResourceInventory.LabelOf(source)));
        }

        [Fact]
        public void Absent_resources_are_reported_as_zero_rather_than_invented()
        {
            // Retail's per-island fuel pods and loot containers did not survive. The
            // honest answer is 0 with an explanation, not a plausible number.
            Assert.All(IslandResourceInventoryCatalog.All, record =>
            {
                Assert.Equal(0, record.FuelPods);
                Assert.Equal(0, record.LootContainers);
            });
            Assert.Equal(182, IslandResourceInventoryCatalog.All.Count(record => record.Trees == 0));
        }

        [Fact]
        public void The_map_asset_join_finds_the_island_the_admin_map_drew()
        {
            // The projected MapFile carries only "<workshopId>.json" - no island id,
            // no cell - so this strip is the entire join between the drawn map and
            // what is on the island.
            IslandResourceInventory first = IslandResourceInventoryCatalog.All[0];
            Assert.Same(first, IslandResourceInventoryCatalog.ByMapAsset(first.WorkshopId + ".json"));
            Assert.Same(first, IslandResourceInventoryCatalog.ByMapAsset(first.WorkshopId));
            Assert.Same(first, IslandResourceInventoryCatalog.ByWorkshopId(first.WorkshopId));

            Assert.Equal("846584820",
                IslandResourceInventoryCatalog.WorkshopIdFromMapAsset("846584820.json"));
            Assert.Null(IslandResourceInventoryCatalog.WorkshopIdFromMapAsset(null));
            Assert.Null(IslandResourceInventoryCatalog.WorkshopIdFromMapAsset("   "));

            // Haven is hand-tuned and deliberately absent from the release catalogue.
            Assert.Null(IslandResourceInventoryCatalog.ByMapAsset("1431299145.json"));
            Assert.Null(IslandResourceInventoryCatalog.ByMapAsset("not-an-island.json"));
            Assert.Null(IslandResourceInventoryCatalog.ByMapAsset(null));
        }

        [Fact]
        public void The_inventory_describes_all_islands_regardless_of_which_are_simulated()
        {
            // Deliberately ungated: "what is on this island" is a fact about the
            // preserved world. Which islands this process simulates is a live
            // question answered elsewhere, from the game server's own stats.
            Assert.Equal(46, ReleaseWorldRolloutPolicy.Select("tier1").Count);
            Assert.Equal(254, IslandResourceInventoryCatalog.All.Count);

            var tierOne = ReleaseWorldRolloutPolicy.Select("tier1")
                .Select(record => IslandResourceInventoryCatalog.ByWorkshopId(record.Survey.WorkshopId))
                .ToList();
            Assert.All(tierOne, Assert.NotNull);
            Assert.Equal(328, tierOne.Sum(record => record!.Deposits));
            Assert.Equal(215, tierOne.Sum(record => record!.Databanks));
            Assert.Equal(727, tierOne.Sum(record => record!.Trees));
        }
    }
}

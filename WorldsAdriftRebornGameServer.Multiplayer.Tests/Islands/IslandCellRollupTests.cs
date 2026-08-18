using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// A zone on the admin map is clickable, and what it answers with is
    /// arithmetic over the release catalogue. These tests hold that arithmetic
    /// here, where it is engine-free, rather than in the page that draws it -
    /// the previous version summed cells in the browser and the numbers ended up
    /// abbreviated and stamped over the terrain.
    ///
    /// The rule with teeth is the provenance one: aggregating a surveyed island
    /// with an unsurveyed one must NOT produce a row that reads as recovered.
    /// </summary>
    public sealed class IslandCellRollupTests
    {
        [Fact]
        public void Every_catalogued_island_lands_in_exactly_one_cell_rollup()
        {
            Assert.Equal(
                IslandResourceInventoryCatalog.All.Count,
                IslandCellRollupCatalog.All.Sum(cell => cell.Islands));
            Assert.Equal(
                IslandCellRollupCatalog.All.Count,
                IslandCellRollupCatalog.All.Select(cell => cell.CellId).Distinct().Count());
            Assert.All(IslandCellRollupCatalog.All, cell => Assert.NotEmpty(cell.Members));
        }

        [Fact]
        public void Cell_rollups_sum_to_the_world_totals()
        {
            IslandResourceTotals totals = IslandResourceInventoryCatalog.Totals;
            Assert.Equal(totals.Islands, IslandCellRollupCatalog.All.Sum(cell => cell.Islands));
            Assert.Equal(totals.Deposits, IslandCellRollupCatalog.All.Sum(cell => cell.Deposits));
            Assert.Equal(totals.Databanks, IslandCellRollupCatalog.All.Sum(cell => cell.Databanks));
            Assert.Equal(totals.Trees, IslandCellRollupCatalog.All.Sum(cell => cell.Trees));
            Assert.Equal(totals.WoodedIslands,
                IslandCellRollupCatalog.All.Sum(cell => cell.WoodedIslands));
            Assert.Equal(totals.IslandsWithInferredOres,
                IslandCellRollupCatalog.All.Sum(cell => cell.IslandsWithInferredOres));
            Assert.Equal(totals.InferredDeposits,
                IslandCellRollupCatalog.All.Sum(cell => cell.InferredDeposits));
        }

        [Fact]
        public void A_cells_ore_rows_account_for_every_deposit_in_it()
        {
            foreach (IslandCellRollup cell in IslandCellRollupCatalog.All)
            {
                Assert.Equal(cell.Deposits, cell.Ores.Sum(ore => ore.Deposits));
            }
        }

        [Fact]
        public void An_unsurveyed_island_makes_the_shared_ore_row_inferred()
        {
            // The weakening rule, held against real islands: take a cell that
            // mixes surveyed and unsurveyed members and check that no ore row it
            // publishes is stronger than the members that fed it.
            IslandCellRollup mixed = IslandCellRollupCatalog.All.First(cell =>
                cell.IslandsWithInferredOres > 0 && cell.IslandsWithRecoveredOres > 0);

            foreach (IslandOreTally row in mixed.Ores)
            {
                bool anyContributorInferred = mixed.Members
                    .Where(member => member.OresAreInferred)
                    .SelectMany(member => member.Ores)
                    .Any(ore => ore.Metal == row.Metal && ore.Quality == row.Quality);

                if (anyContributorInferred)
                {
                    Assert.Equal(ResourceProvenance.Inferred, row.Provenance);
                }
            }

            Assert.True(mixed.HasInferredOres);
        }

        [Fact]
        public void Aggregating_one_surveyed_and_one_unsurveyed_island_never_reads_as_recovered()
        {
            IslandResourceInventory surveyed = IslandResourceInventoryCatalog.All
                .First(island => !island.OresAreInferred && island.Deposits > 0);
            IslandResourceInventory unsurveyed = IslandResourceInventoryCatalog.All
                .First(island => island.OresAreInferred
                                 && island.Ores.Any(ore =>
                                        surveyed.Ores.Any(other =>
                                            other.Metal == ore.Metal && other.Quality == ore.Quality)));

            IslandCellRollup rolled = IslandCellRollupCatalog.Aggregate(
                "test-cell", new[] { surveyed, unsurveyed });

            Assert.Equal("test-cell", rolled.CellId);
            Assert.Equal(2, rolled.Islands);
            Assert.Equal(surveyed.Deposits + unsurveyed.Deposits, rolled.Deposits);
            Assert.Equal(rolled.Deposits, rolled.Ores.Sum(ore => ore.Deposits));

            IslandOreTally shared = rolled.Ores.First(ore =>
                unsurveyed.Ores.Any(other => other.Metal == ore.Metal && other.Quality == ore.Quality));
            Assert.Equal(ResourceProvenance.Inferred, shared.Provenance);
        }

        [Fact]
        public void Ore_rows_are_ordered_richest_first_and_species_are_deduplicated()
        {
            IslandCellRollup cell = IslandCellRollupCatalog.All
                .OrderByDescending(candidate => candidate.Deposits).First();

            for (int i = 1; i < cell.Ores.Count; i++)
            {
                Assert.True(cell.Ores[i - 1].Deposits >= cell.Ores[i].Deposits);
            }

            Assert.Equal(cell.TreeSpecies.Count,
                cell.TreeSpecies.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(
                cell.TreeSpecies.OrderBy(wood => wood, StringComparer.OrdinalIgnoreCase).ToList(),
                cell.TreeSpecies.ToList());
        }

        [Fact]
        public void Bossas_two_unnamed_tier_four_cells_roll_up_under_their_catalogue_ids()
        {
            var ids = IslandCellRollupCatalog.All.Select(cell => cell.CellId).ToList();
            Assert.Contains("A2", ids);
            Assert.Contains("E3", ids);
            Assert.All(ids.Where(id => id.StartsWith("unassigned-t", StringComparison.Ordinal)),
                id => Assert.NotNull(IslandCellRollupCatalog.ForCell(id)));
        }

        [Fact]
        public void An_uncatalogued_cell_answers_null_rather_than_a_zero_filled_measurement()
        {
            Assert.Null(IslandCellRollupCatalog.ForCell("no-such-cell"));
            Assert.Null(IslandCellRollupCatalog.ForCell(null));
        }
    }
}

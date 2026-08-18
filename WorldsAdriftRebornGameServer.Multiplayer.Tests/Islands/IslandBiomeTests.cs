using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// The district-to-biome join is RECOVERED DATA restated as a table, and a
    /// restated table can drift from its source without anyone noticing - so the
    /// table is asserted row by row against the values in Bossa's own
    /// wamap-islands.json Voronoi centres, and the two catalogue-level facts the
    /// fauna work depends on are asserted against the real release catalogue:
    /// every tier-1 island is Biome1 (the reason biome cannot drive tier-1
    /// populations), and biome equals tier for every island except Holy Ruins
    /// (the recovered 253/254 agreement, with its one named exception).
    /// </summary>
    public sealed class IslandBiomeTests
    {
        // Each row restates one Voronoi centre's Type from wamap-islands.json.
        [Theory]
        [InlineData("A1", 2)]
        [InlineData("A2", 1)]
        [InlineData("A3", 1)]
        [InlineData("A4", 2)]
        [InlineData("B1", 2)]
        [InlineData("B2", 1)]
        [InlineData("B3", 1)]
        [InlineData("B4", 2)]
        [InlineData("C1", 3)]
        [InlineData("C2", 3)]
        [InlineData("C3", 3)]
        [InlineData("C4", 3)]
        [InlineData("C5", 3)]
        [InlineData("C6", 3)]
        [InlineData("D1", 4)]
        [InlineData("D2", 4)]
        [InlineData("D3", 4)]
        [InlineData("E3", 4)]
        public void Every_district_maps_to_its_voronoi_centre_type(string cell, int expected) =>
            Assert.Equal(expected, IslandBiome.VoronoiTypeForCell(cell));

        [Theory]
        [InlineData("unassigned-t4-1")]
        [InlineData("unassigned-t4-2")]
        public void The_unassigned_cells_are_the_two_type4_none_centres(string cell) =>
            Assert.Equal(4, IslandBiome.VoronoiTypeForCell(cell));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Z9")]
        public void Anything_unrecognised_degrades_to_the_client_default_biome(string? cell) =>
            Assert.Equal(IslandBiome.DefaultVoronoiType, IslandBiome.VoronoiTypeForCell(cell));

        [Fact]
        public void Every_catalogue_island_gets_a_valid_wire_value()
        {
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                int biome = IslandBiome.VoronoiTypeForCell(island.CellId);
                Assert.InRange(biome, 1, 4);
            }
        }

        [Fact]
        public void All_tier1_islands_are_biome1_which_is_why_biome_cannot_drive_their_populations()
        {
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                if (island.Survey.Tier == 1)
                {
                    Assert.Equal(1, IslandBiome.VoronoiTypeForCell(island.CellId));
                }
            }
        }

        [Fact]
        public void Biome_equals_tier_everywhere_except_holy_ruins()
        {
            // The recovered 253/254 agreement. If this ever grows a second
            // exception, either the catalogue changed or the table drifted -
            // both are worth a loud stop.
            List<string> mismatches = new List<string>();
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                if (IslandBiome.VoronoiTypeForCell(island.CellId) != island.Survey.Tier)
                {
                    mismatches.Add(island.Definition.DisplayName);
                }
            }

            string only = Assert.Single(mismatches);
            Assert.Equal("Holy Ruins", only);
        }
    }
}

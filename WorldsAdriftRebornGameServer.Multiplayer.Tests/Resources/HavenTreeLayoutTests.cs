using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Resources;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Resources
{
    /// <summary>
    /// Guards the visible promise of Haven's resource reseed: trees cover the whole
    /// terrain instead of one low shelf, sit on plausible ground, and the production
    /// Haven profile remains birch rather than cycling unrelated island species.
    /// </summary>
    public class HavenTreeLayoutTests
    {
        [Fact]
        public void The_tree_field_reaches_its_reviewed_whole_island_count()
        {
            Assert.Equal(HavenSurface.TreeTargetCount, HavenSurface.TreeLocals().Count);
            Assert.Equal(HavenSurface.TreeTargetCount, WorldEntities.DistributedTreeLocals.Count);
        }

        [Fact]
        public void Trees_cover_low_high_east_and_west_terrain()
        {
            IReadOnlyList<GeneratedPlacement> trees = HavenSurface.TreeLocals();
            Assert.True(trees.Max(x => x.LocalX) - trees.Min(x => x.LocalX) > 400.0);
            Assert.True(trees.Max(x => x.LocalZ) - trees.Min(x => x.LocalZ) > 200.0);
            Assert.Contains(trees, x => x.LocalX < 0.0);
            Assert.Contains(trees, x => x.LocalX > 150.0);
            Assert.Contains(trees, x => x.LocalY > 20.0);
            Assert.Contains(trees, x => x.LocalY < 0.0);
        }

        [Fact]
        public void Every_large_walkable_region_contains_a_tree_or_deposit()
        {
            // A span-only assertion can still hide a barren middle. Partition the
            // eligible terrain into a coarse 4x3 coverage grid and require every
            // occupied terrain cell to contain at least one natural resource.
            IReadOnlyList<SurfaceSample> eligible = HavenSurface.Samples
                .Where(s => s.Ny >= HavenSurface.DepositMinUpwardNormal)
                .ToList();
            double minX = eligible.Min(s => s.LocalX);
            double maxX = eligible.Max(s => s.LocalX);
            double minZ = eligible.Min(s => s.LocalZ);
            double maxZ = eligible.Max(s => s.LocalZ);

            HashSet<(int X, int Z)> terrainCells = new HashSet<(int, int)>();
            foreach (SurfaceSample s in eligible)
            {
                terrainCells.Add(Cell(s.LocalX, s.LocalZ, minX, maxX, minZ, maxZ));
            }

            HashSet<(int X, int Z)> resourceCells = new HashSet<(int, int)>();
            foreach (GeneratedPlacement p in HavenSurface.TreeLocals()
                         .Concat(HavenSurface.DepositLocals()))
            {
                resourceCells.Add(Cell(p.LocalX, p.LocalZ, minX, maxX, minZ, maxZ));
            }

            Assert.All(terrainCells, cell => Assert.Contains(cell, resourceCells));
        }

        [Fact]
        public void Every_tree_has_flat_ground_and_minimum_trunk_spacing()
        {
            IReadOnlyList<GeneratedPlacement> trees = HavenSurface.TreeLocals();
            foreach (GeneratedPlacement tree in trees)
            {
                Assert.True(tree.Ny >= HavenSurface.TreeMinUpwardNormal - 1e-9);
                Assert.InRange(tree.LocalY, HavenSurface.ResourceMinHeight, HavenSurface.ResourceMaxHeight);
                Assert.False(HavenSurface.TreeConfig().IsExcluded(tree.LocalX, tree.LocalZ));
            }

            for (int i = 0; i < trees.Count; i++)
            {
                for (int j = i + 1; j < trees.Count; j++)
                {
                    double dx = trees[i].LocalX - trees[j].LocalX;
                    double dy = trees[i].LocalY - trees[j].LocalY;
                    double dz = trees[i].LocalZ - trees[j].LocalZ;
                    double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    Assert.True(distance >= HavenSurface.TreeMinSpacing - 1e-9,
                        $"trees {i},{j} only {distance:0.###} m apart");
                }
            }
        }

        [Fact]
        public void Haven_world_registry_uses_birch_for_every_distributed_tree()
        {
            // Production calls this non-varied route even if an old server unit still
            // carries WAREBORN_TREE_SPECIES=1. The varied overload remains only as
            // reusable machinery for future islands with recovered species tables.
            Assert.All(WorldEntities.DistributedTrees(), tree =>
                Assert.Equal(Trees.AssetName, tree.AssetName));
        }

        private static (int X, int Z) Cell(
            double x, double z, double minX, double maxX, double minZ, double maxZ)
        {
            int cx = Math.Min(3, (int)((x - minX) / (maxX - minX) * 4.0));
            int cz = Math.Min(2, (int)((z - minZ) / (maxZ - minZ) * 3.0));
            return (cx, cz);
        }
    }
}

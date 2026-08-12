using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The env caps applied through <see cref="WorldEntities.Default"/>: dialling
    /// the test-populated tree and ore counts down without a rebuild, while the
    /// near-spawn HavenTree and the proven metal node (placement index 0) always
    /// survive. The counting policy itself is asserted in
    /// <see cref="SpawnCountPolicyTests"/>; this pins the wiring.
    /// </summary>
    public class WorldEntitiesCountTests
    {
        private static WorldEntityRegistry Build(string? treeCount, string? oreCount)
        {
            return WorldEntities.Default(
                new EntityIdAllocator(),
                includeProofIsland: false,
                includeTree: true,
                includeMetal: true,
                metalOnlyProven: false,
                treeCountEnv: treeCount,
                oreCountEnv: oreCount);
        }

        private static int TreeCountIn(WorldEntityRegistry r)
        {
            // HavenTree ("tree-haven") + distributed ("tree-0"..).
            return r.Registrations.Count(e => e.AssetName == Trees.AssetName);
        }

        private static int OreCountIn(WorldEntityRegistry r)
        {
            return r.Registrations.Count(e => e.AssetName == MetalNodes.AssetName);
        }

        [Fact]
        public void UnsetCountsGiveTheFullPlacedSet()
        {
            WorldEntityRegistry r = Build(null, null);
            Assert.Equal(1 + WorldEntities.DistributedTreeLocals.Count, TreeCountIn(r));
            Assert.Equal(MetalNodes.HavenPlacements.Count, OreCountIn(r));
        }

        [Fact]
        public void TreeCountCapsTotalTreesAndKeepsTheNearSpawnTree()
        {
            WorldEntityRegistry r = Build("1", null);
            Assert.Equal(1, TreeCountIn(r));
            // The one kept is the near-spawn HavenTree, never a distributed one.
            Assert.NotNull(r.ByKey(WorldEntities.HavenTreeKey));
            Assert.Null(r.ByKey("tree-0"));
        }

        [Fact]
        public void TreeCountOfFiveIsHavenTreePlusFourDistributed()
        {
            WorldEntityRegistry r = Build("5", null);
            Assert.Equal(5, TreeCountIn(r));
            Assert.NotNull(r.ByKey(WorldEntities.HavenTreeKey));
            Assert.NotNull(r.ByKey("tree-3"));
            Assert.Null(r.ByKey("tree-4"));
        }

        [Fact]
        public void OreCountCapsNodesAndKeepsTheProvenNodeIndexZero()
        {
            WorldEntityRegistry r = Build(null, "1");
            Assert.Equal(1, OreCountIn(r));
            // Placement index 0 is the proven node; "first N" keeps it for N >= 1.
            Assert.NotNull(r.ByKey(MetalNodes.KeyFor(0)));
            Assert.Null(r.ByKey(MetalNodes.KeyFor(1)));
        }

        [Fact]
        public void ACountOfZeroClampsToOneRatherThanEmptyingTheWorld()
        {
            WorldEntityRegistry r = Build("0", "0");
            Assert.Equal(1, TreeCountIn(r));
            Assert.Equal(1, OreCountIn(r));
        }

        [Fact]
        public void OverLargeCountsClampToWhatIsPlaced()
        {
            WorldEntityRegistry r = Build("999", "999");
            Assert.Equal(1 + WorldEntities.DistributedTreeLocals.Count, TreeCountIn(r));
            Assert.Equal(MetalNodes.HavenPlacements.Count, OreCountIn(r));
        }

        [Fact]
        public void ProvenModeStillWinsOverTheOreCount()
        {
            WorldEntityRegistry r = WorldEntities.Default(
                new EntityIdAllocator(),
                includeProofIsland: false,
                includeTree: false,
                includeMetal: true,
                metalOnlyProven: true,
                treeCountEnv: null,
                oreCountEnv: "999");
            Assert.Equal(1, OreCountIn(r));
            Assert.NotNull(r.ByKey(MetalNodes.KeyFor(0)));
        }
    }
}

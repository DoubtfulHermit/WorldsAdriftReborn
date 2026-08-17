using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    public sealed class IslandTerrainResourceOrderingPolicyTests
    {
        private static readonly IslandId A = new("a");
        private static readonly IslandId B = new("b");

        [Fact]
        public void Disabled_terrain_interest_preserves_legacy_resource_add_and_serve()
        {
            Assert.True(IslandTerrainResourceOrderingPolicy.MayAddResource(false, false));
            Assert.True(IslandTerrainResourceOrderingPolicy.MayServeResourceComponents(true, false, false));
        }

        [Fact]
        public void Enabled_interest_never_adds_or_serves_a_resource_before_terrain()
        {
            Assert.False(IslandTerrainResourceOrderingPolicy.MayAddResource(true, false));
            Assert.False(IslandTerrainResourceOrderingPolicy.MayServeResourceComponents(true, true, false));
            Assert.True(IslandTerrainResourceOrderingPolicy.MayAddResource(true, true));
            Assert.True(IslandTerrainResourceOrderingPolicy.MayServeResourceComponents(true, true, true));
        }

        [Fact]
        public void Drain_cancels_island_adds_and_puts_loaded_removes_before_other_work()
        {
            HashSet<long> loaded = new() { 10, 11, 20 };
            Dictionary<long, IslandId> owners = new()
            {
                [10] = A, [11] = A, [12] = A, [20] = B, [21] = B,
            };
            ResourceStreamAction[] pending =
            {
                new(ResourceStreamActionKind.Add, 12),
                new(ResourceStreamActionKind.Add, 21),
                new(ResourceStreamActionKind.Remove, 10),
                new(ResourceStreamActionKind.Remove, 20),
            };

            Assert.Equal(new[]
            {
                new ResourceStreamAction(ResourceStreamActionKind.Remove, 10),
                new ResourceStreamAction(ResourceStreamActionKind.Remove, 11),
                new ResourceStreamAction(ResourceStreamActionKind.Add, 21),
                new ResourceStreamAction(ResourceStreamActionKind.Remove, 20),
            }, IslandTerrainResourceOrderingPolicy.DrainBeforeTerrainRemoval(
                pending, loaded, owners, A));
        }

        [Fact]
        public void Drain_truth_is_island_specific_for_two_peer_ledgers()
        {
            Dictionary<long, IslandId> owners = new() { [10] = A, [20] = B };
            HashSet<long> nearPeer = new() { 10 };
            HashSet<long> farPeer = new() { 20 };

            Assert.False(IslandTerrainResourceOrderingPolicy.IsDrained(nearPeer, owners, A));
            Assert.True(IslandTerrainResourceOrderingPolicy.IsDrained(farPeer, owners, A));
            Assert.False(IslandTerrainResourceOrderingPolicy.IsDrained(farPeer, owners, B));
        }
    }
}

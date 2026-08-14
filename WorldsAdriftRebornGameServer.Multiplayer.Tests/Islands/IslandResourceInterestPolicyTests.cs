using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    public class IslandResourceInterestPolicyTests
    {
        [Fact]
        public void Global_positions_are_owned_by_the_nearest_evidenced_island()
        {
            IslandRegistry islands = IslandRegistry.CreateDefault();

            Assert.Equal(IslandCatalog.HavenId,
                IslandResourceInterestPolicy.ClosestIsland(
                    IslandCatalog.Haven.LocalToGlobal(200, 5, 4), islands.All));
            Assert.Equal(IslandCatalog.TradesChallengeId,
                IslandResourceInterestPolicy.ClosestIsland(
                    IslandCatalog.TradesChallenge.LocalToGlobal(-64, 2, -64), islands.All));
        }

        [Fact]
        public void Active_island_candidates_exclude_unvisited_distant_resources()
        {
            var resources = new[]
            {
                new IslandResource(1, IslandCatalog.Haven.LocalToGlobal(10, 0, 0), IslandCatalog.HavenId),
                new IslandResource(2, IslandCatalog.TradesChallenge.LocalToGlobal(10, 0, 0), IslandCatalog.TradesChallengeId),
            };

            IReadOnlyList<(long Id, FixedPointPosition Position)> result =
                IslandResourceInterestPolicy.ReconcileSet(
                    IslandCatalog.HavenId, resources, new HashSet<long>());

            Assert.Equal(new[] { 1L }, result.Select(x => x.Id));
        }

        [Fact]
        public void Previously_loaded_old_island_resources_remain_candidates_for_removal()
        {
            var resources = new[]
            {
                new IslandResource(1, IslandCatalog.Haven.LocalToGlobal(10, 0, 0), IslandCatalog.HavenId),
                new IslandResource(2, IslandCatalog.TradesChallenge.LocalToGlobal(10, 0, 0), IslandCatalog.TradesChallengeId),
            };

            IReadOnlyList<(long Id, FixedPointPosition Position)> candidates =
                IslandResourceInterestPolicy.ReconcileSet(
                    IslandCatalog.TradesChallengeId, resources, new HashSet<long> { 1 });
            IReadOnlyList<ResourceStreamAction> actions = ResourceInterestPolicy.Reconcile(
                IslandCatalog.TradesChallenge.GlobalOrigin,
                candidates,
                new HashSet<long> { 1 },
                loadRadius: 100,
                unloadRadius: 150);

            Assert.Equal(new[]
            {
                new ResourceStreamAction(ResourceStreamActionKind.Remove, 1),
                new ResourceStreamAction(ResourceStreamActionKind.Add, 2),
            }, actions);
        }

        [Fact]
        public void Terrain_registration_key_resolves_without_boot_entity_id_identity()
        {
            IslandRegistry islands = IslandRegistry.CreateDefault();

            Assert.Equal(IslandCatalog.HavenId,
                islands.ByWorldEntityKey(IslandCatalog.Haven.WorldEntityKey)!.Id);
            Assert.Equal(IslandCatalog.TradesChallengeId,
                islands.ByWorldEntityKey(IslandCatalog.TradesChallenge.WorldEntityKey)!.Id);
            Assert.Null(islands.ByWorldEntityKey("built-ship:0:hull"));
        }
    }
}

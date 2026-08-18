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
        public void Only_admitted_islands_resources_are_desired()
        {
            var resources = new[]
            {
                new IslandResource(1, IslandCatalog.Haven.LocalToGlobal(10, 0, 0), IslandCatalog.HavenId),
                new IslandResource(2, IslandCatalog.TradesChallenge.LocalToGlobal(10, 0, 0), IslandCatalog.TradesChallengeId),
            };

            IReadOnlyList<(long Id, FixedPointPosition Position, bool Desired)> result =
                IslandResourceCheckoutPolicy.Desire(
                    resources, new HashSet<IslandId> { IslandCatalog.HavenId });

            Assert.Equal(new[] { 1L }, result.Where(x => x.Desired).Select(x => x.Id));
            Assert.Equal(new[] { 2L }, result.Where(x => !x.Desired).Select(x => x.Id));
        }

        /// <summary>
        /// The whole point of leaving a departed island's already-loaded resources in
        /// the offered set: they come back Desired = false and that is what emits
        /// their Remove. Drop them from the set and the peer keeps them forever.
        /// </summary>
        [Fact]
        public void Previously_loaded_old_island_resources_are_removed_when_it_is_not_admitted()
        {
            var resources = new[]
            {
                new IslandResource(1, IslandCatalog.Haven.LocalToGlobal(10, 0, 0), IslandCatalog.HavenId),
                new IslandResource(2, IslandCatalog.TradesChallenge.LocalToGlobal(10, 0, 0), IslandCatalog.TradesChallengeId),
            };

            IReadOnlyList<ResourceStreamAction> actions = ResourceInterestPolicy.Reconcile(
                IslandCatalog.TradesChallenge.GlobalOrigin,
                IslandResourceCheckoutPolicy.Desire(
                    resources, new HashSet<IslandId> { IslandCatalog.TradesChallengeId }),
                new HashSet<long> { 1 });

            Assert.Equal(new[]
            {
                new ResourceStreamAction(ResourceStreamActionKind.Remove, 1),
                new ResourceStreamAction(ResourceStreamActionKind.Add, 2),
            }, actions);
        }

        /// <summary>
        /// The regression this whole change exists for, in miniature: a node 400 m
        /// from the player, far outside the 120 m bubble, on the island the player is
        /// standing on. It used to be dropped; it must now be held.
        /// </summary>
        [Fact]
        public void A_node_far_across_the_island_the_player_stands_on_is_still_held()
        {
            var resources = new[]
            {
                new IslandResource(1, IslandCatalog.Haven.LocalToGlobal(400, 0, 0), IslandCatalog.HavenId),
            };

            Assert.Equal(new[] { new ResourceStreamAction(ResourceStreamActionKind.Add, 1) },
                ResourceInterestPolicy.Reconcile(
                    IslandCatalog.Haven.GlobalOrigin,
                    IslandResourceCheckoutPolicy.Desire(
                        resources, new HashSet<IslandId> { IslandCatalog.HavenId }),
                    new HashSet<long>()));

            Assert.Empty(ResourceInterestPolicy.Reconcile(
                IslandCatalog.Haven.GlobalOrigin,
                resources.Select(r => (r.EntityId, r.Position)),
                new HashSet<long>(),
                loadRadius: 120,
                unloadRadius: 155));
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

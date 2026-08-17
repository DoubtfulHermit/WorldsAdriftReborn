using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Resources;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Resources
{
    public class TradesChallengeResourceTests
    {
        [Fact]
        public void Recovered_profile_is_five_aluminium_q4_deposits_and_five_databanks()
        {
            Assert.Equal(5, TradesChallengeResources.DepositLocals().Count);
            Assert.Equal(5, TradesChallengeResources.DatabankLocals().Count);

            for (int i = 0; i < TradesChallengeResources.DepositCount; i++)
            {
                MetalNode node = TradesChallengeResources.DepositByKey(
                    TradesChallengeResources.DepositKeyFor(i))!;
                Assert.Equal("aluminium", node.MetalType);
                Assert.Equal(4, node.Quality);
                Assert.True(node.IsDeposit);
                Assert.Equal(MetalDeposits.VariantIdFor(i), node.VariantId);
            }
        }

        [Fact]
        public void Generated_resources_cover_real_upward_surface_without_crowding_landing()
        {
            IReadOnlyList<GeneratedPlacement> all = TradesChallengeResources.DepositLocals()
                .Concat(TradesChallengeResources.DatabankLocals()).ToArray();
            foreach (GeneratedPlacement p in all)
            {
                Assert.InRange(p.LocalY, -10, 10);
                Assert.True(p.Ny >= 0.9);
                double dx = p.LocalX - -64;
                double dz = p.LocalZ - -64;
                Assert.True(dx * dx + dz * dz >= 8 * 8);
            }

            // The production teleport lands at (-64,-64). At least one node of each
            // family must be inside a conservative 150 m checkout radius so the PR4
            // transition has a live resource to prove, without crowding the landing.
            Assert.Contains(TradesChallengeResources.DepositLocals(), p =>
                SquaredDistanceFromLanding(p) <= 150 * 150);
            Assert.Contains(TradesChallengeResources.DatabankLocals(), p =>
                SquaredDistanceFromLanding(p) <= 150 * 150);
        }

        private static double SquaredDistanceFromLanding(GeneratedPlacement p)
        {
            double dx = p.LocalX - -64;
            double dz = p.LocalZ - -64;
            return dx * dx + dz * dz;
        }

        [Fact]
        public void Production_second_island_registers_only_its_evidenced_resource_families()
        {
            WorldEntityRegistry registry = WorldEntities.Default(
                new EntityIdAllocator(),
                includeTree: false,
                includeMetal: false,
                includeDeck: false,
                includeExtraParts: false,
                includeDeposit: false,
                includeDatabank: false,
                includeAtlasShard: true,
                includeFuelPods: false,
                includeStaticShip: false,
                includeProductionSecondIsland: true);

            Assert.NotNull(registry.ByKey(IslandCatalog.TradesChallenge.WorldEntityKey));
            Assert.NotNull(registry.ByKey(WorldEntities.GlobalEntityKey));
            Assert.Equal(5, registry.Registrations.Count(x =>
                x.Key.StartsWith(TradesChallengeResources.DepositKeyPrefix, StringComparison.Ordinal)));
            Assert.Equal(5, registry.Registrations.Count(x =>
                x.Key.StartsWith(TradesChallengeResources.DatabankKeyPrefix, StringComparison.Ordinal)));
            Assert.Equal(5, registry.Registrations.Count(x =>
                x.Key.StartsWith(AtlasShardCatalogue.KeyPrefix
                    + TradesChallengeResources.DepositKeyPrefix, StringComparison.Ordinal)));
            Assert.DoesNotContain(registry.Registrations, x => x.Key.StartsWith("tree-", StringComparison.Ordinal));
            Assert.DoesNotContain(registry.Registrations, x => x.Key.StartsWith("fuel-pod-", StringComparison.Ordinal));
        }
    }
}

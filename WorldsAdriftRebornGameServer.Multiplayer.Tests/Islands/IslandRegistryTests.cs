using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    public sealed class IslandRegistryTests
    {
        [Fact]
        public void Haven_definition_preserves_the_authoritative_identity_and_terrain_facts()
        {
            IslandDefinition haven = IslandCatalog.Haven;

            Assert.Equal(new IslandId("haven"), haven.Id);
            Assert.Equal("Haven", haven.DisplayName);
            Assert.Equal(WorldEntities.IslandKey, haven.WorldEntityKey);
            Assert.Equal(new FixedPointPosition(69650145, -1305269, -4645549), haven.GlobalOrigin);
            Assert.Equal("1431299145@Island", haven.TerrainAssetName);
            Assert.Equal("notNeeded?", haven.TerrainAssetContext);
            Assert.Equal(SpawnOrder.BeforePlayer, haven.SpawnOrder);
            Assert.Equal(SpawnPolicy.IslandPosition, haven.GlobalOrigin);
        }

        [Fact]
        public void Default_registry_resolves_Haven_by_stable_id()
        {
            IslandRegistry registry = IslandRegistry.CreateDefault();

            Assert.Equal(2, registry.All.Count);
            Assert.Same(IslandCatalog.Haven, registry.Require(new IslandId("haven")));
            Assert.Same(IslandCatalog.TradesChallenge,
                registry.Require(new IslandId("the-trades-challenge")));
            Assert.Null(registry.ById(IslandCatalog.AnchorageIsleId));
            Assert.Null(registry.ById(IslandCatalog.OldMilitaryAcademyId));
            Assert.Null(registry.ById(IslandCatalog.ShatteredMausoleumId));
            Assert.Null(registry.ById(new IslandId("missing")));
        }

        [Fact]
        public void First_region_catalogue_can_be_registered_without_identity_collisions()
        {
            IslandRegistry registry = new IslandRegistry();
            foreach (IslandDefinition island in IslandCatalog.FirstRegionTerrain)
                registry.Register(island);

            Assert.Equal(IslandCatalog.FirstRegionTerrain.Count, registry.All.Count);
            foreach (IslandDefinition island in IslandCatalog.FirstRegionTerrain)
            {
                Assert.Same(island, registry.Require(island.Id));
                Assert.Same(island, registry.ByWorldEntityKey(island.WorldEntityKey));
            }
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(0, 1)]
        [InlineData(1, 2)]
        [InlineData(2, 3)]
        [InlineData(4, 5)]
        [InlineData(99, 5)]
        public void First_region_factory_registers_Haven_plus_a_bounded_candidate_prefix(
            int optionalCount,
            int expectedTotal)
        {
            IslandRegistry registry = IslandRegistry.CreateWithFirstRegionTerrain(optionalCount);

            Assert.Equal(expectedTotal, registry.All.Count);
            Assert.Same(IslandCatalog.Haven, registry.Require(IslandCatalog.HavenId));

            int bounded = FirstRegionTerrainCountPolicy.Clamp(optionalCount);
            foreach (IslandDefinition included in IslandCatalog.FirstRegionTerrain.Take(bounded + 1))
                Assert.Same(included, registry.Require(included.Id));
            foreach (IslandDefinition excluded in IslandCatalog.FirstRegionTerrain.Skip(bounded + 1))
                Assert.Null(registry.ById(excluded.Id));
        }

        [Fact]
        public void First_region_terrain_is_ordered_as_one_required_then_four_optional_candidates()
        {
            Assert.Equal(
                new[]
                {
                    IslandCatalog.HavenId,
                    IslandCatalog.TradesChallengeId,
                    IslandCatalog.AnchorageIsleId,
                    IslandCatalog.OldMilitaryAcademyId,
                    IslandCatalog.ShatteredMausoleumId,
                },
                IslandCatalog.FirstRegionTerrain.Select(island => island.Id));

            Assert.Equal(SpawnOrder.BeforePlayer, IslandCatalog.FirstRegionTerrain[0].SpawnOrder);
            Assert.All(
                IslandCatalog.FirstRegionTerrain.Skip(1),
                island => Assert.Equal(SpawnOrder.AfterPlayer, island.SpawnOrder));
        }

        [Theory]
        [InlineData("anchorage-isle", "Anchorage Isle", "island-anchorage-isle",
            53748326, -1229240, 1475919, "650186469@Island")]
        [InlineData("the-old-military-academy", "The Old Military Academy",
            "island-the-old-military-academy", 58796532, 277533, 7307445,
            "1673355094@Island")]
        [InlineData("shattered-mausoleum", "Shattered Mausoleum",
            "island-shattered-mausoleum", 58660618, -2158603, -19035735,
            "949069116@Island")]
        public void C6_candidates_preserve_release_MapFile_identity_and_transform(
            string id,
            string displayName,
            string entityKey,
            long x,
            long y,
            long z,
            string assetName)
        {
            IslandDefinition island = IslandCatalog.FirstRegionTerrain.Single(
                candidate => candidate.Id == new IslandId(id));

            Assert.Equal(displayName, island.DisplayName);
            Assert.Equal(entityKey, island.WorldEntityKey);
            Assert.Equal(new FixedPointPosition(x, y, z), island.GlobalOrigin);
            Assert.Equal(assetName, island.TerrainAssetName);
            Assert.Equal(IslandCatalog.DefaultTerrainAssetContext, island.TerrainAssetContext);
            Assert.Equal(SpawnOrder.AfterPlayer, island.SpawnOrder);
        }

        [Fact]
        public void Duplicate_ids_are_rejected_even_when_definitions_differ()
        {
            IslandRegistry registry = new IslandRegistry();
            registry.Register(IslandCatalog.Haven);

            IslandDefinition duplicate = Definition("haven", 1);
            Assert.Throws<ArgumentException>(() => registry.Register(duplicate));
        }

        [Fact]
        public void Enumeration_is_ordinal_by_id_not_registration_order()
        {
            IslandRegistry registry = new IslandRegistry();
            registry.Register(Definition("wilderness-002", 2));
            registry.Register(Definition("haven", 1));
            registry.Register(Definition("atlas", 3));

            Assert.Equal(
                new[] { "atlas", "haven", "wilderness-002" },
                registry.All.Select(island => island.Id.Value));
        }

        [Theory]
        [InlineData(208.0, 6.70, 4.0, 70502113, -1277826, -4629165)]
        [InlineData(208.0, 4.99, 8.0, 70502113, -1284830, -4612781)]
        [InlineData(0.999, -0.999, 1.0, 69654236, -1309360, -4641453)]
        public void Haven_local_to_global_preserves_exact_client_truncation(
            double x, double y, double z, long expectedX, long expectedY, long expectedZ)
        {
            Assert.Equal(
                new FixedPointPosition(expectedX, expectedY, expectedZ),
                IslandCatalog.Haven.LocalToGlobal(x, y, z));
        }

        [Fact]
        public void Every_deterministic_Haven_resource_position_matches_the_pre_registry_transform()
        {
            FixedPointPosition oldOrigin = new FixedPointPosition(69650145, -1305269, -4645549);

            foreach (MetalNodes.Placement p in MetalNodes.HavenPlacements)
                AssertLegacyEqualsRegistry(oldOrigin, p.LocalX, p.LocalY, p.LocalZ);
            foreach (MetalDeposits.Placement p in MetalDeposits.HavenPlacements)
                AssertLegacyEqualsRegistry(oldOrigin, p.LocalX, p.LocalY, p.LocalZ);
            foreach (FuelPods.Placement p in FuelPods.HavenPlacements)
                AssertLegacyEqualsRegistry(oldOrigin, p.LocalX, p.LocalY, p.LocalZ);
            foreach (Databanks.Placement p in Databanks.HavenPlacements)
                AssertLegacyEqualsRegistry(oldOrigin, p.LocalX, p.LocalY, p.LocalZ);
            foreach ((double x, double y, double z) in WorldEntities.DistributedTreeLocals)
                AssertLegacyEqualsRegistry(oldOrigin, x, y, z);
        }

        [Fact]
        public void Haven_world_entity_is_built_from_the_registered_definition_without_wire_changes()
        {
            WorldEntity entity = WorldEntities.Island(IslandRegistry.CreateDefault().Require(IslandCatalog.HavenId));

            Assert.Equal(WorldEntities.IslandKey, entity.Key);
            Assert.Equal(SpawnOrder.BeforePlayer, entity.Order);
            Assert.Equal(IslandCatalog.Haven.TerrainAssetName, entity.AssetName);
            Assert.Equal(IslandCatalog.Haven.TerrainAssetContext, entity.AssetContext);
            Assert.Equal(IslandCatalog.Haven.GlobalOrigin, entity.Position);
            Assert.Empty(entity.SeedComponents);
        }

        [Fact]
        public void Production_second_island_preserves_the_Bossa_MapFile_identity_and_transform()
        {
            IslandDefinition island = IslandCatalog.TradesChallenge;

            Assert.Equal(new IslandId("the-trades-challenge"), island.Id);
            Assert.Equal("island-the-trades-challenge", island.WorldEntityKey);
            Assert.Equal(new FixedPointPosition(54286560, -791844, -8077469), island.GlobalOrigin);
            Assert.Equal("1206286558@Island", island.TerrainAssetName);
            Assert.Equal(SpawnOrder.AfterPlayer, island.SpawnOrder);

            WorldEntity entity = WorldEntities.Island(island);
            Assert.Equal(island.WorldEntityKey, entity.Key);
            Assert.Equal(island.GlobalOrigin, entity.Position);
            Assert.Equal(island.TerrainAssetName, entity.AssetName);
            Assert.Equal(SpawnOrder.AfterPlayer, entity.Order);
        }

        private static void AssertLegacyEqualsRegistry(
            FixedPointPosition origin, double x, double y, double z)
        {
            Assert.Equal(
                MetalNodes.IslandLocalToWorldFixed(origin, x, y, z),
                IslandCatalog.Haven.LocalToGlobal(x, y, z));
        }

        private static IslandDefinition Definition(string id, long x)
        {
            return new IslandDefinition(
                new IslandId(id), id, "island-" + id,
                new FixedPointPosition(x, 0, 0), "asset", "context", SpawnOrder.AfterPlayer);
        }
    }
}

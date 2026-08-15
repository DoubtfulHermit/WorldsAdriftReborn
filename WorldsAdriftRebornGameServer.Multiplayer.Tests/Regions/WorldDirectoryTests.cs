using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using WorldsAdriftRebornGameServer.Multiplayer.Resources;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Regions
{
    public sealed class WorldDirectoryTests
    {
        [Fact]
        public void Full_production_boot_registry_is_completely_classified()
        {
            WorldEntityRegistry entities = FullRegistry();
            WorldDirectory directory = Build(entities);

            Assert.Equal(entities.Registrations.Count, directory.Entries.Count);
            Assert.All(entities.Registrations, entity =>
                Assert.NotNull(directory.ByEntityKey(entity.Key)));
            Assert.Single(directory.OwnedBy(WorldOwner.Global));
        }

        [Fact]
        public void Island_terrain_and_resources_belong_to_their_evidenced_regions()
        {
            WorldDirectory directory = Build(FullRegistry());
            WorldOwner haven = WorldOwner.ForRegion(RegionCatalog.HavenRegionId);
            WorldOwner trades = WorldOwner.ForRegion(RegionCatalog.TradesChallengeRegionId);

            Assert.Equal(haven, directory.ByEntityKey(IslandCatalog.Haven.WorldEntityKey)!.Owner);
            Assert.Equal(haven, directory.ByEntityKey(MetalDeposits.KeyFor(0))!.Owner);
            Assert.Equal(haven, directory.ByEntityKey(FuelPods.KeyFor(0))!.Owner);
            Assert.Equal(trades, directory.ByEntityKey(IslandCatalog.TradesChallenge.WorldEntityKey)!.Owner);
            Assert.Equal(trades,
                directory.ByEntityKey(TradesChallengeResources.DepositKeyFor(0))!.Owner);
            Assert.Equal(trades,
                directory.ByEntityKey(TradesChallengeResources.DatabankKeyFor(0))!.Owner);
            Assert.Equal(IslandCatalog.HavenId,
                directory.ByEntityKey(MetalDeposits.KeyFor(0))!.IslandId);
            Assert.Equal(IslandCatalog.TradesChallengeId,
                directory.ByEntityKey(TradesChallengeResources.DepositKeyFor(0))!.IslandId);
        }

        [Fact]
        public void Global_biome_data_is_explicitly_global_not_nearest_region()
        {
            WorldDirectory directory = Build(FullRegistry());

            Assert.Equal(WorldOwner.Global,
                directory.ByEntityKey(WorldEntities.GlobalEntityKey)!.Owner);
            Assert.Null(directory.ByEntityKey(WorldEntities.GlobalEntityKey)!.IslandId);
        }

        [Fact]
        public void Static_and_built_ship_members_share_their_stable_hull_root()
        {
            WorldEntityRegistry entities = FullRegistry();
            WorldEntity builtHull = Entity(BuiltShipPlacement.HullKey(17), IslandCatalog.Haven.GlobalOrigin);
            WorldEntity builtDeck = Entity(BuiltShipPlacement.DeckKey(17, 3), IslandCatalog.Haven.GlobalOrigin);
            entities.Register(builtHull);
            entities.Register(builtDeck);
            WorldDirectory directory = Build(entities);

            Assert.Equal(WorldOwner.ForShip(WorldEntities.ShipFrameKey),
                directory.ByEntityKey(WorldEntities.ShipFrameKey)!.Owner);
            Assert.Equal(WorldOwner.ForShip(WorldEntities.ShipFrameKey),
                directory.ByEntityKey(WorldEntities.HelmKey)!.Owner);
            Assert.Equal(WorldOwner.ForShip(builtHull.Key), directory.ByEntityKey(builtHull.Key)!.Owner);
            Assert.Equal(WorldOwner.ForShip(builtHull.Key), directory.ByEntityKey(builtDeck.Key)!.Owner);
            Assert.Null(directory.ByEntityKey(builtHull.Key)!.IslandId);
            Assert.Null(directory.ByEntityKey(builtDeck.Key)!.IslandId);
        }

        [Fact]
        public void Mounted_loose_part_override_moves_ownership_from_region_to_ship()
        {
            WorldEntityRegistry entities = new(new EntityIdAllocator());
            WorldEntity hull = Entity(BuiltShipPlacement.HullKey(4), IslandCatalog.Haven.GlobalOrigin);
            WorldEntity part = Entity(LoosePartPlacement.Key(2, "lamp01"), IslandCatalog.Haven.GlobalOrigin);
            entities.Register(hull);
            entities.Register(part);

            WorldDirectory loose = Build(entities);
            Assert.Equal(WorldOwner.ForRegion(RegionCatalog.HavenRegionId),
                loose.ByEntityKey(part.Key)!.Owner);

            WorldDirectory mounted = Build(entities, new Dictionary<string, string>
            {
                [part.Key] = hull.Key,
            });
            Assert.Equal(WorldOwner.ForShip(hull.Key), mounted.ByEntityKey(part.Key)!.Owner);
        }

        [Fact]
        public void Invalid_mount_override_is_rejected_without_mutating_world_registry()
        {
            WorldEntityRegistry entities = new(new EntityIdAllocator());
            WorldEntity part = Entity(LoosePartPlacement.Key(1, "sail01"), IslandCatalog.Haven.GlobalOrigin);
            entities.Register(part);

            Assert.Throws<ArgumentException>(() => Build(entities, new Dictionary<string, string>
            {
                [part.Key] = BuiltShipPlacement.HullKey(999),
            }));
            Assert.Same(part, entities.ByKey(part.Key));
        }

        [Fact]
        public void Orphaned_built_ship_member_is_rejected()
        {
            WorldEntityRegistry entities = new(new EntityIdAllocator());
            entities.Register(Entity(
                BuiltShipPlacement.DeckKey(41, 0),
                IslandCatalog.Haven.GlobalOrigin));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                Build(entities));
            Assert.Contains(BuiltShipPlacement.HullKey(41), error.Message);
        }

        [Fact]
        public void Directory_enumeration_is_stable_independent_of_registration_order()
        {
            WorldEntity a = Entity("placed-z:2", IslandCatalog.Haven.GlobalOrigin);
            WorldEntity b = Entity("placed-a:1", IslandCatalog.Haven.GlobalOrigin);
            WorldEntityRegistry entities = new(new EntityIdAllocator());
            entities.Register(a);
            entities.Register(b);

            Assert.Equal(new[] { b.Key, a.Key }, Build(entities).Entries.Select(e => e.Entity.Key));
        }

        [Fact]
        public void Resolved_island_affinity_survives_when_two_islands_share_one_region()
        {
            IslandDefinition west = new(new IslandId("west"), "West", "terrain-west",
                FixedPointPosition.FromMetres(0, 0, 0), "west-asset", "ctx", SpawnOrder.BeforePlayer);
            IslandDefinition east = new(new IslandId("east"), "East", "terrain-east",
                FixedPointPosition.FromMetres(10_000, 0, 0), "east-asset", "ctx", SpawnOrder.BeforePlayer);
            var islands = new IslandRegistry();
            islands.Register(west);
            islands.Register(east);
            var regions = new RegionRegistry(islands);
            RegionId shared = new("shared");
            regions.Register(new RegionDefinition(shared, "Shared", new[] { west.Id, east.Id }));
            var entities = new WorldEntityRegistry(new EntityIdAllocator());
            entities.Register(Entity("resource-west", FixedPointPosition.FromMetres(5, 0, 0)));
            entities.Register(Entity("resource-east", FixedPointPosition.FromMetres(9_995, 0, 0)));

            WorldDirectory directory = WorldDirectory.Build(entities, islands, regions);

            Assert.Equal(WorldOwner.ForRegion(shared), directory.ByEntityKey("resource-west")!.Owner);
            Assert.Equal(WorldOwner.ForRegion(shared), directory.ByEntityKey("resource-east")!.Owner);
            Assert.Equal(west.Id, directory.ByEntityKey("resource-west")!.IslandId);
            Assert.Equal(east.Id, directory.ByEntityKey("resource-east")!.IslandId);
        }

        private static WorldDirectory Build(
            WorldEntityRegistry entities,
            IReadOnlyDictionary<string, string>? overrides = null)
        {
            IslandRegistry islands = IslandRegistry.CreateDefault();
            return WorldDirectory.Build(entities, islands, RegionRegistry.CreateDefault(islands), overrides);
        }

        private static WorldEntityRegistry FullRegistry() => WorldEntities.Default(
            new EntityIdAllocator(),
            includeProofIsland: false,
            includeTree: true,
            includeMetal: true,
            metalOnlyProven: false,
            treeCountEnv: "999",
            oreCountEnv: "999",
            includeDeck: true,
            includeExtraParts: true,
            recogniseShip: true,
            includeDeposit: true,
            depositCountEnv: "999",
            includeDatabank: true,
            databankCountEnv: "999",
            includeAtlasShard: true,
            atlasRateEnv: "1",
            includeFuelPods: true,
            fuelPodCountEnv: "999",
            varyTreeSpecies: false,
            includeStaticShip: true,
            includeProductionSecondIsland: true);

        private static WorldEntity Entity(string key, FixedPointPosition position) =>
            new(key, "asset", WorldEntities.DefaultAssetContext, position);
    }
}

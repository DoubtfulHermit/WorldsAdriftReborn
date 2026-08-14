using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Regions
{
    public sealed class RegionRegistryTests
    {
        [Fact]
        public void Default_topology_assigns_each_proven_island_exactly_once()
        {
            IslandRegistry islands = IslandRegistry.CreateDefault();
            RegionRegistry regions = RegionRegistry.CreateDefault(islands);

            Assert.Same(RegionCatalog.Haven, regions.ByIsland(IslandCatalog.HavenId));
            Assert.Same(
                RegionCatalog.TradesChallenge,
                regions.ByIsland(IslandCatalog.TradesChallengeId));
            Assert.Equal(islands.All.Count, regions.All.Sum(region => region.IslandIds.Count));
        }

        [Fact]
        public void Default_topology_does_not_change_island_identity_or_coordinates()
        {
            IslandRegistry islands = IslandRegistry.CreateDefault();
            RegionRegistry regions = RegionRegistry.CreateDefault(islands);

            foreach (IslandDefinition island in islands.All)
            {
                Assert.NotNull(regions.ByIsland(island.Id));
                Assert.Same(island, islands.Require(island.Id));
            }

            Assert.Equal(
                new FixedPointPosition(69650145, -1305269, -4645549),
                islands.Require(IslandCatalog.HavenId).GlobalOrigin);
            Assert.Equal(
                new FixedPointPosition(54286560, -791844, -8077469),
                islands.Require(IslandCatalog.TradesChallengeId).GlobalOrigin);
        }

        [Fact]
        public void Region_and_member_enumeration_are_deterministic()
        {
            IslandRegistry islands = IslandRegistry.CreateDefault();
            RegionRegistry regions = new RegionRegistry(islands);
            regions.Register(Definition("z-region", IslandCatalog.TradesChallengeId));
            regions.Register(Definition("a-region", IslandCatalog.HavenId));

            Assert.Equal(new[] { "a-region", "z-region" }, regions.All.Select(r => r.Id.Value));
        }

        [Fact]
        public void Duplicate_region_ids_are_rejected()
        {
            RegionRegistry regions = new RegionRegistry(IslandRegistry.CreateDefault());
            regions.Register(Definition("shared", IslandCatalog.HavenId));

            Assert.Throws<ArgumentException>(() =>
                regions.Register(Definition("shared", IslandCatalog.TradesChallengeId)));
        }

        [Fact]
        public void An_island_cannot_belong_to_two_regions()
        {
            RegionRegistry regions = new RegionRegistry(IslandRegistry.CreateDefault());
            regions.Register(Definition("first", IslandCatalog.HavenId));

            Assert.Throws<ArgumentException>(() =>
                regions.Register(Definition("second", IslandCatalog.HavenId)));
        }

        [Fact]
        public void Unknown_island_membership_is_rejected_without_partial_registration()
        {
            RegionRegistry regions = new RegionRegistry(IslandRegistry.CreateDefault());
            RegionDefinition invalid = Definition("unknown-owner", new IslandId("missing"));

            Assert.Throws<KeyNotFoundException>(() => regions.Register(invalid));
            Assert.Null(regions.ById(invalid.Id));
        }

        [Fact]
        public void Duplicate_members_inside_one_definition_are_rejected()
        {
            Assert.Throws<ArgumentException>(() => new RegionDefinition(
                new RegionId("duplicate-members"),
                "Duplicate members",
                new[] { IslandCatalog.HavenId, IslandCatalog.HavenId }));
        }

        private static RegionDefinition Definition(string id, params IslandId[] islands) =>
            new RegionDefinition(new RegionId(id), id, islands);
    }
}

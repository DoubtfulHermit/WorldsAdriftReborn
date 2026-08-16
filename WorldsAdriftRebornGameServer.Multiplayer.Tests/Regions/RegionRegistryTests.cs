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

            Assert.Equal(2, islands.All.Count);
            Assert.Equal(2, regions.All.Count);
            Assert.Same(RegionCatalog.Haven, regions.ByIsland(IslandCatalog.HavenId));
            Assert.Same(
                RegionCatalog.TradesChallenge,
                regions.ByIsland(IslandCatalog.TradesChallengeId));
            Assert.Equal(islands.All.Count, regions.All.Sum(region => region.IslandIds.Count));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void Opt_in_first_C6_topology_has_one_owner_for_every_selected_island(
            int optionalCount)
        {
            IslandRegistry islands = IslandRegistry.CreateWithFirstRegionTerrain(optionalCount);
            RegionRegistry regions =
                RegionRegistry.CreateWithFirstRegionTerrain(islands, optionalCount);

            RegionDefinition region = regions.Require(RegionCatalog.FirstC6RegionId);
            Assert.Single(regions.All);
            Assert.Equal(optionalCount + 1, region.IslandIds.Count);
            Assert.Equal(islands.All.Count, region.IslandIds.Count);
            foreach (IslandDefinition island in islands.All)
                Assert.Same(region, regions.ByIsland(island.Id));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void Opt_in_first_C6_membership_is_deterministic_and_ordinal(int optionalCount)
        {
            IslandRegistry firstIslands =
                IslandRegistry.CreateWithFirstRegionTerrain(optionalCount);
            IslandRegistry secondIslands =
                IslandRegistry.CreateWithFirstRegionTerrain(optionalCount);
            RegionDefinition first = RegionRegistry
                .CreateWithFirstRegionTerrain(firstIslands, optionalCount)
                .Require(RegionCatalog.FirstC6RegionId);
            RegionDefinition second = RegionRegistry
                .CreateWithFirstRegionTerrain(secondIslands, optionalCount)
                .Require(RegionCatalog.FirstC6RegionId);

            Assert.Equal(first.IslandIds, second.IslandIds);
            Assert.Equal(
                first.IslandIds.OrderBy(id => id.Value, StringComparer.Ordinal),
                first.IslandIds);
        }

        [Fact]
        public void Opt_in_first_C6_region_cannot_gain_a_second_owner_for_a_member()
        {
            IslandRegistry islands = IslandRegistry.CreateWithFirstRegionTerrain(4);
            RegionRegistry regions = RegionRegistry.CreateWithFirstRegionTerrain(islands, 4);

            Assert.Throws<ArgumentException>(() =>
                regions.Register(Definition("duplicate-owner", IslandCatalog.AnchorageIsleId)));
        }

        [Fact]
        public void Opt_in_factory_rejects_a_registry_from_a_different_prefix()
        {
            IslandRegistry islands = IslandRegistry.CreateWithFirstRegionTerrain(2);

            Assert.Throws<ArgumentException>(() =>
                RegionRegistry.CreateWithFirstRegionTerrain(islands, 3));
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

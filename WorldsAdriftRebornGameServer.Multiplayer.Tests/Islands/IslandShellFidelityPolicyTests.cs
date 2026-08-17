using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    public sealed class IslandShellFidelityPolicyTests
    {
        /// <summary>
        /// REGRESSION GUARD. Both bounded-rollout islands are records in the same
        /// embedded 254-island catalogue, so a membership-only test would downgrade
        /// them to a compact prism the moment the catalogue shipped. The bounded
        /// configuration must keep asking for their retail terrain LOD.
        /// </summary>
        [Fact]
        public void Bounded_rollout_keeps_retail_lod_for_catalogued_islands()
        {
            Assert.NotNull(ReleaseWorldCatalog.ByIsland(IslandCatalog.MentalFacilityId));
            Assert.NotNull(ReleaseWorldCatalog.ByIsland(IslandCatalog.TradesChallengeId));

            Assert.Equal(IslandShellFidelity.RetailLod, IslandShellFidelityPolicy.Choose(
                ReleaseWorldCatalog.ByIsland(IslandCatalog.MentalFacilityId),
                releaseWorldRolloutActive: false));
            Assert.Equal(IslandShellFidelity.RetailLod, IslandShellFidelityPolicy.Choose(
                ReleaseWorldCatalog.ByIsland(IslandCatalog.TradesChallengeId),
                releaseWorldRolloutActive: false));
        }

        [Fact]
        public void Complete_rollout_uses_the_compact_outline_fallback()
        {
            Assert.All(ReleaseWorldCatalog.All, record => Assert.Equal(
                IslandShellFidelity.CompactOutline, IslandShellFidelityPolicy.Choose(
                    record, releaseWorldRolloutActive: true)));
        }

        [Fact]
        public void Island_without_a_release_record_is_always_retail_lod()
        {
            Assert.Null(ReleaseWorldCatalog.ByIsland(IslandCatalog.HavenId));

            Assert.Equal(IslandShellFidelity.RetailLod, IslandShellFidelityPolicy.Choose(
                ReleaseWorldCatalog.ByIsland(IslandCatalog.HavenId),
                releaseWorldRolloutActive: false));
            Assert.Equal(IslandShellFidelity.RetailLod, IslandShellFidelityPolicy.Choose(
                ReleaseWorldCatalog.ByIsland(IslandCatalog.HavenId),
                releaseWorldRolloutActive: true));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(33)]
        public void Outline_the_protocol_cannot_encode_never_selects_v2(int points)
        {
            Assert.False(IslandShellFidelityPolicy.IsEncodableOutline(points));
            Assert.Equal(IslandShellFidelity.RetailLod, IslandShellFidelityPolicy.Choose(
                points, releaseWorldRolloutActive: true));
            Assert.Equal(IslandShellFidelity.RetailLod, IslandShellFidelityPolicy.Choose(
                points, releaseWorldRolloutActive: false));
        }

        [Fact]
        public void Compact_outline_cannot_be_encoded_without_catalogue_outline_data()
        {
            Assert.Throws<InvalidOperationException>(
                () => IslandShellFidelityPolicy.RequireOutline(null));
            Assert.Same(ReleaseWorldCatalog.Require(IslandCatalog.MentalFacilityId),
                IslandShellFidelityPolicy.RequireOutline(
                    ReleaseWorldCatalog.ByIsland(IslandCatalog.MentalFacilityId)));
        }

        [Fact]
        public void Every_catalogued_outline_is_within_the_wire_encodable_range()
        {
            Assert.All(ReleaseWorldCatalog.All, record =>
            {
                Assert.True(IslandShellFidelityPolicy.IsEncodableOutline(record.Shell.Count));
                Assert.NotNull(IslandDistantShellProtocol.ProceduralRequest(
                    record.Definition.Id.Value, 1, 0, 0, 0,
                    record.Envelope.MinY, record.Envelope.MaxY, record.Shell.ToArray()));
            });
        }
    }
}

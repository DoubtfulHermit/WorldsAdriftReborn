using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class IslandCollisionProxyAdapterTests
    {
        [Fact]
        public void Haven_proxy_uses_extracted_world_translated_envelope_and_stamp()
        {
            ShadowVector3 origin = new(IslandCatalog.Haven.GlobalOrigin.MetresX,
                IslandCatalog.Haven.GlobalOrigin.MetresY,
                IslandCatalog.Haven.GlobalOrigin.MetresZ);
            IslandCollisionProxyBatch batch = IslandCollisionProxyAdapter.Nearby(origin, 42, 9, 1);
            CollisionRuntimeProxy haven = Assert.Single(batch.Proxies);
            IslandTerrainEnvelope envelope = IslandTerrainEnvelopes.Require(IslandCatalog.HavenId);

            Assert.True(batch.EvaluationComplete);
            Assert.Equal("island:haven", haven.Proxy.Id);
            Assert.Equal(42, haven.FixedStep);
            Assert.Equal(9, haven.AuthorityGeneration);
            Assert.Equal(CollisionGeometryConfidence.ConservativeEnvelope,
                haven.GeometryConfidence);
            Assert.Equal(origin.X + envelope.MinX, haven.Proxy.Bounds.Minimum.X, 8);
            Assert.Equal(origin.Y + envelope.MaxY, haven.Proxy.Bounds.Maximum.Y, 8);
        }

        [Fact]
        public void Nearby_selection_is_stable_bounded_and_excludes_distant_map()
        {
            ShadowVector3 haven = new(IslandCatalog.Haven.GlobalOrigin.MetresX,
                IslandCatalog.Haven.GlobalOrigin.MetresY,
                IslandCatalog.Haven.GlobalOrigin.MetresZ);
            IslandCollisionProxyBatch first = IslandCollisionProxyAdapter.Nearby(haven, 1, 1, 10000);
            IslandCollisionProxyBatch second = IslandCollisionProxyAdapter.Nearby(haven, 1, 1, 10000);

            Assert.Equal(first.Proxies.Select(x => x.Proxy.Id),
                second.Proxies.Select(x => x.Proxy.Id));
            Assert.InRange(first.Proxies.Count, 1, CollisionShadowLimits.MaxTerrainProxies);
            Assert.True(first.CandidateCount < IslandLocationPolicy.KnownWorld().Count());
        }

        [Fact]
        public void Invalid_frame_fails_closed_without_geometry()
        {
            IslandCollisionProxyBatch bad = IslandCollisionProxyAdapter.Nearby(
                new ShadowVector3(double.NaN, 0, 0), -1, 0);
            Assert.False(bad.EvaluationComplete);
            Assert.Empty(bad.Proxies);
        }
    }
}

using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The coordinate-frame guard. These assert the two things that matter: it can never
    /// refuse a REAL on-island placement (which would starve the world of ore for no
    /// reason), and it always refuses the specific frame/scale catastrophes that put
    /// deposits in the sky.
    /// </summary>
    public class IslandBoundsTests
    {
        private static (double X, double Y, double Z) HavenGlobal(double lx, double ly, double lz)
        {
            FixedPointPosition p = MetalNodes.IslandLocalToWorldFixed(SpawnPolicy.IslandPosition, lx, ly, lz);
            return (p.MetresX, p.MetresY, p.MetresZ);
        }

        [Fact]
        public void Haven_box_contains_the_island_origin()
        {
            IslandBounds b = IslandBounds.Haven();
            Assert.True(b.Contains(
                SpawnPolicy.IslandPosition.MetresX,
                SpawnPolicy.IslandPosition.MetresY,
                SpawnPolicy.IslandPosition.MetresZ));
        }

        [Fact]
        public void Haven_box_contains_the_player_spawn_point()
        {
            IslandBounds b = IslandBounds.Haven();
            Assert.True(b.Contains(
                SpawnPolicy.PlayerSpawnPosition.MetresX,
                SpawnPolicy.PlayerSpawnPosition.MetresY,
                SpawnPolicy.PlayerSpawnPosition.MetresZ));
        }

        [Fact]
        public void Haven_box_contains_every_hand_placed_deposit()
        {
            IslandBounds b = IslandBounds.Haven();
            foreach (MetalNode node in MetalDeposits.Haven(MetalDeposits.HavenPlacements.Count))
            {
                Assert.True(
                    b.Contains(node.Position.MetresX, node.Position.MetresY, node.Position.MetresZ),
                    "hand-placed deposit '" + node.Key + "' at " + node.Position + " must be inside " + b);
            }
        }

        [Fact]
        public void Haven_box_contains_the_measured_surface_corners()
        {
            IslandBounds b = IslandBounds.Haven();
            (double X, double Y, double Z) min = HavenGlobal(
                IslandBounds.HavenLocalMin.X, IslandBounds.HavenLocalMin.Y, IslandBounds.HavenLocalMin.Z);
            (double X, double Y, double Z) max = HavenGlobal(
                IslandBounds.HavenLocalMax.X, IslandBounds.HavenLocalMax.Y, IslandBounds.HavenLocalMax.Z);

            Assert.True(b.Contains(min.X, min.Y, min.Z));
            Assert.True(b.Contains(max.X, max.Y, max.Z));
        }

        [Fact]
        public void Haven_box_contains_a_point_just_outside_the_measured_mesh()
        {
            // The extracted surface is a SAMPLE of the mesh, so a legitimate placement may
            // sit a little wider than the measured AABB. The margin must absorb that.
            IslandBounds b = IslandBounds.Haven();
            (double X, double Y, double Z) p = HavenGlobal(
                IslandBounds.HavenLocalMax.X + 100.0, 20.0, IslandBounds.HavenLocalMax.Z + 100.0);
            Assert.True(b.Contains(p.X, p.Y, p.Z));
        }

        [Fact]
        public void Refuses_an_unremapped_island_local_reply()
        {
            // THE live failure mode: the client's OffsetOrigin is still zero, so it replies
            // in island-local metres instead of global ones.
            IslandBounds b = IslandBounds.Haven();
            Assert.False(b.Contains(216.0, 4.57, 8.0));
            Assert.False(b.Contains(0, 0, 0));
        }

        [Fact]
        public void Refuses_a_scale_error_on_any_axis()
        {
            IslandBounds b = IslandBounds.Haven();
            (double X, double Y, double Z) good = HavenGlobal(216.0, 4.57, 8.0);

            Assert.True(b.Contains(good.X, good.Y, good.Z));
            Assert.False(b.Contains(good.X * 100.0, good.Y * 100.0, good.Z * 100.0));
            Assert.False(b.Contains(good.X / 100.0, good.Y / 100.0, good.Z / 100.0));
            // One axis is enough to refuse the whole placement.
            Assert.False(b.Contains(good.X * 4096.0, good.Y, good.Z));
            Assert.False(b.Contains(good.X, good.Y * 4096.0, good.Z));
            Assert.False(b.Contains(good.X, good.Y, good.Z * 4096.0));
        }

        [Fact]
        public void Refuses_the_sky()
        {
            IslandBounds b = IslandBounds.Haven();
            (double X, double Y, double Z) good = HavenGlobal(216.0, 4.57, 8.0);
            Assert.False(b.Contains(good.X, good.Y + 5000.0, good.Z));
        }

        [Fact]
        public void Around_applies_the_margin_symmetrically()
        {
            FixedPointPosition origin = FixedPointPosition.FromMetres(1000, 2000, 3000);
            IslandBounds b = IslandBounds.Around(origin, (-10, -10, -10), (10, 10, 10), 5);

            Assert.Equal(1000 - 15, b.MinX, 3);
            Assert.Equal(1000 + 15, b.MaxX, 3);
            Assert.Equal(2000 - 15, b.MinY, 3);
            Assert.Equal(3000 + 15, b.MaxZ, 3);

            Assert.True(b.Contains(1015, 2015, 3015));
            Assert.False(b.Contains(1015.001, 2000, 3000));
        }

        [Fact]
        public void A_negative_margin_is_treated_as_zero()
        {
            FixedPointPosition origin = FixedPointPosition.FromMetres(0, 0, 0);
            IslandBounds b = IslandBounds.Around(origin, (-1, -1, -1), (1, 1, 1), -50);
            Assert.True(b.Contains(1, 1, 1));
            Assert.False(b.Contains(1.5, 0, 0));
        }
    }
}

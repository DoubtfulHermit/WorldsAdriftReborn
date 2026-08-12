using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The env-to-config and in-range rules of connect-time spatial interest. Every
    /// rule must fail SAFE: a bad env var can neither empty the world nor throw the
    /// server down, and a disabled radius must leave EVERY entity in range so the
    /// old all-entities behaviour is preserved byte-for-byte.
    /// </summary>
    public class InterestPolicyTests
    {
        // ------------------------------------------------------------------
        // Radius parsing - opt in, fail safe
        // ------------------------------------------------------------------

        [Fact]
        public void Radius_is_disabled_for_unset_empty_or_nonsense()
        {
            Assert.Equal(0.0, InterestPolicy.RadiusMetresFrom(null));
            Assert.Equal(0.0, InterestPolicy.RadiusMetresFrom(""));
            Assert.Equal(0.0, InterestPolicy.RadiusMetresFrom("   "));
            Assert.Equal(0.0, InterestPolicy.RadiusMetresFrom("banana"));
            Assert.Equal(0.0, InterestPolicy.RadiusMetresFrom("NaN"));
        }

        [Fact]
        public void Zero_or_negative_radius_is_disabled()
        {
            Assert.Equal(0.0, InterestPolicy.RadiusMetresFrom("0"));
            Assert.Equal(0.0, InterestPolicy.RadiusMetresFrom("-1"));
            Assert.Equal(0.0, InterestPolicy.RadiusMetresFrom("-350.5"));
        }

        [Fact]
        public void A_positive_radius_parses_with_invariant_culture()
        {
            Assert.Equal(300.0, InterestPolicy.RadiusMetresFrom("300"));
            Assert.Equal(350.5, InterestPolicy.RadiusMetresFrom("350.5"));
        }

        [Fact]
        public void A_colossal_radius_is_clamped()
        {
            Assert.Equal(InterestPolicy.MaxRadiusMetres, InterestPolicy.RadiusMetresFrom("1e12"));
        }

        [Fact]
        public void IsEnabled_tracks_a_positive_radius()
        {
            Assert.False(InterestPolicy.IsEnabled(0.0));
            Assert.False(InterestPolicy.IsEnabled(-5.0));
            Assert.True(InterestPolicy.IsEnabled(1.0));
            Assert.True(InterestPolicy.IsEnabled(300.0));
        }

        // ------------------------------------------------------------------
        // In-range - fail open when disabled
        // ------------------------------------------------------------------

        [Fact]
        public void Disabled_radius_keeps_everything_in_range()
        {
            FixedPointPosition center = FixedPointPosition.FromMetres(0, 0, 0);
            FixedPointPosition faraway = FixedPointPosition.FromMetres(100000, 0, 0);
            Assert.True(InterestPolicy.InRange(center, faraway, 0.0));
            Assert.True(InterestPolicy.InRange(center, faraway, -1.0));
        }

        [Fact]
        public void An_entity_inside_the_radius_is_in_range()
        {
            FixedPointPosition center = FixedPointPosition.FromMetres(0, 0, 0);
            FixedPointPosition near = FixedPointPosition.FromMetres(100, 0, 0);
            Assert.True(InterestPolicy.InRange(center, near, 300.0));
        }

        [Fact]
        public void An_entity_outside_the_radius_is_out_of_range()
        {
            FixedPointPosition center = FixedPointPosition.FromMetres(0, 0, 0);
            FixedPointPosition far = FixedPointPosition.FromMetres(400, 0, 0);
            Assert.False(InterestPolicy.InRange(center, far, 300.0));
        }

        [Fact]
        public void The_boundary_is_inclusive()
        {
            FixedPointPosition center = FixedPointPosition.FromMetres(0, 0, 0);
            FixedPointPosition onEdge = FixedPointPosition.FromMetres(300, 0, 0);
            Assert.True(InterestPolicy.InRange(center, onEdge, 300.0));
        }

        [Fact]
        public void Distance_is_full_3d()
        {
            FixedPointPosition center = FixedPointPosition.FromMetres(0, 0, 0);
            // (30,40,120) has magnitude 130 (3-4-5-12-13 style): inside 131, outside 129.
            FixedPointPosition p = FixedPointPosition.FromMetres(30, 40, 120);
            Assert.True(InterestPolicy.InRange(center, p, 131.0));
            Assert.False(InterestPolicy.InRange(center, p, 129.0));
        }

        [Fact]
        public void In_range_is_measured_from_the_center_not_the_origin()
        {
            FixedPointPosition center = FixedPointPosition.FromMetres(1000, 0, 1000);
            FixedPointPosition nearCenter = FixedPointPosition.FromMetres(1050, 0, 1000);
            FixedPointPosition nearOrigin = FixedPointPosition.FromMetres(50, 0, 0);
            Assert.True(InterestPolicy.InRange(center, nearCenter, 300.0));
            Assert.False(InterestPolicy.InRange(center, nearOrigin, 300.0));
        }

        // ------------------------------------------------------------------
        // Partition
        // ------------------------------------------------------------------

        [Fact]
        public void Partition_splits_by_range_and_preserves_order()
        {
            FixedPointPosition center = FixedPointPosition.FromMetres(0, 0, 0);
            List<(string Name, FixedPointPosition Pos)> items = new()
            {
                ("near1", FixedPointPosition.FromMetres(10, 0, 0)),
                ("far1", FixedPointPosition.FromMetres(500, 0, 0)),
                ("near2", FixedPointPosition.FromMetres(0, 0, 50)),
                ("far2", FixedPointPosition.FromMetres(0, 0, 900)),
            };

            var (inside, outside) = InterestPolicy.Partition(center, items, i => i.Pos, 100.0);

            Assert.Equal(new[] { "near1", "near2" }, inside.Select(i => i.Name).ToArray());
            Assert.Equal(new[] { "far1", "far2" }, outside.Select(i => i.Name).ToArray());
        }

        [Fact]
        public void Partition_with_a_disabled_radius_keeps_everything_in_range()
        {
            FixedPointPosition center = FixedPointPosition.FromMetres(0, 0, 0);
            List<FixedPointPosition> items = new()
            {
                FixedPointPosition.FromMetres(10, 0, 0),
                FixedPointPosition.FromMetres(100000, 0, 0),
            };

            var (inside, outside) = InterestPolicy.Partition(center, items, p => p, 0.0);

            Assert.Equal(2, inside.Count);
            Assert.Empty(outside);
        }
    }
}

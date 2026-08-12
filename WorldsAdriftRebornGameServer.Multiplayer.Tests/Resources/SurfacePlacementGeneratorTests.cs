using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Resources;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Resources
{
    /// <summary>
    /// The pure DETERMINISTIC placement generator, tested on synthetic surfaces with
    /// no embedded data and no game types: the acceptance rules (upward normal,
    /// reachable-height band, exclusions), the min-spacing thinning, the target cap,
    /// anchor handling, and - the property the whole revival leans on - that the same
    /// surface and config always yield the identical layout, independent of input
    /// order. If any of these break, the world's resources move under persistence and
    /// mining state that was keyed to where they used to be.
    /// </summary>
    public class SurfacePlacementGeneratorTests
    {
        private static SurfacePlacementConfig Config(
            double minNormal = 0.9,
            double minH = 1.0,
            double maxH = 12.0,
            double spacing = 5.0,
            int target = 1000,
            IReadOnlyList<PlacementExclusion>? excl = null)
            => new SurfacePlacementConfig(minNormal, minH, maxH, spacing, target, excl);

        // A wide flat grid of upward-facing points at reachable height, 4 m apart.
        private static List<SurfaceSample> FlatGrid(int nx, int nz, double step = 4.0, double y = 5.0, double ny = 1.0)
        {
            List<SurfaceSample> s = new List<SurfaceSample>();
            for (int i = 0; i < nx; i++)
            {
                for (int j = 0; j < nz; j++)
                {
                    s.Add(new SurfaceSample(i * step, y, j * step, 0.0, ny, 0.0));
                }
            }
            return s;
        }

        [Fact]
        public void Rejects_surfaces_whose_normal_is_below_the_upward_threshold()
        {
            List<SurfaceSample> s = new List<SurfaceSample>
            {
                new SurfaceSample(0, 5, 0, 0, 1.0, 0),   // dead flat - accepted
                new SurfaceSample(50, 5, 0, 0.9, 0.4, 0), // a 0.4 slope - rejected
                new SurfaceSample(100, 5, 0, 1.0, 0.0, 0) // a wall - rejected
            };
            var placed = SurfacePlacementGenerator.Generate(s, Config(minNormal: 0.9));
            Assert.Single(placed);
            Assert.Equal(1.0, placed[0].Ny);
        }

        [Fact]
        public void Rejects_points_outside_the_reachable_height_band()
        {
            List<SurfaceSample> s = new List<SurfaceSample>
            {
                new SurfaceSample(0, 0.2, 0, 0, 1, 0),   // below the band
                new SurfaceSample(50, 5.0, 0, 0, 1, 0),  // in the band - accepted
                new SurfaceSample(100, 45.0, 0, 0, 1, 0) // the unreachable camp platform
            };
            var placed = SurfacePlacementGenerator.Generate(s, Config(minH: 1.0, maxH: 12.0));
            Assert.Single(placed);
            Assert.Equal(5.0, placed[0].LocalY);
        }

        [Fact]
        public void Rejects_points_inside_an_exclusion_disc()
        {
            var excl = new[] { new PlacementExclusion(0, 0, 10.0) };
            List<SurfaceSample> s = new List<SurfaceSample>
            {
                new SurfaceSample(0, 5, 0, 0, 1, 0),    // dead centre of the disc
                new SurfaceSample(5, 5, 5, 0, 1, 0),    // inside (r ~ 7.07)
                new SurfaceSample(20, 5, 0, 0, 1, 0)    // outside - accepted
            };
            var placed = SurfacePlacementGenerator.Generate(s, Config(excl: excl));
            Assert.Single(placed);
            Assert.Equal(20.0, placed[0].LocalX);
        }

        [Fact]
        public void No_two_accepted_placements_are_closer_than_the_min_spacing()
        {
            // A 4 m grid thinned to 10 m spacing: every accepted pair must be >= 10 m.
            var placed = SurfacePlacementGenerator.Generate(FlatGrid(12, 12), Config(spacing: 10.0));
            Assert.True(placed.Count > 1);
            for (int i = 0; i < placed.Count; i++)
            {
                for (int j = i + 1; j < placed.Count; j++)
                {
                    double dx = placed[i].LocalX - placed[j].LocalX;
                    double dy = placed[i].LocalY - placed[j].LocalY;
                    double dz = placed[i].LocalZ - placed[j].LocalZ;
                    double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    Assert.True(d >= 10.0 - 1e-9, $"pair {i},{j} only {d:0.###} m apart");
                }
            }
        }

        [Fact]
        public void Never_emits_more_than_the_target_count()
        {
            var placed = SurfacePlacementGenerator.Generate(FlatGrid(20, 20), Config(spacing: 4.0, target: 15));
            Assert.Equal(15, placed.Count);
        }

        [Fact]
        public void Anchors_reserve_space_are_not_emitted_and_count_against_the_target()
        {
            // An anchor at the origin must push generated points at least a spacing
            // away from it, and must not itself appear in the output.
            var anchors = new[] { new GeneratedPlacement(0, 5, 0, 1.0) };
            var placed = SurfacePlacementGenerator.Generate(FlatGrid(12, 12), Config(spacing: 10.0), anchors);

            foreach (var p in placed)
            {
                double d = Math.Sqrt(p.LocalX * p.LocalX + (p.LocalY - 5) * (p.LocalY - 5) + p.LocalZ * p.LocalZ);
                Assert.True(d >= 10.0 - 1e-9, $"generated point {d:0.###} m from the anchor");
                Assert.False(p.LocalX == 0 && p.LocalZ == 0, "the anchor was re-emitted");
            }

            // The anchor counts against the cap: target 5 total => 4 generated.
            var capped = SurfacePlacementGenerator.Generate(FlatGrid(20, 20), Config(spacing: 4.0, target: 5), anchors);
            Assert.Equal(4, capped.Count);
        }

        [Fact]
        public void Is_deterministic_and_independent_of_input_order()
        {
            var grid = FlatGrid(10, 10);
            var a = SurfacePlacementGenerator.Generate(grid, Config(spacing: 9.0));

            // Reverse the input order; the hash-ordered generator must return the
            // identical SEQUENCE, not merely the same set - so the layout can never
            // depend on how the surface table happened to be enumerated.
            var reversed = new List<SurfaceSample>(grid);
            reversed.Reverse();
            var b = SurfacePlacementGenerator.Generate(reversed, Config(spacing: 9.0));

            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].LocalX, b[i].LocalX);
                Assert.Equal(a[i].LocalY, b[i].LocalY);
                Assert.Equal(a[i].LocalZ, b[i].LocalZ);
            }
        }

        [Fact]
        public void Empty_or_fully_filtered_input_yields_an_empty_layout_without_throwing()
        {
            Assert.Empty(SurfacePlacementGenerator.Generate(new List<SurfaceSample>(), Config()));

            // All points are walls -> all rejected -> empty, no exception.
            var walls = new List<SurfaceSample>
            {
                new SurfaceSample(0, 5, 0, 1, 0.0, 0),
                new SurfaceSample(10, 5, 0, 1, 0.1, 0)
            };
            Assert.Empty(SurfacePlacementGenerator.Generate(walls, Config(minNormal: 0.9)));
        }

        [Fact]
        public void The_coordinate_hash_is_a_pure_function_of_the_point()
        {
            // Same coordinates -> same hash; different -> (essentially always) different.
            Assert.Equal(
                SurfacePlacementGenerator.HashKey(216.0, 4.57, 8.0),
                SurfacePlacementGenerator.HashKey(216.0, 4.57, 8.0));
            Assert.NotEqual(
                SurfacePlacementGenerator.HashKey(216.0, 4.57, 8.0),
                SurfacePlacementGenerator.HashKey(216.0, 4.57, 8.001));
        }
    }
}

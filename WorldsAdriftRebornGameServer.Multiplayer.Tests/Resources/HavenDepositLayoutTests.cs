using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Resources;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Resources
{
    /// <summary>
    /// The Haven deposit field as it comes off the REAL embedded surface table under
    /// the reviewed config: that it is genuinely DENSE (the point of the change - far
    /// more than the old ~23 hand-placed rocks), that every deposit sits on reachable,
    /// upward-facing ground and is spaced out and clear of the spawn/ship/trees, and -
    /// the load-bearing property - that it is byte-stable across restarts so mining
    /// and persistence state keyed to a deposit index never drifts.
    /// </summary>
    public class HavenDepositLayoutTests
    {
        [Fact]
        public void The_embedded_surface_table_loads_the_full_candidate_set()
        {
            // The extracted Haven table records 2,139 candidate surface points.
            Assert.Equal(2139, HavenSurface.Samples.Count);
        }

        [Fact]
        public void The_field_is_dense_far_beyond_the_old_hand_placed_handful()
        {
            // The user asked for a resource-RICH world: well over 100 deposits, not 23.
            IReadOnlyList<GeneratedPlacement> locals = HavenSurface.DepositLocals();
            Assert.True(locals.Count > 100, $"expected a dense field, got {locals.Count}");
            Assert.Equal(locals.Count, MetalDeposits.HavenPlacements.Count);
        }

        [Fact]
        public void The_proven_deposit_is_index_zero()
        {
            GeneratedPlacement first = HavenSurface.DepositLocals()[0];
            Assert.Equal(216.0, first.LocalX);
            Assert.Equal(4.57, first.LocalY);
            Assert.Equal(8.0, first.LocalZ);
        }

        [Fact]
        public void Every_deposit_sits_on_reachable_upward_facing_ground()
        {
            foreach (GeneratedPlacement p in HavenSurface.DepositLocals())
            {
                Assert.InRange(p.LocalY, HavenSurface.DepositMinHeight, HavenSurface.DepositMaxHeight);
                Assert.True(p.Ny >= HavenSurface.DepositMinUpwardNormal - 1e-9,
                    $"deposit at ({p.LocalX},{p.LocalY},{p.LocalZ}) has ny {p.Ny}");
            }
        }

        [Fact]
        public void Every_deposit_is_at_least_the_min_spacing_from_every_other()
        {
            IReadOnlyList<GeneratedPlacement> p = HavenSurface.DepositLocals();
            for (int i = 0; i < p.Count; i++)
            {
                for (int j = i + 1; j < p.Count; j++)
                {
                    double dx = p[i].LocalX - p[j].LocalX;
                    double dy = p[i].LocalY - p[j].LocalY;
                    double dz = p[i].LocalZ - p[j].LocalZ;
                    double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    Assert.True(d >= HavenSurface.DepositMinSpacing - 1e-9,
                        $"deposits {i},{j} only {d:0.###} m apart");
                }
            }
        }

        [Fact]
        public void No_deposit_falls_inside_the_spawn_ship_or_tree_keep_out_discs()
        {
            SurfacePlacementConfig cfg = HavenSurface.DepositConfig();
            foreach (GeneratedPlacement p in HavenSurface.DepositLocals())
            {
                Assert.False(cfg.IsExcluded(p.LocalX, p.LocalZ),
                    $"deposit at ({p.LocalX},{p.LocalZ}) is inside a keep-out disc");
            }
        }

        [Fact]
        public void The_layout_is_stable_across_restarts()
        {
            // Simulate two server boots: regenerate from scratch off the same embedded
            // surface and config. The generator has no RNG and no clock, so the two
            // layouts must be identical element-for-element - the guarantee that a
            // deposit's index (and thus its persistence/mining state) means the same
            // place after a restart.
            IReadOnlyList<GeneratedPlacement> boot1 =
                SurfacePlacementGenerator.Generate(HavenSurface.Samples, HavenSurface.DepositConfig());
            IReadOnlyList<GeneratedPlacement> boot2 =
                SurfacePlacementGenerator.Generate(HavenSurface.Samples, HavenSurface.DepositConfig());

            Assert.Equal(boot1.Count, boot2.Count);
            for (int i = 0; i < boot1.Count; i++)
            {
                Assert.Equal(boot1[i].LocalX, boot2[i].LocalX);
                Assert.Equal(boot1[i].LocalY, boot2[i].LocalY);
                Assert.Equal(boot1[i].LocalZ, boot2[i].LocalZ);
            }
        }

        [Fact]
        public void A_deposit_index_maps_to_the_same_world_position_every_time()
        {
            // The registry keys deposits "deposit-N"; N must resolve to a fixed world
            // position so a late joiner's seed and the live entity agree. Two lookups
            // of the same index are identical, and index 0 is the proven vertex.
            Assert.Equal(MetalDeposits.NodeAt(5).Position, MetalDeposits.NodeAt(5).Position);
            FixedPointPosition proven = MetalNodes.IslandLocalToWorldFixed(
                MetalDeposits.IslandOrigin, 216.0, 4.57, 8.0);
            Assert.Equal(proven, MetalDeposits.NodeAt(0).Position);
        }
    }
}

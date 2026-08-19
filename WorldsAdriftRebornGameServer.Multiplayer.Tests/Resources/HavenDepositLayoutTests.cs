using System;
using System.Collections.Generic;
using System.Linq;
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
        public void The_field_uses_the_recovered_retail_count_across_the_whole_island()
        {
            IReadOnlyList<GeneratedPlacement> locals = HavenSurface.DepositLocals();
            Assert.Equal(HavenSurface.DepositTargetCount, locals.Count);
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
        public void Every_deposit_sits_on_upward_facing_ground_without_an_altitude_band()
        {
            foreach (GeneratedPlacement p in HavenSurface.DepositLocals())
            {
                Assert.InRange(p.LocalY, HavenSurface.ResourceMinHeight, HavenSurface.ResourceMaxHeight);
                Assert.True(p.Ny >= HavenSurface.DepositMinUpwardNormal - 1e-9,
                    $"deposit at ({p.LocalX},{p.LocalY},{p.LocalZ}) has ny {p.Ny}");
            }

            // Regression for the barren-ridge bug: the old y<=12 filter could not
            // put a single rock on the high terrain visible from the player's shot.
            Assert.Contains(HavenSurface.DepositLocals(), p => p.LocalY > 20.0);
        }

        [Fact]
        public void Deposits_span_the_full_walkable_width_and_depth()
        {
            IReadOnlyList<GeneratedPlacement> p = HavenSurface.DepositLocals();
            Assert.True(p.Max(x => x.LocalX) - p.Min(x => x.LocalX) > 400.0);
            Assert.True(p.Max(x => x.LocalZ) - p.Min(x => x.LocalZ) > 200.0);
            Assert.Contains(p, x => x.LocalX < 0.0);
            Assert.Contains(p, x => x.LocalX > 150.0);
        }

        /// <summary>
        /// This used to assert that every Haven deposit was iron at quality 6, and
        /// it was right to: the alternative on offer then was an invented rotating
        /// assortment, which would have manufactured lore for a Bossa-authored
        /// island that has no surviving survey row.
        ///
        /// Haven now draws from the surveyed TIER-1 COHORT instead - the same
        /// cohort, and the same method, that already composed metal tables for the
        /// 193 islands the community survey never recorded - so the metals are
        /// inferred the way every other unsurveyed island's already are rather than
        /// chosen. What the old test was really protecting is kept and made
        /// explicit: iron still dominates, and the node beside the spawn is still
        /// iron, so the starter loop is not starved.
        ///
        /// The QUALITY assertion is unchanged on purpose. Strict cohort fidelity
        /// would put Haven in the tier-1 band of 1..4, i.e. a balance cut, and that
        /// is a maintainer's decision rather than a side effect of varying metals.
        /// </summary>
        [Fact]
        public void Haven_deposits_lean_on_starter_iron_but_are_no_longer_uniform()
        {
            Assert.All(MetalDeposits.HavenPlacements, p => Assert.Equal(6, p.Quality));

            Assert.Equal("iron", MetalDeposits.HavenPlacements[0].MetalType);

            IReadOnlyList<string> metals = MetalDeposits.HavenPlacements
                .Select(p => p.MetalType).ToList();

            Assert.True(metals.Distinct().Count() >= 5,
                "Haven should span the tier-1 cohort, not one metal");
            Assert.True(metals.Count(m => m == "iron") * 2 >= metals.Count,
                "iron should still be at least half of Haven's deposits");
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

    }
}

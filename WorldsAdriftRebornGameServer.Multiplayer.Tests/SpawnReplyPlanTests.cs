using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public class SpawnReplyPlanTests
    {
        private static ResourceReplyItem Metal(double x, double y, double z, string? variant = "metal_deposit_composite_light_01")
            => new ResourceReplyItem(x, y, z, SpawnReplyPlan.MetalMetadata, variant);

        [Fact]
        public void IsMetal_case_insensitive_and_rejects_egg()
        {
            Assert.True(SpawnReplyPlan.IsMetal("MetalDeposit"));
            Assert.True(SpawnReplyPlan.IsMetal("metaldeposit"));
            Assert.False(SpawnReplyPlan.IsMetal("Egg"));
            Assert.False(SpawnReplyPlan.IsMetal(null));
        }

        [Fact]
        public void Accept_maps_metres_to_fixed_point_like_the_client()
        {
            var items = new[] { Metal(17004.43, -318.66, -1134.16) };
            var got = SpawnReplyPlan.Accept(items, 0, 10, null);
            Assert.Single(got);
            // (long)(m * 4096), truncate toward zero - the client's own encoding.
            Assert.Equal(FixedPointPosition.FromMetres(17004.43, -318.66, -1134.16), got[0].Position);
        }

        [Fact]
        public void Accept_drops_non_metal()
        {
            var items = new[]
            {
                new ResourceReplyItem(1, 1, 1, "Egg", ""),
                Metal(2, 2, 2),
                new ResourceReplyItem(3, 3, 3, null, ""),
            };
            var got = SpawnReplyPlan.Accept(items, 0, 10, null);
            Assert.Single(got);
            Assert.Equal(FixedPointPosition.FromMetres(2, 2, 2), got[0].Position);
        }

        [Fact]
        public void Accept_cycles_missing_variants_from_the_stable_admission_index()
        {
            var got = SpawnReplyPlan.Accept(
                new[]
                {
                    Metal(1, 1, 1, "  "),
                    Metal(2, 2, 2, null),
                    Metal(3, 3, 3, ""),
                },
                alreadySpawned: 1,
                requestedCount: 4,
                existing: null);

            Assert.Equal(
                new[]
                {
                    MetalDeposits.VariantIds[1],
                    MetalDeposits.VariantIds[2],
                    MetalDeposits.VariantIds[0],
                },
                got.Select(deposit => deposit.Variant));
        }

        [Fact]
        public void Accept_keeps_client_variant()
        {
            var got = SpawnReplyPlan.Accept(new[] { Metal(1, 1, 1, "metal_deposit_composite_light_03") }, 0, 10, null);
            Assert.Equal("metal_deposit_composite_light_03", got[0].Variant);
        }

        [Fact]
        public void Accept_dedups_within_a_batch()
        {
            var items = new[] { Metal(5, 5, 5), Metal(5, 5, 5), Metal(6, 6, 6) };
            var got = SpawnReplyPlan.Accept(items, 0, 10, null);
            Assert.Equal(2, got.Count);
        }

        [Fact]
        public void Accept_dedups_against_existing()
        {
            var existing = new HashSet<FixedPointPosition> { FixedPointPosition.FromMetres(5, 5, 5) };
            var got = SpawnReplyPlan.Accept(new[] { Metal(5, 5, 5), Metal(7, 7, 7) }, 1, 10, existing);
            Assert.Single(got);
            Assert.Equal(FixedPointPosition.FromMetres(7, 7, 7), got[0].Position);
        }

        [Fact]
        public void Accept_does_not_mutate_existing()
        {
            var existing = new HashSet<FixedPointPosition>();
            SpawnReplyPlan.Accept(new[] { Metal(7, 7, 7) }, 0, 10, existing);
            Assert.Empty(existing);
        }

        [Fact]
        public void Accept_clamps_to_remaining_budget()
        {
            var items = new List<ResourceReplyItem>();
            for (int i = 0; i < 50; i++)
            {
                items.Add(Metal(i, 0, 0));
            }
            // requested 40, already spawned 38 -> only 2 more admitted.
            var got = SpawnReplyPlan.Accept(items, 38, 40, null);
            Assert.Equal(2, got.Count);
        }

        [Fact]
        public void Accept_nothing_when_budget_exhausted()
        {
            var got = SpawnReplyPlan.Accept(new[] { Metal(1, 1, 1) }, 40, 40, null);
            Assert.Empty(got);
        }

        [Fact]
        public void Accept_honours_the_hard_cap_even_if_requested_is_huge()
        {
            var items = new List<ResourceReplyItem>();
            for (int i = 0; i < IslandResourceHandshake.MaxMetalCount + 50; i++)
            {
                items.Add(Metal(i, 0, 0));
            }
            var got = SpawnReplyPlan.Accept(items, 0, 100000, null);
            Assert.Equal(IslandResourceHandshake.MaxMetalCount, got.Count);
        }

        [Fact]
        public void Accept_null_items_is_empty()
        {
            Assert.Empty(SpawnReplyPlan.Accept(null, 0, 10, null));
        }

        // ------------------------------------------------------------------
        // Evaluate: the coordinate-frame guard and the drop-reason counters.
        // ------------------------------------------------------------------

        private static ResourceReplyItem OnHaven(double lx, double ly, double lz)
        {
            FixedPointPosition p = MetalNodes.IslandLocalToWorldFixed(SpawnPolicy.IslandPosition, lx, ly, lz);
            return Metal(p.MetresX, p.MetresY, p.MetresZ);
        }

        [Fact]
        public void Evaluate_without_bounds_behaves_exactly_like_Accept()
        {
            var items = new[] { Metal(1, 0, 0), Metal(2, 0, 0) };
            Assert.Equal(
                SpawnReplyPlan.Accept(items, 0, 10, null).Count,
                SpawnReplyPlan.Evaluate(items, 0, 10, null, bounds: null).Accepted.Count);
        }

        [Fact]
        public void Evaluate_accepts_real_on_island_placements()
        {
            var items = new[] { OnHaven(216.0, 4.57, 8.0), OnHaven(-100.0, 20.0, 60.0) };
            var got = SpawnReplyPlan.Evaluate(items, 0, 10, null, IslandBounds.Haven());
            Assert.Equal(2, got.Accepted.Count);
            Assert.Equal(0, got.OutOfBounds);
        }

        [Fact]
        public void Evaluate_refuses_an_unremapped_island_local_reply()
        {
            // The live failure mode: OffsetOrigin still zero, so the client replies in
            // island-local metres. Nothing may be spawned from it.
            var items = new[] { Metal(216.0, 4.57, 8.0), Metal(200.0, 4.27, 0.0) };
            var got = SpawnReplyPlan.Evaluate(items, 0, 10, null, IslandBounds.Haven());
            Assert.Empty(got.Accepted);
            Assert.Equal(2, got.OutOfBounds);
            Assert.Equal(0, got.Duplicate);
            Assert.NotNull(got.FirstOutOfBounds);
            Assert.Equal(216.0, got.FirstOutOfBounds!.Value.X);
        }

        [Fact]
        public void Evaluate_refuses_a_scale_error_but_keeps_the_good_ones()
        {
            ResourceReplyItem good = OnHaven(216.0, 4.57, 8.0);
            var items = new[] { good, Metal(good.X * 100.0, good.Y * 100.0, good.Z * 100.0) };
            var got = SpawnReplyPlan.Evaluate(items, 0, 10, null, IslandBounds.Haven());
            Assert.Single(got.Accepted);
            Assert.Equal(1, got.OutOfBounds);
        }

        [Fact]
        public void Evaluate_reports_out_of_bounds_rather_than_duplicate_for_a_repeated_bad_point()
        {
            // Bounds runs BEFORE dedup on purpose: a wall of identical out-of-frame points
            // must read as a coordinate bug, not as harmless duplicates.
            var items = new[] { Metal(0, 0, 0), Metal(0, 0, 0), Metal(0, 0, 0) };
            var got = SpawnReplyPlan.Evaluate(items, 0, 10, null, IslandBounds.Haven());
            Assert.Equal(3, got.OutOfBounds);
            Assert.Equal(0, got.Duplicate);
        }

        [Fact]
        public void Evaluate_counts_non_metal_separately()
        {
            var items = new[]
            {
                new ResourceReplyItem(0, 0, 0, "Egg", ""),
                OnHaven(216.0, 4.57, 8.0),
            };
            var got = SpawnReplyPlan.Evaluate(items, 0, 10, null, IslandBounds.Haven());
            Assert.Single(got.Accepted);
            Assert.Equal(1, got.NonMetal);
            Assert.Equal(0, got.OutOfBounds);
        }

        [Fact]
        public void Evaluate_counts_duplicates_of_a_valid_point()
        {
            ResourceReplyItem good = OnHaven(216.0, 4.57, 8.0);
            var got = SpawnReplyPlan.Evaluate(new[] { good, good }, 0, 10, null, IslandBounds.Haven());
            Assert.Single(got.Accepted);
            Assert.Equal(1, got.Duplicate);
        }

        [Fact]
        public void Evaluate_over_budget_admits_nothing_more()
        {
            var items = new[] { OnHaven(216, 4.57, 8), OnHaven(200, 4.27, 0), OnHaven(184, 7.32, 0) };
            var got = SpawnReplyPlan.Evaluate(items, alreadySpawned: 2, requestedCount: 3, null, IslandBounds.Haven());
            Assert.Single(got.Accepted);
        }
    }
}

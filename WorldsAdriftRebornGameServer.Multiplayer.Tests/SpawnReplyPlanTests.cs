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
        public void Accept_defaults_missing_variant()
        {
            var got = SpawnReplyPlan.Accept(new[] { Metal(1, 1, 1, "  ") }, 0, 10, null);
            Assert.Single(got);
            Assert.Equal(SpawnReplyPlan.DefaultVariant, got[0].Variant);
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
    }
}

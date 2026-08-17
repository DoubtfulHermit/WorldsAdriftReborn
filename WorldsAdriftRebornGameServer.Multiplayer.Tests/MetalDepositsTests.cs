using System;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The anchored metal DEPOSIT policy: the wire prefab name, the VERIFIED
    /// variantId (a real MetalDepositVisuals asset id, since a value that does not
    /// resolve is an invisible dead entity), the depletion sizing, and the pure
    /// health curve the live 1016 broadcast and a late joiner's seed both compute.
    /// Pure - no ENet, no game types - so the numbers are pinned here rather than in
    /// front of a running client (the standing caveat bites hardest on exactly this).
    /// </summary>
    public class MetalDepositsTests
    {
        [Fact]
        public void The_prefab_name_is_the_bare_metal_deposit_entity_the_client_can_resolve()
        {
            // VERIFIED: metal_deposit_entity is line 328 of prefab-names.tsv (client
            // AND worker "yes") and the name strings-scanned out of resources.assets.
            // Bare, because the client appends the worker suffix itself.
            Assert.Equal("metal_deposit_entity", MetalDeposits.AssetName);
            Assert.DoesNotContain("_unity", MetalDeposits.AssetName);
        }

        [Fact]
        public void The_three_shipped_deposit_variants_are_pinned_in_stable_order()
        {
            // VERIFIED by a strings scan of sharedassets0.assets: the
            // MetalDepositsByBiome table lists metal_deposit_composite_light_01/_02/_03
            // under every biome. A variantId that does not resolve leaves the
            // visualiser disabled (invisible entity), so this string is load-bearing.
            Assert.Equal(
                new[]
                {
                    "metal_deposit_composite_light_01",
                    "metal_deposit_composite_light_02",
                    "metal_deposit_composite_light_03",
                },
                MetalDeposits.VariantIds);
            Assert.Equal(MetalDeposits.DefaultVariantId, MetalDeposits.VariantIds[0]);
        }

        [Fact]
        public void Placement_indices_cycle_deterministically_through_all_three_shapes()
        {
            Assert.Equal(MetalDeposits.VariantIds[0], MetalDeposits.VariantIdFor(0, null));
            Assert.Equal(MetalDeposits.VariantIds[1], MetalDeposits.VariantIdFor(1, null));
            Assert.Equal(MetalDeposits.VariantIds[2], MetalDeposits.VariantIdFor(2, null));
            Assert.Equal(MetalDeposits.VariantIds[0], MetalDeposits.VariantIdFor(3, null));
            Assert.Equal(MetalDeposits.VariantIds[2], MetalDeposits.VariantIdFor(11, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => MetalDeposits.VariantIdFor(-1, null));
        }

        [Fact]
        public void The_variant_is_globally_overridable_for_live_iteration_without_a_rebuild()
        {
            Assert.Equal("metal_deposit_composite_light_03",
                MetalDeposits.VariantIdFor(0, "metal_deposit_composite_light_03"));
            Assert.Equal("metal_deposit_composite_light_03",
                MetalDeposits.VariantIdFor(19, " metal_deposit_composite_light_03 "));
            Assert.Equal(MetalDeposits.VariantIds[1], MetalDeposits.VariantIdFor(1, "  "));
            Assert.Equal(MetalDeposits.VariantIds[2], MetalDeposits.VariantIdFor(2, null));
        }

        [Fact]
        public void The_sizing_makes_the_core_empty_in_exactly_the_stated_shots()
        {
            // 200 damage x 10 shots == 2000 maxHealth, the measured ~7.5 s of beam.
            Assert.Equal(MetalDeposits.MaxHealth,
                MetalDeposits.SalvageShootDamage * MetalDeposits.ShotsToDeplete);
        }

        [Fact]
        public void Health_is_a_clamped_pure_function_of_the_shot_count()
        {
            Assert.Equal(2000, MetalDeposits.HealthAfter(0));
            Assert.Equal(1800, MetalDeposits.HealthAfter(1));
            Assert.Equal(200, MetalDeposits.HealthAfter(9));
            Assert.Equal(0, MetalDeposits.HealthAfter(10));
            // Never negative, even if the counter is over-driven, and never above max.
            Assert.Equal(0, MetalDeposits.HealthAfter(50));
            Assert.Equal(2000, MetalDeposits.HealthAfter(-3));
        }

        [Fact]
        public void A_placed_deposit_is_a_deposit_node_that_carries_the_variant()
        {
            MetalNode node = MetalDeposits.NodeAt(0);
            Assert.True(node.IsDeposit);
            Assert.Equal(MetalDeposits.VariantIdFor(0), node.VariantId);
            Assert.Equal(MetalDeposits.KeyFor(0), node.Key);
        }

        [Fact]
        public void Placed_Haven_deposits_carry_their_stable_shape_selection()
        {
            for (int i = 0; i < MetalDeposits.HavenPlacements.Count; i++)
                Assert.Equal(MetalDeposits.VariantIdFor(i), MetalDeposits.NodeAt(i).VariantId);
        }

        [Fact]
        public void A_nugget_is_not_a_deposit_and_carries_no_variant()
        {
            // The default MetalNode ctor path (the nugget) must be untouched.
            MetalNode nugget = new MetalNode("metal-0", "iron", 5,
                new FixedPointPosition(0, 0, 0));
            Assert.False(nugget.IsDeposit);
            Assert.Null(nugget.VariantId);
        }

        [Fact]
        public void ByKey_round_trips_deposit_keys_and_rejects_others()
        {
            Assert.True(MetalDeposits.IsDepositKey("deposit-0"));
            Assert.False(MetalDeposits.IsDepositKey("metal-0"));
            Assert.False(MetalDeposits.IsDepositKey(null));

            Assert.NotNull(MetalDeposits.ByKey("deposit-0"));
            Assert.Equal("deposit-0", MetalDeposits.ByKey("deposit-0")!.Key);
            Assert.Null(MetalDeposits.ByKey("metal-0"));
            Assert.Null(MetalDeposits.ByKey("deposit-999"));
            Assert.Null(MetalDeposits.ByKey("deposit-x"));
        }

        [Fact]
        public void The_proven_deposit_is_index_zero_and_shared_with_the_nugget_proven_vertex()
        {
            // Index 0 is the same measured LOD0 surface vertex (216, 4.57, 8) the
            // nugget's proven node uses - 8.9 m from spawn, so a tester walks up and
            // aims. Its world position is the island origin plus that local offset.
            MetalNode proven = MetalDeposits.NodeAt(0);
            FixedPointPosition expected = MetalNodes.IslandLocalToWorldFixed(
                MetalDeposits.IslandOrigin, 216.0, 4.57, 8.0);
            Assert.Equal(expected, proven.Position);
        }

        [Fact]
        public void Haven_clamps_the_count_and_always_keeps_the_proven_deposit()
        {
            Assert.Empty(MetalDeposits.Haven(0));
            Assert.Single(MetalDeposits.Haven(1));
            Assert.Equal(MetalDeposits.KeyFor(0), MetalDeposits.Haven(1)[0].Key);

            // Over-large is clamped to the full table, never throws.
            Assert.Equal(MetalDeposits.HavenPlacements.Count, MetalDeposits.Haven(999).Count);
        }
    }
}

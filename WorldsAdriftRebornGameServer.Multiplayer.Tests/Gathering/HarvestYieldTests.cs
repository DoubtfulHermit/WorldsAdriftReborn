using System;
using WorldsAdriftRebornGameServer.Multiplayer.Gathering;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Gathering
{
    public class HarvestYieldTests
    {
        [Fact]
        public void An_unregistered_source_resolves_to_nothing()
        {
            HarvestYield yields = new();

            Assert.Null(yields.Resolve("iron", 1));
            Assert.False(yields.Has("iron"));
        }

        [Fact]
        public void A_registered_source_yields_amount_per_unit_times_units()
        {
            HarvestYield yields = new();
            yields.Register("iron", new YieldRule("iron", amountPerUnit: 12));

            YieldGrant? grant = yields.Resolve("iron", units: 1);

            Assert.NotNull(grant);
            Assert.Equal("iron", grant!.Value.ItemTypeId);
            Assert.Equal(12, grant.Value.Amount);
            Assert.Equal(0, grant.Value.Quality);
        }

        [Fact]
        public void Units_scale_the_amount()
        {
            HarvestYield yields = new();
            yields.Register("birch", new YieldRule("birch", amountPerUnit: 1));

            // A cut that fells three sections is three wood.
            Assert.Equal(3, yields.Resolve("birch", units: 3)!.Value.Amount);
        }

        [Fact]
        public void The_granted_item_type_need_not_equal_the_source_key()
        {
            // A metal node kind that drops a differently-named metal item.
            HarvestYield yields = new();
            yields.Register("MetalNugget", new YieldRule("iron", amountPerUnit: 8, quality: 5));

            YieldGrant grant = yields.Resolve("MetalNugget", 1)!.Value;

            Assert.Equal("iron", grant.ItemTypeId);
            Assert.Equal(8, grant.Amount);
            Assert.Equal(5, grant.Quality);
        }

        [Fact]
        public void Zero_or_negative_units_yield_nothing()
        {
            HarvestYield yields = new();
            yields.Register("iron", new YieldRule("iron", 12));

            // A hit that felled nothing is not a yield, and a zero-count "Salvaged
            // Iron x0" toast is worse than silence.
            Assert.Null(yields.Resolve("iron", 0));
            Assert.Null(yields.Resolve("iron", -4));
        }

        [Fact]
        public void Register_reports_new_versus_replaced()
        {
            HarvestYield yields = new();

            Assert.True(yields.Register("iron", new YieldRule("iron", 12)));
            Assert.False(yields.Register("iron", new YieldRule("iron", 20)));

            // The replacement wins.
            Assert.Equal(20, yields.Resolve("iron", 1)!.Value.Amount);
            Assert.Equal(1, yields.Count);
        }

        [Fact]
        public void A_null_or_empty_source_key_is_safe_to_query()
        {
            HarvestYield yields = new();

            Assert.False(yields.Has(null!));
            Assert.Null(yields.RuleFor(null!));
            Assert.Null(yields.Resolve(null!, 1));
        }

        [Fact]
        public void A_rule_that_yields_less_than_one_per_unit_is_rejected_at_construction()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new YieldRule("iron", amountPerUnit: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new YieldRule("iron", amountPerUnit: -1));
        }

        [Fact]
        public void A_rule_with_no_item_type_is_rejected_at_construction()
        {
            Assert.Throws<ArgumentException>(() => new YieldRule("", 1));
            Assert.Throws<ArgumentException>(() => new YieldRule(null!, 1));
        }
    }
}

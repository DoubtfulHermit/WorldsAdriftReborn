using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Gathering;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Gathering
{
    /// <summary>
    /// THE REGRESSION THESE EXIST FOR: every metal a player mined on this server
    /// arrived at quality 0. The node knew its quality, the island catalogue knew
    /// its quality, and the one hop from the node to the granted item threw it
    /// away - because the metal name and the quality were two separate arguments
    /// and only the name was obviously needed.
    ///
    /// Quality 0 is not "the lowest quality". Retail's scale is 1..10 and it is a
    /// FLOOR in a crafting slot, so 0 satisfies nothing that asks for anything.
    /// The bug therefore did not look like a wrong number; it looked like recipes
    /// being uncraftable.
    /// </summary>
    public class NodeYieldTests
    {
        private static MetalNode Node(string metal, int quality) =>
            new MetalNode("deposit-test", metal, quality, new FixedPointPosition(0, 0, 0));

        [Fact]
        public void A_nodes_rule_carries_the_nodes_quality()
        {
            YieldRule rule = NodeYield.RuleFor(Node("nickel", 7));

            Assert.Equal("nickel", rule.ItemTypeId);
            Assert.Equal(1, rule.AmountPerUnit);
            Assert.Equal(7, rule.Quality);
        }

        [Fact]
        public void The_source_key_is_the_metal_name()
        {
            Assert.Equal("orthite", NodeYield.SourceKeyFor(Node("orthite", 9)));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        public void Every_quality_on_retails_scale_is_carried_verbatim(int quality)
        {
            Assert.Equal(quality, NodeYield.QualityOf(Node("iron", quality)));
        }

        [Theory]
        [InlineData(0, YieldRule.MinQuality)]
        [InlineData(-3, YieldRule.MinQuality)]
        [InlineData(11, YieldRule.MaxQuality)]
        [InlineData(99, YieldRule.MaxQuality)]
        public void A_node_outside_the_scale_is_clamped_rather_than_thrown_on(int declared, int expected)
        {
            // A node's quality comes from a community survey and a tier generator,
            // not from code. One bad row must cost one node's quality, not the boot.
            Assert.Equal(expected, NodeYield.QualityOf(Node("iron", declared)));
            Assert.Equal(expected, NodeYield.RuleFor(Node("iron", declared)).Quality);
        }

        [Fact]
        public void A_hit_on_a_node_grants_that_nodes_quality_even_when_another_node_of_the_same_metal_registered_last()
        {
            // THE STRUCTURAL BUG, stated as a test. The yield table is keyed by the
            // METAL NAME, but quality belongs to the NODE. Shattered Mausoleum alone
            // carries eleven metals at seven different qualities, so name-keying means
            // the last iron node to register decides what every iron node in the world
            // pays out. The per-hit override is what makes two iron nodes distinguishable.
            HarvestYield yields = new();
            MetalNode rich = Node("iron", 8);
            MetalNode poor = Node("iron", 2);

            yields.Register(NodeYield.SourceKeyFor(rich), NodeYield.RuleFor(rich));
            yields.Register(NodeYield.SourceKeyFor(poor), NodeYield.RuleFor(poor));

            // The table now holds ONE iron rule - the poor node's, registered last.
            Assert.Equal(2, yields.RuleFor("iron")!.Quality);

            // Yet mining the rich node still pays quality 8, because the hit carries
            // the node's own quality rather than looking it up by name.
            Assert.Equal(8, yields.Resolve(NodeYield.SourceKeyFor(rich), 1, NodeYield.QualityOf(rich))!.Value.Quality);
            Assert.Equal(2, yields.Resolve(NodeYield.SourceKeyFor(poor), 1, NodeYield.QualityOf(poor))!.Value.Quality);
        }

        [Fact]
        public void Registering_through_NodeYield_alone_still_beats_the_old_quality_zero()
        {
            // BELT AND BRACES. If the per-hit override is ever lost again, the
            // registered rule must still carry a real quality, so the failure degrades
            // to "the wrong node's quality" instead of all the way back to the
            // out-of-range 0 that made crafting slots unsatisfiable.
            HarvestYield yields = new();
            MetalNode node = Node("titanium", 6);
            yields.Register(NodeYield.SourceKeyFor(node), NodeYield.RuleFor(node));

            YieldGrant grant = yields.Resolve("titanium", 1)!.Value;

            Assert.Equal(6, grant.Quality);
            Assert.NotEqual(0, grant.Quality);
        }

        [Fact]
        public void Every_deposit_the_release_world_ships_produces_a_rule_on_retails_scale()
        {
            // THE LIVE DATA, not a fixture. 1930 deposits are stamped from the
            // per-island metal tables in release-runtime-catalog.json; this asserts
            // that not one of them produces an out-of-range rule, which is what would
            // throw at boot now that YieldRule validates its range.
            IReadOnlyList<MetalNode> deposits = ReleaseWorldCatalog.All
                .SelectMany(island => island.Deposits)
                .ToList();

            Assert.NotEmpty(deposits);

            foreach (MetalNode deposit in deposits)
            {
                YieldRule rule = NodeYield.RuleFor(deposit);
                Assert.InRange(rule.Quality, YieldRule.MinQuality, YieldRule.MaxQuality);
                Assert.False(string.IsNullOrWhiteSpace(rule.ItemTypeId));
            }
        }

        [Fact]
        public void The_release_world_really_does_vary_its_metals_and_qualities()
        {
            // The maintainer's question, answered by the shipped data rather than by
            // assertion: a deposit is a generic rock whose METAL is per-node data.
            // If this ever collapses to one metal or one quality, the per-island table
            // has stopped reaching the nodes and Phase 2 has regressed.
            IReadOnlyList<MetalNode> deposits = ReleaseWorldCatalog.All
                .SelectMany(island => island.Deposits)
                .ToList();

            Assert.True(deposits.Select(d => d.MetalType).Distinct().Count() > 5,
                "release-world deposits should span many metals, not one");
            Assert.True(deposits.Select(d => NodeYield.QualityOf(d)).Distinct().Count() > 5,
                "release-world deposits should span many qualities, not one");
        }

        [Fact]
        public void A_null_node_is_a_loud_throw_rather_than_a_silent_zero()
        {
            Assert.Throws<ArgumentNullException>(() => NodeYield.SourceKeyFor(null!));
            Assert.Throws<ArgumentNullException>(() => NodeYield.QualityOf(null!));
        }
    }
}

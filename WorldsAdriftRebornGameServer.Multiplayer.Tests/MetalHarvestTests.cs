using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The counting half of mining: a salvage shot is a discrete pulse (the client
    /// rate-limits itself to one 2106 <c>ShotEvent</c> per deploy), so unlike the
    /// tree there is no clock here - only "how many shots empty a node, and what is
    /// that worth". These pin the two properties the glue leans on: the deplete
    /// transition fires exactly once, and everything else is a safe no-op.
    /// </summary>
    public class MetalHarvestTests
    {
        private const long Node = 900;
        private const long OtherNode = 901;
        private const long NotANode = 42;

        // ------------------------------------------------------------------
        // Placing
        // ------------------------------------------------------------------

        [Fact]
        public void A_placed_node_starts_intact()
        {
            MetalHarvest harvest = new MetalHarvest();
            Assert.True(harvest.Place(Node, unitsYield: 5));

            Assert.True(harvest.IsNode(Node));
            Assert.False(harvest.IsDepleted(Node));
            Assert.Equal(0, harvest.HitsOn(Node));
        }

        [Fact]
        public void An_unplaced_id_is_not_a_node()
        {
            MetalHarvest harvest = new MetalHarvest();
            Assert.False(harvest.IsNode(NotANode));
            Assert.False(harvest.IsDepleted(NotANode));
            Assert.Null(harvest.ShotsRemaining(NotANode));
        }

        [Fact]
        public void Placing_the_same_node_twice_is_idempotent_and_keeps_progress()
        {
            // The second joiner walking the identical spawn plan must not refill a
            // node someone has already been chipping at.
            MetalHarvest harvest = new MetalHarvest(defaultShotsToDeplete: 3);
            Assert.True(harvest.Place(Node, unitsYield: 5));

            harvest.Hit(Node); // one shot landed

            Assert.False(harvest.Place(Node, unitsYield: 999, shotsToDeplete: 1));
            Assert.Equal(1, harvest.HitsOn(Node));           // not reset
            Assert.Equal(2, harvest.ShotsRemaining(Node));   // still on the original 3-shot rule
        }

        [Fact]
        public void Place_rejects_a_non_positive_yield()
        {
            MetalHarvest harvest = new MetalHarvest();
            Assert.Throws<ArgumentOutOfRangeException>(() => harvest.Place(Node, unitsYield: 0));
        }

        [Fact]
        public void Place_rejects_a_zero_shot_node()
        {
            MetalHarvest harvest = new MetalHarvest();
            Assert.Throws<ArgumentOutOfRangeException>(() => harvest.Place(Node, unitsYield: 5, shotsToDeplete: 0));
        }

        [Fact]
        public void The_constructor_rejects_a_zero_shot_default()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MetalHarvest(defaultShotsToDeplete: 0));
        }

        // ------------------------------------------------------------------
        // Hitting
        // ------------------------------------------------------------------

        [Fact]
        public void Early_shots_wear_the_node_without_yielding()
        {
            MetalHarvest harvest = new MetalHarvest(defaultShotsToDeplete: 3);
            harvest.Place(Node, unitsYield: 5);

            MetalHitOutcome first = harvest.Hit(Node);
            Assert.False(first.Depleted);
            Assert.Equal(0, first.Units);
            Assert.Equal(1, harvest.HitsOn(Node));

            MetalHitOutcome second = harvest.Hit(Node);
            Assert.False(second.Depleted);
            Assert.Equal(0, second.Units);
        }

        [Fact]
        public void The_threshold_shot_depletes_and_yields_exactly_once()
        {
            MetalHarvest harvest = new MetalHarvest(defaultShotsToDeplete: 3);
            harvest.Place(Node, unitsYield: 5);

            Assert.Equal(MetalHitOutcome.Nothing, harvest.Hit(Node));
            Assert.Equal(MetalHitOutcome.Nothing, harvest.Hit(Node));

            MetalHitOutcome third = harvest.Hit(Node);
            Assert.True(third.Depleted);
            Assert.Equal(5, third.Units);
            Assert.True(harvest.IsDepleted(Node));
            Assert.Null(harvest.ShotsRemaining(Node));
        }

        [Fact]
        public void A_shot_after_depletion_is_a_no_op_no_double_grant()
        {
            // The most important property: the glue grants and marks the registry
            // destroyed on the deplete transition, so a fourth shot must NOT pay out
            // again (a held beam keeps publishing ShotEvents until the node teleports
            // away and the raycast misses).
            MetalHarvest harvest = new MetalHarvest(defaultShotsToDeplete: 2);
            harvest.Place(Node, unitsYield: 7);

            harvest.Hit(Node);
            MetalHitOutcome deplete = harvest.Hit(Node);
            Assert.True(deplete.Depleted);

            MetalHitOutcome after = harvest.Hit(Node);
            Assert.False(after.Depleted);
            Assert.Equal(0, after.Units);
        }

        [Fact]
        public void A_one_shot_node_depletes_on_the_first_hit()
        {
            MetalHarvest harvest = new MetalHarvest();
            harvest.Place(Node, unitsYield: 4, shotsToDeplete: 1);

            MetalHitOutcome outcome = harvest.Hit(Node);
            Assert.True(outcome.Depleted);
            Assert.Equal(4, outcome.Units);
        }

        [Fact]
        public void A_shot_at_a_non_node_yields_nothing()
        {
            MetalHarvest harvest = new MetalHarvest();
            harvest.Place(Node, unitsYield: 5);

            Assert.Equal(MetalHitOutcome.Nothing, harvest.Hit(NotANode));
        }

        [Fact]
        public void Nodes_deplete_independently()
        {
            MetalHarvest harvest = new MetalHarvest(defaultShotsToDeplete: 1);
            harvest.Place(Node, unitsYield: 5);
            harvest.Place(OtherNode, unitsYield: 8);

            Assert.True(harvest.Hit(Node).Depleted);
            Assert.False(harvest.IsDepleted(OtherNode));
            Assert.True(harvest.Hit(OtherNode).Depleted);
        }

        [Fact]
        public void Count_reflects_placed_nodes()
        {
            MetalHarvest harvest = new MetalHarvest();
            Assert.Equal(0, harvest.Count);
            harvest.Place(Node, unitsYield: 5);
            harvest.Place(OtherNode, unitsYield: 5);
            Assert.Equal(2, harvest.Count);
        }
    }
}

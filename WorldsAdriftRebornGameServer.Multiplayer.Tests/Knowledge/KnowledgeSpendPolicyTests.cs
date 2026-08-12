using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Knowledge
{
    /// <summary>
    /// The SPEND half of the knowledge loop: clicking a node pays its cost and learns
    /// its schematic, but only when affordable, unlocked and not already bought. The
    /// server re-checks everything the client claims. Pure - small in-memory trees.
    /// </summary>
    public class KnowledgeSpendPolicyTests
    {
        // A tiny tree modelled on the real one: "Shipbuilding" is a cheap root
        // (cost 20), "Makeshift Bandages" hangs off it (cost 60), "Glider" is the
        // craftable-today node that aliases to the "glider" catalogue recipe.
        private static Dictionary<string, KnowledgeNodeInfo> Tree()
        {
            return new Dictionary<string, KnowledgeNodeInfo>
            {
                ["Shipbuilding"] = new KnowledgeNodeInfo(
                    "Shipbuilding", 20, new[] { "RevivalChamberInterface" }, "SCHEMATIC_LIST", -1, isRoot: true),
                ["Makeshift Bandages"] = new KnowledgeNodeInfo(
                    "Makeshift Bandages", 60, new[] { "Shipbuilding" }, "SCHEMATIC_LIST", -1, isRoot: true),
                ["Fuel Gauge"] = new KnowledgeNodeInfo(
                    "Fuel Gauge", 120, new[] { "Makeshift Bandages" }, "SCHEMATIC_LIST", -1, isRoot: false),
                ["Glider"] = new KnowledgeNodeInfo(
                    "Glider", 240, new[] { "Fuel Gauge" }, "SCHEMATIC_LIST", -1, isRoot: false),
                ["EnginesSlot1"] = new KnowledgeNodeInfo(
                    "EnginesSlot1", 1, new[] { "Shipbuilding" }, "SLOT", -1, isRoot: false),
            };
        }

        private static Dictionary<string, int> Uses(params string[] purchased)
        {
            var m = new Dictionary<string, int>();
            foreach (string p in purchased) m[p] = 1;
            return m;
        }

        [Fact]
        public void Buying_the_cheap_root_deducts_the_cost_and_learns_the_shipyard_recipe()
        {
            // MILESTONE: the cheapest root (Shipbuilding, cost 20) aliases to the
            // recovered "shipyard" catalogue recipe - the tree has no literal
            // "Shipyard" node, so this root is how a player earns it.
            NodeSpend s = KnowledgeSpendPolicy.Evaluate(Tree(), knowledge: 51, Uses(), "Shipbuilding");

            Assert.Equal(NodeSpendResponse.Success, s.Response);
            Assert.Equal(31, s.NewKnowledge);      // 51 - 20
            Assert.Equal(1, s.NewNodeUseCount);
            Assert.Equal("shipyard", s.LearnedSchematicId);
        }

        [Fact]
        public void The_glider_node_learns_the_lowercase_catalogue_recipe()
        {
            // Prereqs purchased so the only thing under test is the alias.
            NodeSpend s = KnowledgeSpendPolicy.Evaluate(
                Tree(), knowledge: 1000, Uses("Shipbuilding", "Makeshift Bandages", "Fuel Gauge"), "Glider");

            Assert.Equal(NodeSpendResponse.Success, s.Response);
            Assert.Equal("glider", s.LearnedSchematicId);   // "Glider" node -> "glider" recipe
        }

        [Fact]
        public void An_unaffordable_node_is_rejected_and_nothing_is_spent()
        {
            NodeSpend s = KnowledgeSpendPolicy.Evaluate(Tree(), knowledge: 10, Uses(), "Shipbuilding");

            Assert.Equal(NodeSpendResponse.NotEnoughKnowledge, s.Response);
            Assert.Equal(10, s.NewKnowledge);
            Assert.Null(s.LearnedSchematicId);
        }

        [Fact]
        public void A_node_whose_parent_is_not_purchased_is_locked()
        {
            // Fuel Gauge needs Makeshift Bandages, which needs Shipbuilding.
            NodeSpend s = KnowledgeSpendPolicy.Evaluate(Tree(), knowledge: 1000, Uses("Shipbuilding"), "Fuel Gauge");

            Assert.Equal(NodeSpendResponse.NodeLocked, s.Response);
            Assert.Equal(1000, s.NewKnowledge);
        }

        [Fact]
        public void A_node_becomes_purchasable_once_its_parent_is_bought()
        {
            NodeSpend s = KnowledgeSpendPolicy.Evaluate(
                Tree(), knowledge: 1000, Uses("Shipbuilding", "Makeshift Bandages"), "Fuel Gauge");

            Assert.Equal(NodeSpendResponse.Success, s.Response);
            Assert.Equal(880, s.NewKnowledge);     // 1000 - 120
        }

        [Fact]
        public void A_schematic_node_cannot_be_bought_twice()
        {
            NodeSpend s = KnowledgeSpendPolicy.Evaluate(
                Tree(), knowledge: 1000, Uses("Shipbuilding"), "Shipbuilding");

            Assert.Equal(NodeSpendResponse.PastMaxUses, s.Response);
        }

        [Fact]
        public void An_unknown_node_is_rejected()
        {
            NodeSpend s = KnowledgeSpendPolicy.Evaluate(Tree(), knowledge: 1000, Uses(), "NoSuchNode");
            Assert.Equal(NodeSpendResponse.InexistentNode, s.Response);
        }

        [Fact]
        public void A_slot_node_purchases_but_learns_no_schematic()
        {
            NodeSpend s = KnowledgeSpendPolicy.Evaluate(Tree(), knowledge: 100, Uses("Shipbuilding"), "EnginesSlot1");

            Assert.Equal(NodeSpendResponse.Success, s.Response);
            Assert.Equal(99, s.NewKnowledge);
            Assert.Null(s.LearnedSchematicId);     // SLOT nodes raise caps, not the book
        }

        [Fact]
        public void The_cheapest_root_is_reachable_from_a_single_databank_scan()
        {
            // One 50-point scan from knowledge 1 -> 51, enough for Shipbuilding (20).
            int afterScan = 1 + (int)Databanks.GrantAmount;
            NodeSpend s = KnowledgeSpendPolicy.Evaluate(Tree(), afterScan, Uses(), "Shipbuilding");
            Assert.Equal(NodeSpendResponse.Success, s.Response);
        }
    }
}

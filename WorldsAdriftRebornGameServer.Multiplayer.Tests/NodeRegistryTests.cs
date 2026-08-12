using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The two rules of node state that are counter-intuitive enough to have their
    /// own findings doc (findings-node-relay.md), pinned natively so a running game
    /// is never the only thing that can tell them going missing:
    ///
    ///   1. a destroyed node STAYS in the registry, so a late joiner sees the stump;
    ///   2. a shot is APPENDED to shotPoints, never replacing them.
    /// </summary>
    public class NodeRegistryTests
    {
        private static MetalNode Node(string key = "metal-0") =>
            new MetalNode(key, "iron", 6, new FixedPointPosition(70534881, -1286551, -4612781));

        [Fact]
        public void Register_is_idempotent_so_the_second_joiner_does_not_reset_state()
        {
            // Every joining client walks the identical spawn plan and reaches the
            // registration step for the same node, but there is one node. The second
            // call must be a no-op that does NOT stand a depleted node back up.
            NodeRegistry registry = new NodeRegistry();

            Assert.True(registry.Register(1, Node()));
            registry.MarkDestroyed(1);

            Assert.False(registry.Register(1, Node()), "re-registration must not succeed");
            Assert.True(registry.IsDestroyed(1), "re-registration must not reset the destroyed flag");
        }

        [Fact]
        public void A_destroyed_node_stays_in_the_registry_so_late_joiners_see_the_stump()
        {
            // THE most counter-intuitive rule: there is no RemoveEntityOp, so a
            // depleted node is kept and replayed to a joiner in its destroyed state.
            // Drop it and late joiners see intact rocks everyone else has mined.
            NodeRegistry registry = new NodeRegistry();
            registry.Register(7, Node());

            Assert.True(registry.MarkDestroyed(7));

            Assert.True(registry.IsNode(7), "the node must stay in the registry after depletion");
            Assert.True(registry.IsDestroyed(7));
            Assert.False(registry.ShouldSpawnIntact(7), "a destroyed node is not spawned intact");

            NodeSnapshot? snap = registry.Snapshot(7);
            Assert.NotNull(snap);
            Assert.True(snap!.IsDestroyed);
        }

        [Fact]
        public void An_intact_node_is_spawned_intact()
        {
            NodeRegistry registry = new NodeRegistry();
            registry.Register(3, Node());

            Assert.True(registry.ShouldSpawnIntact(3));
            Assert.False(registry.IsDestroyed(3));
            Assert.False(registry.Snapshot(3)!.IsDestroyed);
        }

        [Fact]
        public void MarkDestroyed_reports_only_the_transition_and_is_idempotent()
        {
            NodeRegistry registry = new NodeRegistry();
            registry.Register(1, Node());

            Assert.True(registry.MarkDestroyed(1), "first depletion is the transition");
            Assert.False(registry.MarkDestroyed(1), "already destroyed - no transition");
        }

        [Fact]
        public void A_shot_is_appended_to_shotPoints_not_replacing_them()
        {
            // shotPoints is STATE that grows, not a value that is overwritten.
            NodeRegistry registry = new NodeRegistry();
            registry.Register(5, Node());

            registry.AddShotPoint(5, new ShotPoint(0.1f, 0.2f, 0.3f));
            registry.AddShotPoint(5, new ShotPoint(0.4f, 0.5f, 0.6f));

            IReadOnlyList<ShotPoint> points = registry.ShotPointsOf(5);
            Assert.Equal(2, points.Count);
            Assert.Equal(0.1f, points[0].X);
            Assert.Equal(0.4f, points[1].X);
        }

        [Fact]
        public void ShotPoints_are_capped_by_dropping_the_oldest()
        {
            // Replicated in full every update and replayed linearly on every join, so
            // it cannot grow without bound; a full crust is the same hole either way.
            NodeRegistry registry = new NodeRegistry();
            registry.Register(9, Node());

            for (int i = 0; i < NodeRegistry.MaxShotPoints + 5; i++)
            {
                registry.AddShotPoint(9, new ShotPoint(i, 0f, 0f));
            }

            IReadOnlyList<ShotPoint> points = registry.ShotPointsOf(9);
            Assert.Equal(NodeRegistry.MaxShotPoints, points.Count);
            // The oldest five were dropped, so the first surviving point is #5.
            Assert.Equal(5f, points[0].X);
        }

        [Fact]
        public void A_shot_on_a_destroyed_node_is_ignored()
        {
            NodeRegistry registry = new NodeRegistry();
            registry.Register(2, Node());
            registry.MarkDestroyed(2);

            Assert.False(registry.AddShotPoint(2, new ShotPoint(1f, 1f, 1f)));
            Assert.Empty(registry.ShotPointsOf(2));
        }

        [Fact]
        public void A_non_node_id_looks_like_a_player_and_touches_nothing()
        {
            // Anything not registered is a player avatar or an id before its
            // AddEntityOp - every query must be false/null/empty, never throw.
            NodeRegistry registry = new NodeRegistry();

            Assert.False(registry.IsNode(42));
            Assert.Null(registry.NodeOf(42));
            Assert.False(registry.IsDestroyed(42));
            Assert.False(registry.MarkDestroyed(42));
            Assert.False(registry.ShouldSpawnIntact(42));
            Assert.False(registry.AddShotPoint(42, new ShotPoint(0f, 0f, 0f)));
            Assert.Empty(registry.ShotPointsOf(42));
            Assert.Null(registry.Snapshot(42));
        }

        [Fact]
        public void NodeOf_returns_the_facts_the_serializer_needs()
        {
            NodeRegistry registry = new NodeRegistry();
            MetalNode node = Node("metal-4");
            registry.Register(11, node);

            Assert.True(registry.IsNode(11));
            Assert.Equal("metal-4", registry.NodeOf(11)!.Key);
            Assert.Equal("iron", registry.NodeOf(11)!.MetalType);
            Assert.Contains(11L, registry.EntityIds);
        }
    }
}

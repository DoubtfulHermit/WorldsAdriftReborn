using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The metal node as a thing in the world: what it is called on the wire, and
    /// that its positions are the island's own plus a measured surface vertex - in
    /// the SAME arithmetic the tree and the player's spawn use, so the numbers check
    /// each other rather than each being taken on trust. Two of this project's most
    /// expensive bugs were coordinates that looked plausible in a source file and
    /// were only wrong in a running game.
    /// </summary>
    public class MetalNodesTests
    {
        [Fact]
        public void The_prefab_name_is_the_bare_MetalNugget_the_client_can_resolve()
        {
            // VERIFIED: MetalNugget is line 163 of prefab-names.tsv, client AND
            // worker columns both "yes". Bare, because the client appends the worker
            // suffix itself - "MetalNugget_unityclient" would be suffixed twice.
            Assert.Equal("MetalNugget", MetalNodes.AssetName);
            Assert.DoesNotContain("_unity", MetalNodes.AssetName);
        }

        [Fact]
        public void The_proven_node_is_the_island_plus_a_measured_island_local_surface_vertex()
        {
            // THE DERIVATION, pinned so the literal placement cannot drift from it:
            //   island (69650145, -1305269, -4645549)
            // + local  (216.00, 4.57, 8.00) m, x4096, truncated toward zero
            // (216, 4.57, 8) is a MEASURED LOD0 surface vertex from
            // island-surfaces/1431299145.json, ny = 0.995, in the walkable ground
            // band - not the spawn point with an offset added.
            FixedPointPosition island = SpawnPolicy.IslandPosition;
            MetalNode proven = MetalNodes.Haven(onlyProven: true)[0];

            Assert.Equal((long)(216.00 * 4096), proven.Position.X - island.X);
            Assert.Equal((long)(4.57 * 4096), proven.Position.Y - island.Y);
            Assert.Equal((long)(8.00 * 4096), proven.Position.Z - island.Z);
        }

        [Fact]
        public void The_proven_node_is_within_walking_distance_of_the_spawn()
        {
            // Close enough to walk to and well inside the aimer's 40 m raycast, far
            // enough not to be inside the player or the tree.
            FixedPointPosition player = SpawnPolicy.PlayerSpawnPosition;
            MetalNode proven = MetalNodes.Haven(onlyProven: true)[0];

            double dx = proven.Position.MetresX - player.MetresX;
            double dy = proven.Position.MetresY - player.MetresY;
            double dz = proven.Position.MetresZ - player.MetresZ;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            Assert.InRange(distance, 3.0, 12.0);
        }

        [Fact]
        public void OnlyProven_returns_exactly_one_node_the_full_set_returns_all_placements()
        {
            // The cautious first-live-test mode the standing caveat calls for: the
            // coordinate chain has never been run, so one node before the whole table.
            Assert.Single(MetalNodes.Haven(onlyProven: true));
            Assert.Equal(MetalNodes.HavenPlacements.Count, MetalNodes.Haven().Count);
            Assert.Equal(MetalNodes.Haven(onlyProven: true)[0].Position, MetalNodes.Haven()[0].Position);
        }

        [Fact]
        public void Every_node_has_a_distinct_key_and_a_distinct_position()
        {
            // Distinct keys because the key is the shared-entity-id key: two nodes
            // under one key would share an entity id, and the second AddEntityOp
            // would silently re-use the first's id. Distinct positions because the
            // whole point of per-entity seeding is N nodes at N places.
            IReadOnlyList<MetalNode> nodes = MetalNodes.Haven();

            HashSet<string> keys = new HashSet<string>();
            HashSet<FixedPointPosition> positions = new HashSet<FixedPointPosition>();
            foreach (MetalNode n in nodes)
            {
                Assert.True(keys.Add(n.Key), "duplicate key " + n.Key);
                Assert.True(positions.Add(n.Position), "duplicate position " + n.Position);
            }
        }

        [Fact]
        public void Every_node_sits_in_the_reachable_ground_band_not_on_the_camp_platforms()
        {
            // The flattest vertices on Haven sit on the metal camp's elevated
            // platforms (island-local y ~ 40-57 m), which a player cannot walk to.
            // Every placement is constrained to island-local y in [1, 12] m, so a
            // player can actually reach it. Checked against the island origin.
            FixedPointPosition island = SpawnPolicy.IslandPosition;
            foreach (MetalNode n in MetalNodes.Haven())
            {
                double localY = n.Position.MetresY - island.MetresY;
                Assert.InRange(localY, 1.0, 12.0);
            }
        }

        [Fact]
        public void No_node_is_placed_at_the_island_or_world_origin()
        {
            foreach (MetalNode n in MetalNodes.Haven())
            {
                Assert.NotEqual(new FixedPointPosition(0, 0, 0), n.Position);
                Assert.NotEqual(SpawnPolicy.IslandPosition, n.Position);
            }
        }

        [Fact]
        public void IslandLocalToWorldFixed_truncates_toward_zero_like_the_client()
        {
            // The client encodes with (long)(d * 4096), a C cast that truncates
            // toward zero. A node at local (1.0, 0.0, 0.0) off a zero origin is
            // exactly 4096; a fractional metre truncates, it does not round.
            FixedPointPosition origin = new FixedPointPosition(0, 0, 0);

            Assert.Equal(new FixedPointPosition(4096, 0, 0),
                MetalNodes.IslandLocalToWorldFixed(origin, 1.0, 0.0, 0.0));
            // 0.999 m x 4096 = 4091.904 -> truncates to 4091, not rounds to 4092.
            Assert.Equal(4091, MetalNodes.IslandLocalToWorldFixed(origin, 0.999, 0.0, 0.0).X);
        }

        [Fact]
        public void The_pickup_interaction_values_are_non_zero_so_the_prompt_appears()
        {
            // InteractiveObjectVisualizer takes radius/timeToUse from the matching
            // InteractionEntry; with no matching entry they fall to 0 and the prompt
            // never appears. The radius must be non-zero for the "E to pick up"
            // prompt to show at all.
            Assert.True(MetalNodes.PickUpRadius > 0f);
        }
    }
}

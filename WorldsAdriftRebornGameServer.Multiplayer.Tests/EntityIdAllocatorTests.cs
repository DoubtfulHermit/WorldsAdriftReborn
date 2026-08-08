using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// docs/multiplayer.md rule 4: all clients get the SAME island entity id.
    /// </summary>
    public class EntityIdAllocatorTests
    {
        [Fact]
        public void Every_client_is_told_the_same_island_entity_id()
        {
            // The symptom of per-client island ids: a relayed TransformState
            // carries Parent = the sender's island id, the receiver looks that id
            // up locally, finds nothing, and the remote avatar stays frozen at
            // the seed position ~90km off-island.
            EntityIdAllocator ids = new();

            long forFirstClient = ids.SharedIslandEntityId;
            ids.Next();                 // first client's player entity
            long forSecondClient = ids.SharedIslandEntityId;
            ids.Next();                 // second client's player entity
            long forThirdClient = ids.SharedIslandEntityId;

            Assert.Equal(forFirstClient, forSecondClient);
            Assert.Equal(forFirstClient, forThirdClient);
        }

        [Fact]
        public void The_island_id_is_allocated_from_the_same_counter_as_player_ids()
        {
            // It must be a real allocation, not a magic constant, or it can
            // collide with a player entity.
            EntityIdAllocator ids = new();

            long island = ids.SharedIslandEntityId;
            long firstPlayer = ids.Next();

            Assert.NotEqual(island, firstPlayer);
        }

        [Fact]
        public void The_island_id_never_collides_with_any_player_entity_id()
        {
            EntityIdAllocator ids = new();
            long island = ids.SharedIslandEntityId;

            for (int i = 0; i < 100; i++)
            {
                Assert.NotEqual(island, ids.Next());
            }
        }

        [Fact]
        public void Island_allocation_is_lazy_so_a_server_with_no_clients_allocates_nothing()
        {
            EntityIdAllocator ids = new();

            Assert.False(ids.IslandAllocated);
            _ = ids.SharedIslandEntityId;
            Assert.True(ids.IslandAllocated);
        }

        [Fact]
        public void Entity_ids_start_at_zero_and_increase_by_one()
        {
            EntityIdAllocator ids = new();

            Assert.Equal(0, ids.Next());
            Assert.Equal(1, ids.Next());
            Assert.Equal(2, ids.Next());
        }

        [Fact]
        public void Entity_ids_are_never_reused_so_stale_cross_client_references_cannot_resolve_to_a_new_player()
        {
            EntityIdAllocator ids = new();
            HashSet<long> seen = new();

            for (int i = 0; i < 1000; i++)
            {
                Assert.True(seen.Add(ids.Next()), "an entity id was handed out twice");
            }
        }
    }
}

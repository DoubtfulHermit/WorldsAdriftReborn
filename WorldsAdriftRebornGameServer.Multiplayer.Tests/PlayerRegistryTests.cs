using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public class PlayerRegistryTests
    {
        private const ulong PeerA = 0x1000;
        private const ulong PeerB = 0x2000;
        private const ulong PeerC = 0x3000;

        [Fact]
        public void EntityOf_returns_null_for_unknown_peer()
        {
            PlayerRegistry registry = new();

            Assert.Null(registry.EntityOf(PeerA));
        }

        [Fact]
        public void Register_then_EntityOf_returns_the_entity()
        {
            PlayerRegistry registry = new();
            registry.Register(PeerA, 42);

            Assert.Equal(42, registry.EntityOf(PeerA));
            Assert.Equal(1, registry.Count);
        }

        [Fact]
        public void Registering_the_same_peer_twice_overwrites_rather_than_throwing()
        {
            // A client reconnecting into the same peer slot must not take the
            // server down.
            PlayerRegistry registry = new();
            registry.Register(PeerA, 42);
            registry.Register(PeerA, 99);

            Assert.Equal(99, registry.EntityOf(PeerA));
            Assert.Equal(1, registry.Count);
        }

        [Fact]
        public void Unregister_returns_the_entity_and_removes_the_peer()
        {
            PlayerRegistry registry = new();
            registry.Register(PeerA, 42);

            Assert.Equal(42, registry.Unregister(PeerA));
            Assert.Null(registry.EntityOf(PeerA));
            Assert.Equal(0, registry.Count);
        }

        [Fact]
        public void Unregister_of_unknown_peer_returns_null_and_does_not_throw()
        {
            PlayerRegistry registry = new();

            Assert.Null(registry.Unregister(PeerA));
        }

        [Fact]
        public void PeersExcept_excludes_the_given_peer()
        {
            PlayerRegistry registry = new();
            registry.Register(PeerA, 1);
            registry.Register(PeerB, 2);
            registry.Register(PeerC, 3);

            IReadOnlyList<ulong> targets = registry.PeersExcept(PeerB);

            Assert.Equal(2, targets.Count);
            Assert.Contains(PeerA, targets);
            Assert.Contains(PeerC, targets);
            Assert.DoesNotContain(PeerB, targets);
        }

        [Fact]
        public void PeersExcept_of_the_only_peer_is_empty()
        {
            PlayerRegistry registry = new();
            registry.Register(PeerA, 1);

            Assert.Empty(registry.PeersExcept(PeerA));
        }

        [Fact]
        public void PeersExcept_of_an_unregistered_peer_returns_everyone()
        {
            // This is what a join needs: the newcomer is not yet registered, but
            // every existing player must still be told about them.
            PlayerRegistry registry = new();
            registry.Register(PeerA, 1);
            registry.Register(PeerB, 2);

            Assert.Equal(2, registry.PeersExcept(PeerC).Count);
        }

        [Fact]
        public void Others_returns_peer_entity_pairs_excluding_the_given_peer()
        {
            PlayerRegistry registry = new();
            registry.Register(PeerA, 10);
            registry.Register(PeerB, 20);

            IReadOnlyList<(ulong PeerId, long EntityId)> others = registry.Others(PeerA);

            (ulong PeerId, long EntityId) only = Assert.Single(others);
            Assert.Equal(PeerB, only.PeerId);
            Assert.Equal(20, only.EntityId);
        }

        // ------------------------------------------------------------------
        // Ownership gate (docs/multiplayer.md rule 6): first-time setup and the
        // AUTHORITY grant may only ever run against the sender's OWN entity.
        // ------------------------------------------------------------------

        [Fact]
        public void Owns_is_true_for_a_peers_own_entity()
        {
            PlayerRegistry registry = new();
            registry.Register(PeerA, 42);

            Assert.True(registry.Owns(PeerA, 42));
        }

        [Fact]
        public void A_peer_never_owns_another_players_entity()
        {
            // The old check was "is this ANY player entity", which handed
            // authority over someone else's avatar to whichever client asked
            // for its components first.
            PlayerRegistry registry = new();
            registry.Register(PeerA, 42);
            registry.Register(PeerB, 43);

            Assert.False(registry.Owns(PeerA, 43));
            Assert.False(registry.Owns(PeerB, 42));
        }

        [Fact]
        public void An_unregistered_peer_owns_nothing_including_entity_zero()
        {
            // Entity 0 is a real id here (the counter starts at 0), so a check
            // that treats "unknown" as 0 would grant the world's first entity to
            // any peer that asks before registering.
            PlayerRegistry registry = new();

            Assert.False(registry.Owns(PeerA, 0));
            Assert.False(registry.Owns(PeerA, 42));
        }

        [Fact]
        public void A_departed_peer_owns_nothing()
        {
            PlayerRegistry registry = new();
            registry.Register(PeerA, 42);
            registry.Unregister(PeerA);

            Assert.False(registry.Owns(PeerA, 42));
        }

        [Fact]
        public void Owns_follows_a_re_registration_rather_than_the_old_entity()
        {
            PlayerRegistry registry = new();
            registry.Register(PeerA, 42);
            registry.Register(PeerA, 99);

            Assert.False(registry.Owns(PeerA, 42));
            Assert.True(registry.Owns(PeerA, 99));
        }

        [Fact]
        public void Entity_id_can_be_reused_after_the_owning_peer_disconnects()
        {
            PlayerRegistry registry = new();
            registry.Register(PeerA, 7);
            registry.Unregister(PeerA);
            registry.Register(PeerB, 7);

            Assert.Null(registry.EntityOf(PeerA));
            Assert.Equal(7, registry.EntityOf(PeerB));
            Assert.Equal(1, registry.Count);
        }
    }
}

using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// Nothing about a peer may survive that peer leaving.
    ///
    /// These drive the registry, the appearance store and the mirror through the
    /// exact call sequence the server's connect/update/disconnect paths use, and
    /// then assert the departed peer is gone from all of them. The ENet-side
    /// per-peer maps are NOT covered here - see docs/testing.md.
    /// </summary>
    public class PeerCleanupTests
    {
        private const ulong PeerA = 0x1000;
        private const ulong PeerB = 0x2000;

        private const long EntityA = 11;
        private const long EntityB = 22;

        private sealed class World
        {
            public readonly PlayerRegistry Registry = new();
            public readonly AppearanceStore Appearances = new();
            public readonly RemotePlayerMirror Mirror;

            public World()
            {
                Mirror = new RemotePlayerMirror(Registry);
            }

            /// <summary>Mirrors WorldsAdriftRebornGameServer.MirrorNewPlayer.</summary>
            public void Join(ulong peer, long entity, string appearance)
            {
                Mirror.OnJoin(peer, entity);
                Appearances.Record(entity, new Dictionary<string, string> { { "bossaNetCharacterData", appearance } });
            }

            /// <summary>Mirrors WorldsAdriftRebornGameServer.OnClientDisconnected.</summary>
            public IReadOnlyList<MirrorIntent> Leave(ulong peer)
            {
                long? ownEntity = Registry.EntityOf(peer);
                IReadOnlyList<MirrorIntent> despawns = Mirror.OnLeave(peer);
                if (ownEntity.HasValue)
                {
                    Appearances.Forget(ownEntity.Value);
                }
                return despawns;
            }
        }

        [Fact]
        public void A_departed_peer_owns_no_entity()
        {
            World world = new();
            world.Join(PeerA, EntityA, "alice");
            world.Join(PeerB, EntityB, "bob");

            world.Leave(PeerA);

            Assert.Null(world.Registry.EntityOf(PeerA));
            Assert.False(world.Registry.Owns(PeerA, EntityA));
        }

        [Fact]
        public void A_departed_peers_appearance_is_forgotten()
        {
            World world = new();
            world.Join(PeerA, EntityA, "alice");

            world.Leave(PeerA);

            Assert.Null(world.Appearances.Get(EntityA));
            Assert.Equal(0, world.Appearances.Count);
        }

        [Fact]
        public void Nothing_is_relayed_to_a_departed_peer()
        {
            World world = new();
            world.Join(PeerA, EntityA, "alice");
            world.Join(PeerB, EntityB, "bob");

            world.Leave(PeerA);
            IReadOnlyList<MirrorIntent> intents = world.Mirror.OnComponentUpdate(PeerB, 190602, new byte[] { 1 });

            Assert.DoesNotContain(intents, i => i.TargetPeer == PeerA);
        }

        [Fact]
        public void Nothing_is_relayed_from_a_departed_peer()
        {
            // Packets already in flight when the disconnect lands are normal.
            World world = new();
            world.Join(PeerA, EntityA, "alice");
            world.Join(PeerB, EntityB, "bob");

            world.Leave(PeerA);

            Assert.Empty(world.Mirror.OnComponentUpdate(PeerA, 190602, new byte[] { 1 }));
        }

        [Fact]
        public void A_player_joining_after_a_disconnect_is_never_told_about_the_departed_avatar()
        {
            // The symptom of a leak here is a frozen body standing in the world
            // for everyone who joins afterwards.
            World world = new();
            world.Join(PeerA, EntityA, "alice");
            world.Leave(PeerA);

            IReadOnlyList<MirrorIntent> intents = world.Mirror.OnJoin(PeerB, EntityB);

            Assert.Empty(intents);
            Assert.Equal(1, world.Registry.Count);
        }

        [Fact]
        public void Disconnecting_one_peer_leaves_the_other_players_state_untouched()
        {
            // Cleanup that is too eager is the same bug wearing a different hat.
            World world = new();
            world.Join(PeerA, EntityA, "alice");
            world.Join(PeerB, EntityB, "bob");

            world.Leave(PeerA);

            Assert.Equal(EntityB, world.Registry.EntityOf(PeerB));
            Assert.True(world.Registry.Owns(PeerB, EntityB));
            Assert.Equal("bob", world.Appearances.Get(EntityB)!["bossaNetCharacterData"]);
        }

        [Fact]
        public void An_ENet_peer_slot_reused_by_a_new_client_inherits_nothing_from_the_old_one()
        {
            // Peer ids are ENetPeer POINTERS and ENet reuses peer slots, so the
            // same id genuinely comes back as a different player.
            World world = new();
            world.Join(PeerA, EntityA, "alice");
            world.Leave(PeerA);

            const long reconnectedEntity = 99;
            world.Join(PeerA, reconnectedEntity, "carol");

            Assert.Equal(reconnectedEntity, world.Registry.EntityOf(PeerA));
            Assert.False(world.Registry.Owns(PeerA, EntityA));
            Assert.Null(world.Appearances.Get(EntityA));
            Assert.Equal("carol", world.Appearances.Get(reconnectedEntity)!["bossaNetCharacterData"]);
        }

        [Fact]
        public void A_disconnect_for_a_peer_that_never_joined_changes_nothing()
        {
            // Normal during connect races; it must not disturb anyone else.
            World world = new();
            world.Join(PeerA, EntityA, "alice");

            Assert.Empty(world.Leave(PeerB));
            Assert.Equal(EntityA, world.Registry.EntityOf(PeerA));
            Assert.Equal(1, world.Appearances.Count);
        }

        [Fact]
        public void Every_player_disconnecting_empties_the_world_completely()
        {
            World world = new();
            world.Join(PeerA, EntityA, "alice");
            world.Join(PeerB, EntityB, "bob");

            world.Leave(PeerA);
            world.Leave(PeerB);

            Assert.Equal(0, world.Registry.Count);
            Assert.Equal(0, world.Appearances.Count);
            Assert.Empty(world.Registry.PeersExcept(PeerA));
        }
    }
}

using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public class RemotePlayerMirrorTests
    {
        private const ulong PeerA = 0x1000;
        private const ulong PeerB = 0x2000;
        private const ulong PeerC = 0x3000;

        private const long EntityA = 11;
        private const long EntityB = 22;
        private const long EntityC = 33;

        private static (PlayerRegistry, RemotePlayerMirror) NewMirror()
        {
            PlayerRegistry registry = new();
            return (registry, new RemotePlayerMirror(registry));
        }

        [Fact]
        public void First_player_to_join_produces_no_intents()
        {
            // Nobody to show them, and nobody to show them to.
            (_, RemotePlayerMirror mirror) = NewMirror();

            Assert.Empty(mirror.OnJoin(PeerA, EntityA));
        }

        [Fact]
        public void First_player_is_still_registered_despite_producing_no_intents()
        {
            (PlayerRegistry registry, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);

            Assert.Equal(EntityA, registry.EntityOf(PeerA));
        }

        [Fact]
        public void Second_player_joining_mirrors_in_both_directions()
        {
            (_, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);

            IReadOnlyList<MirrorIntent> intents = mirror.OnJoin(PeerB, EntityB);

            // B is told about A, and A is told about B: entity + components each.
            Assert.Equal(4, intents.Count);

            Assert.Contains(intents, i => i.TargetPeer == PeerB && i.Op == MirrorOp.AddEntity && i.EntityId == EntityA);
            Assert.Contains(intents, i => i.TargetPeer == PeerB && i.Op == MirrorOp.AddComponents && i.EntityId == EntityA);
            Assert.Contains(intents, i => i.TargetPeer == PeerA && i.Op == MirrorOp.AddEntity && i.EntityId == EntityB);
            Assert.Contains(intents, i => i.TargetPeer == PeerA && i.Op == MirrorOp.AddComponents && i.EntityId == EntityB);
        }

        [Fact]
        public void Join_never_mirrors_a_player_to_themselves()
        {
            (_, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);

            IReadOnlyList<MirrorIntent> intents = mirror.OnJoin(PeerB, EntityB);

            Assert.DoesNotContain(intents, i => i.TargetPeer == PeerB && i.EntityId == EntityB);
            Assert.DoesNotContain(intents, i => i.TargetPeer == PeerA && i.EntityId == EntityA);
        }

        [Fact]
        public void Join_never_grants_authority_over_a_remote_avatar()
        {
            // Only a peer's own entity may be authoritative. There is deliberately
            // no intent type that could carry authority to a remote entity.
            (_, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);

            IReadOnlyList<MirrorIntent> intents = mirror.OnJoin(PeerB, EntityB);

            Assert.All(intents, i => Assert.True(i.Op is MirrorOp.AddEntity or MirrorOp.AddComponents));
        }

        [Fact]
        public void Third_player_joining_is_mirrored_to_both_existing_players()
        {
            (_, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);
            mirror.OnJoin(PeerB, EntityB);

            IReadOnlyList<MirrorIntent> intents = mirror.OnJoin(PeerC, EntityC);

            // C learns about A and B; A and B each learn about C.
            Assert.Equal(8, intents.Count);
            Assert.Equal(2, intents.Count(i => i.TargetPeer == PeerA && i.EntityId == EntityC));
            Assert.Equal(2, intents.Count(i => i.TargetPeer == PeerB && i.EntityId == EntityC));
            Assert.Equal(2, intents.Count(i => i.TargetPeer == PeerC && i.EntityId == EntityA));
            Assert.Equal(2, intents.Count(i => i.TargetPeer == PeerC && i.EntityId == EntityB));
        }

        [Fact]
        public void Update_from_A_is_relayed_only_to_B()
        {
            (_, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);
            mirror.OnJoin(PeerB, EntityB);

            byte[] payload = { 1, 2, 3 };
            IReadOnlyList<MirrorIntent> intents = mirror.OnComponentUpdate(PeerA, 1003, payload);

            MirrorIntent only = Assert.Single(intents);
            Assert.Equal(PeerB, only.TargetPeer);
            Assert.Equal(MirrorOp.RelayComponentUpdate, only.Op);
            Assert.Equal(EntityA, only.EntityId);
            Assert.Equal(1003u, only.ComponentId);
            Assert.Same(payload, only.Payload);
        }

        [Fact]
        public void Update_is_never_echoed_back_to_its_sender()
        {
            (_, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);
            mirror.OnJoin(PeerB, EntityB);

            IReadOnlyList<MirrorIntent> intents = mirror.OnComponentUpdate(PeerA, 1003, new byte[] { 9 });

            Assert.DoesNotContain(intents, i => i.TargetPeer == PeerA);
        }

        [Fact]
        public void Update_from_the_only_player_produces_no_intents()
        {
            (_, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);

            Assert.Empty(mirror.OnComponentUpdate(PeerA, 1003, new byte[] { 9 }));
        }

        [Fact]
        public void Update_from_an_unregistered_peer_is_ignored_rather_than_throwing()
        {
            // Packets during join and teardown races are normal. One player's bad
            // state must never abort the packet loop.
            (_, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);

            Assert.Empty(mirror.OnComponentUpdate(PeerC, 1003, new byte[] { 9 }));
        }

        [Fact]
        public void Leaving_tells_remaining_players_to_despawn_the_avatar()
        {
            (_, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);
            mirror.OnJoin(PeerB, EntityB);

            IReadOnlyList<MirrorIntent> intents = mirror.OnLeave(PeerA);

            MirrorIntent only = Assert.Single(intents);
            Assert.Equal(PeerB, only.TargetPeer);
            Assert.Equal(MirrorOp.RemoveEntity, only.Op);
            Assert.Equal(EntityA, only.EntityId);
        }

        [Fact]
        public void Leaving_unregisters_the_peer()
        {
            (PlayerRegistry registry, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);
            mirror.OnJoin(PeerB, EntityB);

            mirror.OnLeave(PeerA);

            Assert.Null(registry.EntityOf(PeerA));
            Assert.Equal(1, registry.Count);
        }

        [Fact]
        public void Leaving_does_not_notify_the_departing_peer()
        {
            (_, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);
            mirror.OnJoin(PeerB, EntityB);

            IReadOnlyList<MirrorIntent> intents = mirror.OnLeave(PeerA);

            Assert.DoesNotContain(intents, i => i.TargetPeer == PeerA);
        }

        [Fact]
        public void Leaving_an_unregistered_peer_produces_nothing_and_does_not_throw()
        {
            (_, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);

            Assert.Empty(mirror.OnLeave(PeerC));
        }

        [Fact]
        public void Last_player_leaving_produces_no_intents()
        {
            (_, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);

            Assert.Empty(mirror.OnLeave(PeerA));
        }

        [Fact]
        public void Updates_from_a_departed_player_stop_being_relayed()
        {
            (_, RemotePlayerMirror mirror) = NewMirror();
            mirror.OnJoin(PeerA, EntityA);
            mirror.OnJoin(PeerB, EntityB);
            mirror.OnLeave(PeerA);

            Assert.Empty(mirror.OnComponentUpdate(PeerA, 1003, new byte[] { 9 }));
        }
    }
}

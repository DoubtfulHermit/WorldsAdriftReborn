using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public sealed class InteractionActivationGateTests
    {
        [Fact]
        public void Repeated_activate_is_one_edge_until_matching_release()
        {
            var gate = new InteractionActivationGate();

            Assert.True(gate.TryBegin(10, 100));
            Assert.False(gate.TryBegin(10, 100));
            Assert.False(gate.TryBegin(10, 100));

            gate.Release(10, 100);

            Assert.True(gate.TryBegin(10, 100));
        }

        [Fact]
        public void Default_release_rearms_every_target_for_only_that_player()
        {
            var gate = new InteractionActivationGate();
            Assert.True(gate.TryBegin(10, 100));
            Assert.True(gate.TryBegin(10, 101));
            Assert.True(gate.TryBegin(11, 100));

            gate.Release(10, -1);

            Assert.True(gate.TryBegin(10, 100));
            Assert.True(gate.TryBegin(10, 101));
            Assert.False(gate.TryBegin(11, 100));
        }

        [Fact]
        public void A_release_for_another_target_does_not_rearm_the_held_edge()
        {
            var gate = new InteractionActivationGate();
            Assert.True(gate.TryBegin(10, 100));

            gate.Release(10, 101);

            Assert.False(gate.TryBegin(10, 100));
        }

        [Fact]
        public void Disconnect_cleanup_rearms_without_touching_other_players()
        {
            var gate = new InteractionActivationGate();
            Assert.True(gate.TryBegin(10, 100));
            Assert.True(gate.TryBegin(11, 100));

            gate.ReleasePlayer(10);

            Assert.True(gate.TryBegin(10, 100));
            Assert.False(gate.TryBegin(11, 100));
        }
    }
}

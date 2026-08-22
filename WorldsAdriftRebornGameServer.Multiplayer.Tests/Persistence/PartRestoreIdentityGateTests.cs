using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Persistence
{
    public class PartRestoreIdentityGateTests
    {
        [Fact]
        public void Stable_identity_can_restore_only_once()
        {
            var gate = new PartRestoreIdentityGate();

            Assert.True(gate.TryAccept("generator-1"));
            Assert.False(gate.TryAccept("generator-1"));
            Assert.Equal(1, gate.StableIdentityCount);
        }

        [Fact]
        public void Empty_legacy_identities_remain_loadable()
        {
            var gate = new PartRestoreIdentityGate();

            Assert.True(gate.TryAccept(""));
            Assert.True(gate.TryAccept(null));
            Assert.Equal(0, gate.StableIdentityCount);
        }

        [Fact]
        public void Identity_comparison_is_ordinal_and_case_sensitive()
        {
            var gate = new PartRestoreIdentityGate();

            Assert.True(gate.TryAccept("ABC"));
            Assert.True(gate.TryAccept("abc"));
            Assert.Equal(2, gate.StableIdentityCount);
        }
    }
}

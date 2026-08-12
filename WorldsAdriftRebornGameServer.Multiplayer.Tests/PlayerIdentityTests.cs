using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The per-player identity policy: playerId == characterUid == the durable character
    /// uid (so IsShipOwner(PlayerId) can match a character-uid owner list), a distinct
    /// synthetic id for a volatile player, and the flag that gates the whole change.
    /// </summary>
    public class PlayerIdentityTests
    {
        private const string UidA = "9bae0367-1234-4abc-9def-0123456789ab";
        private const string UidB = "11112222-3333-4444-5555-666677778888";

        [Fact]
        public void DurablePlayer_IdIsTheCharacterUid()
        {
            // field2 playerId and field3 characterUid are served as the SAME value - the
            // durable uid - so the cross-axis quest gate (IsShipOwner(PlayerId)) can pass.
            Assert.Equal(UidA, PlayerIdentity.IdFor(UidA, entityId: 500));
        }

        [Fact]
        public void DurablePlayer_IdIgnoresEntityId()
        {
            // A durable owner's id must not depend on which entity it is served on, or the
            // owner's yard registration (no entity id available) would not match their own
            // 1086 served on their player entity.
            Assert.Equal(
                PlayerIdentity.IdFor(UidA, entityId: 1),
                PlayerIdentity.IdFor(UidA, entityId: 99999));
        }

        [Fact]
        public void VolatilePlayer_GetsDistinctPerEntitySyntheticId()
        {
            // No durable uid -> a per-entity synthetic, distinct between two such players so
            // their labels do not collapse and neither accidentally owns anything.
            string a = PlayerIdentity.IdFor("", entityId: 500);
            string b = PlayerIdentity.IdFor(null, entityId: 501);
            Assert.NotEqual(a, b);
            Assert.False(string.IsNullOrEmpty(a));
        }

        [Fact]
        public void TwoDurablePlayers_HaveDistinctIdsAndLabels()
        {
            Assert.NotEqual(PlayerIdentity.IdFor(UidA, 1), PlayerIdentity.IdFor(UidB, 2));
            Assert.NotEqual(
                PlayerIdentity.DisplayNameFor(UidA, 1),
                PlayerIdentity.DisplayNameFor(UidB, 2));
        }

        [Fact]
        public void OwnerPlayerId_IsTheOwnerCharacterUid_EmptyStaysEmpty()
        {
            Assert.Equal(UidA, PlayerIdentity.OwnerPlayerId(UidA));
            Assert.Equal("", PlayerIdentity.OwnerPlayerId(""));
            Assert.Equal("", PlayerIdentity.OwnerPlayerId(null));
        }

        [Theory]
        [InlineData("1", true)]
        [InlineData("true", true)]
        [InlineData(" YES ", true)]
        [InlineData("on", true)]
        [InlineData("0", false)]
        [InlineData("off", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Flag_ParsesTruthyValues(string? raw, bool expected)
        {
            Assert.Equal(expected, PlayerIdentity.ParseEnabled(raw));
        }

        [Fact]
        public void LegacyStubs_AreUnchanged()
        {
            // The flag-off serve must stay byte-identical to the pre-fix stubs.
            Assert.Equal("sp00ktober", PlayerIdentity.LegacyDisplayName);
            Assert.Equal("id", PlayerIdentity.LegacyPlayerId);
            Assert.Equal("cUid", PlayerIdentity.LegacyCharacterUid);
            Assert.Equal(LocalPlayerIdentity.PlayerId, PlayerIdentity.LegacyPlayerId);
        }
    }
}

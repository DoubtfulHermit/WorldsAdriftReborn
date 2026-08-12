using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The one identity string the client reads as <c>LocalPlayer.PlayerId</c> (1086
    /// PlayerName field2_player_id). The ship editor's SAVE/RESET buttons are gated on
    /// <c>1206 ownerPlayerId == LocalPlayer.PlayerId</c>, so the 1206 owner and the 1086
    /// serve MUST both use this value. This guards it from silently drifting.
    /// </summary>
    public class LocalPlayerIdentityTests
    {
        [Fact]
        public void Player_id_is_the_1086_stub_value()
        {
            // Must stay byte-identical to the string the 1086 PlayerName serve puts in
            // field2_player_id ("id"), or SAVE greys out again.
            Assert.Equal("id", LocalPlayerIdentity.PlayerId);
        }

        [Fact]
        public void Player_id_is_non_empty()
        {
            // An empty owner id would also fail the GetOwnerId() == PlayerId compare
            // (both empty is not what the client checks out) and is never a valid id.
            Assert.False(string.IsNullOrEmpty(LocalPlayerIdentity.PlayerId));
        }
    }
}

using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// The ownership identity gates behind "who may build on a shipyard / place parts on a
    /// ship", now PER PLAYER. Gate A (shipyard) compares the registered list against the
    /// checker's PlayerId; under per-player identity a yard registers the OWNER's PlayerId
    /// (== owner character uid), so the owner passes and every other peer is denied. Gate B
    /// (ship) compares the character uid. Plus the server-side authorization the editor
    /// command seam enforces so the boundary does not rely on the client gate.
    /// </summary>
    public class OwnershipRegistrationPolicyTests
    {
        private const string OwnerUid = "9bae0367-1234-4abc-9def-0123456789ab";
        private const string OtherUid = "11112222-3333-4444-5555-666677778888";

        // ---- GATE A: shipyard build-access (registeredCharacterUids) ----

        [Fact]
        public void OwnedShipyard_RegistersTheOwnersPlayerId()
        {
            // Per-player: ownerPlayerId == owner character uid, so the owner's own
            // LocalPlayer.PlayerId is what the yard registers.
            string ownerPlayerId = PlayerIdentity.OwnerPlayerId(OwnerUid);
            var registered = OwnershipRegistrationPolicy.ShipyardRegisteredUids(OwnerUid, ownerPlayerId);

            Assert.Contains(ownerPlayerId, registered);
            Assert.Single(registered);
        }

        [Fact]
        public void OwnedShipyard_OwnerPasses_NonOwnerDenied()
        {
            string ownerPlayerId = PlayerIdentity.OwnerPlayerId(OwnerUid);

            // The owner's PlayerId (== their character uid) passes Contains().
            Assert.True(OwnershipRegistrationPolicy.ShipyardGrantsAccessTo(
                checkerPlayerId: OwnerUid, OwnerUid, ownerPlayerId));

            // Any other peer, whose PlayerId is its own distinct character uid, is DENIED -
            // the collapse the pre-fix shared stub caused.
            Assert.False(OwnershipRegistrationPolicy.ShipyardGrantsAccessTo(
                checkerPlayerId: OtherUid, OwnerUid, ownerPlayerId));
        }

        [Fact]
        public void UnownedShipyard_GrantsNobody()
        {
            var registered = OwnershipRegistrationPolicy.ShipyardRegisteredUids(
                "", PlayerIdentity.OwnerPlayerId(""));

            Assert.Empty(registered);
            Assert.False(OwnershipRegistrationPolicy.ShipyardGrantsAccessTo(OwnerUid, "", ""));
        }

        [Fact]
        public void LegacyStub_RegistersTheSharedId_AllPeersPass()
        {
            // Flag-off path: the yard registers the shared stub, so every peer (all serving
            // PlayerId == "id") passes. This is the documented pre-fix behaviour, preserved
            // only behind the legacy path so the flag toggles cleanly.
            var registered = OwnershipRegistrationPolicy.ShipyardRegisteredUids(
                OwnerUid, LocalPlayerIdentity.PlayerId);

            Assert.Contains(LocalPlayerIdentity.PlayerId, registered);
            Assert.True(OwnershipRegistrationPolicy.ShipyardGrantsAccessTo(
                LocalPlayerIdentity.PlayerId, OwnerUid, LocalPlayerIdentity.PlayerId));
        }

        // ---- GATE B: ship ownership (8062/4349 owner lists) ----

        [Fact]
        public void BuiltOwnedHull_OwnerPasses_NonOwnerDenied()
        {
            var owners = OwnershipRegistrationPolicy.ShipOwnerUids(isBuiltHull: true, OwnerUid);
            Assert.Contains(OwnerUid, owners);
            Assert.Single(owners);

            Assert.True(OwnershipRegistrationPolicy.ShipOwnedBy(OwnerUid, true, OwnerUid));
            Assert.False(OwnershipRegistrationPolicy.ShipOwnedBy(OtherUid, true, OwnerUid));
        }

        [Fact]
        public void NonBuiltHull_SeedsEmpty_EvenWithAnOwnerUid()
        {
            var owners = OwnershipRegistrationPolicy.ShipOwnerUids(isBuiltHull: false, OwnerUid);
            Assert.Empty(owners);
            Assert.False(OwnershipRegistrationPolicy.ShipOwnedBy(OwnerUid, false, OwnerUid));
        }

        [Fact]
        public void BuiltHullWithoutOwner_SeedsEmpty()
        {
            var owners = OwnershipRegistrationPolicy.ShipOwnerUids(isBuiltHull: true, "");
            Assert.Empty(owners);
        }

        // ---- SERVER-SIDE AUTHORIZATION (editor/SAVE command seam) ----

        [Fact]
        public void ServerAuth_OwnedYard_OnlyOwnerMayEdit()
        {
            Assert.True(OwnershipRegistrationPolicy.ServerAllowsYardEdit(OwnerUid, OwnerUid));
            Assert.False(OwnershipRegistrationPolicy.ServerAllowsYardEdit(OtherUid, OwnerUid));
            // A volatile sender (no durable uid) cannot edit an owned yard.
            Assert.False(OwnershipRegistrationPolicy.ServerAllowsYardEdit("", OwnerUid));
        }

        [Fact]
        public void ServerAuth_UnownedYard_AnyoneMayEdit()
        {
            // No owner to protect: the static/test flows and unowned yards stay editable.
            Assert.True(OwnershipRegistrationPolicy.ServerAllowsYardEdit(OwnerUid, ""));
            Assert.True(OwnershipRegistrationPolicy.ServerAllowsYardEdit("", ""));
        }
    }
}

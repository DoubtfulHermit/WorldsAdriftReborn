using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// Regression tests for the two ownership identity gates behind the "ship/shipyard is
    /// red after relog" bug. They pin the ASYMMETRY the client demands: Gate A compares the
    /// registered list against LocalPlayer.PlayerId (the stub), Gate B against the character
    /// uid. Filling the wrong identifier - the pre-fix behaviour - fails these.
    /// </summary>
    public class OwnershipRegistrationPolicyTests
    {
        private const string CharacterUid = "9bae0367-1234-4abc-9def-0123456789ab";

        // ---- GATE A: shipyard build-access (registeredCharacterUids) ----

        [Fact]
        public void OwnedShipyard_RegistersPlayerIdStub_NotCharacterUid()
        {
            var registered = OwnershipRegistrationPolicy.ShipyardRegisteredUids(
                CharacterUid, LocalPlayerIdentity.PlayerId);

            // The client checks Contains(LocalPlayer.PlayerId) == Contains("id").
            Assert.Contains(LocalPlayerIdentity.PlayerId, registered);
            // The PRE-FIX bug: it registered the character uid, so Contains("id") was false.
            Assert.DoesNotContain(CharacterUid, registered);
            Assert.Single(registered);
        }

        [Fact]
        public void UnownedShipyard_RegistersNobody()
        {
            var registered = OwnershipRegistrationPolicy.ShipyardRegisteredUids(
                "", LocalPlayerIdentity.PlayerId);

            Assert.Empty(registered);
        }

        // ---- GATE B: ship ownership (8062/4349 owner lists) ----

        [Fact]
        public void BuiltOwnedHull_SeedsOwnerCharacterUid()
        {
            var owners = OwnershipRegistrationPolicy.ShipOwnerUids(isBuiltHull: true, CharacterUid);

            // IsShipOwner matches SelectedCharacterUid (the character uid) against this list.
            Assert.Contains(CharacterUid, owners);
            Assert.Single(owners);
        }

        [Fact]
        public void NonBuiltHull_SeedsEmpty_EvenWithAnOwnerUid()
        {
            // The static test ship is not a built hull: it must stay unowned regardless.
            var owners = OwnershipRegistrationPolicy.ShipOwnerUids(isBuiltHull: false, CharacterUid);

            Assert.Empty(owners);
        }

        [Fact]
        public void BuiltHullWithoutOwner_SeedsEmpty()
        {
            var owners = OwnershipRegistrationPolicy.ShipOwnerUids(isBuiltHull: true, "");

            Assert.Empty(owners);
        }
    }
}

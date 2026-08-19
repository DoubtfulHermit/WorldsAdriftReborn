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
            var owners = OwnershipRegistrationPolicy.ShipOwnerUids(
                isBuiltHull: true, CharacterUid, LocalPlayerIdentity.PlayerId);

            // IsShipOwner matches SelectedCharacterUid (the character uid) against this list.
            Assert.Contains(CharacterUid, owners);
        }

        [Fact]
        public void NonBuiltHull_SeedsEmpty_EvenWithAnOwnerUid()
        {
            // The static test ship is not a built hull: it must stay unowned regardless.
            var owners = OwnershipRegistrationPolicy.ShipOwnerUids(
                isBuiltHull: false, CharacterUid, LocalPlayerIdentity.PlayerId);

            Assert.Empty(owners);
        }

        [Fact]
        public void BuiltHullWithoutOwner_SeedsEmpty()
        {
            var owners = OwnershipRegistrationPolicy.ShipOwnerUids(
                isBuiltHull: true, "", LocalPlayerIdentity.PlayerId);

            Assert.Empty(owners);
        }

        // ---- GATE C: "It's locked." on a ship container ----
        //
        // InteractAgentObserver.cs:358 feeds LocalPlayer.PlayerId - the GATE A
        // identifier - into ShipVisualizer.IsShipOwner, which searches the GATE B
        // list. Miss it and InteractAgentObserver.cs:391-394 prints "It's locked."
        // and never sends the 1211, so the four ship containers can never open no
        // matter how correctly they are seeded.

        [Fact]
        public void BuiltOwnedHull_AlsoRegistersLocalPlayerId_OrEveryShipContainerReadsLocked()
        {
            var owners = OwnershipRegistrationPolicy.ShipOwnerUids(
                isBuiltHull: true, CharacterUid, LocalPlayerIdentity.PlayerId);

            // The PRE-FIX bug: only the character uid was here, so the cross-axis
            // compare against PlayerId always missed - for the OWNER too.
            Assert.Contains(LocalPlayerIdentity.PlayerId, owners);
            Assert.Equal(2, owners.Count);
        }

        [Fact]
        public void BothGateIdentifiersSatisfyIsShipOwner_WhichIsAnExists()
        {
            var owners = OwnershipRegistrationPolicy.ShipOwnerUids(
                isBuiltHull: true, CharacterUid, LocalPlayerIdentity.PlayerId);

            // Gate B (HostileItemPlacingPredicate -> SelectedCharacterUid) and
            // gate C (InteractAgentObserver -> LocalPlayer.PlayerId) must BOTH
            // find a match, from the one list, on the one hull.
            Assert.Contains(CharacterUid, owners);
            Assert.Contains(LocalPlayerIdentity.PlayerId, owners);
        }

        [Fact]
        public void AddingTheLocalPlayerIdCannotWeakenGateB()
        {
            var owners = OwnershipRegistrationPolicy.ShipOwnerUids(
                isBuiltHull: true, CharacterUid, LocalPlayerIdentity.PlayerId);

            // Gate B compares a REAL per-player character uid from BossaNet. The
            // stub can never be one, so no non-owner gains ship access from it.
            Assert.DoesNotContain("11111111-2222-3333-4444-555555555555", owners);
            Assert.NotEqual(CharacterUid, LocalPlayerIdentity.PlayerId);
        }

        [Fact]
        public void WhenPlayerIdBecomesTheCharacterUid_TheListDoesNotDuplicate()
        {
            // feat/per-player-identity makes 1086 field2 == the character uid. The
            // second entry must then collapse rather than seeding the same uid twice.
            var owners = OwnershipRegistrationPolicy.ShipOwnerUids(
                isBuiltHull: true, CharacterUid, CharacterUid);

            Assert.Single(owners);
            Assert.Equal(CharacterUid, owners[0]);
        }
    }
}

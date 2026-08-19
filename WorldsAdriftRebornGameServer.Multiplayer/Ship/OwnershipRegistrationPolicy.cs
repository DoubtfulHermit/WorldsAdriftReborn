using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The PURE, engine-free rule for WHICH identifier the server puts in the two
    /// client-facing ownership lists that decide whether the local player may build on
    /// a shipyard and place parts on a ship. It exists as its own module because the
    /// choice is the whole bug: the two gates compare against DIFFERENT identifiers, and
    /// filling the wrong one (or leaving one empty) reads to the player as a red,
    /// "doesn't belong to me" structure after relog.
    ///
    /// GATE A - shipyard build-access. The client's
    /// <c>ShipyardVisualizer.IsLocalPlayerRegistered</c> checks
    /// <c>RegisteredCharacterUids.Contains(LocalPlayer.Instance.PlayerId)</c>
    /// (ShipyardVisualizer.cs:27). <c>LocalPlayer.PlayerId</c> is the 1086 PlayerName
    /// field2, which the server serves as the fixed <see cref="LocalPlayerIdentity.PlayerId"/>
    /// stub for every client - NOT the character uid. So an owned yard's
    /// <c>registeredCharacterUids</c> must contain the PlayerId stub, not the owner's
    /// character uid, or the Contains check is always false. When identity stops being a
    /// stub this becomes the owner's real per-player PlayerId - still passed in, not the
    /// character uid.
    ///
    /// GATE B - ship ownership. The client's
    /// <c>HostileItemPlacingPredicate.IsInaccessibleShip</c> marks a ship forbidden
    /// unless <c>ShipVisualizer.IsShipOwner(BossaNetBootstrap.Instance.SelectedCharacterUid)</c>
    /// (HostileItemPlacingPredicate.cs:56-66). <c>SelectedCharacterUid</c> IS the
    /// character uid, and <c>IsShipOwner</c> matches it against 8062
    /// <c>ShipOwnersDeprecatedState</c> / 4349 <c>ShipRegisteredCharactersState</c>
    /// (ShipVisualizer.cs:66). So a built, owned hull's owner list must contain the
    /// owner's CHARACTER uid. An unowned server hull (the static test ship, or a built
    /// hull with no recorded owner) keeps an EMPTY list so nobody owns it.
    ///
    /// The two gates key on opposite identifiers on purpose; that asymmetry lives here,
    /// unit-tested, so the engine-side serve glue is a straight list-of-uids conversion.
    ///
    /// GATE C - "It's locked." THE THIRD CONSUMER, and the one this file originally
    /// missed. <c>InteractAgentObserver.CheckInteraction</c> (InteractAgentObserver.cs:358)
    /// reads <c>LocalPlayerInit.PlayerId</c> - the GATE A identifier - and feeds it to
    /// <c>ShipPartVisualizer.IsShipPartInFriendlyShip</c>, which hands it straight to
    /// <c>ShipVisualizer.IsShipOwner</c> (ShipPartVisualizer.cs:281), i.e. the GATE B
    /// list. So one client call site crosses the axes: it compares a PlayerId against a
    /// list of character uids. When that compare fails on a ship the client considers
    /// OWNED, and the part carries no <c>RespawnerVisualizer</c>,
    /// <c>IsShipPartInFriendlyShip</c> returns false and E on a ship container prints
    /// <c>"It's locked."</c> and never sends the 1211 (InteractAgentObserver.cs:391-394).
    /// That is the whole of the reported bug: the containers, their 1081 grids, their
    /// 1210 Inventory verb and the open echo were all correct and simply never reached.
    /// (<c>Travellers.Quests/ShipPartAttachedToOwnedShipCondition.cs:91</c> crosses the
    /// same axes, so it is retail's inconsistency, not ours - but retail only felt it
    /// once a ship became owned, and in retail 4349 was filled by REGISTERING A PERSONAL
    /// REVIVER, which almost no hull had.)
    ///
    /// So an owned hull's owner list carries BOTH identifiers: the owner's character uid
    /// for gate B, and the local-player id for gate C. Adding the second entry cannot
    /// weaken gate B - <c>SelectedCharacterUid</c> is a real per-player uid and never
    /// equals the <c>"id"</c> stub - and while 1086 identity IS that shared stub, every
    /// PlayerId-keyed gate is already unable to tell two players apart. Registering it
    /// makes that fail OPEN (your own container opens) instead of closed (nobody's does).
    /// When <c>feat/per-player-identity</c> lands and PlayerId becomes the character uid,
    /// the two entries collapse to one value and the second becomes a duplicate to drop.
    /// </summary>
    public static class OwnershipRegistrationPolicy
    {
        /// <summary>
        /// GATE A. The uids to put in a shipyard's 1205 <c>registeredCharacterUids</c>.
        /// An OWNED yard (<paramref name="ownerCharacterUid"/> non-empty) registers the
        /// local-player id <paramref name="localPlayerId"/> - the value the client
        /// compares via <c>LocalPlayer.PlayerId</c>, NOT the character uid. An unowned
        /// yard registers nobody.
        /// </summary>
        public static List<string> ShipyardRegisteredUids(string ownerCharacterUid, string localPlayerId)
        {
            var registered = new List<string>();
            if (!string.IsNullOrEmpty(ownerCharacterUid))
            {
                registered.Add(localPlayerId);
            }
            return registered;
        }

        /// <summary>
        /// GATES B AND C. The uids to seed a hull's 8062 <c>ownersDeprecated</c> /
        /// 4349 <c>reviverInfosCache</c> owner lists. A BUILT hull with a recorded owner
        /// (<paramref name="isBuiltHull"/> and <paramref name="ownerCharacterUid"/>
        /// non-empty) registers TWO entries, because two client call sites read this one
        /// list with two different identifiers:
        ///   * the owner's CHARACTER uid, which <c>HostileItemPlacingPredicate</c>
        ///     compares via <c>SelectedCharacterUid</c> (gate B);
        ///   * <paramref name="localPlayerId"/>, which <c>InteractAgentObserver</c> and
        ///     <c>ShipPartAttachedToOwnedShipCondition</c> compare via
        ///     <c>LocalPlayer.PlayerId</c> (gate C) - see the type remarks.
        /// Order matters only for readability; <c>IsShipOwner</c> is an <c>Exists</c>.
        /// Anything else (a non-built hull such as the static test ship, or a built hull
        /// with no owner) stays UNOWNED - empty list, which the client reads as
        /// "not owned by anyone" and therefore friendly to everyone.
        /// </summary>
        public static List<string> ShipOwnerUids(bool isBuiltHull, string ownerCharacterUid, string localPlayerId)
        {
            var owners = new List<string>();
            if (!isBuiltHull || string.IsNullOrEmpty(ownerCharacterUid))
            {
                return owners;
            }

            owners.Add(ownerCharacterUid);
            if (!string.IsNullOrEmpty(localPlayerId) && localPlayerId != ownerCharacterUid)
            {
                owners.Add(localPlayerId);
            }
            return owners;
        }
    }
}

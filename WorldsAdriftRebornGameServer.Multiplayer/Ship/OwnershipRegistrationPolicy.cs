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
        /// GATE B. The character uids to seed a hull's 8062 <c>ownersDeprecated</c> /
        /// 4349 <c>reviverInfosCache</c> owner lists. A BUILT hull with a recorded owner
        /// (<paramref name="isBuiltHull"/> and <paramref name="ownerCharacterUid"/>
        /// non-empty) is owned by that CHARACTER uid - the value the client compares via
        /// <c>SelectedCharacterUid</c>. Anything else (a non-built hull such as the static
        /// test ship, or a built hull with no owner) stays UNOWNED - empty list.
        /// </summary>
        public static List<string> ShipOwnerUids(bool isBuiltHull, string ownerCharacterUid)
        {
            var owners = new List<string>();
            if (isBuiltHull && !string.IsNullOrEmpty(ownerCharacterUid))
            {
                owners.Add(ownerCharacterUid);
            }
            return owners;
        }
    }
}

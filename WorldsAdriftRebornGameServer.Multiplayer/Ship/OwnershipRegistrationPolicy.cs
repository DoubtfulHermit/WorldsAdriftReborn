using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The PURE, engine-free rule for WHICH identifier the server puts in the two
    /// client-facing ownership lists that decide whether a player may build on a shipyard
    /// and place parts on a ship - PLUS the server-side authorization the editor command
    /// seam enforces so the boundary does not rely on the client gate alone.
    ///
    /// The two client gates key on DIFFERENT identifiers on purpose:
    ///
    /// GATE A - shipyard build-access. <c>ShipyardVisualizer.IsLocalPlayerRegistered</c>
    /// checks <c>RegisteredCharacterUids.Contains(LocalPlayer.Instance.PlayerId)</c>
    /// (ShipyardVisualizer.cs:27). So an owned yard's <c>registeredCharacterUids</c> must
    /// contain the OWNER's PlayerId - and under per-player identity PlayerId == the owner's
    /// character uid (see <see cref="PlayerIdentity"/>), so the owner passes and every other
    /// peer, comparing its own distinct PlayerId, fails. An unowned yard registers nobody.
    ///
    /// GATE B - ship ownership. <c>ShipVisualizer.IsShipOwner(SelectedCharacterUid)</c>
    /// matches the local player's CHARACTER uid against 8062 / 4349. So a built, owned
    /// hull's owner list must contain the owner's CHARACTER uid; an unowned hull keeps an
    /// EMPTY list so nobody owns it.
    ///
    /// SERVER AUTHORIZATION. The client gates can be defeated (a stale/global identity, a
    /// modified client), so the ship-hull editor command seam must independently refuse a
    /// SAVE/edit for a shipyard the sender does not own. That decision is
    /// <see cref="ServerAllowsYardEdit"/>: pure, unit-tested, and compares durable character
    /// uids (sender vs the yard's recorded owner). An UNOWNED yard is editable by anyone -
    /// there is no owner to protect, and the static/test flows depend on it.
    /// </summary>
    public static class OwnershipRegistrationPolicy
    {
        /// <summary>
        /// GATE A. The ids to put in a shipyard's 1205 <c>registeredCharacterUids</c>. An
        /// OWNED yard (<paramref name="ownerCharacterUid"/> non-empty) registers the OWNER's
        /// PlayerId <paramref name="ownerPlayerId"/> - the value the client compares via
        /// <c>LocalPlayer.PlayerId</c>. Under per-player identity that is the owner's own
        /// character uid, so the owner passes and others do not; under the legacy stub it is
        /// the shared id (every peer passes - the documented pre-fix behaviour, kept only
        /// behind the flag-off path). An unowned yard registers nobody.
        /// </summary>
        public static List<string> ShipyardRegisteredUids(string ownerCharacterUid, string ownerPlayerId)
        {
            var registered = new List<string>();
            if (!string.IsNullOrEmpty(ownerCharacterUid))
            {
                registered.Add(ownerPlayerId);
            }
            return registered;
        }

        /// <summary>
        /// GATE B. The character uids to seed a hull's 8062 / 4349 owner lists. A BUILT hull
        /// with a recorded owner is owned by that CHARACTER uid; anything else stays UNOWNED
        /// (empty list).
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

        /// <summary>
        /// GATE A, modelled as the client's Contains() check: does a checker whose PlayerId
        /// is <paramref name="checkerPlayerId"/> gain build-access to a yard owned per
        /// (<paramref name="ownerCharacterUid"/>, <paramref name="ownerPlayerId"/>)? Owner
        /// passes, everyone else is denied, an unowned yard grants nobody.
        /// </summary>
        public static bool ShipyardGrantsAccessTo(
            string checkerPlayerId, string ownerCharacterUid, string ownerPlayerId)
        {
            return ShipyardRegisteredUids(ownerCharacterUid, ownerPlayerId).Contains(checkerPlayerId);
        }

        /// <summary>
        /// GATE B, modelled as the client's <c>IsShipOwner</c> check: is the ship owned by
        /// the character <paramref name="checkerCharacterUid"/>?
        /// </summary>
        public static bool ShipOwnedBy(
            string checkerCharacterUid, bool isBuiltHull, string ownerCharacterUid)
        {
            return ShipOwnerUids(isBuiltHull, ownerCharacterUid).Contains(checkerCharacterUid);
        }

        /// <summary>
        /// SERVER AUTHORIZATION. Whether a sender whose durable character uid is
        /// <paramref name="senderCharacterUid"/> may issue an editor/SAVE command against a
        /// shipyard whose recorded owner uid is <paramref name="yardOwnerCharacterUid"/>.
        /// An UNOWNED yard (empty owner) is editable by anyone; an OWNED yard only by its
        /// owner. This is the server-side boundary the client gate must not be trusted to
        /// enforce alone.
        /// </summary>
        public static bool ServerAllowsYardEdit(string senderCharacterUid, string yardOwnerCharacterUid)
        {
            if (string.IsNullOrEmpty(yardOwnerCharacterUid))
            {
                return true;
            }
            return senderCharacterUid == yardOwnerCharacterUid;
        }
    }
}

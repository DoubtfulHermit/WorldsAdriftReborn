using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>
    /// The server's record of every loose part MOUNTED onto a built ship this session,
    /// keyed by the part's world-entity id, plus the per-player CARRY tracker that says
    /// which part a player is currently holding. It is the mount counterpart of
    /// <see cref="LooseParts"/> / <see cref="BuiltShips"/>: <c>PartMountService</c>
    /// writes one entry on an accepted <c>1070 PlacePart</c>, and
    /// <c>ComponentsSerializer</c> reads it back to serve the mounted per-entity truth
    /// its loose-part branches otherwise contradict -
    ///
    ///   * 8066 ShipRootState: shipRoot = the hull (isRoot=false), so the part is a
    ///     MEMBER of that ship rather than a free entity on the deck;
    ///   * 190602 TransformState: parent = Parent(hullId, "~") + the stored hull-local
    ///     offset, so a re-checkout re-seeds the part already RIDING the hull;
    ///   * 1120 ShipPartState: attached=true, held cleared, so the part reads as bolted
    ///     on rather than liftable.
    ///
    /// In-memory only, exactly like the loose-part / built-ship / node ledgers:
    /// "persistent" for this milestone means "visible to every connected client until
    /// the server restarts". A restart-durable mount ledger is the documented follow-on
    /// (the built-ship ledger's persistence work is the template) and is deliberately
    /// left to the parallel persistence work rather than restructured here.
    ///
    /// NOT thread-safe, deliberately: the server is a single poll loop and the 1070
    /// event is drained on it, like every other writer here.
    /// </summary>
    internal static class MountedParts
    {
        /// <summary>
        /// One mounted part's re-checkout truth: which hull it belongs to, its
        /// hull-local offset (fixed point, ready for the 190602 seed) and the loose-part
        /// metadata (prefab/attach/title/itemType) the 1120 seed still needs. The
        /// <see cref="AttachedToEntityId"/> is the safe default the 1120 seed reports
        /// (the hull) - see findings-part-mount-spec.md 3.4 for why it is not load-bearing
        /// for an inert non-panel part.
        /// </summary>
        internal readonly struct Mount
        {
            internal Mount(long hullEntityId, FixedPointPosition localOffset, long attachedToEntityId,
                string prefabName, string attachmentType, string title, string itemType,
                uint packedRotation, string ownerCharacterUid)
            {
                HullEntityId = hullEntityId;
                LocalOffset = localOffset;
                AttachedToEntityId = attachedToEntityId;
                PrefabName = prefabName;
                AttachmentType = attachmentType;
                Title = title;
                ItemType = itemType;
                PackedRotation = packedRotation;
                OwnerCharacterUid = ownerCharacterUid ?? "";
            }

            internal long HullEntityId { get; }
            internal FixedPointPosition LocalOffset { get; }
            internal long AttachedToEntityId { get; }
            internal string PrefabName { get; }
            internal string AttachmentType { get; }
            internal string Title { get; }
            internal string ItemType { get; }

            /// <summary>
            /// The packed <c>Quaternion32</c> hull-local rotation the player placed the part
            /// at. Honored by the 190602 mount re-seed so a re-checkout (and a boot restore)
            /// keeps the part at its placed facing rather than snapping to identity.
            /// </summary>
            internal uint PackedRotation { get; }

            /// <summary>The character uid of the player who mounted the part.</summary>
            internal string OwnerCharacterUid { get; }
        }

        private static readonly Dictionary<long, Mount> ByEntityId = new Dictionary<long, Mount>();

        /// <summary>
        /// The per-player carry tracker: player entity id -&gt; the part entity id that
        /// player is currently holding. Populated from 1239 PickedUpEntityEvent and
        /// cleared on drop / on a completed mount. This is how the 1070 handler resolves
        /// the part a <c>PlacePart</c> refers to, since <c>PlacePart</c> itself carries
        /// no part id.
        /// </summary>
        private static readonly Dictionary<long, long> CarriedByPlayer = new Dictionary<long, long>();

        /// <summary>Records that <paramref name="partEntityId"/> is now mounted on a ship.</summary>
        internal static void Register(long partEntityId, Mount mount)
        {
            ByEntityId[partEntityId] = mount;
        }

        /// <summary>Whether this entity id is a part mounted onto a built ship.</summary>
        internal static bool Is(long partEntityId)
        {
            return ByEntityId.ContainsKey(partEntityId);
        }

        /// <summary>The mount record for a part, or null when the id is not a mounted part.</summary>
        internal static Mount? MountFor(long partEntityId)
        {
            return ByEntityId.TryGetValue(partEntityId, out Mount mount) ? mount : (Mount?)null;
        }

        /// <summary>
        /// Removes a part's mount record - it has been LIFTED OFF the ship and is loose
        /// again. Returns true if it was mounted. Called from the 1239 pickup handler so a
        /// player can re-position a part they already placed: without this the re-lifted
        /// part stays in the ledger and its next PlacePart is rejected
        /// <see cref="Multiplayer.Ship.PartMountReject.PartAlreadyMounted"/>. The
        /// authoritative component revert (1120 attached=false, 8066 no-ship, 190602 loose)
        /// is a documented follow-on; clearing the ledger is what unblocks the re-mount and
        /// is enough for the static-ship milestone (the part re-seeds loose on its next
        /// checkout via the loose-part branches, which still know it).
        /// </summary>
        internal static bool Unmount(long partEntityId)
        {
            return ByEntityId.Remove(partEntityId);
        }

        /// <summary>How many parts have been mounted this session.</summary>
        internal static int Count => ByEntityId.Count;

        // ------------------------------------------------------------------
        // CARRY tracking (client-driven lift; PlacePart carries no part id).
        // ------------------------------------------------------------------

        /// <summary>Records that <paramref name="playerEntityId"/> picked up <paramref name="partEntityId"/>.</summary>
        internal static void SetCarried(long playerEntityId, long partEntityId)
        {
            CarriedByPlayer[playerEntityId] = partEntityId;
        }

        /// <summary>Clears whatever <paramref name="playerEntityId"/> was carrying (drop / mounted).</summary>
        internal static void ClearCarried(long playerEntityId)
        {
            CarriedByPlayer.Remove(playerEntityId);
        }

        /// <summary>The part a player is carrying, or null when the server has seen no pickup.</summary>
        internal static long? CarriedBy(long playerEntityId)
        {
            return CarriedByPlayer.TryGetValue(playerEntityId, out long partId) ? partId : (long?)null;
        }
    }
}

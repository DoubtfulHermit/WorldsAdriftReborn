using System;
using Bossa.Travellers.Motion;
using Bossa.Travellers.Player;
using Bossa.Travellers.Ship;
using Improbable;
using Improbable.Collections;
using Improbable.Math;
using WorldsAdriftRebornGameServer.Game.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// THE MOUNT COMMIT. Turns an accepted <c>1070 BuilderState.PlacePart</c> into a
    /// loose ship part becoming a MEMBER of a built ship that rides its hull - the
    /// attach half of the part-mount flow (the craft-&gt;loose-part half is
    /// <see cref="Crafting.LoosePartSpawner"/>).
    ///
    /// WHO DRIVES WHAT. Carry is CLIENT-LOCAL: the client lifts the part
    /// (PlayerScannerTool), previews, and on place fires ONE server-bound event,
    /// <c>1070 PlacePart</c>, then Cancel()s locally without waiting. This service is
    /// the server side of that event: resolve which part the player is carrying (from
    /// the 1239 pickup notifications, since PlacePart carries no part id), re-check the
    /// client's own gate (a modified client can send anything), and write the
    /// authoritative attach as three component VALUE-UPDATES on the PART entity, which
    /// every peer already sees:
    ///   * 8066 ShipRootState  - shipRoot = the hull, isRoot=false (membership);
    ///   * 190602 TransformState - Parent(hullId, "~") + the hull-local offset (ride);
    ///   * 1120 ShipPartState  - attached=true, held cleared, attach bookkeeping.
    ///
    /// MULTIPLAYER CLASSIFICATION: EVENT-DRIVEN. One event in, three value-updates out
    /// (NOT re-seeds - a re-seed re-fires the client OnDisable-&gt;Clear destroy). The
    /// part then RIDES the hull via the same <c>Parent(shipId,"~")</c> follow the
    /// bolted parts use - NO per-part world-position stream is ever published. On the
    /// STATIC built ships of this phase the hull does not move, so the seeded/updated
    /// hull-relative transform holds the part in place with no heartbeat; a continuous
    /// wake for a MOVING built hull is deferred alongside built-ship flight (the built
    /// deck itself does not yet ride a moving built hull either - BuiltShipPlacement).
    /// </summary>
    internal static class PartMountService
    {
        /// <summary>
        /// The monotonic wake counter for the 190602 value-update stamp, shared with
        /// nothing else (each mount publishes at most a few updates). Static and
        /// unlocked on purpose: the server is a single poll loop.
        /// </summary>
        private static long _sample;

        /// <summary>
        /// Handles one decoded <c>PlacePart</c> for the player entity that sent it.
        /// Ownership is re-checked by the caller (the 1070 handler); this method resolves
        /// the carried part, runs the pure gate, and commits. A rejection does nothing:
        /// the client already Cancel()ed locally, so the part stays loose.
        /// </summary>
        internal static void HandlePlacePart(bool ownsPlayerEntity, long playerEntityId, PlacePart pp)
        {
            long shipId = pp.shipId.Id;
            long parentId = pp.parentId.Id;
            long? carried = MountedParts.CarriedBy(playerEntityId);

            bool hasCarried = carried.HasValue;
            // Split the old single "mountable" fact into its two reasons so a live
            // rejection names WHICH one failed (findings: PartNotMountable was ambiguous
            // between "not a known loose part" and "already mounted").
            bool carriedIsLoosePart = hasCarried && Crafting.LooseParts.Is(carried.Value);
            bool carriedNotAlreadyMounted = hasCarried && !MountedParts.Is(carried.Value);
            bool shipIsBuilt = Crafting.BuiltShips.IsBuiltHull(shipId);
            bool targetChild = TargetIsChildOfShip(parentId, shipId);

            PartMountReject verdict = PartMount.EvaluatePlace(
                ownsPlayerEntity, hasCarried, carriedIsLoosePart, carriedNotAlreadyMounted, shipIsBuilt, targetChild);

            if (verdict != PartMountReject.Accept)
            {
                Console.WriteLine("[info] part-mount: REJECTED PlacePart from player entity " + playerEntityId
                    + " (part " + (carried.HasValue ? carried.Value.ToString() : "none")
                    + " -> ship " + shipId + ", target " + parentId + "): " + verdict + ".");
                return;
            }

            Commit(carried.Value, shipId, pp);
            MountedParts.ClearCarried(playerEntityId);
        }

        /// <summary>
        /// Handles a CancelPlacePart: the player cancelled the carry (the client dropped
        /// the lift locally). Just clear the carry tracker; the part is still loose and
        /// its components are unchanged, so no component write is needed.
        /// </summary>
        internal static void HandleCancelPlacePart(long playerEntityId)
        {
            if (MountedParts.CarriedBy(playerEntityId).HasValue)
            {
                Console.WriteLine("[info] part-mount: player entity " + playerEntityId + " cancelled its carry.");
                MountedParts.ClearCarried(playerEntityId);
            }
        }

        /// <summary>
        /// Whether the client's <c>parentId</c> surface resolves to the named
        /// <c>shipId</c> - the server mirror of the client's
        /// <c>spatialOsEntity.HasParentEntity(shipEntity)</c> gate. Valid when the target:
        /// <list type="bullet">
        ///   <item>IS the hull root itself - the case for a part placed on the hull's own
        ///     geometry (the frame sides/struts an engine or wing mounts on; the client's
        ///     raycast on those surfaces resolves the owning entity up to the HULL, so it
        ///     sends parentId == shipId);</item>
        ///   <item>is a BUILT DECK whose sibling hull is that ship - the case a HELM/lamp/
        ///     sail (attachmentType "deck", client PlacementLocationType.ShipDeck) mounts
        ///     on. The deck is made a Unity child of its hull by the 190602 built-deck seed
        ///     branch (Parent(hullId,"deck")), which is exactly what makes the client's
        ///     HasParentEntity pass and turns the deck into a legal placement surface;</item>
        ///   <item>is a part already MOUNTED on that ship - so a part can be stacked on an
        ///     already-placed part (e.g. a lamp on the helm) the same way it rides the hull.</item>
        /// </list>
        /// The per-attachmentType surface CHOICE (which of these a given part is allowed on)
        /// is enforced client-side by the raycast layer+tag (see PlacementPreview.GetMask/
        /// GetTag in the decompile); the server only re-checks the child-of-ship invariant
        /// every one of those surfaces shares, since it cannot see Unity layers.
        /// </summary>
        private static bool TargetIsChildOfShip(long parentId, long shipId)
        {
            if (parentId == shipId)
            {
                return true;
            }
            // A part already mounted on this same ship is itself a valid surface (stacking).
            if (Crafting.MountedParts.MountFor(parentId)?.HullEntityId == shipId)
            {
                return true;
            }
            if (!Crafting.BuiltShips.IsBuiltDeck(parentId))
            {
                return false;
            }
            string? deckKey = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(parentId)?.Key;
            string? hullKey = BuiltShipPlacement.HullKeyForDeckKey(deckKey);
            long? hullId = hullKey == null
                ? null
                : WorldsAdriftRebornGameServer.WorldEntities.BoundEntityIdFor(hullKey);
            return hullId.HasValue && hullId.Value == shipId;
        }

        /// <summary>
        /// Writes the mount as three component value-updates on the part entity and
        /// records it in the <see cref="MountedParts"/> ledger so a re-checkout re-seeds
        /// it already attached. Broadcast to every fully-loaded peer via the same
        /// reliable path the hull's 1130 and the bolted parts' 190602 wake use.
        /// </summary>
        private static void Commit(long partEntityId, long hullEntityId, PlacePart pp)
        {
            FixedPointPosition localOffset = PartMount.ShipLocalOffset(
                pp.shipLocalPosition.X, pp.shipLocalPosition.Y, pp.shipLocalPosition.Z);

            // 8066 membership: the part now belongs to the hull.
            ShipRootState.Update rootUpdate = new ShipRootState.Update()
                .SetShipRoot(new Option<EntityId>(new EntityId(hullEntityId)))
                .SetIsRoot(false);
            ShipPublisher.Broadcast(partEntityId, 8066u, rootUpdate);

            // 190602 ride: a VALUE UPDATE (never a re-seed) carrying Parent(hullId, "~")
            // and the hull-local offset, with a fresh monotonic stamp - exactly the
            // bolted-part wake shape, which fires the follow-visualizer's WakeUp. The
            // localRotation is the player's PLACED hull-relative rotation (PlacePart
            // .shipLocalRotation), packed to the client's 32-bit wire form. Quaternion32
            // Packing.Encode substitutes the identity sentinel for a non-finite/degenerate
            // value, so an unrotated or bogus placement is "facing north", never a NaN
            // rejection of the whole transform.
            uint packedShipLocalRotation = Multiplayer.Placement.Quaternion32Packing.Encode(
                pp.shipLocalRotation.w, pp.shipLocalRotation.x, pp.shipLocalRotation.y, pp.shipLocalRotation.z);
            float stamp = ShipPartMotionPolicy.StampFor(++_sample, ShipPartMotionPolicy.HeartbeatIntervalSeconds);
            var transformUpdate = ShipPartTransform.BuildWakeUpdate(
                localOffset, hullEntityId, BoltedPartTransform.RelativeSlotKey, stamp,
                new Improbable.Corelibrary.Math.Quaternion32(packedShipLocalRotation));
            ShipPublisher.Broadcast(partEntityId, 190602u, transformUpdate);

            // 1120 logical attachment. attachedTo = the hull is the safe default (not
            // load-bearing for an inert non-panel part; capture-only for full fidelity).
            // Both the 190602 localRotation (above) and the 1120 attach bookkeeping now
            // carry the player's placed rotation, so the part sits at the orientation it
            // was placed at rather than snapping to identity.
            ShipPartState.Update partUpdate = new ShipPartState.Update()
                .SetAttached(true)
                .SetHeld(false)
                .SetHeldBy(EntityId.InvalidEntityId)
                .SetHeldByTool(EntityId.InvalidEntityId)
                .SetAttachedTo(new EntityId(hullEntityId))
                .SetAttachPos(pp.shipLocalPosition)
                .SetAttachRot(pp.shipLocalRotation)
                .SetLastAttachment(new RelativeLocation(
                    new EntityId(hullEntityId), pp.shipLocalPosition, pp.shipLocalRotation))
                .SetPlayersPlacingPart(new Improbable.Collections.List<EntityId>());
            ShipPublisher.Broadcast(partEntityId, 1120u, partUpdate);

            // Record for re-checkout (and future restart-durable persistence + a moving-
            // hull wake). Prefab/attach/title/itemType come from the loose-part ledger so
            // the 1120 re-seed still loads the right prefab.
            var def = Crafting.LooseParts.DefFor(partEntityId);
            MountedParts.Register(partEntityId, new MountedParts.Mount(
                hullEntityId,
                localOffset,
                hullEntityId,
                def?.PrefabName ?? "",
                def?.AttachmentType ?? "",
                def?.Title ?? "",
                def?.ItemType ?? ""));

            Console.WriteLine("[info] part-mount: MOUNTED part " + partEntityId + " onto hull " + hullEntityId
                + " at hull-local " + localOffset + " (attached=true, Parent(hull,\"~\")). Mount #"
                + MountedParts.Count + " this session.");
        }
    }
}

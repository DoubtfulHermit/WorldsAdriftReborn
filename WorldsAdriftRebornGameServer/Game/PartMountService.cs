using System;
using Bossa.Travellers.Motion;
using Bossa.Travellers.Player;
using Bossa.Travellers.Ship;
using Bossa.Travellers.Interact;
using Bossa.Travellers.Salvaging;
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
        /// The monotonic wake counter for the 190602 value-update stamp. Static and
        /// unlocked on purpose: the server is a single poll loop.
        ///
        /// SHARED with <see cref="ShipFlightService"/> via
        /// <see cref="NextTimelineSample"/>, and that sharing is load-bearing: the
        /// client's parent-sampling fix (ShipPartMotionPolicy.ParentStampFor) puts a
        /// built hull and its "~" children on ONE timeline, and the client's
        /// interpolator DISCARDS a stamp that does not advance. If flight ran its own
        /// counter from zero while mounts had already advanced this one to N, the
        /// first flight wake would stamp BELOW the last mount stamp and every
        /// mounted part would silently stop following - the exact class of bug the
        /// monotonicity tests exist for.
        /// </summary>
        private static long _sample;

        /// <summary>
        /// The next stamp index on the built-ship 190602 timeline. Every producer of
        /// a mounted-part or built-hull 190602 value update (mount commit, detach,
        /// flight wake) MUST draw from this one counter - see the field remarks.
        /// </summary>
        internal static long NextTimelineSample()
        {
            return ++_sample;
        }

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
            string requesterUid = CharacterOwnership.UidForEntity(playerEntityId);
            string shipOwnerUid = Crafting.BuiltShips.OwnerFor(shipId);
            bool requesterOwnsShip = string.IsNullOrEmpty(shipOwnerUid)
                || string.Equals(requesterUid, shipOwnerUid, StringComparison.Ordinal);
            bool targetChild = TargetIsChildOfShip(parentId, shipId);
            bool representableTransform = PartMount.IsRepresentableLocalOffset(
                pp.shipLocalPosition.X, pp.shipLocalPosition.Y, pp.shipLocalPosition.Z);

            PartMountReject verdict = PartMount.EvaluatePlace(
                ownsPlayerEntity, hasCarried, carriedIsLoosePart, carriedNotAlreadyMounted,
                shipIsBuilt, requesterOwnsShip, targetChild, representableTransform);

            if (verdict != PartMountReject.Accept)
            {
                Console.WriteLine("[info] part-mount: REJECTED PlacePart from player entity " + playerEntityId
                    + " (part " + (carried.HasValue ? carried.Value.ToString() : "none")
                    + " -> ship " + shipId + ", target " + parentId + "): " + verdict + ".");
                return;
            }

            // OWNER = the mounting player's durable character uid, the same identity the
            // crafted part was owned by. Persisted on the mount record.
            Commit(carried.Value, shipId, pp, requesterUid);
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
        /// Completes the re-lift DETACH transaction the <see cref="MountedParts.Unmount"/>
        /// ledger op used to defer (findings-mount-placement.md section 2). Called from the
        /// 1239 pickup handler the instant a MOUNTED part is lifted, with the mount record
        /// captured before it was removed, it broadcasts the authoritative reverse of the
        /// three mount value-updates so carry state is consistent and the next place is
        /// deterministic:
        ///   * 8066 - ship membership CLEARED (shipRoot absent, isRoot=false), the loose seed;
        ///   * 190602 - a GLOBAL (parentless) transform at the part's last world pose, so the
        ///     <c>"~"</c> hull parent is dropped and the part no longer rides the hull;
        ///   * 1120 - attached=false, attachment cleared, so it reads as liftable again (held
        ///     is left to the client's own carry writer, which is lifting the part now).
        /// Value-updates on the same reliable path the mount used; NOT re-seeds.
        /// </summary>
        internal static void BroadcastDetach(long partEntityId, MountedParts.Mount priorMount)
        {
            long hullEntityId = priorMount.HullEntityId;
            WorldsAdriftRebornGameServer.ShipMembership.Unregister(partEntityId, hullEntityId);

            // 8066: no ship. Exactly what the loose 8066 re-seed serves once the ledger
            // entry is gone (ComponentsSerializer loose branch).
            ShipRootState.Update rootClear = new ShipRootState.Update()
                .SetShipRoot(new Option<EntityId>())
                .SetIsRoot(false);
            ShipPublisher.Broadcast(partEntityId, 8066u, rootClear);

            // 190602: global, parent cleared, at the part's last world pose. Fresh
            // monotonic stamp from the shared mount clock so it fires PropertyUpdated.
            FixedPointPosition hullWorldPos = default;
            uint hullWorldRotation = Multiplayer.Placement.Quaternion32Packing.Identity;
            var hullPos = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(hullEntityId)?.Position;
            // A FLOWN hull's real pose lives in its flight session, not the registry
            // (which still says "spawn") - detaching a part from a ship parked away
            // from spawn must drop it where the ship IS, not where it was built.
            if (WorldsAdriftRebornGameServer.Flight.TryGetFlownPose(hullEntityId,
                    out FixedPointPosition detachHullPos, out uint detachHullRotation))
            {
                hullPos = detachHullPos;
                hullWorldRotation = detachHullRotation;
            }
            else
            {
                hullWorldRotation = WorldsAdriftRebornGameServer.WorldEntities.RotationSeedFor(hullEntityId);
            }
            FixedPointPosition globalPos = priorMount.LocalOffset;
            uint globalRotation = priorMount.PackedRotation;
            if (hullPos.HasValue)
            {
                hullWorldPos = hullPos.Value;
                (globalPos, globalRotation) = ShipSalvagePolicy.DropPose(
                    hullWorldPos, hullWorldRotation, priorMount.LocalOffset, priorMount.PackedRotation);
            }
            // A reconnect in this same boot must seed the detached world pose, not the
            // part's old craft/hull parking position.
            WorldsAdriftRebornGameServer.WorldEntities.Relocate(partEntityId, globalPos, globalRotation);
            LocalDomainOwnership.MoveToIsland(
                WorldsAdriftRebornGameServer.DomainHost, partEntityId, globalPos);
            WorldsAdriftRebornGameServer.Flight.RefreshDomainOwnership(hullEntityId);
            float stamp = ShipPartMotionPolicy.StampFor(NextTimelineSample(), ShipPartMotionPolicy.HeartbeatIntervalSeconds);
            var looseTransform = ShipPartTransform.BuildParentlessWakeUpdate(
                globalPos, new Improbable.Corelibrary.Math.Quaternion32(globalRotation), stamp);
            ShipPublisher.Broadcast(partEntityId, 190602u, looseTransform);

            // 1120: attached=false, attachment target cleared. The load-bearing flip back to
            // liftable; matches the loose 1120 re-seed's attach fields.
            ShipPartState.Update partClear = new ShipPartState.Update()
                .SetAttached(false)
                .SetAttachedTo(EntityId.InvalidEntityId)
                .SetLastAttachment(new RelativeLocation(
                    EntityId.InvalidEntityId,
                    new Improbable.Math.Vector3f(0f, 0f, 0f),
                    new Improbable.Corelib.Math.Quaternion(1, 0, 0, 0)))
                .SetPlayersPlacingPart(new Improbable.Collections.List<EntityId>());
            ShipPublisher.Broadcast(partEntityId, 1120u, partClear);

            // 1099 client raycast capability remains enabled while loose: a frame salvage
            // drops its attachments into the yard and those loose parts must still emit
            // 2106 hits. The server enforces the owned-yard radius.
            ShipPublisher.Broadcast(partEntityId, 1099u,
                new SalvageAndRepairState.Update().SetIsSalvageable(true));

            // Helm/sail/lamp/horn/storage keep their prefab-baked interaction entry
            // from initial checkout, but it is usable only while mounted. The client
            // caches that entry at OnEnable, so change availability rather than
            // replacing the interaction list after the fact. The SET is the policy's
            // to name, not this file's: spelled out here it silently omitted storage
            // the moment containers gained a verb.
            if (PartInteractionPolicy.IsMountOperated(priorMount.ItemType))
            {
                ShipPublisher.Broadcast(partEntityId, 1210u,
                    new InteractiveState.Update().SetAvailable(false));
            }

            Console.WriteLine("[info] part-mount: DETACHED part " + partEntityId + " from hull "
                + hullEntityId + " for re-placement (8066 cleared, 1120 attached=false, 190602 loose global).");
        }

        /// <summary>
        /// Writes the mount as three component value-updates on the part entity and
        /// records it in the <see cref="MountedParts"/> ledger so a re-checkout re-seeds
        /// it already attached. Broadcast to every fully-loaded peer via the same
        /// component-update path the hull's 1130 and the bolted parts' 190602 wake use.
        /// </summary>
        private static void Commit(long partEntityId, long hullEntityId, PlacePart pp, string ownerCharacterUid)
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
            // RETAIL ROTATION LOCK: a HELM always mounts facing the ship's BOW. The
            // Helm01 prefab blocks both placement-rotation modes (ShipHelmPlacement
            // .Awake, decompile) precisely because retail helms could only ever face
            // forward - the pilot camera aligns to the SHIP's rotation on man, so a
            // helm mounted at an angle leaves the pilot steering while looking off the
            // side of the ship. The lock is hull-local identity COMPOSED with the
            // WAREBORN_HELM_MOUNT_YAW offset, which DEFAULTS TO 0: the orientation
            // audit proved the editor's Forward, the hull's fore-aft axis, the Helm01
            // prefab's authored forward and the flight integrator's heading are all
            // hull-local +Z, so identity already faces the bow. See HelmMountLock for
            // the citations and for why this knob can never fix a "the ship flies
            // sideways" report. Every other part keeps the player's placed rotation.
            // HelmMountLock is the ONE definition all three commit sites below
            // (190602, 1120 attach fields, ledger/persistence) draw from, so they
            // cannot disagree.
            // The part's catalogue item type, read ONCE: the helm rotation lock and the
            // 190602 hierarchy key both key off it, and reading it twice invites the two
            // to be given different answers by a future edit.
            string mountedItemType = Crafting.LooseParts.DefFor(partEntityId)?.ItemType ?? "";
            bool isHelmMount = mountedItemType == "helm";
            double helmYawDegrees = Multiplayer.Ship.HelmMountLock.YawDegrees();
            (float W, float X, float Y, float Z) helmLock = Multiplayer.Ship.HelmMountLock.LockRotation(helmYawDegrees);
            uint packedShipLocalRotation = isHelmMount
                ? Multiplayer.Ship.HelmMountLock.PackedLockRotation(helmYawDegrees)
                : Multiplayer.Placement.Quaternion32Packing.Encode(
                    pp.shipLocalRotation.w, pp.shipLocalRotation.x, pp.shipLocalRotation.y, pp.shipLocalRotation.z);
            if (isHelmMount)
            {
                Console.WriteLine("[info] part-mount: HELM lock -> hull-local yaw "
                    + helmYawDegrees.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " deg (packed " + packedShipLocalRotation + ", knob "
                    + Multiplayer.Ship.HelmMountLock.YawEnvVar + ").");
            }
            long sample = NextTimelineSample();
            float stamp = ShipPartMotionPolicy.StampFor(sample, ShipPartMotionPolicy.HeartbeatIntervalSeconds);
            // The hierarchy key is per-part and comes from the ONE policy the checkout
            // seed and the in-flight wake also read. A BAR PIPE gets a real word, which
            // makes the client re-parent it as a genuine Unity CHILD of the hull instead
            // of merely position-following it - the whole point, because every client
            // placement walk (NeedToBeOnShip, flag4/CanPlace, ownership, AttachedShip,
            // HasParentEntity) climbs transform.parent and a "~" part has none. Every
            // other part keeps "~" exactly as before. See MountedPartHierarchy.
            string mountHierarchyKey = Multiplayer.Ship.MountedPartHierarchy
                .HierarchyKeyFor(mountedItemType);
            var transformUpdate = ShipPartTransform.BuildWakeUpdate(
                localOffset, hullEntityId, mountHierarchyKey, stamp,
                new Improbable.Corelibrary.Math.Quaternion32(packedShipLocalRotation));
            ShipPublisher.Broadcast(partEntityId, 190602u, transformUpdate);
            if (Multiplayer.Ship.MountedPartHierarchy.IsUnityChild(mountedItemType))
            {
                Console.WriteLine("[info] part-mount: part " + partEntityId + " (" + mountedItemType
                    + ") mounted as a REAL Unity CHILD of hull " + hullEntityId + " (parent key "
                    + mountHierarchyKey + "); it rides the hull through the scene graph and is"
                    + " excluded from the flight wake.");
            }

            // PARENT TIMELINE (findings-mount-placement.md section 2). Advance the HULL's own
            // 190602 to the SAME stamp the child just took, so the client's parent-sampling
            // time REACHES this new child sample. Without it the hull sits frozen at its seed
            // timestamp 0 (ShipPartTransform.BuildSeed) - its 1130 motion never touches the
            // 190602 stamp - so the client keeps sampling the FIRST mount and a re-position or
            // a rotation change on an already-placed part is a visible no-op. This is a
            // value-UPDATE carrying the hull's own unchanged world pose/rotation (only the
            // timestamp moves): NOT a re-seed (a re-seed re-fires the client OnDisable->Clear),
            // NOT a per-frame stream. Event-driven: exactly one extra hull 190602 per accepted
            // place. On a MOVING built hull this stamp is owned by the hull's motion clock
            // (ShipFlightService publishes the same parentless hull update per flight wake,
            // drawing from the SAME NextTimelineSample counter); that path adds a wire
            // cadence and wants the 2-player soak, but the per-mount bump here does not.
            // The hull's CURRENT pose: the flight session's, when the hull has been
            // flown (the session is the only holder of the flown pose - the registry
            // still says "spawn", and stamping the spawn pose onto a hull parked
            // 300 m away would visually yank the whole ship back for this update).
            var hullPos = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(hullEntityId)?.Position;
            uint hullRotationPacked = WorldsAdriftRebornGameServer.WorldEntities.RotationSeedFor(hullEntityId);
            if (WorldsAdriftRebornGameServer.Flight.TryGetFlownPose(hullEntityId, out FixedPointPosition flownPos, out uint flownRot))
            {
                hullPos = flownPos;
                hullRotationPacked = flownRot;
            }
            if (hullPos.HasValue)
            {
                float parentStamp = ShipPartMotionPolicy.ParentStampFor(
                    sample, ShipPartMotionPolicy.HeartbeatIntervalSeconds);
                var hullTimelineUpdate = ShipPartTransform.BuildParentlessWakeUpdate(
                    hullPos.Value, new Improbable.Corelibrary.Math.Quaternion32(hullRotationPacked), parentStamp);
                ShipPublisher.Broadcast(hullEntityId, 190602u, hullTimelineUpdate);
            }

            // 1120 logical attachment. attachedTo = the hull is the safe default (not
            // load-bearing for an inert non-panel part; capture-only for full fidelity).
            // Both the 190602 localRotation (above) and the 1120 attach bookkeeping now
            // carry the player's placed rotation, so the part sits at the orientation it
            // was placed at rather than snapping to identity.
            // The same helm rotation lock as the 190602 packing above: 1120 and the
            // last-attachment record must agree with the served transform, or a
            // re-checkout would restore the tilted facing. Full-precision quaternion
            // here (the 1120 fields are not packed); (w,x,y,z) ctor order - the
            // identity used elsewhere in this file is new Quaternion(1, 0, 0, 0).
            Improbable.Corelib.Math.Quaternion attachRotation = isHelmMount
                ? new Improbable.Corelib.Math.Quaternion(helmLock.W, helmLock.X, helmLock.Y, helmLock.Z)
                : pp.shipLocalRotation;
            ShipPartState.Update partUpdate = new ShipPartState.Update()
                .SetAttached(true)
                .SetHeld(false)
                .SetHeldBy(EntityId.InvalidEntityId)
                .SetHeldByTool(EntityId.InvalidEntityId)
                .SetAttachedTo(new EntityId(hullEntityId))
                .SetAttachPos(pp.shipLocalPosition)
                .SetAttachRot(attachRotation)
                .SetLastAttachment(new RelativeLocation(
                    new EntityId(hullEntityId), pp.shipLocalPosition, attachRotation))
                .SetPlayersPlacingPart(new Improbable.Collections.List<EntityId>());
            ShipPublisher.Broadcast(partEntityId, 1120u, partUpdate);

            // Record for re-checkout (and restart-durable persistence + a moving-hull wake).
            // Prefab/attach/title/itemType come from the loose-part ledger so the 1120 re-seed
            // still loads the right prefab; the packed rotation makes the 190602 mount re-seed
            // honor the placed facing, and the owner is carried for persistence.
            var def = Crafting.LooseParts.DefFor(partEntityId);
            MountedParts.Register(partEntityId, new MountedParts.Mount(
                hullEntityId,
                localOffset,
                hullEntityId,
                def?.PrefabName ?? "",
                def?.AttachmentType ?? "",
                def?.Title ?? "",
                def?.ItemType ?? "",
                packedShipLocalRotation,
                ownerCharacterUid));
            WorldsAdriftRebornGameServer.ShipMembership.Register(partEntityId, hullEntityId);
            LocalDomainOwnership.MoveToShip(
                WorldsAdriftRebornGameServer.DomainHost, partEntityId, hullEntityId);
            WorldsAdriftRebornGameServer.Flight.RefreshDomainOwnership(hullEntityId);

            // 1099 client raycast gate. The old seed hardcoded false, which meant
            // PlayerMultitool.TryDeploySalvager never emitted a ShotEvent for any ship
            // component and the shipyard-only server policy was unreachable. This is
            // only the client capability; exact position + owner checks remain server-side.
            ShipPublisher.Broadcast(partEntityId, 1099u,
                new SalvageAndRepairState.Update().SetIsSalvageable(true));

            // INTERACTABLE-PART LEDGERS: a mounted sail/lamp/horn becomes operable
            // (1211 Activate via PartInteractionService). Fresh-mount defaults are the
            // states the parts have always served: sail furled, lamp on, horn charged.
            // Register is idempotent, so a re-mount after a lift starts fresh (the
            // lift's Unregister cleared the old state).
            switch (def?.ItemType)
            {
                case "sail":
                    WorldsAdriftRebornGameServer.Sails.Register(partEntityId, hullEntityId);
                    break;
                case "lamp":
                    WorldsAdriftRebornGameServer.Lamps.Register(partEntityId, hullEntityId);
                    break;
                case "horn":
                    WorldsAdriftRebornGameServer.Horns.Register(partEntityId, hullEntityId);
                    break;
            }

            // A mounted sky core is what gives a hull a FUEL SYSTEM at all: it is the
            // only ship part whose Activate verb is prefab-baked and unclaimed, so it
            // is the only refuel door the shipped client leaves open. Idempotent, and
            // deliberately never a refill - see ShipFuelLedger.
            WorldsAdriftRebornGameServer.ShipFuel.OnPartMounted(def?.ItemType, partEntityId, hullEntityId);

            // The correct Man/Activate/Inventory entry was seeded while loose
            // (unavailable), because InteractiveObjectVisualizer only caches it in
            // OnEnable. Mounting now needs exactly one value flip to make the
            // already-cached verb usable. WITHOUT THIS FLIP the part is a prompt that
            // never appears - which is what a bolted trunk was for as long as this
            // condition was a hand-written Man||Activate list.
            PartVerb seededInteraction = PartInteractionPolicy.SeedVerbFor(def?.ItemType);
            if (PartInteractionPolicy.IsMountOperated(def?.ItemType))
            {
                ShipPublisher.Broadcast(partEntityId, 1210u,
                    new InteractiveState.Update().SetAvailable(true));
                Console.WriteLine("[info] part-mount: enabled " + seededInteraction
                    + " interaction for mounted part " + partEntityId + ".");
            }

            Console.WriteLine("[info] part-mount: MOUNTED part " + partEntityId + " onto hull " + hullEntityId
                + " at hull-local " + localOffset + " (attached=true, Parent(hull,\"~\")). Mount #"
                + MountedParts.Count + " this session.");

            // PERSIST the mount so the part comes back ALREADY ATTACHED next boot. The ship is
            // referenced by its durable PERSISTENT INDEX (not the volatile hull id), and the
            // part's loose record is removed in the same breath so it is never both loose and
            // mounted in the save. A mount on a non-persisted ship (the static test hull has no
            // index) is session-only and simply not persisted.
            int? shipIndex = Crafting.BuiltShips.PersistentIndexFor(hullEntityId);
            string? partUid = Crafting.LooseParts.PartUidFor(partEntityId);
            if (shipIndex.HasValue && !string.IsNullOrEmpty(partUid))
            {
                Persistence.WorldStatePersistence.RecordMountedPart(new Multiplayer.Persistence.MountedPartRecord
                {
                    PartUid = partUid!,
                    BuiltShipIndex = shipIndex.Value,
                    SchematicId = def?.SchematicId ?? "",
                    ItemType = def?.ItemType ?? "",
                    Title = def?.Title ?? "",
                    PrefabName = def?.PrefabName ?? "",
                    AttachmentType = def?.AttachmentType ?? "",
                    PartSpecificComponents = def != null
                        ? System.Linq.Enumerable.ToArray(def.PartSpecificComponents)
                        : System.Array.Empty<uint>(),
                    LocalX = localOffset.X,
                    LocalY = localOffset.Y,
                    LocalZ = localOffset.Z,
                    PackedRotation = packedShipLocalRotation,
                    OwnerCharacterUid = ownerCharacterUid ?? "",
                });
                Persistence.WorldStatePersistence.RemoveLoosePart(partUid!);
            }
            else
            {
                Console.WriteLine("[info] part-mount: mount of part " + partEntityId + " onto hull " + hullEntityId
                    + " is session-only (ship has no persistent index or the part has no PartUid); not persisted.");
            }
        }
    }
}

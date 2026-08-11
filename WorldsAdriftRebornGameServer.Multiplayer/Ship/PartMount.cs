namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// Why a <c>1070 PlacePart</c> was refused, or <see cref="Accept"/>. The pure
    /// mirror of the client's own <c>DeployItem</c>/<c>AttachToShip</c> gate: a
    /// modified or replayed client can send a <c>PlacePart</c> that its own UI would
    /// never have produced, so the server re-checks every precondition. See
    /// findings-part-mount-spec.md section 2 for the client check each row mirrors.
    /// </summary>
    public enum PartMountReject
    {
        /// <summary>Every check passed; commit the mount.</summary>
        Accept,

        /// <summary>The player is not the owner of the entity that sent the event (rule 6).</summary>
        NotOwner,

        /// <summary>The server has no record of this player carrying a part (no pickup seen).</summary>
        NoCarriedPart,

        /// <summary>The carried entity is not a mountable loose part, or is already mounted.</summary>
        PartNotMountable,

        /// <summary>The named ship root is not a built (docked) ship this server spawned.</summary>
        ShipNotBuilt,

        /// <summary>The target surface is not a Unity child of the named ship root.</summary>
        TargetNotChildOfShip,
    }

    /// <summary>
    /// The PURE decisions of the part-mount flow, kept engine-free so the validation
    /// order and the ship-local -&gt; fixed-point offset conversion are asserted in
    /// unit tests rather than on a running client. The impure half - resolving the
    /// carried part, reading the ledgers, writing the component value-updates - lives
    /// in <c>Game.PartMountService</c>.
    ///
    /// MULTIPLAYER CLASSIFICATION. The mount is EVENT-DRIVEN: one <c>1070 PlacePart</c>
    /// in, three component VALUE-UPDATES out (8066 membership, 190602 hull-relative
    /// transform, 1120 attached). The part then RIDES the hull via the same
    /// <c>Parent(shipId,"~")</c> follow the bolted parts use - NO per-part world
    /// position stream is ever published. See <see cref="BoltedPartTransform"/>.
    /// </summary>
    public static class PartMount
    {
        /// <summary>
        /// The server-side gate for a <c>PlacePart</c>, evaluated in the same order
        /// the client applies its own checks so the first thing wrong is the thing
        /// reported. Each argument is a fact the impure caller resolves from the
        /// ledgers/registry:
        /// <list type="bullet">
        ///   <item><paramref name="ownsPlayerEntity"/> - the event rode the sender's OWN player entity.</item>
        ///   <item><paramref name="hasCarriedPart"/> - the server saw this player pick a part up (1239 PickedUpEntityEvent) and has not seen it dropped.</item>
        ///   <item><paramref name="partIsMountable"/> - the carried entity is a loose crafted part and is not already mounted.</item>
        ///   <item><paramref name="shipIsBuilt"/> - the named <c>shipId</c> is a built ship hull this server spawned.</item>
        ///   <item><paramref name="targetIsChildOfShip"/> - the named <c>parentId</c> surface resolves (built-deck-of-ship, or the hull itself) to <c>shipId</c>.</item>
        /// </list>
        /// </summary>
        public static PartMountReject EvaluatePlace(
            bool ownsPlayerEntity,
            bool hasCarriedPart,
            bool partIsMountable,
            bool shipIsBuilt,
            bool targetIsChildOfShip)
        {
            if (!ownsPlayerEntity) return PartMountReject.NotOwner;
            if (!hasCarriedPart) return PartMountReject.NoCarriedPart;
            if (!partIsMountable) return PartMountReject.PartNotMountable;
            if (!shipIsBuilt) return PartMountReject.ShipNotBuilt;
            if (!targetIsChildOfShip) return PartMountReject.TargetNotChildOfShip;
            return PartMountReject.Accept;
        }

        /// <summary>
        /// The mounted part's LOCAL offset from the hull, in fixed-point units, from
        /// the client's <c>PlacePart.shipLocalPosition</c> - which the client already
        /// computed hull-relative (<c>ship.transform.InverseTransformPoint(...)</c>),
        /// in Unity METRES. This drops straight into the 190602 <c>Parent(hullId,"~")</c>
        /// transform with no extra maths, exactly as <see cref="BoltedPartTransform.LocalOffset"/>
        /// does for a statically-bolted part - only here the offset is the player's
        /// chosen placement rather than a registration constant.
        /// </summary>
        public static FixedPointPosition ShipLocalOffset(float shipLocalX, float shipLocalY, float shipLocalZ)
        {
            return FixedPointPosition.FromMetres(shipLocalX, shipLocalY, shipLocalZ);
        }
    }
}

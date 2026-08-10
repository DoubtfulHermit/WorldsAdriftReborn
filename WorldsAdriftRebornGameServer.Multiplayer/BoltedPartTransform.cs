namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The transform policy for a ship part BOLTED onto the moving hull - the deck,
    /// the helm, and the opt-in engine and sail.
    ///
    /// THE BUG THIS FIXES. Each bolted part is its own world entity. It used to be
    /// seeded with an ABSOLUTE world position (parent absent), so it stood still in
    /// world space while the hull's <c>PathFollower</c> micro-adjusted the hull
    /// every frame: the two drifted apart and the player fell THROUGH the solid deck
    /// onto the hull's trigger-only virtual deck.
    ///
    /// THE FIX. Seed the part's 190602 <c>TransformState</c> RELATIVE to the hull:
    /// <c>parent = Parent(hullId, "~")</c> and <c>localPosition</c> = the part's
    /// offset FROM the hull. The client's <c>FixedUpdateLerpLocalTransformBehaviour</c>
    /// resolves the <c>"~"</c> key (VERIFIED, <c>GetRelativeEntity</c>,
    /// FixedUpdateLerpLocalTransformBehaviour.cs:444) and every FixedUpdate composes
    /// the hull's <c>NextFramePosition</c> (from the hull's
    /// <c>SSPDeadReckoningVisualizer</c>) with this local offset
    /// (FixedUpdateLerpLocalTransformBehaviour.cs:365), then origin-remaps the result
    /// to Unity space (MoveTransform, :251 - a "~" parent is NOT a Unity re-parent, so
    /// the part needs no GameObject hierarchy change). The part therefore tracks the
    /// hull's live position exactly and can no longer drift.
    ///
    /// This module is the PURE half: given the two GLOBAL seeds it yields the LOCAL
    /// offset the client needs. The runtime half - resolving the live hull entity id -
    /// stays in <c>ComponentsSerializer</c>, because entity ids are allocated at spawn
    /// time and cannot be exercised in a unit test.
    /// </summary>
    public static class BoltedPartTransform
    {
        /// <summary>
        /// The part's LOCAL offset from the hull, in fixed-point units: a plain
        /// component-wise subtraction of the hull's global seed from the part's global
        /// seed.
        ///
        /// Because every part's global seed IS the hull position plus a CONSTANT offset
        /// (<see cref="Deck.OnHull"/>, <see cref="Helm.OnDeckOf"/>,
        /// <see cref="ShipParts.EngineOnHull"/> / <see cref="ShipParts.SailOnHull"/>),
        /// this offset does not depend on where the hull is - which is exactly why the
        /// client can compose it with the hull's LIVE position - and
        /// <c>(this local offset) + hull == the part's original global seed</c> by
        /// construction, so the parented seed places the part in the same spot the old
        /// world-absolute seed did, only now it MOVES with the hull.
        /// </summary>
        public static FixedPointPosition LocalOffset(FixedPointPosition partGlobal, FixedPointPosition hullGlobal)
        {
            return new FixedPointPosition(
                partGlobal.X - hullGlobal.X,
                partGlobal.Y - hullGlobal.Y,
                partGlobal.Z - hullGlobal.Z);
        }

        /// <summary>
        /// The local offset for a bolted-part KEY, derived from the very same
        /// <c>OnHull</c>/<c>OnDeckOf</c> offset functions the registrations use, or
        /// <c>null</c> if the key is not a bolted part.
        ///
        /// Kept beside <see cref="LocalOffset"/> so a test can assert the two agree for
        /// every part - i.e. that the registry's global part positions and the
        /// hull-relative offsets the client is seeded can never fall out of step. The
        /// serializer itself uses the plain <see cref="LocalOffset"/> subtraction of
        /// the two registrations' positions, which is offset-function-agnostic and so
        /// keeps working for any part added later without a new branch here.
        /// </summary>
        public static FixedPointPosition? LocalOffsetFor(string? key, FixedPointPosition hull)
        {
            if (key == WorldEntities.DeckKey)   return LocalOffset(Deck.OnHull(hull), hull);
            if (key == WorldEntities.HelmKey)   return LocalOffset(Helm.OnDeckOf(hull), hull);
            if (key == WorldEntities.EngineKey) return LocalOffset(ShipParts.EngineOnHull(hull), hull);
            if (key == WorldEntities.SailKey)   return LocalOffset(ShipParts.SailOnHull(hull), hull);
            return null;
        }

        /// <summary>
        /// The 190602 <c>TransformState.parent</c> hierarchy KEY to seed for a bolted
        /// part: <see cref="Deck.HierarchyKey"/> for the walkable DECK (a real,
        /// non-<c>"~"</c> key that makes it a Unity CHILD of the hull so the ground
        /// raycast climbs to the hull's <c>PathFollower</c> and the player is carried),
        /// and the relative slot <c>"~"</c> for every other part (helm, engine, sail),
        /// which only need to position-FOLLOW the hull and must keep their own rigidbody.
        ///
        /// This is the ONE place that decision lives, so the seed
        /// (<c>ComponentsSerializer</c>) and the wake filter
        /// (<see cref="ShipPartMotionService"/>) can never disagree about which parts are
        /// real Unity children. A non-part key gets <c>"~"</c> too - harmless, since only
        /// bolted parts are ever seeded with a parent at all.
        /// </summary>
        public const string RelativeSlotKey = "~";

        public static string HierarchyKeyFor(string? partKey)
        {
            return partKey == WorldEntities.DeckKey ? Deck.HierarchyKey : RelativeSlotKey;
        }

        /// <summary>
        /// True when a bolted part is seeded as a REAL Unity child of the hull (a
        /// non-<c>"~"</c> key), i.e. the DECK. Such a part is dragged along by the hull's
        /// transform through the Unity hierarchy and MUST be excluded from the
        /// <see cref="ShipPartMotionService"/> wake heartbeat: re-sending its
        /// <c>parent</c> field every heartbeat would re-fire the client's
        /// <c>ParentUpdated</c> and churn an unparent+reparent (rigidbody destroyed and
        /// re-added) twice a second. A <c>"~"</c> follower, by contrast, needs the wake
        /// to stay awake and keep tracking.
        /// </summary>
        public static bool IsUnityChild(string? partKey)
        {
            return HierarchyKeyFor(partKey) != RelativeSlotKey;
        }
    }
}

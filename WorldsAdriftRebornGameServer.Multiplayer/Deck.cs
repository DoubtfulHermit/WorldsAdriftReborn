using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The one ship PART that turns a bare hull into a ship you can STAND on: a
    /// <c>Deck01</c> whose <see cref="LocalVertices"/> the client turns into a
    /// SOLID floor collider. Like <see cref="Helm"/> and <see cref="ShipHull"/>
    /// this is a bag of VALUES kept out of the component serializer so each can be
    /// asserted on natively.
    ///
    /// WHY A DECK PART AT ALL. A bare <c>ShipFrame</c> is a solid BEAM skeleton
    /// with NO floor between the beams: <c>MeshGenerator.MakeVirtualDeck</c> builds
    /// the hull's own deck plane and then flips its collider to
    /// <c>isTrigger = true</c> (VERIFIED, MeshGenerator.cs:379-383), so a player
    /// standing between beams has nothing to stand on. The walkable floor is a
    /// SEPARATE <c>Deck01</c> entity.
    ///
    /// THE SOLID-FLOOR MECHANISM (VERIFIED, ilspycmd on Assembly-CSharp.dll):
    ///   * <c>ShipDeckVisualizer</c> [Require]s only <c>ShipDeckState</c> (1518) and
    ///     <c>SalvageAndRepairState</c> (1099). When both arrive it enables and its
    ///     <c>OnVerticesUpdated</c> calls
    ///     <c>MeshGenerator.MakeDeck(transform, deckProto, 2f, ShipDeck(vertices),
    ///     GetMaterial(), withSides: true, physicMaterial, isTriggerCollider:
    ///     (bool)_phantom)</c>. For a server-spawned (non-phantom) deck
    ///     <c>_phantom</c> is null, so <c>(bool)_phantom == false</c> and the last
    ///     argument is <c>isTriggerCollider: false</c>.
    ///   * Inside <c>MakeDeck</c> that flag lands on a REAL collider:
    ///     <c>IsRectangular</c> vertices get a <c>BoxCollider</c> sized to the mesh
    ///     bounds, others a convex <c>MeshCollider</c>; either way
    ///     <c>collider.isTrigger = isTriggerCollider = false</c>
    ///     (VERIFIED, MeshGenerator.cs:387-418). This is the game's OWN deck-build
    ///     path - the exact code that makes crafted-ship decks walkable - not a
    ///     collider we synthesise, which is why the floor is genuinely solid.
    ///
    /// The hull's virtual deck and this part's deck run the SAME <c>MakeDeck</c>;
    /// the only difference is the hull re-flips its collider to a trigger afterwards
    /// and the part does not. That single fact is the whole deliverable.
    /// </summary>
    public static class Deck
    {
        /// <summary>
        /// The bare prefab name. The client appends its own worker suffix (so
        /// "Deck01", never "Deck01_unityclient"). It resolves without an island
        /// manifest for the same reason <see cref="Helm.AssetName"/> and
        /// <see cref="WorldEntities.ShipFrameAssetName"/> do: ship-part prefabs are
        /// baked into the always-resident resources.assets and the client's
        /// dispatch ignores prefab CONTEXT for every name not starting with
        /// Traveller/ModalErrorPopup/Spectator.
        /// </summary>
        public const string AssetName = "Deck01";

        /// <summary>The deck's registration key. One deck, one ship, for now.</summary>
        public const string Key = "deck-haven";

        /// <summary>
        /// The 190602 <c>TransformState.parent</c> HIERARCHY KEY that makes the deck a
        /// REAL Unity child of the hull GameObject - the whole carry fix.
        ///
        /// WHY NOT <c>"~"</c>. Every other bolted part is seeded with the relative slot
        /// <c>"~"</c>, which is a POSITION-follow, NOT a Unity re-parent: the client's
        /// <c>RelativeParentTransformChildHierarchyBehaviour.TrySetNewParent</c> treats
        /// <c>"~"</c> as "stay unparented, just track" (VERIFIED,
        /// RelativeParentTransformChildHierarchyBehaviour.cs:35-45), and
        /// <c>TransformManageRigidbodyBehaviour</c> coerces a <c>"~"</c> parent to "no
        /// parent" so it KEEPS the part's own kinematic rigidbody (VERIFIED,
        /// TransformManageRigidbodyBehaviour.cs:180-182). That is exactly why a player on
        /// the current <c>"~"</c> deck reports <c>relativeTo = the deck</c> (its own
        /// rigidbody is what the ground-raycast hits) yet is never carried: the deck has
        /// no <c>PathFollower</c>, and the client only arms the carry off a
        /// <c>PathFollower</c> on the SAME object it stands on
        /// (<c>ClientAuthoritativePlayerMovement.RelativeGameObject</c> setter guards
        /// <c>LocalRelativeGroundObject == value</c>, VERIFIED :84-104).
        ///
        /// WHAT A NON-<c>"~"</c> KEY DOES. A real key sends the deck down the game's own
        /// bolted-part path:
        ///   * <c>RelativeParentTransformChildHierarchyBehaviour.TrySetNewParent</c> ->
        ///     <c>base.TrySetNewParent</c> -> <c>TransformChildHierarchyBehaviour</c>
        ///     sets <c>CachedTransform.parent = hull offset</c> - a REAL Unity re-parent
        ///     under the hull (VERIFIED, TransformChildHierarchyBehaviour.cs:195-201). The
        ///     key is a PLAIN word (no leading <c>#</c>), so it is NOT a registered
        ///     <c>TransformOffsetsRegistry</c> slot and <c>GetTransformOffset</c> falls
        ///     back to the hull ROOT transform (VERIFIED,
        ///     TransformParentHierarchyBehaviour.cs:59-66) - the deck parents directly
        ///     under the hull, at this part's <c>localPosition</c> (its offset from the
        ///     hull, a flat (0,0,0) since the deck is hull-centred).
        ///   * <c>TransformManageRigidbodyBehaviour</c> sees a real parent transition and
        ///     <c>Object.Destroy</c>s the deck's own rigidbody (VERIFIED,
        ///     TransformManageRigidbodyBehaviour.cs:184-192, 222-241). With no rigidbody
        ///     of its own, the deck collider's <c>attachedRigidbody</c> now climbs the
        ///     Unity hierarchy to the HULL's kinematic rigidbody.
        /// So a player standing on the deck raycasts the hull's rigidbody
        /// (<c>PlayerMove.standingOnObject = raycastHit.rigidbody</c>, VERIFIED
        /// PlayerMove.cs:603,2333), <c>GetGroundedObject</c> returns the HULL
        /// (ClientAuthoritativePlayerMovement.cs:336-338), the server echoes
        /// <c>relativeTo = hull</c>, and the hull's existing <c>PathFollower</c> - the one
        /// that ALREADY carries a player standing on the bare beams - carries the player,
        /// now on a WIDE stable floor. The deck rides the moving hull for free as its
        /// Unity child.
        ///
        /// ASSUMPTIONS that only a live client can settle (all prefab-baked, invisible to
        /// the decompiled C#): the Deck01 prefab's authored TransformNature has
        /// <c>GameObjectCanBeParented = true</c> (so it carries the child-hierarchy
        /// behaviour) and <c>ShouldRemoveRigidbodyOnParented = true</c> (so its rigidbody
        /// is destroyed on parent); the ShipFrame prefab has
        /// <c>GameObjectCanBeParent = true</c> and serves 190601 TransformHierarchyState
        /// (so it carries the parent-hierarchy behaviour + offsets registry). If the deck
        /// keeps its rigidbody when parented, the raycast stops at the deck and the
        /// fallback is to give the deck its OWN PathFollower via a 1130.
        ///
        /// Value is a plain, ship-agnostic word. It never collides with a "#"-prefixed
        /// offset slot, so it always resolves to the hull root.
        /// </summary>
        public const string HierarchyKey = "deck";

        // ------------------------------------------------------------------
        // The 1518 ShipDeckState vertices - the deck polygon.
        //
        // VERIFIED (ilspycmd): ShipDeckState.Data(Improbable.Collections.List<Vector3f>
        // vertices); the reader's VerticesUpdated hands ShipDeckVisualizer a
        // List<Vector3f> which it feeds straight to MeshGenerator.MakeDeck.
        // ------------------------------------------------------------------

        /// <summary>
        /// The deck polygon, in the deck entity's LOCAL space and BEFORE the
        /// client's fixed <c>ShipScale = 2</c> (which <c>MeshGenerator.MakeMesh</c>
        /// applies as <c>vertex * scale</c>). Four corners of a rectangle CENTRED on
        /// the entity origin: x in [-3, +3], z in [-1, +1], y = 0.
        ///
        /// AT SCALE 2 that is a 12 m (port-starboard) x 4 m (fore-aft) floor at the
        /// entity's own y = 0. Those are the one-cell hull's own dimensions:
        /// <see cref="ShipHull.MinimumHullData"/> decodes to a single stock
        /// <c>ShipSection</c> whose vertices sit at x = +/-3.02 m (VERIFIED by
        /// decoding the 39-byte blob: sbyte +/-24 over range 16 = +/-3.02), and the
        /// findings-first-ship measurement of the frame is "12 m across, 4 m
        /// fore-to-aft". CENTRED on the origin so the deck entity's own 190602
        /// position IS the floor's centre, which is the same frame
        /// <see cref="Helm.DeckForwardMetres"/> measures its +1 m from.
        ///
        /// FOUR RECTANGULAR corners on purpose: <c>MeshGenerator.IsRectangular</c>
        /// (VERIFIED, MeshGenerator.cs:421-432) then picks the <c>BoxCollider</c>
        /// branch, a solid axis-aligned box sized to the mesh bounds - the most
        /// robust "stand on this" collider there is. Winding does not matter to the
        /// collider; it only flips the +/-0.04 m mesh extrude direction.
        ///
        /// ASSUMPTION on the 4 m fore-aft half-depth (z = +/-1 pre-scale): the raw
        /// hull blob carries z = 0 on every section vertex, so the cell's fore-aft
        /// span is applied by ShipPlan cell placement, not read here. +/-2 m at
        /// scale matches both the findings measurement and the helm's "+/-2 m stays
        /// on the deck" note. A deck a little larger than the beams still reads as a
        /// floor and is still walkable; if a live client shows it over/undershooting
        /// the beams, this is the one array to nudge.
        /// </summary>
        public static readonly IReadOnlyList<(double X, double Y, double Z)> LocalVertices =
            new (double, double, double)[]
            {
                (-3.0, 0.0, -1.0),
                ( 3.0, 0.0, -1.0),
                ( 3.0, 0.0,  1.0),
                (-3.0, 0.0,  1.0),
            };

        // ------------------------------------------------------------------
        // The 1099 SalvageAndRepairState material - NON-EMPTY, unlike the hull.
        //
        // The hull deliberately ships an EMPTY originalMaterials list (findings:
        // an invented material id would NRE ComponentMaterialColors). A DECK CANNOT:
        // ShipDeckVisualizer.OnEnable reads _salvageable.OriginalMaterials[0]
        // .rawMaterial.category (VERIFIED, ShipDeckVisualizer.cs:60) and GetMaterial()
        // reads [0].rawMaterial.materialTypeId - an empty list is an
        // IndexOutOfRangeException in OnEnable and the deck never builds. So the
        // deck's 1099 MUST carry exactly one material.
        // ------------------------------------------------------------------

        /// <summary>
        /// The 1099 material category. "Wood" or "Metal" (VERIFIED,
        /// ShipDeckVisualizer.cs:61) selects the wooden vs metal deck prototype; any
        /// other string is a logged error and the deck still builds off the Awake
        /// default. Wood, because the starter ship is wooden.
        ///
        /// ASSUMPTION: that <c>_woodenDeckProto</c> is assigned on the shipped
        /// Deck01 prefab. If a live client logs "No deck proto" / instantiates null,
        /// flip this to "Metal" (the metal beam prototype is definitely present -
        /// MeshGenerator uses <c>_metalBeam</c> unconditionally) - a one-word edit.
        /// </summary>
        public const string MaterialCategory = "Wood";

        /// <summary>
        /// The 1099 material name, resolved by
        /// <c>MaterialManager.MaterialDefinitionFromName</c>. "birch", matching
        /// <see cref="Trees.WoodType"/> so the server names one wood everywhere.
        ///
        /// SAFE even if it is not a real material name: MaterialDefinitionFromName
        /// returns <c>fallbackDefinition</c> (never null, no throw) for an unknown
        /// name (VERIFIED, MaterialManager.cs:109-118), so a wrong name is a
        /// slightly-off tint, not a crash. The collider - the thing that matters -
        /// is built before any material is resolved.
        /// </summary>
        public const string MaterialTypeId = Trees.WoodType;

        // ------------------------------------------------------------------
        // Where the deck sits, relative to the hull's own registration.
        //
        // The deck is a SEPARATE entity, but its 190602 is seeded hull-RELATIVE
        // (parent = Parent(hullId, "~"), see Multiplayer.BoltedPartTransform and the
        // 190602 branch in ComponentsSerializer), so the client tracks the moving
        // hull and the deck cannot drift out from under a player. OnHull below is the
        // GLOBAL position the deck WOULD sit at; the serializer subtracts the hull's
        // own seed from it to get the local offset the client actually receives, so
        // this arithmetic stays the single source of the deck's placement.
        // ------------------------------------------------------------------

        /// <summary>
        /// Metres the deck is raised above the hull's registration Y. ZERO, because
        /// the hull's deck plane is at the hull entity's own local y = 0
        /// (findings-first-ship, "The hull root") and <see cref="LocalVertices"/>
        /// are at y = 0 too, so the deck floor lands exactly on the hull's deck
        /// plane. Documented and separate so raising it is one edit if a live client
        /// shows it z-fighting the beams or sunk.
        /// </summary>
        public const double DeckUpMetres = 0.0;

        /// <summary>
        /// The deck's global 190602 seed: the hull's registration plus the up
        /// offset, in fixed point. X and Z are the hull's exactly, because
        /// <see cref="LocalVertices"/> are centred on the origin, so the deck's own
        /// centre coincides with the hull's centre. A pure function of the hull
        /// position so this and the hull stay locked together and the arithmetic is
        /// asserted in tests rather than pasted as literals.
        /// </summary>
        public static FixedPointPosition OnHull(FixedPointPosition hull)
        {
            return new FixedPointPosition(
                hull.X,
                hull.Y + (long)(DeckUpMetres * FixedPointPosition.UnitsPerMetre),
                hull.Z);
        }
    }
}

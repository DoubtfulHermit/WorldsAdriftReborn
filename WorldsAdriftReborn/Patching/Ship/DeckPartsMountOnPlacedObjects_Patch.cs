using System;
using Assets.Scripts.Visualisers.Ship;
using HarmonyLib;
using Improbable;
using Improbable.Unity.Entity;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Ship
{
    /// <summary>
    /// Lets a deck-mounted ship part be bolted to something ALREADY PLACED - a
    /// railing, a fence, a cupboard, a hull panel - instead of only to the bare deck.
    /// The reported symptom is "the altimeter can only go on the floor; I put a fence
    /// down and I want to place it on that".
    ///
    /// WHY THIS IS A CLIENT PATCH AND NOT SERVER REFDATA. The whole gate is
    /// client-side. <c>PlacementPreview.IsValidTarget</c> accepts a raycast hit only
    /// if <c>go.IsInLayerMask(GetCurrentMask()) &amp;&amp; (tag empty ||
    /// go.CompareTag(tag))</c>, and both come from one <c>PlacementLocationType</c>
    /// value that the server authors as a string on 1120. Our 23 deck rows send
    /// "deck" -&gt; <c>ShipDeck</c> -&gt; mask <c>ShipAttachmentSolid</c>, tag
    /// <c>"ShipDeck"</c>. A placed railing is on layer <c>Default</c> and is
    /// <c>Untagged</c> (read out of the shipped <c>resources.assets</c>: every
    /// <c>rail_*_straight_*</c> variant carries 4-5 enabled non-trigger Box/Capsule
    /// colliders, all layer 0, all tag 0), so it fails BOTH halves. The server cannot
    /// fix that: <c>Default</c> is only reachable through <c>Layers.Environment</c>,
    /// which does not include <c>ShipAttachmentSolid</c>, so any string we could send
    /// buys the fence and loses the deck.
    ///
    /// WHY NOT <c>PlacementLocationType.All</c>, which is the obvious answer and the
    /// one the roadmap sketched. Every behaviour switch on the preview compares the
    /// WHOLE flag value with <c>==</c>, not <c>&amp;</c>:
    /// <c>PlacingOnDeck</c> (:126), <c>NeedToBeOnShip</c> (:128),
    /// <c>PlacingOnSurface</c> (:130), <c>PlacingDeck</c> (:122),
    /// <c>PlacingCoreModule</c> (:124). Widening <c>ValidSurfaceTypes</c> to anything
    /// other than a single flag silently turns all five FALSE, which drops the deck
    /// flatness rule (:670), the ship-aligned base rotation (:757) and the
    /// requirement to be on a ship at all (:666) - so an altimeter could be stuck to
    /// the side of a cliff.
    ///
    /// SO THIS PATCH LEAVES <c>ValidSurfaceTypes</c> ALONE and widens only the two
    /// things that decide what the ray may HIT. Every downstream rule keeps running
    /// exactly as it does today:
    ///   * the surface must still be within 26 degrees of horizontal
    ///     (<c>Mathf.Abs(hitNormal.y) &lt; 0.9f</c>) - so this puts an instrument on
    ///     TOP of a rail or a cupboard, never on a vertical face;
    ///   * the hit must still resolve to a ship (<c>NeedToBeOnShip</c>), so nothing
    ///     becomes placeable on terrain;
    ///   * the pose is still the ship-aligned <c>PlacingOnDeck</c> rotation, so parts
    ///     stay square to the hull instead of twitching off a raw hit normal.
    ///
    /// SERVER SIDE IS ALREADY DONE. <c>PartMountService</c> accepts a mount whose
    /// parent is another mounted part, and <c>attachmentType</c> is unchanged - so an
    /// unpatched client keeps working exactly as before and a patched one is not
    /// sending anything new. There is no schema change and nothing to deploy
    /// alongside this.
    ///
    /// WAREBORN TUNING, said out loud: retail chose the mounting surface from a
    /// per-item server string and those values are unrecoverable (no item table ships
    /// in the client; it lived on the GSim). What is PROVED is that mounting on an
    /// already-placed object was retail's DEFAULT, because the opt-out is an explicit
    /// per-prefab marker component, <c>BlockItemPlacement</c> - a marker exists to
    /// suppress a behaviour, which is only worth writing if the behaviour was on.
    /// This patch restores that default for deck parts; the exact per-item table is
    /// ours to choose and this is our choice.
    /// </summary>
    internal static class DeckPartsMountOnPlacedObjects_Patch
    {
        /// <summary>
        /// Widens the placement raycast for deck parts to also hit Environment-layer
        /// geometry - which is where every placed prop's collider lives.
        ///
        /// <c>ShipAttachmentSolid</c> (the deck itself) is NOT removed, so this is
        /// purely additive: everything that could be aimed at before still can be.
        /// </summary>
        [HarmonyPatch(typeof(PlacementPreview), "GetCurrentMask")]
        internal static class WidenMask
        {
            private static void Postfix(PlacementPreview __instance, ref LayerMask __result)
            {
                try
                {
                    if (!AppliesTo(__instance))
                    {
                        return;
                    }

                    __result = __result | Layers.Environment;
                }
                catch (Exception exception)
                {
                    // Placement must stay usable whatever happens here: the unmodified
                    // mask is exactly retail's, so falling through costs the feature
                    // and nothing else.
                    Warn("mask widening skipped: " + exception.Message);
                }
            }
        }

        /// <summary>
        /// Drops the <c>"ShipDeck"</c> tag requirement for deck parts.
        ///
        /// THIS IS THE HALF THAT ACTUALLY UNBLOCKS THE FENCE, and it is the half a
        /// mask change alone would not: <c>GetTag</c> returns ONE tag and
        /// <c>IsValidTarget</c> applies it to EVERY hit, so a railing raycast on the
        /// widened mask would still be rejected for being <c>Untagged</c>.
        ///
        /// What the tag was protecting is not lost - it is enforced better downstream.
        /// The only untagged things on the widened mask that a deck-part ray can now
        /// reach are placed props and (via <c>ModularWing</c>'s runtime relayer) a
        /// wing's upper skin, and the &gt;=0.9 flatness gate still rejects every face
        /// of them that is not level.
        /// </summary>
        [HarmonyPatch(typeof(PlacementPreview), "GetCurrentTag")]
        internal static class ClearTag
        {
            private static void Postfix(PlacementPreview __instance, ref string __result)
            {
                try
                {
                    if (!AppliesTo(__instance))
                    {
                        return;
                    }

                    __result = string.Empty;
                }
                catch (Exception exception)
                {
                    Warn("tag clearing skipped: " + exception.Message);
                }
            }
        }

        /// <summary>
        /// ONLY plain deck parts, and only while a phantom is actually up.
        ///
        /// <c>ValidSurfaceTypes == ShipDeck</c> excludes, by construction, every case
        /// whose placement is a different geometry problem: hull panels and windows
        /// (<c>ShipSide</c>, handled by
        /// <see cref="ShipSidePanelExterior_Patch"/>), the deck plates themselves
        /// (<c>DeckGrid</c>), sky-core modules (<c>CoreModule</c>, which re-resolves to
        /// a named socket), engines and wings. Those are left bit-for-bit as they are.
        /// </summary>
        private static bool AppliesTo(PlacementPreview preview)
        {
            return preview != null
                && preview.Phantom != null
                && preview.ValidSurfaceTypes == PlacementLocationType.ShipDeck;
        }

        /// <summary>
        /// THE SECOND HALF, and without it the first half only gets you a BLUE
        /// phantom that refuses to place. Reported symptom: "I can now target the
        /// railing with the altimeter but I can't place it, it's blue."
        ///
        /// BLUE IS A SPECIFIC NEGATIVE, and it names the gate. The ship-part palette
        /// has three colours (<c>ShipPartPlacement.cs:22-29</c>), assigned at
        /// <c>PlayerScannerTool.cs:577</c>: green = <c>CanPlace</c>, faint red = not
        /// placeable and not droppable, and <c>DropHighlight</c> blue = "I will not
        /// bolt this to the ship, but I will free-drop it here". Blue requires
        /// <c>_canDrop</c> (<c>:524</c>), which requires <c>!flag4</c>, and
        /// <c>flag4</c> is <c>ShipPartPlacement.IsAttachedToShip(TargetObject)</c> -
        /// i.e. "does a <c>DockableVisualizer</c> exist above the thing I am aiming
        /// at". Every other candidate gate (flatness, overlap, distance,
        /// BlockItemPlacement) leaves <c>flag4</c> TRUE and therefore paints RED. So
        /// the colour is runtime proof that the parent walk failed, and 11.9's open
        /// question is answered: it does.
        ///
        /// WHY IT FAILS, and it is not a bug in the client. A mounted ship part is
        /// NOT a Unity child of the hull on this server, deliberately. We seed it
        /// <c>Parent(hull, "~")</c>, and
        /// <c>RelativeParentTransformChildHierarchyBehaviour.TrySetNewParent</c>
        /// treats the <c>"~"</c> key as <c>SetNoParent()</c> - the part is composed
        /// into world space every frame instead of being reparented. Only the DECK
        /// gets a real hierarchy key, which is exactly why a deck works as a
        /// placement surface and a railing does not. Every
        /// <c>GetComponentInParents&lt;DockableVisualizer&gt;()</c> is a plain
        /// <c>transform.parent</c> loop (<c>GameObjectX.cs:192-209</c>), so from a
        /// railing it can only ever return null.
        ///
        /// WHAT THIS DOES. When a deck-part ray lands on a mounted part that has no
        /// resolvable ship, it re-points <c>_targetObject</c> at the HULL that part's
        /// own <c>8066 ShipRootState</c> already names
        /// (<c>ShipPartVisualizer.ShipEntityId</c>, the link the server maintains and
        /// re-broadcasts on every mount). That is not an invented relationship: the
        /// part genuinely belongs to that hull, and the client's <c>"~"</c>
        /// convention simply declines to express it as a transform.
        ///
        /// AND IT IS ONE HOOK, NOT FOUR, because everything downstream reads
        /// <c>TargetObject</c>:
        ///   * <c>PlacementPreview.cs:664</c> then finds the hull's own
        ///     <c>DockableVisualizer</c> on the FIRST branch, so
        ///     <c>NeedToBeOnShip</c> is satisfied honestly rather than bypassed;
        ///   * <c>PositionOnShip</c> still poses from <c>info.hitPoint</c> and
        ///     <c>info.hitNormal</c> - the RAILING's surface - and only takes the
        ///     ship's forward axis from the hull, so the part lands where the player
        ///     aimed, square to the hull;
        ///   * the &gt;=0.9 flatness gate is untouched: it reads
        ///     <c>info.hitNormal</c>, which is still the railing's face. A vertical
        ///     face is still refused;
        ///   * <c>PlayerScannerTool</c>'s <c>flag4</c>/<c>flag5</c> and
        ///     <c>ShipPartPlacement.IsAttachedToShip(preview, DockedShip)</c> both
        ///     resolve to the docked hull, so ownership is checked, not skipped;
        ///   * <c>AttachToShip</c>'s <c>HasParentEntity</c> check (<c>:213</c>), which
        ///     is ALSO a Unity-hierarchy walk and would otherwise silently return
        ///     false after the preview turned green, now compares the hull with
        ///     itself and passes;
        ///   * the committed <c>Build</c> sends parent = the hull and a hull-local
        ///     pose, which is byte-identical in shape to what a deck mount already
        ///     sends. <c>PartMountService</c> needs no change and an unpatched client
        ///     is unaffected.
        ///
        /// The one thing re-pointing costs is the overlap exemption at
        /// <c>ShipPartPlacement.cs:175</c>, which exempts <c>TargetObject</c> - so the
        /// railing you are standing an instrument on would start counting as an
        /// obstruction. <see cref="ExemptTheSurfaceWeAimedAt"/> gives it back through
        /// the client's own <c>PlacementRules.CanOverlapWith</c> door.
        /// </summary>
        [HarmonyPatch(typeof(PlacementPreview), "UpdateTargetObject")]
        internal static class ResolveShipThroughShipRoot
        {
            private static void Postfix(PlacementPreview __instance, GameObject targetObj)
            {
                try
                {
                    // Cleared FIRST, every call. UpdatePhantomPosition calls this with
                    // null at the top of every frame's evaluation, so a redirect can
                    // never outlive the ray that caused it - which is the whole reason
                    // the overlap exemption below is safe to key on one field.
                    _redirectedFrom = EntityId.InvalidEntityId;

                    if (targetObj == null || !AppliesTo(__instance))
                    {
                        return;
                    }

                    GameObject resolved = __instance.TargetObject;
                    if (resolved == null || resolved.GetComponentInParents<DockableVisualizer>() != null)
                    {
                        // Already on a ship the client can see - the deck, or the hull
                        // itself. Leave retail's answer exactly as it is.
                        return;
                    }

                    ShipPartVisualizer part = resolved.GetComponent<ShipPartVisualizer>();
                    if (part == null)
                    {
                        // Not a ship part at all: terrain, a creature, a deployable.
                        // NeedToBeOnShip must keep refusing those.
                        return;
                    }

                    EntityId hullId = part.ShipEntityId;
                    if (!hullId.IsValid())
                    {
                        // A LOOSE part lying on the deck. 8066 says it belongs to no
                        // hull, which is the truth, so it is not a mounting surface.
                        return;
                    }

                    // Fully qualified: this namespace already has a Patching.SpatialOS.
                    IEntityObject hull = global::Improbable.Unity.Core.SpatialOS.Universe.Get(hullId);
                    GameObject hullObject = hull?.UnderlyingGameObject;
                    if (hullObject == null || hullObject.GetComponent<DockableVisualizer>() == null)
                    {
                        // The hull is not checked out on this client yet. Refusing is
                        // correct; a later frame will resolve it.
                        return;
                    }

                    _redirectedFrom = resolved.EntityId();
                    AccessTools.Field(typeof(PlacementPreview), "_targetObject")
                        .SetValue(__instance, hullObject);
                }
                catch (Exception exception)
                {
                    // Placement must stay usable whatever happens here: leaving
                    // _targetObject alone is exactly retail's behaviour, so falling
                    // through costs the feature and nothing else.
                    Warn("ship resolve skipped: " + exception.Message);
                }
            }
        }

        /// <summary>
        /// Gives back the one exemption <see cref="ResolveShipThroughShipRoot"/> takes
        /// away.
        ///
        /// <c>IsValidShipPlacement</c>'s overlap test ignores an obstructor whose
        /// entity id is <c>preview.TargetObject.EntityId()</c>
        /// (<c>ShipPartPlacement.cs:175</c>) - "you may of course overlap the thing
        /// you are placing ON". Re-pointing <c>TargetObject</c> at the hull moves that
        /// exemption onto the hull, and an instrument standing on a railing overlaps
        /// the railing by construction, so without this the preview would go from blue
        /// to red instead of blue to green.
        ///
        /// It is returned through <c>PlacementRules.CanOverlapWith</c> - the client's
        /// own per-prefab overlap door, already consulted in the same predicate
        /// (<c>flag5</c>) - rather than by widening the overlap test itself, so
        /// nothing else about overlapping is relaxed. Exactly one entity is exempted:
        /// the surface the current ray landed on.
        /// </summary>
        [HarmonyPatch(typeof(PlacementRules), "CanOverlapWith")]
        internal static class ExemptTheSurfaceWeAimedAt
        {
            private static void Postfix(IEntityObject obstructor, ref bool __result)
            {
                try
                {
                    if (__result || obstructor == null || !_redirectedFrom.IsValid())
                    {
                        return;
                    }
                    __result = obstructor.EntityId == _redirectedFrom;
                }
                catch (Exception exception)
                {
                    Warn("overlap exemption skipped: " + exception.Message);
                }
            }
        }

        /// <summary>
        /// The entity the current ray landed on, when it was a mounted part we
        /// re-pointed away from. Rewritten every time the preview updates its target
        /// and never read outside one frame's placement evaluation, so it cannot
        /// exempt an object the player is no longer aiming at.
        /// </summary>
        private static EntityId _redirectedFrom = EntityId.InvalidEntityId;

        private static float _nextLogAt;

        private static void Warn(string message)
        {
            if (Time.realtimeSinceStartup < _nextLogAt)
            {
                return;
            }
            _nextLogAt = Time.realtimeSinceStartup + 2f;
            Debug.LogWarning("[WAR][deck-mount] " + message);
        }
    }
}

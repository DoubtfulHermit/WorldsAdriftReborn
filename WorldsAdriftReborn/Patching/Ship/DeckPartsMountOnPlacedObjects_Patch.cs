using System;
using HarmonyLib;
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

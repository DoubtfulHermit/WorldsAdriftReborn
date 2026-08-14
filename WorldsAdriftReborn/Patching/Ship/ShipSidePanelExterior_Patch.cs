using System;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Ship
{
    /// <summary>
    /// Makes panels and windows attach to the OUTSIDE skin of a generated hull.
    ///
    /// Retail's generic ShipSide path uses the first ShipAttachable hit.  A custom
    /// frame exposes all of its ribs and cross-members on that layer, so a player
    /// aiming from below/inside can hit an internal beam first.  PositionOnShip then
    /// quite correctly centres the panel on that beam, leaving the boards running
    /// through the cage instead of covering its exterior.
    ///
    /// The frame already owns the exact answer: ShipSideHull's SRC mesh.  For panel
    /// phantoms only, recast at the selected height/longitudinal position from well
    /// outside the chosen port/starboard side back toward the centre.  The first SRC
    /// hit is the tapered/curved exterior surface.  No ship width or panel-specific
    /// offset is invented, and engines/wings keep retail's separate placement rules.
    /// </summary>
    [HarmonyPatch(typeof(PlacementPreview), "PositionOnShip")]
    internal static class ShipSidePanelExterior_Patch
    {
        // ShipEditorConstants permits a raw half-width of 8 m and the rendered ship
        // scale is 2, so 24 m is safely outside every legal frame while remaining a
        // short, deterministic ray.
        private const float OutsideDistanceMetres = 24f;
        private const float RayLengthMetres = 48f;
        private static float _nextLogAt;

        private static void Prefix(
            PlacementPreview __instance,
            GameObject ship,
            ref Vector3 hitPoint,
            ref Vector3 hitNormal,
            ref Transform hitTransform)
        {
            try
            {
                if (__instance == null || ship == null || __instance.Phantom == null
                    || __instance.ValidSurfaceTypes != PlacementLocationType.ShipSide
                    || !IsPanel(__instance.Phantom))
                {
                    return;
                }

                ShipSideHull sideHull = ship.GetComponentInChildren<ShipSideHull>(true);
                SRCMesh sideMesh = sideHull == null ? null : sideHull.GetComponent<SRCMesh>();
                if (sideMesh == null)
                {
                    return;
                }

                Vector3 localHit = ship.transform.InverseTransformPoint(hitPoint);
                Vector3 localNormal = ship.transform.InverseTransformDirection(hitNormal).normalized;
                Vector3[] outwardAxes =
                {
                    Vector3.up, Vector3.down,
                    Vector3.right, Vector3.left,
                    Vector3.forward, Vector3.back
                };

                DRCHitInfo exteriorHit = null;
                Vector3 chosenOutward = Vector3.zero;
                float bestScore = float.NegativeInfinity;
                for (int i = 0; i < outwardAxes.Length; i++)
                {
                    Vector3 outward = outwardAxes[i];
                    Vector3 outsideLocal = localHit + outward * OutsideDistanceMetres;
                    Ray exteriorRay = new Ray(
                        ship.transform.TransformPoint(outsideLocal),
                        ship.transform.TransformDirection(-outward));

                    DRCHitInfo candidate = new DRCHitInfo();
                    if (!sideMesh.RayCast(new DRCRay(exteriorRay), ref candidate)
                        || candidate.hitDistance > RayLengthMetres)
                    {
                        continue;
                    }

                    // Pick the exterior FACE represented by the beam the player aimed
                    // at, not merely the nearest triangle. Absolute normal alignment
                    // chooses top/side/end independent of whether the camera struck the
                    // inside or outside face. For a horizontal beam both +/-Y align;
                    // +Y deliberately wins because a hull covering belongs ABOVE the
                    // frame, never hanging underneath it. Lateral/end ties choose the
                    // port/starboard or bow/stern half containing the aimed point.
                    float alignment = Math.Abs(Vector3.Dot(localNormal, outward));
                    float outwardPreference = 0f;
                    if (outward == Vector3.up)
                    {
                        outwardPreference = 12f;
                    }
                    else if (outward == Vector3.right && localHit.x >= 0f
                        || outward == Vector3.left && localHit.x < 0f
                        || outward == Vector3.forward && localHit.z >= 0f
                        || outward == Vector3.back && localHit.z < 0f)
                    {
                        outwardPreference = 8f;
                    }
                    float distance = Vector3.Distance(hitPoint, candidate.hitPoint);
                    float score = alignment * 100f + outwardPreference - distance * 0.1f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        exteriorHit = candidate;
                        chosenOutward = outward;
                    }
                }

                if (exteriorHit == null)
                {
                    return;
                }

                float correction = Vector3.Distance(hitPoint, exteriorHit.hitPoint);
                hitPoint = exteriorHit.hitPoint;
                hitNormal = exteriorHit.hitNormal;
                hitTransform = sideHull.transform;

                if (correction > 0.25f && Time.realtimeSinceStartup >= _nextLogAt)
                {
                    _nextLogAt = Time.realtimeSinceStartup + 2f;
                    Debug.Log("[WAR][ship-panel] snapped ShipSide preview "
                        + correction.ToString("F2")
                        + " m from hull-local " + localHit.ToString("F2")
                        + " to " + ship.transform.InverseTransformPoint(hitPoint).ToString("F2")
                        + " on exterior face " + chosenOutward.ToString("F0") + ".");
                }
            }
            catch (Exception exception)
            {
                // Placement must remain usable if an unusual retail hull has no
                // compatible SRC data. The original hit remains untouched on failure.
                if (Time.realtimeSinceStartup >= _nextLogAt)
                {
                    _nextLogAt = Time.realtimeSinceStartup + 2f;
                    Debug.LogWarning("[WAR][ship-panel] exterior snap fell back to retail placement: "
                        + exception.Message);
                }
            }
        }

        private static bool IsPanel(Assets.Scripts.PartPlacement.PhantomPart phantom)
        {
            // PhantomPart.Create notifies PhantomVisualizers before its delayed Init.
            // During that window the generated ShipPanel child is inactive, which is
            // precisely when PositionOnShip begins running. The ordinary no-argument
            // GetComponentInChildren silently misses it. Check inactive children and
            // the authoritative original carried entity as a second exact signal.
            if (phantom.GetComponentInChildren<ShipPanel>(true) != null)
            {
                return true;
            }
            return phantom.OriginalPart.HasValue
                && phantom.OriginalPart.Value != null
                && phantom.OriginalPart.Value.GetComponentInChildren<ShipPanel>(true) != null;
        }
    }
}

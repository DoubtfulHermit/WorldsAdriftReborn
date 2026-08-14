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
                float side = Math.Abs(localHit.x) > 0.05f ? Math.Sign(localHit.x) : 0f;
                if (side == 0f && CameraManager.MainCamera != null)
                {
                    side = Math.Sign(ship.transform.InverseTransformPoint(
                        CameraManager.MainCamera.transform.position).x);
                }
                if (side == 0f)
                {
                    side = Math.Sign(ship.transform.InverseTransformDirection(hitNormal).x);
                }
                if (side == 0f)
                {
                    side = 1f;
                }

                Vector3 outsideLocal = new Vector3(
                    side * OutsideDistanceMetres, localHit.y, localHit.z);
                Vector3 inwardLocal = new Vector3(-side, 0f, 0f);
                Ray exteriorRay = new Ray(
                    ship.transform.TransformPoint(outsideLocal),
                    ship.transform.TransformDirection(inwardLocal));

                DRCHitInfo exteriorHit = new DRCHitInfo();
                if (!sideMesh.RayCast(new DRCRay(exteriorRay), ref exteriorHit)
                    || exteriorHit.hitDistance > RayLengthMetres)
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
                        + " on the exterior hull skin.");
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

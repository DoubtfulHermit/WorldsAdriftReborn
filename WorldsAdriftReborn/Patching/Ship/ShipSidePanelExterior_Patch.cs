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
        // ShipPanel.CreatePanels creates a 0.10 m-thick, pivot-centred collider.
        // The attachment point is therefore not the visible inner face: leaving the
        // pivot on the hull surface embeds half of every panel in the frame. Move the
        // pivot out by 5 cm plus 1 cm of visual clearance.
        private const float PanelSkinClearanceMetres = 0.06f;
        private static float _nextLogAt;
        private static float _nextGeometryLogAt;
        private static Assets.Scripts.PartPlacement.PhantomPart _diagnosticPhantom;
        private static GameObject _diagnosticShip;
        private static Vector3 _diagnosticSkinPoint;
        private static Vector3 _diagnosticOutward;
        private static Vector3 _diagnosticFace;
        private static string _diagnosticMethod;

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

                // ShipSideHull's SRC contains the lateral skin but has holes where a
                // deck/roof panel belongs. A ray from above through one of those holes
                // returns no triangle and the old code fell back to the beam underneath.
                // A vertically struck rail is unambiguous: this is a covering panel, so
                // put its plane just ABOVE the rendered hull envelope and make ship-up
                // its normal. Bounds are measured live from this authored hull rather
                // than guessed from a particular blueprint's dimensions.
                if (Math.Abs(localNormal.y) >= Math.Abs(localNormal.x)
                    && Math.Abs(localNormal.y) >= Math.Abs(localNormal.z)
                    && TryProjectAboveHull(sideHull, ship.transform.up, hitPoint,
                        out Vector3 roofPoint))
                {
                    float roofCorrection = Vector3.Distance(hitPoint, roofPoint);
                    hitPoint = roofPoint;
                    hitNormal = ship.transform.up;
                    hitTransform = sideHull.transform;
                    SetDiagnostic(__instance.Phantom, ship,
                        roofPoint - ship.transform.up * PanelSkinClearanceMetres,
                        ship.transform.up, Vector3.up, "roof envelope");
                    LogSnap(ship, localHit, localNormal, hitPoint, Vector3.up,
                        roofCorrection, "roof envelope");
                    return;
                }

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
                    if (Time.realtimeSinceStartup >= _nextLogAt)
                    {
                        _nextLogAt = Time.realtimeSinceStartup + 2f;
                        Debug.LogWarning("[WAR][ship-panel] no exterior SRC hit for hull-local "
                            + localHit.ToString("F2") + " normal " + localNormal.ToString("F2")
                            + "; retaining the retail preview.");
                    }
                    return;
                }

                Vector3 surfaceOutward = exteriorHit.hitNormal.normalized;
                Vector3 worldChosenOutward = ship.transform.TransformDirection(chosenOutward);
                if (Vector3.Dot(surfaceOutward, worldChosenOutward) < 0f)
                {
                    surfaceOutward = -surfaceOutward;
                }

                Vector3 skinPoint = exteriorHit.hitPoint;
                Vector3 clearedPoint = skinPoint
                    + surfaceOutward * PanelSkinClearanceMetres;
                float correction = Vector3.Distance(hitPoint, clearedPoint);
                hitPoint = clearedPoint;
                hitNormal = surfaceOutward;
                hitTransform = sideHull.transform;
                SetDiagnostic(__instance.Phantom, ship, skinPoint, surfaceOutward,
                    chosenOutward, "SRC exterior");

                LogSnap(ship, localHit, localNormal, hitPoint, chosenOutward,
                    correction, "SRC exterior");
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

        private static bool TryProjectAboveHull(
            ShipSideHull sideHull,
            Vector3 shipUp,
            Vector3 hitPoint,
            out Vector3 roofPoint)
        {
            float topProjection = float.NegativeInfinity;
            Renderer[] renderers = sideHull.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    topProjection = Math.Max(topProjection,
                        MaxProjection(renderers[i].bounds, shipUp));
                }
            }
            Collider[] colliders = sideHull.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    topProjection = Math.Max(topProjection,
                        MaxProjection(colliders[i].bounds, shipUp));
                }
            }
            if (float.IsNegativeInfinity(topProjection))
            {
                roofPoint = hitPoint;
                return false;
            }

            float currentProjection = Vector3.Dot(hitPoint, shipUp);
            roofPoint = hitPoint + shipUp
                * (topProjection - currentProjection + PanelSkinClearanceMetres);
            return true;
        }

        private static float MaxProjection(Bounds bounds, Vector3 axis)
        {
            Vector3 extents = bounds.extents;
            return Vector3.Dot(bounds.center, axis)
                + Math.Abs(axis.x) * extents.x
                + Math.Abs(axis.y) * extents.y
                + Math.Abs(axis.z) * extents.z;
        }

        private static void LogSnap(
            GameObject ship,
            Vector3 localHit,
            Vector3 localNormal,
            Vector3 result,
            Vector3 face,
            float correction,
            string method)
        {
            if (Time.realtimeSinceStartup < _nextLogAt)
            {
                return;
            }
            _nextLogAt = Time.realtimeSinceStartup + 2f;
            Debug.Log("[WAR][ship-panel] " + method + " moved preview "
                + correction.ToString("F2") + " m from hull-local "
                + localHit.ToString("F2") + " normal " + localNormal.ToString("F2")
                + " to " + ship.transform.InverseTransformPoint(result).ToString("F2")
                + " on face " + face.ToString("F0")
                + "; pivot clearance "
                + PanelSkinClearanceMetres.ToString("F2") + " m.");
        }

        private static void SetDiagnostic(
            Assets.Scripts.PartPlacement.PhantomPart phantom,
            GameObject ship,
            Vector3 skinPoint,
            Vector3 outward,
            Vector3 face,
            string method)
        {
            _diagnosticPhantom = phantom;
            _diagnosticShip = ship;
            _diagnosticSkinPoint = skinPoint;
            _diagnosticOutward = outward.normalized;
            _diagnosticFace = face;
            _diagnosticMethod = method;
        }

        /// <summary>
        /// Logs the geometry AFTER PhantomPart.Update has applied the placement pose.
        /// This makes the next visual report actionable: negative inner-face values
        /// prove that rendered/collision geometry still penetrates the selected hull
        /// skin, while zero or positive values prove the whole panel is outside it.
        /// </summary>
        [HarmonyPatch(typeof(Assets.Scripts.PartPlacement.PhantomPart), "Update")]
        private static class PhantomGeometryDiagnostics
        {
            private static void Postfix(Assets.Scripts.PartPlacement.PhantomPart __instance)
            {
                if (__instance == null || __instance != _diagnosticPhantom
                    || _diagnosticShip == null
                    || Time.realtimeSinceStartup < _nextGeometryLogAt)
                {
                    return;
                }

                _nextGeometryLogAt = Time.realtimeSinceStartup + 2f;
                float skinProjection = Vector3.Dot(_diagnosticSkinPoint,
                    _diagnosticOutward);
                float pivotFromSkin = Vector3.Dot(__instance.transform.position,
                    _diagnosticOutward) - skinProjection;

                ShipPanel panel = __instance.GetComponentInChildren<ShipPanel>(true);
                string rendererRange = RangeFromSkin(
                    panel == null ? null : panel.GetComponentsInChildren<Renderer>(true),
                    skinProjection, _diagnosticOutward);
                string colliderRange = RangeFromSkin(
                    panel == null ? null : panel.GetComponentsInChildren<Collider>(true),
                    skinProjection, _diagnosticOutward);

                Debug.Log("[WAR][ship-panel][geometry] " + _diagnosticMethod
                    + " face " + _diagnosticFace.ToString("F0")
                    + " skinLocal "
                    + _diagnosticShip.transform.InverseTransformPoint(
                        _diagnosticSkinPoint).ToString("F3")
                    + " pivotLocal "
                    + _diagnosticShip.transform.InverseTransformPoint(
                        __instance.transform.position).ToString("F3")
                    + " pivotFromSkin " + pivotFromSkin.ToString("F3") + " m"
                    + " rendererFromSkin " + rendererRange
                    + " colliderFromSkin " + colliderRange
                    + ". Negative minima mean penetration.");
            }
        }

        private static string RangeFromSkin(
            Renderer[] components,
            float skinProjection,
            Vector3 outward)
        {
            if (components == null || components.Length == 0)
            {
                return "n/a";
            }

            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    continue;
                }
                ProjectBounds(components[i].bounds, outward,
                    ref minimum, ref maximum);
            }
            return FormatRange(minimum, maximum, skinProjection);
        }

        private static string RangeFromSkin(
            Collider[] components,
            float skinProjection,
            Vector3 outward)
        {
            if (components == null || components.Length == 0)
            {
                return "n/a";
            }

            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    continue;
                }
                ProjectBounds(components[i].bounds, outward,
                    ref minimum, ref maximum);
            }
            return FormatRange(minimum, maximum, skinProjection);
        }

        private static void ProjectBounds(
            Bounds bounds,
            Vector3 axis,
            ref float minimum,
            ref float maximum)
        {
            float centre = Vector3.Dot(bounds.center, axis);
            Vector3 extents = bounds.extents;
            float radius = Math.Abs(axis.x) * extents.x
                + Math.Abs(axis.y) * extents.y
                + Math.Abs(axis.z) * extents.z;
            minimum = Math.Min(minimum, centre - radius);
            maximum = Math.Max(maximum, centre + radius);
        }

        private static string FormatRange(
            float minimum,
            float maximum,
            float skinProjection)
        {
            if (float.IsPositiveInfinity(minimum)
                || float.IsNegativeInfinity(maximum))
            {
                return "n/a";
            }
            return "[" + (minimum - skinProjection).ToString("F3")
                + "," + (maximum - skinProjection).ToString("F3") + "]m";
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

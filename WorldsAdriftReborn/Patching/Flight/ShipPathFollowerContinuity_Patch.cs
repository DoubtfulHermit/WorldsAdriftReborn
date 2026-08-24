using System;
using System.Reflection;
using Bossa.DeadReckoning;
using Bossa.DeadReckoning.Improbable;
using HarmonyLib;
using Improbable.CoreLibrary.CoordinateRemapping;
using Improbable.CoreLibrary.Transforms.Local;
using Improbable.Corelib.Util;
using Improbable.Corelibrary.Transforms;
using Improbable.Unity.Core;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Flight
{
    /// <summary>
    /// Removes two retail receive-side dead zones that become visible in WAReborn's
    /// server-authoritative ship topology.
    ///
    /// Retail normally simulates the local ship as one Rigidbody hierarchy. Here the
    /// hull is driven by PathFollower and each bolted "~" entity recomposes against
    /// that hull. PathFollower otherwise waits for a 1 cm position error, while the
    /// relative follower waits for a 0.01 quaternion-component error. At low speed or
    /// during a shallow turn those errors accumulate and are then applied as visible
    /// little jumps. These patches preserve the stock spline/control point and exact
    /// target pose; they only ensure each fixed-step target reaches the kinematic root
    /// and its active hull-relative followers.
    /// </summary>
    [HarmonyPatch(typeof(PathFollower), "Move")]
    internal static class ShipPathFollowerContinuity_Patch
    {
        private static readonly FieldInfo RigidbodyField =
            AccessTools.Field(typeof(PathFollower), "_rigidbody");
        private static bool _loggedFailure;

        private static bool Prepare()
        {
            bool ready = RigidbodyField != null;
            if (!ready)
            {
                Debug.LogWarning("[WAR][flight] PathFollower Rigidbody field was not resolvable;"
                    + " low-speed continuity patch skipped.");
            }
            return ready;
        }

        private static void Postfix(PathFollower __instance, ControlPoint controlPoint)
        {
            try
            {
                if (__instance == null
                    || __instance.GetComponent<SSPDeadReckoningVisualizer>() == null)
                {
                    return;
                }

                Rigidbody body = RigidbodyField.GetValue(__instance) as Rigidbody;
                if (body == null)
                {
                    return;
                }

                ControlPoint rendered = controlPoint.Remap();
                Vector3 position = rendered.Position.ToUnityVector3();
                Quaternion rotation = rendered.Rotation;
                if (!IsFinite(position) || !IsFinite(rotation))
                {
                    return;
                }

                // Stock Move may already have applied these exact values. Repeating
                // MovePosition/MoveRotation is harmless; below its 1 cm / 0.1 degree
                // optimization thresholds this is the call that preserves continuity.
                body.MovePosition(position);
                body.MoveRotation(rotation);
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Debug.LogWarning("[WAR][flight] low-speed hull continuity failed (once): "
                        + e.Message);
                }
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z)
                && !float.IsNaN(value.w) && !float.IsInfinity(value.w);
        }
    }

    [HarmonyPatch(typeof(FixedUpdateLerpLocalTransformBehaviour),
        "TransformExceedsThreshold")]
    internal static class ShipRelativeRotationContinuity_Patch
    {
        private static readonly FieldInfo TransformReaderField =
            AccessTools.Field(typeof(FixedUpdateLerpLocalTransformBehaviour),
                "TransformStateReader");
        private static bool _loggedFailure;

        private static bool Prepare()
        {
            bool ready = TransformReaderField != null;
            if (!ready)
            {
                Debug.LogWarning("[WAR][flight] relative TransformState reader was not resolvable;"
                    + " mounted-part turn continuity patch skipped.");
            }
            return ready;
        }

        private static void Postfix(FixedUpdateLerpLocalTransformBehaviour __instance,
            ref bool __result)
        {
            if (__result || __instance == null)
            {
                return;
            }

            try
            {
                var reader = TransformReaderField.GetValue(__instance) as TransformStateReader;
                if (reader == null || !reader.Parent.HasValue
                    || !string.Equals(reader.Parent.Value.key, "~", StringComparison.Ordinal))
                {
                    return;
                }

                var parent = global::Improbable.Unity.Core.SpatialOS.Universe.Get(
                    reader.Parent.Value.parentId);
                if (parent == null || parent.UnderlyingGameObject == null)
                {
                    return;
                }

                SSPDeadReckoningVisualizer motion =
                    parent.UnderlyingGameObject.GetComponent<SSPDeadReckoningVisualizer>();
                if (motion != null && motion.isActiveAndEnabled && motion.PathFollower != null
                    && motion.PathFollower.enabled && motion.PathFollower.PreviousSample.HasValue)
                {
                    // The desired value is already the stock hull pose composed with
                    // the unchanged local mount transform. Only bypass the coarse
                    // quaternion dead zone while that exact parent is actively moving.
                    __result = true;
                }
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Debug.LogWarning("[WAR][flight] mounted-part turn continuity failed (once): "
                        + e.Message);
                }
            }
        }
    }
}

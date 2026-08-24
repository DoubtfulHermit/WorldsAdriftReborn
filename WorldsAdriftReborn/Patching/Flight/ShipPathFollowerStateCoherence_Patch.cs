using System;
using System.Reflection;
using Bossa.DeadReckoning;
using Bossa.DeadReckoning.Improbable;
using HarmonyLib;
using Improbable.CoreLibrary.CoordinateRemapping;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Flight
{
    /// <summary>
    /// Keeps the retail PathFollower's complete pose/sample/velocity update path
    /// active while an authoritative ship sample is still moving.
    /// </summary>
    [HarmonyPatch(typeof(PathFollower), "Move")]
    internal static class ShipPathFollowerStateCoherence_Patch
    {
        private const float RetailMotionTailSeconds = 1f;
        private const double MovingVelocitySquaredEpsilon = 1e-12;
        private const float MovingRotationDegreesEpsilon = 0.0001f;

        private static readonly FieldInfo MotionTimerField =
            AccessTools.Field(typeof(PathFollower), "_disableRigidbodyUpdatesTimer");
        private static readonly FieldInfo RigidbodyField =
            AccessTools.Field(typeof(PathFollower), "_rigidbody");

        private static bool _loggedFailure;

        private static bool Prepare()
        {
            bool ready = MotionTimerField != null && RigidbodyField != null;
            if (!ready)
            {
                Debug.LogWarning("[WAR][flight] PathFollower state-coherence fields were not"
                    + " resolvable; low-speed ship correction skipped.");
            }
            return ready;
        }

        private static void Prefix(PathFollower __instance, ControlPoint controlPoint)
        {
            try
            {
                if (__instance == null
                    || __instance.GetComponent<SSPDeadReckoningVisualizer>() == null
                    || __instance.GetComponent<ShipVisualizer>() == null
                    || !IsFinite(controlPoint))
                {
                    return;
                }

                Rigidbody body = RigidbodyField.GetValue(__instance) as Rigidbody;
                if (body == null)
                {
                    return;
                }

                ControlPoint rendered = controlPoint.Remap();
                double speedSquared = (controlPoint.Velocity.X * controlPoint.Velocity.X)
                    + (controlPoint.Velocity.Y * controlPoint.Velocity.Y)
                    + (controlPoint.Velocity.Z * controlPoint.Velocity.Z);
                bool translating = speedSquared > MovingVelocitySquaredEpsilon;
                bool rotating = Quaternion.Angle(body.rotation, rendered.Rotation)
                    > MovingRotationDegreesEpsilon;
                if (!translating && !rotating)
                {
                    return;
                }

                float remaining = (float)MotionTimerField.GetValue(__instance);
                if (!IsFinite(remaining) || remaining < RetailMotionTailSeconds)
                {
                    MotionTimerField.SetValue(__instance, RetailMotionTailSeconds);
                }
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Debug.LogWarning("[WAR][flight] low-speed PathFollower state coherence failed"
                        + " closed (once): " + e.Message);
                }
            }
        }

        private static bool IsFinite(ControlPoint point)
        {
            return IsFinite(point.Position.X) && IsFinite(point.Position.Y)
                && IsFinite(point.Position.Z) && IsFinite(point.Velocity.X)
                && IsFinite(point.Velocity.Y) && IsFinite(point.Velocity.Z)
                && IsFinite(point.Rotation.x) && IsFinite(point.Rotation.y)
                && IsFinite(point.Rotation.z) && IsFinite(point.Rotation.w);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}

using System;
using System.Reflection;
using Assets.Scripts.Visualisers.Ship;
using Bossa.Travellers.Controls;
using HarmonyLib;
using Improbable;
using Improbable.Unity.Core;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Flight
{
    /// <summary>
    /// Restores retail's zero-round-trip helm animation for our separately
    /// registered helm entities.
    ///
    /// Retail ShipControlsBehaviour.UpdateHelm uses GetComponentInParent on the
    /// local player. That works when the controlled helm is in the same Unity
    /// hierarchy, but our mounted helm is its own SpatialOS entity following the
    /// hull. The lookup returns null, so the wheel waits for client 1111 -> server
    /// integration -> echoed helm 1111 before moving. At ordinary Internet RTT,
    /// the 240 ms ship cadence and HelmVisualizer's reader interpolation make
    /// that feel close to a second.
    ///
    /// PilotState already names the exact helm in ControlEntityId. After the
    /// HelmVisualizer has consumed its possibly stale server reader for this
    /// frame, reapply the local ShipControlsBehaviour values to that helm only.
    /// Ship movement remains server-authoritative; this predicts presentation,
    /// not physics. A later server echo converges to the same held input.
    /// </summary>
    [HarmonyPatch(typeof(HelmVisualizer), "Update")]
    internal static class LocalHelmFeedback_Patch
    {
        private static readonly FieldInfo PilotField =
            AccessTools.Field(typeof(ShipControlsBehaviour), "_pilot");
        private static readonly FieldInfo ThrottleField =
            AccessTools.Field(typeof(ShipControlsBehaviour), "_throttle");
        private static readonly FieldInfo VerticalField =
            AccessTools.Field(typeof(ShipControlsBehaviour), "_vertical");
        private static readonly FieldInfo AxesField =
            AccessTools.Field(typeof(ShipControlsBehaviour), "_axes");

        private static bool _loggedActive;
        private static bool _loggedFailure;

        private static bool Prepare()
        {
            bool ready = PilotField != null && ThrottleField != null
                && VerticalField != null && AxesField != null;
            if (!ready)
            {
                Debug.LogWarning("[WAR][flight] local helm feedback fields were not resolvable;"
                    + " prediction patch skipped.");
            }
            return ready;
        }

        private static void Postfix(HelmVisualizer __instance)
        {
            try
            {
                ShipControlsBehaviour controls = ShipControlsBehaviour.Instance;
                if (controls == null || __instance == null)
                {
                    return;
                }

                var pilot = PilotField.GetValue(controls) as PilotStateReader;
                if (pilot == null
                    || EntityId.IsInvalidEntityId(pilot.DrivingEntityId)
                    || EntityId.IsInvalidEntityId(pilot.ControlEntityId))
                {
                    return;
                }

                var helmEntity = global::Improbable.Unity.Core.SpatialOS.Universe.Get(pilot.ControlEntityId);
                if (helmEntity == null || helmEntity.UnderlyingGameObject == null)
                {
                    return;
                }

                HelmVisualizer controlledHelm =
                    helmEntity.UnderlyingGameObject.GetComponentInChildren<HelmVisualizer>(true);
                if (controlledHelm != __instance)
                {
                    return; // never predict another player's or another ship's helm
                }

                float throttle = (float)ThrottleField.GetValue(controls);
                float vertical = (float)VerticalField.GetValue(controls);
                Vector3 axes = (Vector3)AxesField.GetValue(controls);
                __instance.SetState(throttle, vertical, axes.x, axes.y, axes.z);

                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Debug.Log("[WAR][flight] local helm feedback is predicted directly from input;"
                        + " server echo remains authoritative for remote observers.");
                }
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Debug.LogWarning("[WAR][flight] local helm feedback prediction failed (once): "
                        + e.Message);
                }
            }
        }
    }
}

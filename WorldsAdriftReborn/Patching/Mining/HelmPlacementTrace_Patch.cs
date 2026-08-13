using System;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Mining
{
    /// <summary>
    /// Names WHY the helm refuses the deck. The Helm01 prefab bakes
    /// <c>ShipHelmPlacement.ValidatePlacement</c> (acs/ShipHelmPlacement.cs:11-19):
    ///
    ///     TargetShip != null &amp;&amp;
    ///     TargetShip.transform.InverseTransformDirection(Location.Rotation * up).y > 0
    ///
    /// - an UPRIGHT-HELM rule the sail does not have, which is why the sail lands on
    /// the deck and the helm only lands on the frame (live report). Static analysis
    /// cannot tell whether the live failure is (a) the ship's local up not matching
    /// world up (a tilted served hull -> the deck preview's world-up fails the
    /// hemisphere test everywhere) or (b) a footprint overlap - both are the same red
    /// preview. This postfix logs the exact inputs, heavily rate-limited, so ONE
    /// placement attempt answers it. Diagnostic only: the verdict is never changed.
    /// </summary>
    [HarmonyPatch(typeof(ShipHelmPlacement), "ValidatePlacement")]
    internal static class HelmPlacementTrace_Patch
    {
        private static float _nextLog;

        private static void Postfix(object __result, PlacementPreview placement)
        {
            try
            {
                if (Time.realtimeSinceStartup < _nextLog || placement == null)
                {
                    return;
                }
                _nextLog = Time.realtimeSinceStartup + 2f;

                var ship = placement.TargetShip;
                string shipName = ship == null ? "<null>" : ship.name;
                float upY = float.NaN;
                if (ship != null)
                {
                    upY = ship.transform.InverseTransformDirection(
                        placement.Location.Rotation * Vector3.up).y;
                }
                Debug.Log("[WAR][helm] ValidatePlacement: ship=" + shipName
                    + " shipLocalUp.y=" + upY.ToString("F3")
                    + " shipRot=" + (ship == null ? "-" : ship.transform.rotation.eulerAngles.ToString("F1"))
                    + " result=" + __result);
            }
            catch (Exception)
            {
                // diagnostic only - never interfere
            }
        }
    }
}

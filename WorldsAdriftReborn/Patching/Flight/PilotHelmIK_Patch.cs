using System;
using System.Reflection;
using HarmonyLib;
using Improbable;
using Improbable.Unity.Core;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Flight
{
    /// <summary>
    /// Restores the pilot's HANDS-ON-WHEEL pose with the HULL as the driven
    /// entity - the one thing the retail resolve path cannot do on our ships.
    ///
    /// WHY IT MISSES. <c>PilotVisualizer.OnChangeLinkedEntity</c> resolves the
    /// helm's IK targets with <c>GetComponentInChildren&lt;HelmVisualizer&gt;()</c>
    /// on the DRIVEN entity's GameObject (acs/PilotVisualizer.cs:110-145). On
    /// retail ships the helm sat inside the ship's Unity hierarchy; on ours a
    /// mounted helm is its OWN entity riding the hull as a "~" relative-FOLLOWER
    /// (never a Unity child), so with 1109 DrivingEntityId = the hull the search
    /// finds nothing, <c>fbikTargets</c> stays null, and <c>IKOrder</c> has no
    /// effectors to put the hands on the wheel (acs/IKOrder.cs:136-142).
    ///
    /// THE FIX uses data the server already sends for exactly this purpose: our
    /// 1109 update carries <c>ControlEntityId = the HELM entity</c>. When the
    /// retail search came up empty, resolve the helm entity from ControlEntityId
    /// and take ITS FullBodyIKTargets. No behaviour is replaced - this only
    /// fills the one static field the retail code would have filled itself if
    /// the helm were a Unity child.
    ///
    /// PilotVisualizer is internal and not publicized, so the target and its
    /// fields are resolved by name; a resolve failure logs once and patches
    /// nothing (Prepare returning false skips the patch cleanly).
    ///
    /// Runs only on the local pilot (PilotVisualizer only exists on the local
    /// rig - 1109 is never mirrored) and only on the transition INTO driving.
    /// </summary>
    [HarmonyPatch]
    internal static class PilotHelmIK_Patch
    {
        private static readonly Type PilotVisualizerType = AccessTools.TypeByName("PilotVisualizer");
        private static readonly FieldInfo PilotReaderField =
            PilotVisualizerType == null ? null : AccessTools.Field(PilotVisualizerType, "_pilot");
        private static readonly FieldInfo IkTargetsField =
            PilotVisualizerType == null ? null : AccessTools.Field(PilotVisualizerType, "fbikTargets");

        private static bool _loggedError;

        private static bool Prepare()
        {
            bool ok = PilotVisualizerType != null && PilotReaderField != null && IkTargetsField != null;
            if (!ok)
            {
                Debug.LogWarning("[WAR][flight] PilotHelmIK_Patch: PilotVisualizer/_pilot/fbikTargets"
                    + " not resolvable; hands-on-wheel IK patch skipped.");
            }
            return ok;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(PilotVisualizerType, "OnChangeLinkedEntity");
        }

        private static void Postfix(object __instance, EntityId drivenEntityId)
        {
            try
            {
                if (EntityId.IsInvalidEntityId(drivenEntityId))
                {
                    return; // the dismount transition
                }
                if (IkTargetsField.GetValue(null) != null)
                {
                    return; // the retail search already succeeded
                }

                var pilot = PilotReaderField.GetValue(__instance) as Bossa.Travellers.Controls.PilotStateReader;
                if (pilot == null)
                {
                    return;
                }

                EntityId helmId = pilot.ControlEntityId;
                if (EntityId.IsInvalidEntityId(helmId))
                {
                    return;
                }

                var helm = global::Improbable.Unity.Core.SpatialOS.Universe.Get(helmId);
                if (helm == null || helm.UnderlyingGameObject == null)
                {
                    return;
                }

                FullBodyIKTargets targets =
                    helm.UnderlyingGameObject.GetComponentInChildren<FullBodyIKTargets>(true);
                if (targets != null)
                {
                    IkTargetsField.SetValue(null, targets);
                    Debug.Log("[WAR][flight] pilot IK targets resolved from helm entity " + helmId.Id
                        + " (the driven hull carries no helm in its Unity hierarchy).");
                }
                else
                {
                    Debug.Log("[WAR][flight] helm entity " + helmId.Id
                        + " has no FullBodyIKTargets in its prefab; hands-on-wheel IK unavailable.");
                }
            }
            catch (Exception e)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    Debug.LogWarning("[WAR][flight] PilotHelmIK_Patch failed (once): " + e.Message);
                }
            }
        }
    }
}

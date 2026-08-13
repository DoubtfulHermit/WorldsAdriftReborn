using System;
using System.Reflection;
using HarmonyLib;
using Improbable;
using Improbable.Unity.Core;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Flight
{
    /// <summary>
    /// Faces the pilot's BODY at the wheel when they take the helm - the
    /// "seated facing to the right of the helm" live symptom.
    ///
    /// WHY THE BODY FACES ANYWHERE. Nothing in the retail client turns the
    /// player on man: the driving state only zeroes locomotion
    /// (PlayerCharacterAnimation.cs:263-267) and binds hand/look IK effectors
    /// (IKOrder.SetupIKTargets) - the body ROOT simply keeps whatever facing the
    /// player approached the helm with. Retail got away with it because the
    /// FBIK pull plus the interact camera made players face the wheel naturally;
    /// on our first flights the mismatch reads as broken.
    ///
    /// THE FIX: a ONE-SHOT yaw snap on the transition into driving, aligning the
    /// player to the helm's forward (fallback: the driven hull's forward). Yaw
    /// only - position is deliberately NOT touched: a position snap risks
    /// intersecting the helm collider and letting physics shove the pilot
    /// through the deck, and the player is already within the 3 m Man radius.
    /// The local player is client-authoritative over its own transform, so the
    /// snap propagates through the normal movement stream with no server fight.
    /// Both the root transform and (when reachable) the movement rigidbody are
    /// rotated, so the physics step does not immediately unwind the snap.
    /// </summary>
    [HarmonyPatch]
    internal static class PilotBodyAnchor_Patch
    {
        private static readonly Type PilotVisualizerType = AccessTools.TypeByName("PilotVisualizer");
        private static readonly FieldInfo PilotReaderField =
            PilotVisualizerType == null ? null : AccessTools.Field(PilotVisualizerType, "_pilot");

        private static bool _loggedError;

        private static bool Prepare()
        {
            bool ok = PilotVisualizerType != null && PilotReaderField != null;
            if (!ok)
            {
                Debug.LogWarning("[WAR][flight] PilotBodyAnchor_Patch: PilotVisualizer/_pilot not"
                    + " resolvable; body-facing patch skipped.");
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
                if (EntityId.IsInvalidEntityId(drivenEntityId) || !LocalPlayer.Exists)
                {
                    return; // dismount transition, or no rig yet
                }

                var pilot = PilotReaderField.GetValue(__instance) as Bossa.Travellers.Controls.PilotStateReader;

                // Prefer the helm's own facing (our 1109 ControlEntityId), fall
                // back to the driven hull's.
                Transform reference = null;
                if (pilot != null && !EntityId.IsInvalidEntityId(pilot.ControlEntityId))
                {
                    var helm = global::Improbable.Unity.Core.SpatialOS.Universe.Get(pilot.ControlEntityId);
                    if (helm != null && helm.UnderlyingGameObject != null)
                    {
                        reference = helm.UnderlyingGameObject.transform;
                    }
                }
                if (reference == null)
                {
                    var vehicle = global::Improbable.Unity.Core.SpatialOS.Universe.Get(drivenEntityId);
                    if (vehicle == null || vehicle.UnderlyingGameObject == null)
                    {
                        return;
                    }
                    reference = vehicle.UnderlyingGameObject.transform;
                }

                Vector3 forward = reference.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 1e-4f)
                {
                    return; // a degenerate facing is not worth a snap
                }

                Quaternion facing = Quaternion.LookRotation(forward.normalized, Vector3.up);

                Transform root = LocalPlayer.Transform;
                if (root != null)
                {
                    root.rotation = facing;
                }

                // The movement rigidbody, via reflection (RigidbodyBehaviour is
                // a custom wrapper): best-effort, the transform snap above is
                // the load-bearing one.
                try
                {
                    object playerMove = Traverse.Create(LocalPlayer.Instance).Property("playerMove").GetValue()
                        ?? Traverse.Create(LocalPlayer.Instance).Field("playerMove").GetValue();
                    if (playerMove is MonoBehaviour moveBehaviour)
                    {
                        Rigidbody body = moveBehaviour.GetComponent<Rigidbody>();
                        if (body != null)
                        {
                            body.MoveRotation(facing);
                        }
                    }
                }
                catch (Exception)
                {
                    // best-effort only
                }

                Debug.Log("[WAR][flight] pilot body faced to the wheel (yaw snap on man).");
            }
            catch (Exception e)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    Debug.LogWarning("[WAR][flight] PilotBodyAnchor_Patch failed (once): " + e.Message);
                }
            }
        }
    }
}

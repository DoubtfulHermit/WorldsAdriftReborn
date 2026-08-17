using System;
using System.Reflection;
using HarmonyLib;
using Improbable;
using Improbable.Unity.Core;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Flight
{
    /// <summary>
    /// Places the local pilot at the helm's authored standing point when they
    /// take control, independent of which side they approached from.
    ///
    /// WHY THE BODY/CAMERA START ANYWHERE. Nothing in the retail client moves the
    /// player on man: the driving state only zeroes locomotion
    /// (PlayerCharacterAnimation.cs:263-267) and binds hand/look IK effectors
    /// (IKOrder.SetupIKTargets) - the body ROOT simply keeps whatever facing the
    /// player approached the helm with. PilotCameraController then uses the
    /// player's CameraTargetPilot as its positional target, so entering from the
    /// left or right permanently offsets the whole pilot camera by that amount.
    ///
    /// THE FAITHFUL ANCHOR. The shipped Helm01_unityclient prefab contains an
    /// explicit child named <c>#PilotPosition</c> at helm-local
    /// (0, 0.074, -1.4070084), identity rotation. It is the safe, authored spot
    /// behind and dead-centre on the wheel - not a guessed camera/body offset.
    /// The modular-cannon retail path uses the same #PilotPosition convention.
    /// On the transition into driving this patch resolves that child from the
    /// server-provided ControlEntityId (the helm), snaps the client-authoritative
    /// player root and rigidbody to its exact world pose, clears stale ground-
    /// relative movement caches, and zeroes locomotion. The ordinary player
    /// transform stream remains authoritative afterwards.
    /// </summary>
    [HarmonyPatch]
    internal static class PilotBodyAnchor_Patch
    {
        private static readonly Type PilotVisualizerType = AccessTools.TypeByName("PilotVisualizer");
        private static readonly FieldInfo PilotReaderField =
            PilotVisualizerType == null ? null : AccessTools.Field(PilotVisualizerType, "_pilot");

        private const string PilotAnchorName = "#PilotPosition";

        private static bool _loggedError;
        private static bool _loggedMissingAnchor;

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

                // Prefer the helm entity named by our 1109 ControlEntityId. The
                // hull fallback preserves compatibility with a retail-style Unity
                // hierarchy where the helm (and its anchor) is a hull child.
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

                Transform anchor = FindDescendant(reference, PilotAnchorName);
                if (anchor == null)
                {
                    if (!_loggedMissingAnchor)
                    {
                        _loggedMissingAnchor = true;
                        Debug.LogWarning("[WAR][flight] helm has no authored " + PilotAnchorName
                            + " child; refusing to invent a pilot/camera offset.");
                    }
                    return;
                }

                Transform root = LocalPlayer.Transform;
                if (root == null)
                {
                    return;
                }

                Vector3 prior = root.position;
                Vector3 position = anchor.position;
                Quaternion rotation = anchor.rotation;

                // Clear the two relative-ground ledgers which otherwise remember
                // the approach-side deck position and can restore it on a later
                // physics correction/dismount.
                ClientAuthoritativePlayerMovement clientMovement =
                    LocalPlayer.Instance.ClientAuthoritativePlayerMovement;
                if (clientMovement != null)
                {
                    clientMovement.PlayerWasRepositioned();
                }
                PlayerMove playerMove = LocalPlayer.Instance.playerMove;
                if (playerMove != null)
                {
                    playerMove.PlayerWasRespositioned(); // retail spelling
                }

                // The Rigidbody and transform are the same physical root in the
                // shipped Traveller prefab. Set both so the current render frame
                // and the next physics frame agree; no delayed MovePosition that
                // leaves the camera one frame on the approach side.
                Rigidbody body = root.GetComponent<Rigidbody>();
                root.position = position;
                root.rotation = rotation;
                if (body != null)
                {
                    body.position = position;
                    body.rotation = rotation;
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                if (playerMove != null)
                {
                    playerMove.ZeroOut(Vector3.zero, Vector3.zero);
                }

                Debug.Log("[WAR][flight] pilot snapped to helm's authored " + PilotAnchorName
                    + " anchor (approach offset " + Vector3.Distance(prior, position).ToString("0.###")
                    + " m cleared).");
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

        private static Transform FindDescendant(Transform root, string exactName)
        {
            if (root == null)
            {
                return null;
            }
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                if (string.Equals(descendants[i].name, exactName, StringComparison.Ordinal))
                {
                    return descendants[i];
                }
            }
            return null;
        }
    }
}

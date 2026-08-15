using System.Reflection;
using Bossa.Travellers.Player;
using HarmonyLib;
using Improbable;
using Improbable.CoreLibrary.CoordinateRemapping;
using Improbable.Corelib.Interpolation;
using Improbable.Math;
using UnityEngine;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftReborn.Patching.Multiplayer
{
    /*
     * Make the game's own interpolating positioner drive remote players, for
     * smooth movement instead of RemoteRigMover's per-frame teleport - but
     * SAFELY. PlayerVisualizer.FixedUpdate has three branches (PlayerVisualizer.cs
     * ~105-141): a Parent branch that writes localPosition with NO origin remap,
     * a relativeObj/bias branch for ships, and the global else branch. The Parent
     * branch is what dropped a remote rig ~90km away and made it fall through the
     * map (a spawn-time LocalTransformTeleportBehaviour publishes a Parent, and
     * this single-island world has no resolvable parent hierarchy).
     *
     * For REMOTE rigs this prefix reproduces the retail GLOBAL and SHIP-RELATIVE
     * branches but deliberately omits only the unsafe Parent branch. Earlier
     * versions forced every remote through global 190602. That made an aboard
     * avatar trail a moving hull by speed x interpolation latency even though its
     * relayed 1073 correctly named the ship and carried a hull-local position.
     *
     * RemoteRigMover still forces the root rigidbody kinematic: the native
     * kinematic path is gated on an AuthorityChanged event that never fires for a
     * never-authoritative remote, and PlayerVisualizer does not set it.
     */
    [HarmonyPatch]
    internal class PlayerVisualizer_Patch
    {
        private static readonly FieldInfo PosInterpField = AccessTools.Field(AccessTools.TypeByName("PlayerVisualizer"), "_interpolator");
        private static readonly FieldInfo RotInterpField = AccessTools.Field(AccessTools.TypeByName("PlayerVisualizer"), "_rotInterpolator");
        private static readonly FieldInfo RelativePosInterpField = AccessTools.Field(AccessTools.TypeByName("PlayerVisualizer"), "_relativeInterpolator");
        private static readonly FieldInfo RelativeRotInterpField = AccessTools.Field(AccessTools.TypeByName("PlayerVisualizer"), "_relativeRotInterpolator");
        private static readonly FieldInfo StateReaderField = AccessTools.Field(AccessTools.TypeByName("PlayerVisualizer"), "_stateReader");
        private static readonly FieldInfo RelativeObjectField = AccessTools.Field(AccessTools.TypeByName("PlayerVisualizer"), "_relativeObj");
        private static readonly FieldInfo BiasField = AccessTools.Field(AccessTools.TypeByName("PlayerVisualizer"), "_bias");
        private static readonly FieldInfo BiasSpeedField = AccessTools.Field(AccessTools.TypeByName("PlayerVisualizer"), "biasInterpolationSpeed");
        private static readonly FieldInfo ResetRelativeField = AccessTools.Field(AccessTools.TypeByName("PlayerVisualizer"), "_resetRelativeInterpolatorsFlag");
        private static readonly MethodInfo CheckRelativeObjectMethod = AccessTools.Method(AccessTools.TypeByName("PlayerVisualizer"), "CheckRelativeObject");

        [HarmonyTargetMethod]
        public static MethodBase GetTargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("PlayerVisualizer"), "FixedUpdate");
        }

        // Classification cache by root instance id. The local/remote decision
        // reads component PRESENCE on the prefab, which never changes for a
        // living root - but the uncached check walked every child MonoBehaviour
        // (GetComponentsInChildren + GetType().Name per component, through an
        // iterator) for EVERY remote player EVERY physics step. Unity instance
        // ids are session-unique, so a stale entry cannot alias a new rig.
        private static readonly System.Collections.Generic.Dictionary<int, bool> classifiedLocal =
            new System.Collections.Generic.Dictionary<int, bool>();

        [HarmonyPrefix]
        public static bool FixedUpdate_Prefix( MonoBehaviour __instance )
        {
            // Local full rig -> run the game's own FixedUpdate unchanged.
            // Checked by local-only COMPONENTS, not the root name: name matching
            // failed and let this prefix drive the LOCAL player from a remote
            // interpolator, which is what sent it falling through the sky.
            // NOTE: this still ORs in a ROOT-NAME check, which is the rule-11
            // violation the rest of the mod eliminated. It is unreachable today
            // (mirrored remotes spawn from context "Default", so their roots are
            // named "Traveller N", never "Traveller@Player"), but it is a
            // landmine. ClientRigPolicyTests pins the intended component-only
            // behaviour so the name clause cannot return.
            UnityEngine.Transform root = __instance.transform.root;
            bool isLocal;
            if (!classifiedLocal.TryGetValue(root.GetInstanceID(), out isLocal))
            {
                isLocal = ClientRigPolicy.TreatAsLocalForPlayerVisualizer(
                    root.name,
                    RemoteRigSweeper.ComponentTypeNames(root));
                classifiedLocal[root.GetInstanceID()] = isLocal;
            }
            if (isLocal)
            {
                return true;
            }

            // Remote rig -> retail global/relative composition, never the stale
            // TransformState.Parent branch.
            PositionInterpolator posInterp = PosInterpField?.GetValue(__instance) as PositionInterpolator;
            RotationInterpolator rotInterp = RotInterpField?.GetValue(__instance) as RotationInterpolator;
            RelativePositionInterpolator relativePosInterp = RelativePosInterpField?.GetValue(__instance) as RelativePositionInterpolator;
            RotationInterpolator relativeRotInterp = RelativeRotInterpField?.GetValue(__instance) as RotationInterpolator;
            ClientAuthoritativePlayerState.Reader stateReader =
                StateReaderField?.GetValue(__instance) as ClientAuthoritativePlayerState.Reader;
            if (posInterp == null || rotInterp == null || relativePosInterp == null
                || relativeRotInterp == null || stateReader == null)
            {
                // Reflection failed unexpectedly; fall back to the original rather
                // than freeze the rig.
                return true;
            }

            Vector3d interpolatedPos = posInterp.GetInterpolatedValue(Time.deltaTime);
            Quaternion interpolatedRot = rotInterp.GetInterpolatedValue(Time.deltaTime);

            CheckRelativeObjectMethod?.Invoke(__instance, null);
            GameObject relativeObject = RelativeObjectField?.GetValue(__instance) as GameObject;
            float bias = BiasField != null ? (float)BiasField.GetValue(__instance) : 0f;
            float biasSpeed = BiasSpeedField != null ? (float)BiasSpeedField.GetValue(__instance) : 0f;
            bias = Mathf.MoveTowards(bias, stateReader.Data.relativeBias,
                Time.deltaTime * biasSpeed);
            BiasField?.SetValue(__instance, bias);

            if (ClientRigPolicy.PositionBranchForRemote(relativeObject != null, bias)
                == RemotePlayerPositionBranch.ShipRelative)
            {
                UnityEngine.Transform relativeTransform = relativeObject.transform;
                bool reset = ResetRelativeField != null
                    && (bool)ResetRelativeField.GetValue(__instance);
                if (reset)
                {
                    relativePosInterp.Reset(
                        relativeTransform.InverseTransformPoint(__instance.transform.position)
                            .ToImprobableVector3(),
                        stateReader.Data.timestamp);
                    relativeRotInterp.Reset(
                        Quaternion.Inverse(relativeTransform.rotation) * __instance.transform.rotation,
                        stateReader.Data.timestamp);
                    ResetRelativeField.SetValue(__instance, false);
                }

                Vector3d relativePosition = relativePosInterp.GetInterpolatedValue(Time.deltaTime);
                Quaternion relativeRotation = relativeRotInterp.GetInterpolatedValue(Time.deltaTime);
                Vector3 shipWorldPosition = relativeTransform.TransformPoint(relativePosition.ToUnityVector3());
                Quaternion shipWorldRotation = relativeTransform.rotation * relativeRotation;
                __instance.transform.position = Vector3.Lerp(
                    interpolatedPos.RemapGlobalToUnityVector(), shipWorldPosition, bias);
                __instance.transform.rotation = Quaternion.Lerp(
                    interpolatedRot, shipWorldRotation, bias);
            }
            else
            {
                __instance.transform.position = interpolatedPos.RemapGlobalToUnityVector();
                __instance.transform.rotation = interpolatedRot;
            }

            return false;
        }
    }
}

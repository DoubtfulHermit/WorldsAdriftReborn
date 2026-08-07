using System.Reflection;
using HarmonyLib;
using Improbable.CoreLibrary.CoordinateRemapping;
using Improbable.Corelib.Interpolation;
using Improbable.Math;
using UnityEngine;

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
     * For REMOTE rigs this prefix runs ONLY the global branch - read the same
     * _interpolator/_rotInterpolator PlayerVisualizer's own OnEnable already
     * feeds from the relayed 190602 updates, and write the remapped global pose -
     * then skips the original (return false), so the Parent/relative/playerBlink
     * paths never run. For the LOCAL rig it returns true and the game's own
     * FixedUpdate runs unchanged.
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

        [HarmonyTargetMethod]
        public static MethodBase GetTargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("PlayerVisualizer"), "FixedUpdate");
        }

        [HarmonyPrefix]
        public static bool FixedUpdate_Prefix( MonoBehaviour __instance )
        {
            // Local full rig -> run the game's own FixedUpdate unchanged.
            // Checked by local-only COMPONENTS, not the root name: name matching
            // failed and let this prefix drive the LOCAL player from a remote
            // interpolator, which is what sent it falling through the sky.
            if (__instance.transform.root.name.StartsWith("Traveller@Player")
                || RemoteRigSweeper.IsLocalRig(__instance.transform.root))
            {
                return true;
            }

            // Remote rig -> global branch only.
            PositionInterpolator posInterp = PosInterpField?.GetValue(__instance) as PositionInterpolator;
            RotationInterpolator rotInterp = RotInterpField?.GetValue(__instance) as RotationInterpolator;
            if (posInterp == null || rotInterp == null)
            {
                // Reflection failed unexpectedly; fall back to the original rather
                // than freeze the rig.
                return true;
            }

            Vector3d interpolatedPos = posInterp.GetInterpolatedValue(Time.deltaTime);
            Quaternion interpolatedRot = rotInterp.GetInterpolatedValue(Time.deltaTime);

            __instance.transform.position = interpolatedPos.RemapGlobalToUnityVector();
            __instance.transform.rotation = interpolatedRot;

            return false;
        }
    }
}

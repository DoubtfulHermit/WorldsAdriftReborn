using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Multiplayer
{
    /*
     * LocalPlayer is the game's global "this is MY player" anchor: camera, UI,
     * weather and movement all resolve through LocalPlayer.Instance. Its Awake
     * AND OnEnable both do "_instance = this" unconditionally, and
     * CameraSelectionVisualizer.Awake does the same for its own singleton.
     *
     * Unity only lets the [Require] visualizer system gate OnEnable/Update -
     * Awake always runs on instantiation. So the moment the server mirrors a
     * second player entity (same Traveller prefab), the remote rig's Awake
     * steals the local-player identity: the camera snaps to the remote's
     * default position in the sky and the own character is gone. The original
     * game never instantiated a second full Traveller on a client, so this was
     * never guarded.
     *
     * Fix: a live singleton is never overwritten. The local player always
     * instantiates first (the server only mirrors remote players after the
     * local AddEntityOp), so keep-first keeps the local player. If the owner is
     * destroyed (respawn), Unity's overloaded == makes the stale reference
     * "null" and the next claim goes through.
     */
    [HarmonyPatch(typeof(LocalPlayer))]
    internal class LocalPlayer_Patch
    {
        private static bool ShouldKeepCurrent( LocalPlayer candidate, string hook )
        {
            LocalPlayer current = LocalPlayer.Instance;
            // Keep-first rule lives in ClientRigPolicy so it is unit-tested.
            // "current != null" uses Unity's overloaded ==, so a DESTROYED owner
            // counts as gone and a respawn can re-claim.
            if (!WorldsAdriftRebornGameServer.Multiplayer.ClientRigPolicy.ShouldClaimSingleton(
                    current != null, ReferenceEquals(current, candidate)))
            {
                Debug.Log("[WAReborn] suppressed LocalPlayer takeover (" + hook + ") by " + candidate.gameObject.name
                          + " at " + candidate.transform.position + " - keeping " + current.gameObject.name);
                return true;
            }
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("Awake")]
        public static bool Awake_Prefix( LocalPlayer __instance )
        {
            return !ShouldKeepCurrent(__instance, "Awake");
        }

        [HarmonyPrefix]
        [HarmonyPatch("OnEnable")]
        public static bool OnEnable_Prefix( LocalPlayer __instance )
        {
            return !ShouldKeepCurrent(__instance, "OnEnable");
        }
    }

    /*
     * Same keep-first guard for CameraSelectionVisualizer.Instance, which is
     * also assigned unconditionally in Awake. The type is internal, so the
     * patch targets it by name.
     */
    [HarmonyPatch()]
    internal class CameraSelectionVisualizer_Awake_Patch
    {
        private static PropertyInfo instanceProp;

        [HarmonyTargetMethod]
        public static MethodBase GetTargetMethod()
        {
            System.Type t = AccessTools.TypeByName("CameraSelectionVisualizer");
            instanceProp = AccessTools.Property(t, "Instance");
            return AccessTools.Method(t, "Awake");
        }

        [HarmonyPrefix]
        public static bool Awake_Prefix( MonoBehaviour __instance )
        {
            Object current = (Object)instanceProp.GetValue(null, null);
            if (!WorldsAdriftRebornGameServer.Multiplayer.ClientRigPolicy.ShouldClaimSingleton(
                    current != null, ReferenceEquals(current, __instance)))
            {
                Debug.Log("[WAReborn] suppressed CameraSelectionVisualizer takeover by " + __instance.gameObject.name);
                return false;
            }
            return true;
        }
    }
}

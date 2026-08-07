using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Multiplayer
{
    /*
     * PlayerVisualizer.FixedUpdate is the game's native remote-player positioner:
     * it writes the root transform from the 190602 interpolator every frame.
     * Seeding 1073 enables it on the plain remote rig - where it fights
     * RemoteRigMover for the same root transform. Two positioners produced a
     * rig that fell through the map on the client where the native one did not
     * cleanly take over.
     *
     * RemoteRigMover is the proven positioner and stays authoritative, so this
     * suppresses PlayerVisualizer.FixedUpdate on REMOTE rigs only (root name is
     * plain "Traveller <id>", not the local "Traveller@Player <id>"). The local
     * player's own PlayerVisualizer, if any, is untouched. Animation is
     * unaffected: it comes from BoneAnimationReader, a different component.
     */
    [HarmonyPatch]
    internal class PlayerVisualizer_Patch
    {
        [HarmonyTargetMethod]
        public static MethodBase GetTargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("PlayerVisualizer"), "FixedUpdate");
        }

        [HarmonyPrefix]
        public static bool FixedUpdate_Prefix( MonoBehaviour __instance )
        {
            // Remote plain rig -> skip the native positioner (RemoteRigMover owns
            // the root). Local full rig -> run it as normal.
            bool isLocalRig = __instance.transform.root.name.StartsWith("Traveller@Player");
            return isLocalRig;
        }
    }
}

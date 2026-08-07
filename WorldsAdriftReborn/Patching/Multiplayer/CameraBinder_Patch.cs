using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Multiplayer
{
    /*
     * CameraBinder sits on the Traveller prefab and is a plain MonoBehaviour with
     * no [Require] gating and no authority check: every instance pushes its OWN
     * transform into the camera controller every frame.
     *
     * With one player that is fine. The moment the server mirrors a second player
     * entity, that remote Traveller's binder also runs, and the camera ends up
     * driven by the remote entity's (default, never locally-simulated) transform:
     * top-down view, own character invisible.
     *
     * Fix: the first binder to run claims the camera and keeps it. The local
     * player is always instantiated before any mirrored remote player (the server
     * only mirrors after the local AddEntityOp), so first-wins is the local one.
     * If the owning binder is destroyed (respawn, scene change), the claim resets
     * and the next binder - again the local player's - takes over.
     *
     * CameraBinder is internal, so the patch targets it by name, following the
     * same convention as the WAConfig patch.
     */
    [HarmonyPatch()]
    internal class CameraBinder_Patch
    {
        private static MonoBehaviour boundInstance;
        private static readonly System.Collections.Generic.HashSet<int> seen = new System.Collections.Generic.HashSet<int>();

        [HarmonyTargetMethod]
        public static MethodBase GetTargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("CameraBinder"), "Update");
        }

        private static string Describe( MonoBehaviour binder )
        {
            UnityEngine.Transform root = binder.transform.root;
            return binder.gameObject.name + " (id " + binder.GetInstanceID() + ", root " + root.name + ", rootPos " + root.position + ")";
        }

        [HarmonyPrefix]
        public static bool Update_Prefix( MonoBehaviour __instance )
        {
            if (seen.Add(__instance.GetInstanceID()))
            {
                Debug.Log("[WAReborn] CameraBinder instance appeared: " + Describe(__instance));
            }

            // Unity's overloaded == treats a destroyed component as null.
            if (boundInstance == null)
            {
                boundInstance = __instance;
                Debug.Log("[WAReborn] CameraBinder CLAIMED by: " + Describe(__instance));
            }

            return ReferenceEquals(__instance, boundInstance);
        }
    }
}

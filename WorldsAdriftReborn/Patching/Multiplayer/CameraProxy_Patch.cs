using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Multiplayer
{
    /*
     * CameraProxy sits on the Traveller prefab. Its Start() unconditionally
     * points the player camera at its own rig:
     *
     *     playerCameraController.SetTargets(CameraTargetFirstPerson, CameraTargetStandard);
     *
     * No authority check, no local-player check. So when the server mirrors a
     * second player, the remote rig's Start() retargets the camera and the view
     * follows the OTHER player - which is exactly what the camera inventory
     * showed: client 2's camera parked at client 1's world position.
     *
     * Keep-first, like the other singleton guards: the first CameraProxy to
     * Start claims the camera (the local player always instantiates before any
     * mirrored remote), later ones are ignored. Start() only runs once per
     * component, so a destroyed owner cannot be re-claimed by an old remote
     * rig; the next claim comes from a freshly spawned local rig.
     */
    [HarmonyPatch(typeof(CameraProxy), "Start")]
    internal class CameraProxy_Patch
    {
        private static CameraProxy owner;

        /// <summary>
        /// Root transform of the LOCAL player's rig - the one whose CameraProxy
        /// claimed the camera. This is the reliable "which Traveller is mine"
        /// anchor: LocalPlayer.Instance is NOT on the Traveller prefab, so its
        /// root never matches a rig root and must not be used for this.
        /// </summary>
        public static Transform OwnerRoot => owner != null ? owner.transform.root : null;

        [HarmonyPrefix]
        public static bool Start_Prefix( CameraProxy __instance )
        {
            // Unity's overloaded == treats a destroyed component as null. The
            // keep-first rule itself lives in ClientRigPolicy so it is unit-tested.
            if (WorldsAdriftRebornGameServer.Multiplayer.ClientRigPolicy.ShouldClaimSingleton(
                    owner != null, ReferenceEquals(owner, __instance)))
            {
                if (owner == null)
                {
                    owner = __instance;
                    Debug.Log("[WAReborn] camera targets claimed by " + __instance.transform.root.name
                              + " at " + __instance.transform.position);
                }
                return true;
            }

            Debug.Log("[WAReborn] suppressed camera retarget by " + __instance.transform.root.name
                      + " at " + __instance.transform.position);
            return false;
        }
    }
}

using UnityEngine;

namespace WorldsAdriftReborn.Patching.Multiplayer
{
    /*
     * The Traveller prefab carries its own Camera and AudioListener. Singleton
     * guards cannot help against those: a freshly instantiated enabled Camera
     * simply renders, so a mirrored remote player hijacks the view from its
     * default spawn transform no matter who owns the game's camera singletons.
     *
     * Every 2 seconds this sweeper:
     *   1. logs every enabled camera (name, root, position) so the log shows the
     *      truth about who is rendering rather than another guess, and
     *   2. disables Camera and AudioListener components under any Traveller root
     *      that is not the local player's root.
     *
     * The local player's root is resolved via LocalPlayer.Instance, with a
     * first-seen fallback for the window before LocalPlayer exists.
     */
    internal class RemoteRigSweeper : MonoBehaviour
    {
        private const string TravellerRootPrefix = "Traveller@Player";
        private float nextSweep;
        private Transform firstSeenTravellerRoot;
        private readonly System.Collections.Generic.HashSet<int> inventoried = new System.Collections.Generic.HashSet<int>();

        /// <summary>
        /// One-shot diagnostic: logs the top-level component list of every rig
        /// whose root name contains "Traveller" (covers both the local
        /// Traveller@Player rig and the plain Traveller remote rig), so the log
        /// shows what a rig actually contains instead of us assuming.
        /// </summary>
        private void InventoryRig(Transform root)
        {
            if (!inventoried.Add(root.GetInstanceID()))
            {
                return;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[WAReborn] rig inventory '").Append(root.name).Append("' components: ");
            foreach (Component comp in root.GetComponents<Component>())
            {
                if (comp != null)
                {
                    sb.Append(comp.GetType().Name).Append(", ");
                }
            }
            sb.Append("| children: ");
            for (int i = 0; i < root.childCount && i < 20; i++)
            {
                sb.Append(root.GetChild(i).name).Append(", ");
            }
            Debug.Log(sb.ToString());
        }

        private Transform LocalRoot()
        {
            // The rig whose CameraProxy claimed the camera IS the local player's
            // rig. LocalPlayer.Instance must NOT be used here: it is a scene
            // object, not part of the Traveller prefab, so its root never equals
            // a rig root - which once made this sweeper tame the LOCAL rig too
            // (frozen falling pose, no movement, camera dropping from the sky).
            Transform claimed = CameraProxy_Patch.OwnerRoot;
            if (claimed != null)
            {
                return claimed;
            }
            return firstSeenTravellerRoot;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextSweep)
            {
                return;
            }
            nextSweep = Time.unscaledTime + 2f;

            Transform localRoot = LocalRoot();

            Camera[] cameras = Object.FindObjectsOfType<Camera>();
            foreach (Camera cam in cameras)
            {
                Transform root = cam.transform.root;
                bool traveller = root.name.StartsWith(TravellerRootPrefix);

                if (traveller && firstSeenTravellerRoot == null)
                {
                    firstSeenTravellerRoot = root;
                    if (localRoot == null)
                    {
                        localRoot = root;
                    }
                }

                if (cam.enabled)
                {
                    Debug.Log("[WAReborn] camera: '" + cam.name + "' root '" + root.name + "' pos " + cam.transform.position
                              + (traveller ? (root == localRoot ? " [local rig]" : " [REMOTE RIG]") : ""));
                }

                if (traveller && localRoot != null && root != localRoot)
                {
                    if (cam.enabled)
                    {
                        cam.enabled = false;
                        Debug.Log("[WAReborn] DISABLED remote rig camera '" + cam.name + "' under '" + root.name + "'");
                    }
                }
            }

            AudioListener[] listeners = Object.FindObjectsOfType<AudioListener>();
            foreach (AudioListener listener in listeners)
            {
                Transform root = listener.transform.root;
                if (listener.enabled && root.name.StartsWith(TravellerRootPrefix) && localRoot != null && root != localRoot)
                {
                    listener.enabled = false;
                    Debug.Log("[WAReborn] DISABLED remote rig AudioListener under '" + root.name + "'");
                }
            }

            // Remote rigs must not SIMULATE, only display. The prefab's physics
            // and movement scripts are plain MonoBehaviours (no [Require]), so a
            // mirrored rig runs a full ragdoll+movement simulation that fights
            // the relayed TransformState positions and burns CPU.
            foreach (GameObject rootGo in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Transform root = rootGo.transform;
                if (!root.name.Contains("Traveller"))
                {
                    continue;
                }

                InventoryRig(root);

                // Taming only ever applies to a full local-player rig
                // (Traveller@Player) that is not our own. The plain Traveller
                // remote rig needs no taming - it is the game's own
                // display-only remote-player prefab.
                if (localRoot != null && root != localRoot && root.name.StartsWith("Traveller@Player"))
                {
                    TameRemoteRig(root);
                }

                // Plain remote rig (e.g. "Traveller 3", never "Traveller@Player 3"):
                // diagnose why it is not on screen, and fix the one known renderable
                // blocker - RemotePlayerLayerHack moves the rig to a RemotePlayer
                // layer, which the camera only draws if its culling mask includes it.
                if (!root.name.StartsWith("Traveller@Player"))
                {
                    DiagnoseRemoteRig(root);
                }
            }
        }

        private float nextRigDiag;

        /// <summary>
        /// Logs where the plain remote rig is and whether the camera can draw it,
        /// and adds its layer to the camera culling mask if missing (the rig's
        /// RemotePlayerLayerHack moves it to a RemotePlayer layer that nothing in
        /// the modded flow adds to the camera).
        /// </summary>
        private void DiagnoseRemoteRig(Transform root)
        {
            if (Time.unscaledTime < nextRigDiag)
            {
                return;
            }
            nextRigDiag = Time.unscaledTime + 5f;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            int enabledRenderers = 0;
            int rendererLayer = root.gameObject.layer;
            foreach (Renderer r in renderers)
            {
                if (r.enabled && r.gameObject.activeInHierarchy)
                {
                    enabledRenderers++;
                    rendererLayer = r.gameObject.layer;
                }
            }

            Camera cam = Camera.main;
            string camInfo = "no main camera";
            if (cam != null)
            {
                bool drawn = (cam.cullingMask & (1 << rendererLayer)) != 0;
                camInfo = "cameraDraws=" + drawn;

                if (!drawn)
                {
                    cam.cullingMask |= 1 << rendererLayer;
                    camInfo += " -> ADDED layer " + rendererLayer + " (" + LayerMask.LayerToName(rendererLayer) + ") to culling mask";
                }
            }

            Debug.Log("[WAReborn] remote rig '" + root.name + "' pos " + root.position
                      + " layer " + rendererLayer + " (" + LayerMask.LayerToName(rendererLayer) + ")"
                      + " renderers " + enabledRenderers + "/" + renderers.Length + " active " + root.gameObject.activeInHierarchy
                      + " | " + camInfo);
        }

        /// <summary>Names of movement/simulation scripts to disable on remote rigs.</summary>
        private static readonly string[] SimulationBehaviours =
        {
            "PlayerMove", "PlayerInput", "PuppetMaster", "BehaviourPuppet", "PlayerKnockout",
            "CharacterControls", "GrapplingHook", "PlayerGliding",
        };

        private void TameRemoteRig(Transform root)
        {
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
            {
                if (!body.isKinematic)
                {
                    body.isKinematic = true;
                    Debug.Log("[WAReborn] remote rig: made rigidbody kinematic on '" + body.gameObject.name + "'");
                }
            }

            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || !behaviour.enabled)
                {
                    continue;
                }
                string typeName = behaviour.GetType().Name;
                foreach (string sim in SimulationBehaviours)
                {
                    if (typeName == sim)
                    {
                        behaviour.enabled = false;
                        Debug.Log("[WAReborn] remote rig: disabled " + typeName + " on '" + behaviour.gameObject.name + "'");
                        break;
                    }
                }
            }
        }
    }
}

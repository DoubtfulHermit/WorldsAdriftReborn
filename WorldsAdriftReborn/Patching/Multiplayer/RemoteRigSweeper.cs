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

        private Transform LocalRoot()
        {
            LocalPlayer lp = LocalPlayer.Instance;
            if (lp != null)
            {
                return lp.transform.root;
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
            if (localRoot != null)
            {
                foreach (GameObject rootGo in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    Transform root = rootGo.transform;
                    if (!root.name.StartsWith("Traveller@Player") || root == localRoot)
                    {
                        continue;
                    }
                    TameRemoteRig(root);
                }
            }
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

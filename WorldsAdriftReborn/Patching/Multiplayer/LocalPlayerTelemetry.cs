using UnityEngine;

namespace WorldsAdriftReborn.Patching.Multiplayer
{
    /*
     * Diagnostic: logs the LOCAL player's actual motion so a fall can be
     * classified from evidence instead of guessed. The local rig is the one whose
     * CameraProxy claimed the camera (CameraProxy_Patch.OwnerRoot).
     *
     * Logs a heartbeat every ~0.5s, and IMMEDIATELY on any large per-frame jump
     * (teleport) or velocity spike (ejection) so the exact moment and kind of a
     * fall is captured. Distinguishes:
     *   - drifting down with low velocity  -> lost positioning / gravity, kinematic false
     *   - sudden big velocity              -> physics ejection (collision impulse)
     *   - instant position jump            -> teleport / reparent
     */
    internal class LocalPlayerTelemetry : MonoBehaviour
    {
        private Transform root;
        private Rigidbody body;
        private Vector3 lastPos;
        private float nextBeat;

        private void Acquire()
        {
            Transform owner = CameraProxy_Patch.OwnerRoot;
            if (owner == null)
            {
                return;
            }
            if (root != owner)
            {
                root = owner;
                body = root.GetComponent<Rigidbody>();
                lastPos = root.position;
                Debug.Log("[WAReborn] LocalPlayerTelemetry tracking '" + root.name + "' at " + root.position
                          + " kinematic=" + (body != null ? body.isKinematic.ToString() : "no-rb"));
            }
        }

        private void FixedUpdate()
        {
            Acquire();
            if (root == null)
            {
                return;
            }

            Vector3 pos = root.position;
            Vector3 delta = pos - lastPos;
            float step = delta.magnitude;

            // Immediate flag on a big move in one physics step.
            if (step > 3f)
            {
                Debug.Log("[WAReborn] LOCAL PLAYER JUMPED " + step.ToString("F1") + "m this step: "
                          + lastPos + " -> " + pos + " deltaY=" + delta.y.ToString("F1")
                          + " vel=" + (body != null ? body.velocity.ToString() : "n/a")
                          + " kinematic=" + (body != null ? body.isKinematic.ToString() : "n/a"));
            }
            lastPos = pos;

            if (Time.unscaledTime >= nextBeat)
            {
                nextBeat = Time.unscaledTime + 0.5f;
                Debug.Log("[WAReborn] local pos " + pos + " vel=" + (body != null ? body.velocity.ToString() : "n/a")
                          + " kinematic=" + (body != null ? body.isKinematic.ToString() : "n/a"));
            }
        }
    }
}

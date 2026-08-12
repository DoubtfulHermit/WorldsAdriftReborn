using System.Reflection;
using HarmonyLib;
using Improbable.CoreLibrary.CoordinateRemapping;
using Improbable.CoreLibrary.Transforms.Hierarchy;
using Improbable.Corelibrary.Math;
using Improbable.Corelibrary.Transforms;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Multiplayer
{
    /*
     * Moves a plain remote-player rig from its TransformState.
     *
     * The plain Traveller prefab has no CharacterTransformVisualizer (the proven
     * mover on the full rig). It positions itself exclusively through the
     * transform HIERARCHY system, which only applies positions relative to a
     * resolved parent entity - and while no parent resolves, relayed position
     * updates change nothing and the rig stays frozen at the seed default.
     *
     * This component polls the rig's injected TransformStateReader every frame:
     *  - Parent set:   stand back and let the game's hierarchy system work.
     *  - Parent empty:  apply LocalPosition/LocalRotation directly, remapped
     *                   global -> Unity exactly like CharacterTransformVisualizer
     *                   does on the full rig (which relayed data provably moved).
     *
     * The reader instance is taken by reflection from the rig's own
     * TransformChildHierarchyBehaviour ([Require]-injected there); a
     * runtime-added component would not get SDK injection itself.
     */
    internal class RemoteRigMover : MonoBehaviour
    {
        private static readonly FieldInfo ReaderField =
            AccessTools.Field(typeof(TransformChildHierarchyBehaviour), "TransformStateReader");

        private TransformStateReader reader;
        private Rigidbody rootBody;
        private bool loggedFirstApply;
        private int frameCounter;


        private void Update()
        {
            // Absolute safety: never touch the LOCAL player. If this component
            // ever lands on the local rig it would force it kinematic and drive
            // it from a remote stream - the infinite-fall bug. Verified by
            // local-only components, not by name.
            if (RemoteRigSweeper.IsLocalRig(transform.root))
            {
                enabled = false;
                return;
            }

            if (reader == null)
            {
                // The rig carries this behaviour TWICE (baked into the prefab and
                // re-added by the mod re-running preprocessors); both are usually
                // injected, but pin the first instance that actually has a reader
                // rather than trusting a fixed one.
                foreach (TransformChildHierarchyBehaviour hierarchy in GetComponentsInChildren<TransformChildHierarchyBehaviour>(true))
                {
                    reader = ReaderField?.GetValue(hierarchy) as TransformStateReader;
                    if (reader != null)
                    {
                        break;
                    }
                }

                if (reader == null)
                {
                    return; // reader not injected yet; retry next frame
                }

                rootBody = transform.root.GetComponent<Rigidbody>();
                Debug.Log("[WAReborn] RemoteRigMover on '" + transform.root.name + "': reader acquired.");
            }

            // A remote avatar must never be physics-simulated. Force the root
            // rigidbody kinematic UNCONDITIONALLY and first, so nothing can fall
            // under gravity no matter which positioner is (or is not) active. The
            // native kinematic path is gated on an AuthorityChanged event that
            // never fires for a never-authoritative remote, so the mod must do it.
            if (rootBody != null && !rootBody.isKinematic)
            {
                rootBody.isKinematic = true;
                Debug.Log("[WAReborn] RemoteRigMover '" + transform.root.name + "': root rigidbody made kinematic.");
            }

            frameCounter++;

            // Positioning is now owned by the game's own PlayerVisualizer (see
            // PlayerVisualizer_Patch), which interpolates the same relayed 190602
            // stream for smooth motion. Yielding to it is safe now that (a)
            // kinematic is forced above regardless, and (b) the patch runs only
            // PlayerVisualizer's global branch, never the Parent branch that
            // dropped the rig off-island before. RemoteRigMover stays as the
            // fallback positioner for the window before PlayerVisualizer enables
            // (or if it never does), so the avatar is never left frozen far away.
            if (nativePositioner == null)
            {
                nativePositioner = FindNativePositioner();
            }
            if (nativePositioner != null && nativePositioner.enabled && nativePositioner.gameObject.activeInHierarchy)
            {
                if (!yielded)
                {
                    yielded = true;
                    Debug.Log("[WAReborn] RemoteRigMover '" + transform.root.name + "': yielding positioning to PlayerVisualizer.");
                }
                return;
            }

            Vector3 unityPos = reader.LocalPosition.RemapGlobalToUnityVector();
            Quaternion unityRot = reader.LocalRotation.ToUnityQuaternion();

            transform.root.position = unityPos;
            transform.root.rotation = unityRot;

            if (!loggedFirstApply || frameCounter % 300 == 1)
            {
                loggedFirstApply = true;
                Debug.Log("[WAReborn] RemoteRigMover '" + transform.root.name + "': fallback global pos "
                          + unityPos + " (t=" + reader.Timestamp + ")");
            }
        }

        private MonoBehaviour nativePositioner;
        private bool yielded;

        /// <summary>The game's own remote positioner (PlayerVisualizer), by type name.</summary>
        private MonoBehaviour FindNativePositioner()
        {
            foreach (MonoBehaviour mb in transform.root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb != null && mb.GetType().Name == "PlayerVisualizer")
                {
                    return mb;
                }
            }
            return null;
        }
    }
}

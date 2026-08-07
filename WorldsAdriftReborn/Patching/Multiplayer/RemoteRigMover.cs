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

            bool parented = reader.Parent.HasValue;
            frameCounter++;

            if (parented)
            {
                // The hierarchy system owns positioning now. Log the transition
                // and periodic state so the logs show which mode the rig is in.
                if (frameCounter % 300 == 1)
                {
                    Debug.Log("[WAReborn] RemoteRigMover '" + transform.root.name + "': PARENTED to entity "
                              + reader.Parent.Value.parentId + ", hierarchy system in control. pos " + transform.root.position);
                }
                return;
            }

            // Live physics on the rig would jitter-fight these per-frame writes
            // (nothing else makes the plain rig kinematic; the tamer deliberately
            // skips plain rigs).
            if (rootBody != null && !rootBody.isKinematic)
            {
                rootBody.isKinematic = true;
                Debug.Log("[WAReborn] RemoteRigMover '" + transform.root.name + "': root rigidbody made kinematic.");
            }

            Vector3 unityPos = reader.LocalPosition.RemapGlobalToUnityVector();
            Quaternion unityRot = reader.LocalRotation.ToUnityQuaternion();

            transform.root.position = unityPos;
            transform.root.rotation = unityRot;

            if (!loggedFirstApply || frameCounter % 300 == 1)
            {
                loggedFirstApply = true;
                Debug.Log("[WAReborn] RemoteRigMover '" + transform.root.name + "': unparented, applying global pos "
                          + unityPos + " (raw " + reader.LocalPosition.ToString() + ", t=" + reader.Timestamp + ")");
            }
        }
    }
}

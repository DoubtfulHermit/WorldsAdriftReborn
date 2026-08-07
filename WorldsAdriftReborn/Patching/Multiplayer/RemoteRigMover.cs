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

            // A remote avatar must never be physics-simulated. Force the root
            // rigidbody kinematic UNCONDITIONALLY and first - a dynamic body falls
            // under gravity between our position writes (that was the bug: a prior
            // version made it kinematic only after an early return, so a rig the
            // native positioner did not cleanly own just dropped through the map).
            if (rootBody != null && !rootBody.isKinematic)
            {
                rootBody.isKinematic = true;
                Debug.Log("[WAReborn] RemoteRigMover '" + transform.root.name + "': root rigidbody made kinematic.");
            }

            frameCounter++;

            // Always position from the global TransformState. We do NOT defer to
            // the parenting/hierarchy branch: once 1073's movement writer is
            // active the sender may publish a Parent, but this flat single-island
            // world has no working parent hierarchy to reposition children, so
            // yielding left the rig stuck at its garbage seed position and it fell
            // through the map (one client raced into that path, the other did not
            // - hence the asymmetric fall). Treating every remote as global-
            // positioned is correct here: the island sits at the origin, so
            // parent-relative and global coincide.
            if (reader.Parent.HasValue && frameCounter % 300 == 1)
            {
                Debug.Log("[WAReborn] RemoteRigMover '" + transform.root.name + "': parent published ("
                          + reader.Parent.Value.parentId + ") but positioning globally anyway.");
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

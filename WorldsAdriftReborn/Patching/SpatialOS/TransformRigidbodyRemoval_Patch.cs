using System;
using HarmonyLib;
using Improbable.CoreLibrary.Transforms;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.SpatialOS
{
    /// <summary>
    /// Keeps dynamic checkout removal transactional on the retail client.
    ///
    /// The server's RemoveEntity operation disables every visualizer before it
    /// removes the entity from SpatialOS' universe.  Some ship/deck prefabs enter
    /// that disable path after their managed Rigidbody has already gone away.
    /// Retail's TransformManageRigidbodyBehaviour.RestoreRigidbody then throws a
    /// NullReferenceException, aborting EntityObject.Dispose halfway through.
    /// The stale entity remains in the universe and the subsequent AddEntity is
    /// rejected component-by-component as "already exists".
    ///
    /// RestoreRigidbody is cleanup-only while the object is being destroyed.  A
    /// missing Rigidbody cannot usefully be restored at that point, so suppress
    /// only the observed NullReferenceException at this narrow boundary.  The
    /// caller can then finish OnDisable and the normal universe/despawn cleanup.
    /// Any different failure remains visible.
    /// </summary>
    [HarmonyPatch(typeof(TransformManageRigidbodyBehaviour), "RestoreRigidbody")]
    internal static class TransformRigidbodyRemoval_Patch
    {
        private static bool _reported;

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            if (!(__exception is NullReferenceException))
            {
                return __exception;
            }

            if (!_reported)
            {
                _reported = true;
                Debug.LogWarning("[WAReborn] checkout cleanup: ignored a missing Rigidbody while"
                    + " removing an entity; disposal will continue. This warning logs once.");
            }

            return null;
        }
    }
}

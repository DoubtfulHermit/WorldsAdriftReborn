using System;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Performance
{
    /// <summary>
    /// Stops the restored <c>ShipCoreVisualizer</c> from throwing every frame on a
    /// LOOSE, deck-placed sky core.
    ///
    /// MEASURED live: 20,023 NullReferenceExceptions in one session -
    ///
    ///     ShipCoreVisualizer.LateUpdate()             (acs/ShipCoreVisualizer.cs:91-103)
    ///       -> ShipLiftVisualizer.get_Load()
    ///         -> ParentingMassAdderVisualizer.get_totalMass()
    ///
    /// The visualizer we restore via <c>SkyCoreSocketRestore</c> enables once its
    /// 1236/190602 readers inject, and its LateUpdate then queries the ship lift's
    /// Load. On a loose core the found <c>ShipLiftVisualizer</c> (the static test
    /// ship's) has an un-injected <c>ParentingMassAdderVisualizer</c>, so get_Load
    /// NREs - per frame, per core, which is both an exception-flood stutter source
    /// and log spam.
    ///
    /// A Harmony FINALIZER swallows only this method's exception: the core simply
    /// skips its glow update that frame (retail-inert), and the moment a real,
    /// fully-injected lift exists (built-ship flight) the vanilla path works
    /// unchanged. Rate-limited one-time log so a real config problem is still
    /// visible.
    /// </summary>
    [HarmonyPatch(typeof(ShipCoreVisualizer), "LateUpdate")]
    internal static class ShipCoreLateUpdate_Patch
    {
        private static bool _reported;

        private static Exception Finalizer(Exception __exception)
        {
            if (__exception != null && !_reported)
            {
                _reported = true;
                Debug.Log("[WAReborn] sky-core: LateUpdate threw (" + __exception.GetType().Name
                    + ") - suppressed; the core stays visually inert until a fully-injected"
                    + " ship lift exists. This logs once.");
            }
            return null; // swallow - never let the per-frame flood return
        }
    }
}

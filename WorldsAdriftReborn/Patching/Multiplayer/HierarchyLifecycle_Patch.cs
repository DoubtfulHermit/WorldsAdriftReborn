using System.Reflection;
using HarmonyLib;
using Improbable.CoreLibrary.Transforms.Hierarchy;
using UnityEngine;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftReborn.Patching.Multiplayer
{
    /// <summary>
    /// Makes the retail relative-parent workaround safe at the two lifecycle
    /// boundaries where its injected TransformState reader does not exist.
    ///
    /// Production evidence (2026-08-15): dynamic interest removal caused
    /// hundreds of NullReferenceExceptions in TransformChildHierarchyBehaviour
    /// OnEnable/OnDisable. RelativeParentTransformHack blindly toggles the
    /// behaviour every 0-2 seconds, including while EntityVisualizers is tearing
    /// the entity down. The base methods dereference TransformStateReader without
    /// a null check. Skipping an uninjected enable/disable is safe: there is no
    /// reader event to subscribe/unsubscribe, and a disposing entity has no state
    /// left to clean through that reader.
    /// </summary>
    internal static class HierarchyLifecycleGuard
    {
        internal static readonly FieldInfo ReaderField =
            AccessTools.Field(typeof(TransformChildHierarchyBehaviour), "TransformStateReader");

        private static bool _loggedSkippedLifecycle;
        private static bool _loggedSkippedHack;

        internal static bool HasUsableReader(TransformChildHierarchyBehaviour behaviour)
        {
            return HierarchyLifecyclePolicy.MayRunInjectedLifecycle(
                behaviour != null,
                behaviour != null && ReaderField?.GetValue(behaviour) != null,
                behaviour != null && behaviour.gameObject != null && behaviour.gameObject.activeInHierarchy);
        }

        internal static void LogLifecycleSkipOnce()
        {
            if (_loggedSkippedLifecycle)
            {
                return;
            }
            _loggedSkippedLifecycle = true;
            Debug.LogWarning("[WAR][hierarchy] skipped uninjected TransformChild hierarchy lifecycle"
                + " during entity activation/removal; prevented retail null-reader exception storm.");
        }

        internal static void LogHackSkipOnce()
        {
            if (_loggedSkippedHack)
            {
                return;
            }
            _loggedSkippedHack = true;
            Debug.LogWarning("[WAR][hierarchy] suppressed RelativeParentTransformHack toggle while"
                + " its hierarchy reader was unavailable.");
        }
    }

    [HarmonyPatch(typeof(TransformChildHierarchyBehaviour), "OnEnable")]
    internal static class TransformChildHierarchyOnEnable_Patch
    {
        private static bool Prefix(TransformChildHierarchyBehaviour __instance)
        {
            if (HierarchyLifecycleGuard.HasUsableReader(__instance))
            {
                return true;
            }
            HierarchyLifecycleGuard.LogLifecycleSkipOnce();
            return false;
        }
    }

    [HarmonyPatch(typeof(TransformChildHierarchyBehaviour), "OnDisable")]
    internal static class TransformChildHierarchyOnDisable_Patch
    {
        private static bool Prefix(TransformChildHierarchyBehaviour __instance)
        {
            if (HierarchyLifecycleGuard.HasUsableReader(__instance))
            {
                return true;
            }
            HierarchyLifecycleGuard.LogLifecycleSkipOnce();
            return false;
        }
    }

    // RelativeParentTransformChildHierarchyBehaviour overrides both lifecycle
    // methods and dereferences the same reader again after calling base. Patching
    // only the base method would therefore suppress the first dereference but
    // still throw in the override. Guard the complete override as well.
    [HarmonyPatch(typeof(global::RelativeParentTransformChildHierarchyBehaviour), "OnEnable")]
    internal static class RelativeTransformChildHierarchyOnEnable_Patch
    {
        private static bool Prefix(global::RelativeParentTransformChildHierarchyBehaviour __instance)
        {
            if (HierarchyLifecycleGuard.HasUsableReader(__instance))
            {
                return true;
            }
            HierarchyLifecycleGuard.LogLifecycleSkipOnce();
            return false;
        }
    }

    [HarmonyPatch(typeof(global::RelativeParentTransformChildHierarchyBehaviour), "OnDisable")]
    internal static class RelativeTransformChildHierarchyOnDisable_Patch
    {
        private static bool Prefix(global::RelativeParentTransformChildHierarchyBehaviour __instance)
        {
            if (HierarchyLifecycleGuard.HasUsableReader(__instance))
            {
                return true;
            }
            HierarchyLifecycleGuard.LogLifecycleSkipOnce();
            return false;
        }
    }

    [HarmonyPatch(typeof(global::RelativeParentTransformHack), "ApplyHack")]
    internal static class RelativeParentTransformHackApply_Patch
    {
        private static readonly FieldInfo BehaviourField =
            AccessTools.Field(typeof(global::RelativeParentTransformHack), "_behaviour");

        private static bool Prefix(object __instance)
        {
            var behaviour = BehaviourField?.GetValue(__instance) as TransformChildHierarchyBehaviour;
            if (HierarchyLifecycleGuard.HasUsableReader(behaviour))
            {
                return true;
            }
            HierarchyLifecycleGuard.LogHackSkipOnce();
            return false;
        }
    }
}

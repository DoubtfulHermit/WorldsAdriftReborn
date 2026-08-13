using System;
using HarmonyLib;
using Assets.Visualizers;
using Bossa.Travellers.Interact;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Flight
{
    /// <summary>
    /// Makes grabbing the helm actually fast: holding E on a mounted helm must
    /// seat the pilot in ~0.15 s.
    ///
    /// THE REAL TIMING PATH (traced in the decompile; the previous fix's
    /// premise was WRONG). The E-hold duration is fed to
    /// <c>TimedInteractionController.StartInteraction(time, ...)</c> - the ONLY
    /// hold timer for world interactables - from ONE call site:
    ///
    ///     InteractAgentObserver.CheckInteraction (decompile :339-419)
    ///         float interactTime = interactLookingAt.GetInteractTime(collider);  // :397
    ///         float time = flag2 ? interactTime + 10f : interactTime;            // :398
    ///         LocalPlayer.Instance.timedInteractionController
    ///             .StartInteraction(time, InputButtons.Interact, ...);           // :400
    ///
    /// Two independent leaks make the hold long:
    ///
    ///   1. GetInteractTime itself: with an InteractiveObjectVerbOverrider on the
    ///      collider its Time wins, else the 1210 InteractionEntry.timeToUse
    ///      captured at OnEnable wins. ASSET-VERIFIED (UnityPy on
    ///      resources.assets): Helm01 carries NO overrider anywhere - the old
    ///      "prefab overrider wins" theory is dead - and its
    ///      InteractiveObjectVisualizer's serialized Verb is 3 (Man), so the time
    ///      is whatever 1210 entry the visualizer captured, which for a mounted
    ///      helm can be stale or a non-helm default.
    ///
    ///   2. The +10 s "non-friendly ship" penalty at :398: flag2 is true whenever
    ///      ShipPartVisualizer.IsShipPartInFriendlyShip resolves false for the
    ///      mounted part - a single missing/mismatched ownership datum (the 4349
    ///      reviverInfosCache uid vs LocalPlayerInit.PlayerId) and every helm
    ///      grab quietly gains TEN SECONDS. The previous GetInteractTime-only
    ///      clamp (which DID apply - the live log shows "Patching completed
    ///      successfully") could never touch this: it fed 0.15 s in and the
    ///      observer added 10 on top, which is exactly a "still long" hold.
    ///
    /// THE FIX therefore clamps at BOTH seams:
    ///   - GetInteractTime postfix: clamps the resolved time for verb Man
    ///     (resolved via the PUBLIC GetVerb(collider), which consults the
    ///     overrider first and the serialized Verb otherwise - no reflection),
    ///     and stamps the frame so the second seam knows this frame resolved a
    ///     Man interaction.
    ///   - StartInteraction prefix: on the SAME frame (same call stack - :397 to
    ///     :400 is synchronous), clamps the FINAL time. This is the last write
    ///     before the timer runs, so nothing upstream - the +10 penalty included -
    ///     can lengthen the hold. Other StartInteraction callers (food, crafting)
    ///     are untouched: no Man resolve this frame, no clamp.
    ///
    /// Every action is logged once (armed at patch time, grep "[WAR][helm]") and
    /// rate-limited when it fires, so the next live session proves the seam from
    /// the log alone.
    /// </summary>
    internal static class HelmManHoldPolicy
    {
        internal const float MaxManHoldSeconds = 0.15f;

        /// <summary>Frame stamp of the last GetInteractTime that resolved verb Man.</summary>
        internal static int LastManResolveFrame = -1;

        /// <summary>What GetInteractTime returned that frame (post-clamp), for the penalty diagnosis log.</summary>
        internal static float LastManInteractTime = -1f;

        private static float _nextLogTime;

        /// <summary>Rate-limited logger: at most one line per 2 s, never throws.</summary>
        internal static void Log(string message)
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextLogTime)
            {
                return;
            }
            _nextLogTime = now + 2f;
            Debug.Log(message);
        }
    }

    [HarmonyPatch(typeof(InteractiveObjectVisualizer), nameof(InteractiveObjectVisualizer.GetInteractTime))]
    internal static class HelmInteractTime_Patch
    {
        private static bool _armedLogged;

        private static bool Prepare()
        {
            if (!_armedLogged)
            {
                _armedLogged = true;
                Debug.Log("[WAR][helm] Man-hold clamp ARMED: InteractiveObjectVisualizer.GetInteractTime postfix.");
            }
            return true;
        }

        private static void Postfix(InteractiveObjectVisualizer __instance, Collider collider, ref float __result)
        {
            try
            {
                if (__instance == null || __instance.GetVerb(collider) != InteractVerb.Man)
                {
                    return;
                }
                HelmManHoldPolicy.LastManResolveFrame = Time.frameCount;
                if (__result > HelmManHoldPolicy.MaxManHoldSeconds)
                {
                    HelmManHoldPolicy.Log("[WAR][helm] Man interact-time clamped at the visualizer: "
                        + __result.ToString("F2") + "s -> " + HelmManHoldPolicy.MaxManHoldSeconds.ToString("F2")
                        + "s (1210/overrider fed a long time).");
                    __result = HelmManHoldPolicy.MaxManHoldSeconds;
                }
                HelmManHoldPolicy.LastManInteractTime = __result;
            }
            catch (Exception)
            {
                // never break the interact flow over a clamp
            }
        }
    }

    [HarmonyPatch(typeof(TimedInteractionController), nameof(TimedInteractionController.StartInteraction))]
    internal static class HelmManHoldTimer_Patch
    {
        private static bool _armedLogged;

        private static bool Prepare()
        {
            if (!_armedLogged)
            {
                _armedLogged = true;
                Debug.Log("[WAR][helm] Man-hold clamp ARMED: TimedInteractionController.StartInteraction prefix.");
            }
            return true;
        }

        private static void Prefix(ref float time)
        {
            try
            {
                // Only the StartInteraction issued by the SAME frame's Man resolve
                // (InteractAgentObserver.CheckInteraction is synchronous between the
                // two calls). Food/crafting/placement holds never see a clamp.
                if (HelmManHoldPolicy.LastManResolveFrame != Time.frameCount
                    || time <= HelmManHoldPolicy.MaxManHoldSeconds)
                {
                    return;
                }
                float resolved = HelmManHoldPolicy.LastManInteractTime;
                bool penalty = resolved >= 0f && time >= resolved + 9.5f;
                HelmManHoldPolicy.Log("[WAR][helm] Man E-hold clamped at the timer: "
                    + time.ToString("F2") + "s -> " + HelmManHoldPolicy.MaxManHoldSeconds.ToString("F2")
                    + "s (interact-time was " + (resolved >= 0f ? resolved.ToString("F2") : "?")
                    + "s; the +10s non-friendly penalty was "
                    + (penalty ? "PRESENT - ship ownership did not resolve to this player" : "not present") + ").");
                time = HelmManHoldPolicy.MaxManHoldSeconds;
            }
            catch (Exception)
            {
                // never break the interact flow over a clamp
            }
        }
    }
}

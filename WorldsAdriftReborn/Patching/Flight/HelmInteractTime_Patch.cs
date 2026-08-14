using System;
using HarmonyLib;
using Assets.Scripts.Visualisers.Ship;
using Assets.Visualizers;
using Bossa.Travellers.Interact;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Flight
{
    /// <summary>
    /// Makes every ship-part interaction fast. Helm, sail, lamp, horn, and any
    /// subsequently enabled ship-part verb complete their E hold in at most 0.15 s.
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
    ///   - GetInteractTime postfix: recognizes the shared ShipPartVisualizer rather
    ///     than maintaining a fragile verb/prefab allow-list, then stamps the frame.
    ///   - StartInteraction prefix: on the SAME frame (same call stack - :397 to
    ///     :400 is synchronous), clamps the FINAL time. This is the last write
    ///     before the timer runs, so nothing upstream - the +10 penalty included -
    ///     can lengthen the hold. Other StartInteraction callers (food, crafting)
    ///     are untouched: no eligible control resolved this frame, no clamp.
    ///
    /// Every action is logged once (armed at patch time, grep "[WAR][ship-control]") and
    /// rate-limited when it fires, so the next live session proves the seam from
    /// the log alone.
    /// </summary>
    internal static class ShipControlHoldState
    {
        /// <summary>Frame stamp of the last eligible ship-part GetInteractTime.</summary>
        internal static int LastResolveFrame = -1;

        /// <summary>What GetInteractTime returned that frame (post-clamp), for the penalty diagnosis log.</summary>
        internal static float LastInteractTime = -1f;

        /// <summary>Human-readable control kind for the rate-limited proof line.</summary>
        internal static string LastControl = "ship control";

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
                Debug.Log("[WAR][ship-control] all ship-part hold clamp ARMED: "
                    + "InteractiveObjectVisualizer.GetInteractTime postfix.");
            }
            return true;
        }

        private static void Postfix(InteractiveObjectVisualizer __instance, Collider collider, ref float __result)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                ShipPartVisualizer shipPart = __instance.GetComponent<ShipPartVisualizer>()
                    ?? __instance.GetComponentInParent<ShipPartVisualizer>();
                if (shipPart == null)
                {
                    return;
                }

                InteractVerb verb = __instance.GetVerb(collider);
                ShipControlHoldState.LastResolveFrame = Time.frameCount;
                ShipControlHoldState.LastControl = "ship part " + verb;
                float clamped = WorldsAdriftRebornGameServer.Multiplayer.Ship.ShipInteractionHoldPolicy
                    .Clamp(true, __result);
                if (clamped != __result)
                {
                    ShipControlHoldState.Log("[WAR][ship-control] " + ShipControlHoldState.LastControl
                        + " interact-time clamped at the visualizer: "
                        + __result.ToString("F2") + "s -> " + clamped.ToString("F2")
                        + "s (1210/overrider fed a long time).");
                    __result = clamped;
                }
                ShipControlHoldState.LastInteractTime = __result;
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
                Debug.Log("[WAR][ship-control] all ship-part hold clamp ARMED: "
                    + "TimedInteractionController.StartInteraction prefix.");
            }
            return true;
        }

        private static void Prefix(ref float time)
        {
            try
            {
                // Only the StartInteraction issued by the SAME frame's ship-part resolve
                // (InteractAgentObserver.CheckInteraction is synchronous between the
                // two calls). Food/crafting/placement holds never see a clamp.
                if (ShipControlHoldState.LastResolveFrame != Time.frameCount)
                {
                    return;
                }
                float clamped = WorldsAdriftRebornGameServer.Multiplayer.Ship.ShipInteractionHoldPolicy
                    .Clamp(true, time);
                if (clamped == time)
                {
                    return;
                }
                float resolved = ShipControlHoldState.LastInteractTime;
                bool penalty = resolved >= 0f && time >= resolved + 9.5f;
                ShipControlHoldState.Log("[WAR][ship-control] " + ShipControlHoldState.LastControl
                    + " E-hold clamped at the timer: "
                    + time.ToString("F2") + "s -> " + clamped.ToString("F2")
                    + "s (interact-time was " + (resolved >= 0f ? resolved.ToString("F2") : "?")
                    + "s; the +10s non-friendly penalty was "
                    + (penalty ? "PRESENT - ship ownership did not resolve to this player" : "not present") + ").");
                time = clamped;
            }
            catch (Exception)
            {
                // never break the interact flow over a clamp
            }
        }
    }
}

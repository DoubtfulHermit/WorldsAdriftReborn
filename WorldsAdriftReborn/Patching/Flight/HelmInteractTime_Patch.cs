using System;
using HarmonyLib;
using Assets.Visualizers;
using Bossa.Travellers.Interact;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Flight
{
    /// <summary>
    /// Makes grabbing the helm actually fast. The E-hold duration is NOT the
    /// server's 1210 <c>timeToUse</c> whenever the prefab bakes an
    /// <c>InteractiveObjectVerbOverrider</c>:
    ///
    ///     GetInteractTime(collider):
    ///         overrider = collider.GetComponentInParent&lt;InteractiveObjectVerbOverrider&gt;()
    ///         if (overrider != null) return overrider.Time;      // prefab wins
    ///         return _interaction.timeToUse;                     // server value
    ///     (acs/Assets.Visualizers/InteractiveObjectVisualizer.cs:104-112)
    ///
    /// Helm01 bakes one with a long Time, so the server-side 0.5s -> 0.15s change
    /// could never land - measured live as "grabbing takes as long as usual".
    /// Clamp the resolved time for the Man verb only; every other interaction
    /// keeps its authored duration.
    /// </summary>
    [HarmonyPatch(typeof(InteractiveObjectVisualizer), "GetInteractTime")]
    internal static class HelmInteractTime_Patch
    {
        private const float MaxManHoldSeconds = 0.15f;

        // Verb is a PRIVATE field on the visualizer (decompile :26) - cached reflection.
        private static readonly System.Reflection.FieldInfo VerbField =
            AccessTools.Field(typeof(InteractiveObjectVisualizer), "Verb");

        private static void Postfix(InteractiveObjectVisualizer __instance, ref float __result)
        {
            try
            {
                if (__instance == null || VerbField == null || __result <= MaxManHoldSeconds)
                {
                    return;
                }
                if (VerbField.GetValue(__instance) is InteractVerb verb && verb == InteractVerb.Man)
                {
                    __result = MaxManHoldSeconds;
                }
            }
            catch (Exception)
            {
                // never break the interact flow over a clamp
            }
        }
    }
}

using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Ship
{
    /// <summary>
    /// Keeps the modular engine's presentation synchronized with the stock
    /// ShipEngineState reader even when its generated update event was missed while
    /// the asynchronous modular propeller hierarchy was still being assembled.
    ///
    /// The value remains component 1116's authoritative CurrentPercentSpin. This
    /// patch does not infer throttle, author force, or bypass the generated reader.
    /// EngineVisualizer already polls that reader every frame for audio; the stock
    /// VFX path otherwise receives it only from CurrentPercentSpinUpdated.
    /// </summary>
    [HarmonyPatch]
    internal static class EngineVisualStateRefresh_Patch
    {
        private static readonly System.Type EngineVisualizerType =
            AccessTools.TypeByName("Assets.Visualizers.EngineVisualizer");
        private static readonly PropertyInfo SpinPctProperty =
            AccessTools.Property(EngineVisualizerType, "SpinPct");
        private static readonly FieldInfo EngineVfxField =
            AccessTools.Field(EngineVisualizerType, "_engineVFX");

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(EngineVisualizerType, "Update");
        }

        private static void Postfix(object __instance)
        {
            if (__instance == null || !WorldsAdrift.IsClient)
            {
                return;
            }

            EngineVFX engineVfx = EngineVfxField == null
                ? null
                : EngineVfxField.GetValue(__instance) as EngineVFX;
            if (engineVfx != null && SpinPctProperty != null)
            {
                engineVfx.SpinPct = (float)SpinPctProperty.GetValue(__instance, null);
            }
        }
    }
}

using HarmonyLib;

namespace WorldsAdriftReborn.Patching.Performance
{
    // Load-in framerate flood #2: the "was not previously disabled, cannot enable" warning
    // storm (~4,500x per load-in on a measured trace).
    //
    // Source of the message (EntityVisualizers.cs:119-138, TryEnableVisualizers):
    //
    //   if (IsMarkedAsDisabled(monoBehaviour)) { ...enable... }
    //   else Improbable.Unity.Logging.Debug.LogWarningFormat(
    //            "Visualiser {0} was not previously disabled, cannot enable.",
    //            monoBehaviour.GetType().Name);
    //
    // It fires while TransformChildHierarchyBehaviour flips HierarchyMode during parent
    // switches (SetNoParent / OnParentUpdated -> CurrentMode setter -> ChangeVisualizersForMode
    // -> TryEnableVisualizers), for StaticLocalTransformBehaviour (which is [DontAutoEnable],
    // so it is frequently already-enabled/never-disabled at the moment enable is requested).
    //
    // The mode switch itself is HARMLESS: for an already-enabled visualiser TryEnableVisualizers
    // does nothing but log - num stays 0, TriggerRequiredComponentsPotentiallyChanged is not
    // called, no state changes. The ONLY cost is the warning, and because warnings keep full
    // stack traces (see WorldsAdriftReborn.QuietenOrdinaryLogStackTraces, which deliberately
    // strips traces ONLY from plain Debug.Log), each of those ~4,500 warnings walks and formats
    // a full stack trace on the main thread during load-in.
    //
    // Chosen approach: (a) suppress THIS one benign message at the logging seam, rather than
    // (b) rewriting TryEnableVisualizers' control flow. Reasoning:
    //   * Lowest risk - it does not touch the enable/disable path or any visualiser state; it
    //     only prevents a log call whose result is thrown away anyway.
    //   * Narrowest - it matches the exact, unique format string of this single call site
    //     (the string appears nowhere else in the decompiled client), so every other warning,
    //     including any real one routed through the same method, still logs with its full trace.
    //   * Approach (b) would have to reimplement the enable loop against private members
    //     (disabledVisualizers, IsMarkedAsDisabled, EnableVisualizer), i.e. more surface area
    //     for no additional benefit since the loop's only observable effect is this log line.
    //
    // Improbable.Unity.Logging.Debug.LogWarningFormat is a public static method that forwards to
    // the registered ILogger (WASpatialLogger -> WALogger.Warn -> UnityEngine.Debug.LogWarning,
    // which is where the trace is captured). Returning false from the prefix skips the whole
    // forward, killing the trace generation at the earliest common point.
    [HarmonyPatch(typeof(Improbable.Unity.Logging.Debug),
        nameof(Improbable.Unity.Logging.Debug.LogWarningFormat))]
    internal static class VisualizerEnableWarning_Patch
    {
        // Exact format string from EntityVisualizers.TryEnableVisualizers - unique in the client.
        private const string SuppressedFormat =
            "Visualiser {0} was not previously disabled, cannot enable.";

        // __0 = the 'message' (format string) argument. Positional injection is used so a
        // decompiled parameter-name mismatch can never silently disable the guard.
        [HarmonyPrefix]
        public static bool LogWarningFormat_Prefix(string __0)
        {
            // Return false ONLY for this exact benign message => skip the log + its stack trace.
            // Every other warning returns true and logs normally.
            return __0 != SuppressedFormat;
        }
    }
}

using HarmonyLib;

namespace WorldsAdriftReborn.Patching.Flight
{
    /// <summary>
    /// The world has NOT ended. Retail's shutdown event ("End of the World") drove
    /// <c>EndOfTheWorldConfig.AtlasMultiplier</c> - a live countdown from OutroDate
    /// toward ApocalypseDate that fades every atlas core's lift to ZERO:
    ///
    ///     AtlasMultiplier => 1.0 - Clamp01(InverseLerp(0.9, 1.0, elapsed/duration))
    ///     (acs/EndOfTheWorldConfig.cs:28-36)
    ///     ShipLiftVisualizer.TotalLift => AtlasMultiplier * state.TotalLift   (:12)
    ///
    /// Running in 2026, the countdown expired years ago, so on an unmodified client
    /// every ship's TotalLift is 0, every ship is permanently "overloaded", and
    /// <c>ShipControlsBehaviour.UpdateVertical</c> EXITS before reading the
    /// LShift/LCtrl vertical inputs (acs/ShipControlsBehaviour.cs:268-292) - the
    /// live "can't go up and down" report. In this revival the apocalypse has not
    /// happened: pin the multiplier at 1.
    /// </summary>
    [HarmonyPatch(typeof(EndOfTheWorldConfig), "AtlasMultiplier", MethodType.Getter)]
    internal static class EndOfTheWorld_Patch
    {
        private static bool Prefix(ref float __result)
        {
            __result = 1f;
            return false; // the countdown never runs
        }
    }
}

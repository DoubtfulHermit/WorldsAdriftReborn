using System.Reflection;
using Assets.Scripts.Player.Utilities;
using HarmonyLib;

namespace WorldsAdriftReborn.Patching.InGameChanges
{
    // gives glider infinite energy
    [HarmonyPatch(typeof(Glider))]
    internal class Glider_Patch
    {
        // Cached: this prefix runs every frame while gliding, and the field
        // lookup was re-resolved per call. (SetValue still boxes the float;
        // one small box per gliding frame is acceptable, re-resolving the
        // FieldInfo was not.)
        private static readonly FieldInfo EnergyField = AccessTools.Field(typeof(Glider), "energy");
        private static readonly object BoxedFullEnergy = 1f;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Glider), "EvaluateControlState")]
        public static void EvaluateControlState_Prefix(Glider __instance, ref UserControlCharacter.State state )
        {
            EnergyField.SetValue(__instance, BoxedFullEnergy);
        }
    }
}

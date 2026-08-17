using System.Collections.Generic;
using System.Reflection;
using Assets.Scripts.Player;
using Bossa.Travellers.Inventory;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Performance
{
    // Load-in framerate flood #1: GearWearablesVisualizer.UpdateActiveWornItemsHealths
    // throws KeyNotFoundException every frame (~3,600x per load-in on a measured trace).
    //
    // The stock method (GearWearablesVisualizer.cs:135-145) is:
    //
    //   for (int i = 0; i < _wearableUtilsState.Data.itemIds.Count; i++)
    //   {
    //       int key = _wearableUtilsState.Data.itemIds[i];
    //       if (_wearableUtilsState.Data.active[i])
    //           _utilityIdToUtility[key].CurrentHealth = Mathf.Max(_utilityIdToUtility[key].CurrentHealth - Time.deltaTime, 0f);
    //   }
    //
    // _utilityIdToUtility is only populated in RegisterWearables() for worn items whose
    // slot AND a valid "totalHealth" meta both resolve. Any active WearableUtilsState
    // (component 1280) itemId that never registered - a common case during load-in while
    // inventory/wearable state streams in - hits the unguarded Dictionary indexer and
    // throws. The throw is caught upstream and re-thrown next frame, so it recurs every
    // Update(); each throw carries a full stack trace, which is the CPU cost.
    //
    // Fix (genuine client defect fix): a prefix that runs the guarded equivalent - the
    // ContainsKey the client simply forgot - and returns false to skip the original.
    // Worn-durability for genuinely-registered items (e.g. the glider) still ticks down
    // exactly as before; only the missing-key iterations are skipped instead of throwing.
    //
    // Both accessed fields are private, so they are read by cached reflection. If reflection
    // ever fails to resolve them we fall back to the original method (return true) rather
    // than swallow the tick - a patch failure never makes things worse than stock.
    [HarmonyPatch(typeof(GearWearablesVisualizer), "UpdateActiveWornItemsHealths")]
    internal static class GearWearablesHealth_Patch
    {
        private static readonly FieldInfo UtilityIdToUtilityField =
            AccessTools.Field(typeof(GearWearablesVisualizer), "_utilityIdToUtility");

        private static readonly FieldInfo WearableUtilsStateField =
            AccessTools.Field(typeof(GearWearablesVisualizer), "_wearableUtilsState");

        [HarmonyPrefix]
        public static bool UpdateActiveWornItemsHealths_Prefix(GearWearablesVisualizer __instance)
        {
            // If we cannot reach the private fields, do nothing clever: let the original run.
            if (UtilityIdToUtilityField == null || WearableUtilsStateField == null)
            {
                return true;
            }

            try
            {
                var map = UtilityIdToUtilityField.GetValue(__instance) as Dictionary<int, UtilityItem>;
                var reader = WearableUtilsStateField.GetValue(__instance) as WearableUtilsState.Reader;
                if (map == null || reader == null)
                {
                    // State not bound yet this frame; nothing to tick. Skip the original
                    // (which would NRE on _wearableUtilsState.Data) - this is strictly safer.
                    return false;
                }

                WearableUtilsStateData data = reader.Data;
                var itemIds = data.itemIds;
                var active = data.active;
                if (itemIds == null || active == null)
                {
                    return false;
                }

                int count = itemIds.Count;
                for (int i = 0; i < count && i < active.Count; i++)
                {
                    if (!active[i])
                    {
                        continue;
                    }

                    int key = itemIds[i];
                    // THE GUARD the stock client lacks: only tick registered utilities.
                    if (!map.ContainsKey(key))
                    {
                        continue;
                    }

                    UtilityItem utility = map[key];
                    if (utility != null)
                    {
                        utility.CurrentHealth = Mathf.Max(utility.CurrentHealth - Time.deltaTime, 0f);
                    }
                }

                // Handled here; do not run the unguarded original.
                return false;
            }
            catch
            {
                // Anything unexpected: fall back to stock behaviour, never worse than before.
                return true;
            }
        }
    }
}

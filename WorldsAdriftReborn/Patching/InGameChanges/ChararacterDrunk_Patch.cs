using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.InGameChanges
{
    /*
     * ChararacterDrunk (Bossa's own typo) is a non-converging retry loop. A real
     * session logged 60,427 NullReferenceExceptions out of 60,514 total - 99.8%
     * of every NRE in the log - one per frame, from log line 2,617 to line
     * 1,559,193, which is the last line of the file. It never settles.
     *
     * The shape:
     *
     *     Update()          num = LocalPlayer.Instance.PlayerBuffBehaviour.DrunkLevel;
     *                       if (_lastDrunkLevel != num) SetDrunkLevel(num);
     *
     *     SetDrunkLevel()   ... resolve _puppetSnapshots off LocalPlayer.playerKnockout
     *                       DrunkEffectCamera.Instance.SetDrunkLevel(drunkLevel);   <-- throws
     *                       ... 20 more lines ...
     *                       _lastDrunkLevel = drunkLevel;                           <-- never reached
     *
     * The assignment that would stop the retry is the LAST statement of the
     * method, behind every dereference. One missing dependency therefore does
     * not degrade the feature, it pins _lastDrunkLevel at -1 forever and makes
     * Update call straight back into the throw on the very next frame.
     *
     * WHAT IS MISSING HERE
     * Proven from the log: the throw is inside SetDrunkLevel and happens BEFORE
     * it reaches DrunkEffectCamera.SetDrunkLevel - across all 60,427 traces the
     * string "DrunkEffectCamera" never appears in a single stack frame, and the
     * whole 92 MB log contains it zero times. So DrunkEffectCamera.Instance is
     * null: nothing ever ran DrunkEffectCamera.Awake, which is the only thing
     * that assigns that static. Unity's release traces carry no IL offsets, so
     * the exact dereference cannot be pinned further from the log alone, and the
     * two lines around it (LocalPlayer.playerKnockout, which NREs through
     * _visualizers, and _drunkState.muscleGroup) are candidates on the same
     * evidence. The guard below therefore covers all of them and names the one
     * it actually found, once, so the next session settles it for good.
     *
     * A separate, much smaller cluster (70 NREs, all during boot) has Update
     * itself as the top frame - that is LocalPlayer.Instance.PlayerBuffBehaviour
     * before LocalPlayer._visualizers is populated. Same class, same session,
     * so it is guarded here too.
     *
     * WHY THE FEATURE IS NOT DELETED
     * Drunkenness is real game content. Everything here is a no-op that only
     * engages while a dependency is absent: when DrunkEffectCamera and the
     * puppet rig exist, both prefixes return true and the stock code runs
     * completely untouched, with _lastDrunkLevel left exactly as stock left it.
     *
     * WHY Update IS THE PRIMARY GUARD
     * Update is the only per-frame entry point, so guarding it stops the loop at
     * the source: when a dependency is missing we pay one static field compare
     * per frame and SetDrunkLevel is never entered at all. It also mutates no
     * state - _lastDrunkLevel keeps its stock value, so the instant the missing
     * piece appears the stock code resumes from a clean -1 and behaves as if
     * this patch had never existed.
     *
     * Guarding SetDrunkLevel alone would be strictly worse. Returning false
     * there still leaves _lastDrunkLevel at -1, so Update keeps calling it every
     * frame anyway - same per-frame cost, and it cannot see the Update-side
     * NREs. And "fixing" convergence by writing _lastDrunkLevel from a prefix
     * would corrupt the exact flag that gates RemoveSnapshotModifier, making the
     * stock code later remove a snapshot modifier that was never added.
     *
     * SetDrunkLevel still gets the same guard as a secondary, because OnDisable
     * calls it directly and bypasses Update. That path is not per-frame, so it
     * costs nothing in the steady state.
     */
    [HarmonyPatch(typeof(ChararacterDrunk))]
    internal class ChararacterDrunk_Patch
    {
        private const string None = null;

        // Read only from inside SetDrunkLevel, never per frame from Update.
        private static readonly FieldInfo DrunkStateField =
            AccessTools.Field(typeof(ChararacterDrunk), "_drunkState");

        private static string lastReason;
        private static bool loggedSwallow;

        /// <summary>
        /// Name of the first dependency SetDrunkLevel would dereference and not
        /// find, in the order the stock method touches them, or null when the
        /// drunk effect can run. Deliberately ordered cheapest-and-most-likely
        /// first: in the broken case this costs a single static field compare.
        /// </summary>
        private static string MissingDependency()
        {
            try
            {
                // The confirmed one. Unity's overloaded == also catches a
                // destroyed component, which a plain null check would miss.
                if (DrunkEffectCamera.Instance == null)
                {
                    return "DrunkEffectCamera.Instance";
                }

                // Off stack, Update reads the _debugDrunkLevel int and
                // SetDrunkLevel uses FindObjectOfType, so nothing below can NRE.
                if (WorldsAdrift.IsOnStack)
                {
                    // LocalPlayer.Exists is Instance != null && _visualizers != null,
                    // and every accessor below goes through _visualizers unguarded.
                    if (!LocalPlayer.Exists)
                    {
                        return "LocalPlayer";
                    }
                    if (LocalPlayer.Instance.PlayerBuffBehaviour == null)
                    {
                        return "LocalPlayer.PlayerBuffBehaviour";
                    }
                    if (LocalPlayer.Instance.playerKnockout == null)
                    {
                        return "LocalPlayer.playerKnockout";
                    }
                }

                return None;
            }
            catch (Exception e)
            {
                // A dependency we cannot even probe is a dependency that is not
                // there. Never let the guard itself become the new thrower.
                return "unreadable (" + e.GetType().Name + ": " + e.Message + ")";
            }
        }

        /// <summary>
        /// Logs a reason at most once per distinct reason, so a missing
        /// dependency costs one line per session instead of one per frame.
        /// </summary>
        private static void Report(string reason)
        {
            if (reason == lastReason)
            {
                return;
            }
            lastReason = reason;

            if (reason == None)
            {
                Debug.Log("[WAReborn] drunk-effect dependencies present again, running stock ChararacterDrunk.");
            }
            else
            {
                Debug.LogWarning("[WAReborn] drunk-effect disabled, missing " + reason
                                 + " - ChararacterDrunk left as a no-op instead of throwing every frame.");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ChararacterDrunk), "Update")]
        public static bool Update_Prefix()
        {
            string missing = MissingDependency();
            Report(missing);
            return missing == None;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ChararacterDrunk), "SetDrunkLevel")]
        public static bool SetDrunkLevel_Prefix(ChararacterDrunk __instance)
        {
            string missing = MissingDependency();
            if (missing == None)
            {
                missing = MissingDrunkState(__instance);
            }
            Report(missing);
            return missing == None;
        }

        /// <summary>
        /// _drunkState.muscleGroup is dereferenced for its Length two lines after
        /// the camera call and is the third NRE candidate. Checked here rather
        /// than in Update because it needs reflection: in a healthy client
        /// SetDrunkLevel only runs when the drunk level actually changes, so
        /// this is not a per-frame cost.
        /// </summary>
        private static string MissingDrunkState(ChararacterDrunk instance)
        {
            if (DrunkStateField == null)
            {
                // Field renamed by a future game build - do not block the
                // feature over a probe we can no longer perform.
                return None;
            }

            try
            {
                PuppetSnapshots.PuppetSnapshotOverriderState state =
                    (PuppetSnapshots.PuppetSnapshotOverriderState)DrunkStateField.GetValue(instance);
                return state.muscleGroup == null ? "ChararacterDrunk._drunkState.muscleGroup" : None;
            }
            catch (Exception e)
            {
                return "unreadable (_drunkState: " + e.GetType().Name + ")";
            }
        }

        /// <summary>
        /// Backstop. The prefix covers every dereference we could identify from
        /// the decompile, but the whole point of this bug is that one unhandled
        /// throw here costs 60,000 log entries and 200,000 stack-trace lines.
        /// If SetDrunkLevel still throws for a reason we did not predict, absorb
        /// it and say so once, so the log can never fill up like that again.
        /// Returning null suppresses the exception.
        /// </summary>
        [HarmonyFinalizer]
        [HarmonyPatch(typeof(ChararacterDrunk), "SetDrunkLevel")]
        public static Exception SetDrunkLevel_Finalizer(Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            if (!loggedSwallow)
            {
                loggedSwallow = true;
                Debug.LogWarning("[WAReborn] drunk-effect threw past the dependency guard, suppressing it"
                                 + " for the rest of the session: " + __exception);
            }

            return null;
        }
    }
}

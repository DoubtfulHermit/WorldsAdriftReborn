using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WorldsAdriftReborn.Config;

namespace WorldsAdriftReborn.Patching.Dynamic.HookConfig
{
    internal class WAConfig_Patch
    {
        private static readonly System.Collections.Generic.HashSet<string> loggedUntouched =
            new System.Collections.Generic.HashSet<string>();

        // Reload the cfg from disk AT MOST once every 5 seconds, lazily, instead
        // of on every WAConfig.Get. The game calls Get at frame frequency - the
        // same two-client session that produced 327,713 log lines here also did
        // 327,713 SYNCHRONOUS DISK READS + full cfg re-parses on the main
        // thread, one per call. The values served here are launcher-written and
        // effectively static for a session; 5 s keeps "edit the cfg while the
        // game runs" working for diagnosis without the per-frame file I/O.
        private static float nextReloadAt;

        private static void ReloadIfStale()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < nextReloadAt)
            {
                return;
            }
            nextReloadAt = now + 5f;
            ModSettings.modConfig.Reload();
        }

        [HarmonyPatch()]
        class Get_String
        {
            [HarmonyTargetMethod]
            public static MethodBase GetTargetMethod()
            {
                return AccessTools.Method(
                                            AccessTools.TypeByName("WAConfig"),
                                            "Get",
                                            new Type[]
                                            {
                                            typeof(string)
                                            }).MakeGenericMethod(typeof(string));
            }

            [HarmonyPrefix]
            public static bool Get_Prefix( ref string __result, string key )
            {
                ReloadIfStale();

                if (key == "BossaNet.RestServerUrl")
                {
                    __result = ModSettings.restServerUrl.Value;
                    return false;
                }
                else if (key == "BossaNet.DeploymentStatusUrl")
                {
                    __result = ModSettings.restServerDeploymentUrl.Value;
                    return false;
                }
                else if (key == "Bootstrap.NtpServer")
                {
                    __result = ModSettings.NTPServerUrl.Value;
                    return false;
                }
                // Log ONCE per key. This fires on every config read, and a
                // two-client session produced 327,713 copies of one line - about
                // 90% of all log output, ~10x the exception count. It is the
                // client-side twin of the server logging stall that starved
                // position relays, and it buries the evidence in any log we then
                // try to diagnose from.
                if (loggedUntouched.Add(key))
                {
                    Debug.LogWarning("not touching " + key);
                }

                return true;
            }
        }

        [HarmonyPatch()]
        class Get_Bool
        {
            [HarmonyTargetMethod]
            public static MethodBase GetTargetMethod()
            {
                return AccessTools.Method(
                                            AccessTools.TypeByName("WAConfig"),
                                            "Get",
                                            new Type[]
                                            {
                                            typeof(string)
                                            }).MakeGenericMethod(typeof(bool));
            }

            [HarmonyPrefix]
            public static bool Get_Prefix( ref bool __result, string key )
            {
                ReloadIfStale();

                if (key == "VOIP.Enabled")
                {
                    __result = false;
                    return false;
                }
                // Log ONCE per key. This fires on every config read, and a
                // two-client session produced 327,713 copies of one line - about
                // 90% of all log output, ~10x the exception count. It is the
                // client-side twin of the server logging stall that starved
                // position relays, and it buries the evidence in any log we then
                // try to diagnose from.
                if (loggedUntouched.Add(key))
                {
                    Debug.LogWarning("not touching " + key);
                }

                return true;
            }
        }
    }
}

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

        /// <summary>
        /// The client's own name for the alliances feature flag, read out of its
        /// ConfigKeys rather than hardcoded.
        ///
        /// ConfigKeys.AlliancesEnabled forwards to SharedConfigKeys, which lives
        /// in an assembly we do not have decompiled, so the literal is not
        /// recoverable by reading. Its siblings are "Alliances.DebugHttp" and
        /// "Alliances.DevRegion", so it is almost certainly "Alliances.Enabled" -
        /// and "almost certainly" is exactly the kind of guess that produces a
        /// patch which silently never fires. Reflecting the real value cannot be
        /// wrong.
        /// </summary>
        private static string alliancesEnabledKey;
        private static bool alliancesKeyResolved;

        internal static string AlliancesEnabledKey()
        {
            if (alliancesKeyResolved) return alliancesEnabledKey;
            alliancesKeyResolved = true;

            try
            {
                Type keys = AccessTools.TypeByName("ConfigKeys");
                FieldInfo field = keys == null ? null : AccessTools.Field(keys, "AlliancesEnabled");
                alliancesEnabledKey = field == null ? null : field.GetValue(null) as string;
                Debug.Log("[WAReborn] alliances feature key resolves to '"
                    + (alliancesEnabledKey ?? "<unresolved>") + "'.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] could not resolve the alliances config key: " + e);
                alliancesEnabledKey = null;
            }

            return alliancesEnabledKey;
        }

        /// <summary>
        /// Forces alliances OFF, which is what makes crews reachable at all.
        ///
        /// Alliances are a dead Bossa WEB SERVICE (StagingConfig points
        /// ConfigKeys.AlliancesUrl at alliances-staging.api.bossagames.com).
        /// The Social Sheet fetches from it on open, the request fails, and
        /// SocialCharacterSheet.TriggerAllianceExceptionHandler - which is SHARED
        /// between alliances and crews - throws up "Can't retrieve alliance or
        /// crew data" over the whole sheet, including the CREW tab.
        ///
        /// With the flag off the client takes its OTHER path, which predates the
        /// web service entirely: SocialScreenUIState.CanThisStateBeOpened returns
        /// false so the Social Sheet never opens, and CharacterSheetScreen adds
        /// OldCrewScreenModule instead - the pre-alliance crew UI, driven by the
        /// SpatialOS components this server actually serves. ChatSpeak confirms
        /// the intent: with alliances off it reads the crew straight from
        /// CrewMembershipState.currentCrewLeaderId, which is 6900.
        ///
        /// So this is not a workaround around a missing feature; it is selecting
        /// the branch of the client that matches the world we can actually host.
        /// </summary>
        private static bool ForcedBool(string key, out bool value)
        {
            if (key == "VOIP.Enabled")
            {
                value = false;
                return true;
            }

            string alliances = AlliancesEnabledKey();
            if (alliances != null && key == alliances)
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        [HarmonyPatch()]
        class GetOrDefault_Bool
        {
            [HarmonyTargetMethod]
            public static MethodBase GetTargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("WAConfig"),
                    "GetOrDefault",
                    new Type[] { typeof(string) }).MakeGenericMethod(typeof(bool));
            }

            // GetOrDefault does NOT route through Get, which is why the alliances
            // key never appeared in the "not touching" log even though every other
            // bool read did. Patching only Get would have looked correct and done
            // nothing.
            [HarmonyPrefix]
            public static bool Prefix( ref bool __result, string key )
            {
                if (!ForcedBool(key, out bool forced)) return true;
                __result = forced;
                return false;
            }
        }

        [HarmonyPatch()]
        class GetOrDefault_Bool_WithFallback
        {
            [HarmonyTargetMethod]
            public static MethodBase GetTargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("WAConfig"),
                    "GetOrDefault",
                    new Type[] { typeof(string), typeof(bool) }).MakeGenericMethod(typeof(bool));
            }

            [HarmonyPrefix]
            public static bool Prefix( ref bool __result, string key )
            {
                if (!ForcedBool(key, out bool forced)) return true;
                __result = forced;
                return false;
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

                if (ForcedBool(key, out bool forced))
                {
                    __result = forced;
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

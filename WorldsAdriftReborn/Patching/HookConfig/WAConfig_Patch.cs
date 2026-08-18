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
                    __result = ModSettings.DeploymentStatusUrl();
                    return false;
                }
                else if (key == "Bootstrap.NtpServer")
                {
                    __result = ModSettings.NTPServerUrl.Value;
                    return false;
                }
                else if (ForcedString(key, out string forced))
                {
                    // The alliances host is normally read through GetOrDefault,
                    // patched below - but the key is also reachable through Get,
                    // and answering it in only one of the two would be the same
                    // half-patch that has caught this file before.
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

        /// <summary>
        /// The client's own name for a ConfigKeys field, read off the type rather
        /// than hardcoded.
        ///
        /// Several of these forward to SharedConfigKeys, which lives in an
        /// assembly we do not have decompiled, so the literals are not recoverable
        /// by reading. Reflecting the real value cannot be wrong; guessing the
        /// sibling naming convention produces a patch which silently never fires,
        /// which is exactly what happened once already.
        /// </summary>
        private static string ConfigKeyLiteral(string fieldName, ref string cached, ref bool resolved)
        {
            if (resolved) return cached;
            resolved = true;

            try
            {
                Type keys = AccessTools.TypeByName("ConfigKeys");
                FieldInfo field = keys == null ? null : AccessTools.Field(keys, fieldName);
                cached = field == null ? null : field.GetValue(null) as string;
                Debug.Log("[WAReborn] ConfigKeys." + fieldName + " resolves to '"
                    + (cached ?? "<unresolved>") + "'.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] could not resolve ConfigKeys." + fieldName + ": " + e);
                cached = null;
            }

            return cached;
        }

        private static string alliancesUrlKey;
        private static bool alliancesUrlKeyResolved;

        /// <summary>
        /// The key naming the alliances/social REST host.
        ///
        /// ConfigKeys.AlliancesUrl forwards to SharedConfigKeys.AlliancesServerUrl,
        /// so it is resolved the same way the feature flag is.
        /// </summary>
        internal static string AlliancesUrlKey()
        {
            return ConfigKeyLiteral("AlliancesUrl", ref alliancesUrlKey, ref alliancesUrlKeyResolved);
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
            return ConfigKeyLiteral("AlliancesEnabled", ref alliancesEnabledKey, ref alliancesKeyResolved);
        }

        /// <summary>
        /// Alliances are left ON, and the retail Social Sheet is what players get.
        ///
        /// This used to force the flag OFF. The reasoning was that alliances are a
        /// dead Bossa WEB SERVICE, that the Social Sheet fetches from it on open,
        /// and that the failure lands in
        /// SocialCharacterSheet.TriggerAllianceExceptionHandler - which is SHARED
        /// between alliances and crews - covering the whole sheet including the
        /// CREW tab. All of that is true. What was wrong was the conclusion that
        /// turning the flag off gave crews a working home.
        ///
        /// It does not. With the flag off the client mounts OldCrewScreen, and
        /// reading it (docs/research/findings-social-api.md, section 1) shows two
        /// things: its ICrewClient field is declared and never used, and its CREATE
        /// CREW button is three SetActive calls that send nothing to anyone. The
        /// old panel could never create a crew, which is exactly the "EMPTY SLOT
        /// everywhere" this flag was flipped to fix.
        ///
        /// Meanwhile the RETAIL crew panel is not SpatialOS-driven at all - it goes
        /// over the same REST API the alliances did, through CrewClient ->
        /// CrewServerImpl -> SocialRequest. So the honest fix is not to pick the
        /// other branch of the client, it is to answer the requests. We do:
        /// WorldsAdriftServer serves that API now, and the redirect below points
        /// the client at it.
        /// </summary>
        private static string useSteamKey;
        private static bool useSteamKeyResolved;

        /// <summary>
        /// The client's own key for "this build talks to Steam".
        ///
        /// Unlike the alliances keys this one is a plain literal in ConfigKeys
        /// rather than a forward into SharedConfigKeys, so the literal below is
        /// recoverable by reading (ConfigKeys.cs:48). It is still resolved by
        /// reflection first, with the read value as the fallback, because a
        /// forcing that silently stops matching is exactly the failure this file
        /// keeps being bitten by and the cost of checking is one field read.
        /// </summary>
        internal static string UseSteamKey()
        {
            return ConfigKeyLiteral("UseSteam", ref useSteamKey, ref useSteamKeyResolved)
                ?? "Bootstrap.UseSteam";
        }

        /// <summary>
        /// Steam is off, permanently.
        ///
        /// SteamChecker.IsUsingSteam is nothing but WAConfig.Get&lt;bool&gt;(ConfigKeys.UseSteam)
        /// and ConfigDefaults sets it true, which is what makes a delisted 2019
        /// game refuse to start without a running store client. Forcing it false
        /// takes the client's OWN no-Steam branch - the one Bossa built for
        /// non-Steam builds - so every consumer is already written to cope:
        ///
        ///   ConnectToNeededServersState.ConnectToSteam   returns a resolved
        ///     promise instead of SteamManager.Authenticate, which is the call
        ///     that hangs the boot;
        ///   ConnectToNeededServersState.CheckSteamBranchAndConfig  and
        ///     SteamChecker.GetSteamBranch  stop calling SteamApps.GetCurrentBetaName;
        ///   ConnectToAnalytics  resolves without touching Steam;
        ///   Improbable.Bootstrap.GetUserName  reads PlayerPrefs/Environment
        ///     instead of SteamManager.SteamUsername;
        ///   DeploymentChooser  skips the GeoLocationLookup that wants a steam id
        ///     and ticket;
        ///   LobbySystem.ConnectToGameServer / DebugLobbyState.SetupMetadata
        ///     build LoginMetadata.TestingMetadata instead of SteamMetadata.
        ///
        /// That last one is the only change of shape on the wire, and it is safe
        /// here: it swaps UserId, Credentials and Platform in the SpatialOS
        /// connect metadata, and nothing in WorldsAdriftRebornCoreSdk or
        /// WorldsAdriftRebornGameServer reads those three keys. The fields that
        /// do matter - playerName, bossaId, bossaNetGameClientToken, characterUid -
        /// are filled afterwards by CompleteConnect from BossaNetBootstrap,
        /// identically on both branches. In-game identity is a server-side stub
        /// (LocalPlayerIdentity.PlayerId = "id") served in component 1086, not a
        /// steam id.
        ///
        /// It also removes an NRE that was waiting to happen: SteamMetadata does
        /// SteamManager.HexAuthTicket.ToUpper() with no null check, and
        /// HexAuthTicket is null until a Steam auth ticket callback arrives -
        /// which for a delisted appid it never does.
        /// </summary>
        private static bool ForcedBool(string key, out bool value)
        {
            if (key == "VOIP.Enabled")
            {
                value = false;
                return true;
            }

            if (key == UseSteamKey())
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        /// <summary>
        /// Redirects the alliances/social host at our own server.
        ///
        /// This has to hook GetOrDefault, NOT Get. SocialHelper reads it as
        ///
        ///     public static readonly string AlliancesServerUrl =
        ///         WAConfig.GetOrDefault&lt;string&gt;(ConfigKeys.AlliancesUrl);
        ///
        /// and GetOrDefault does not route through Get - the same trap that made
        /// an earlier patch of the alliances BOOL look correct and do nothing.
        ///
        /// It is also a static readonly field, so it is resolved once by
        /// SocialHelper's type initializer the first time anything touches it.
        /// Harmony patches at plugin load, long before any social code runs, so
        /// the redirect is in place before that read happens - but it does mean
        /// editing the cfg mid-session cannot move it, unlike the other URLs here.
        /// </summary>
        private static bool ForcedString(string key, out string value)
        {
            // The login screen's two outbound links. LandingScreen.CreateAccount
            // and LandingScreen.ForgotPassword are both a single
            // Application.OpenURL(WAConfig.Get<string>(key)) call, so redirecting
            // the key redirects the button - no patch on the screen needed at
            // all. Both defaults were S3 redirect pages that Bossa took down.
            if (key == "BossaNet.CreateAccountUrl")
            {
                value = ModSettings.createAccountUrl.Value;
                return true;
            }

            if (key == "BossaNet.ResetPasswordUrl")
            {
                value = ModSettings.passwordResetUrl.Value;
                return true;
            }

            string alliancesUrl = AlliancesUrlKey();
            if (alliancesUrl != null && key == alliancesUrl)
            {
                // Resolved, not raw: the setting's default is BLANK, meaning
                // "same origin as REST_ServerUrl". Handing the raw value over
                // would send the Social Sheet at "" on a default install.
                value = ModSettings.AlliancesUrl();
                return true;
            }

            value = null;
            return false;
        }

        [HarmonyPatch()]
        class GetOrDefault_String
        {
            [HarmonyTargetMethod]
            public static MethodBase GetTargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("WAConfig"),
                    "GetOrDefault",
                    new Type[] { typeof(string) }).MakeGenericMethod(typeof(string));
            }

            [HarmonyPrefix]
            public static bool Prefix( ref string __result, string key )
            {
                ReloadIfStale();
                if (!ForcedString(key, out string forced)) return true;
                __result = forced;
                return false;
            }
        }

        /// <summary>
        /// The two-argument <c>WAConfig.GetOrDefault&lt;T&gt;(string key, T defaultValue)</c>,
        /// closed over <typeparamref name="T"/>.
        ///
        /// It cannot be found with <c>AccessTools.Method(type, name, new[] { typeof(string), typeof(bool) })</c>.
        /// The second parameter of the generic DEFINITION is the open type
        /// parameter <c>T</c>, not <c>bool</c> or <c>string</c>, so matching on a
        /// concrete type finds nothing, AccessTools returns null, and
        /// <c>MakeGenericMethod</c> then throws a NullReferenceException out of
        /// GetTargetMethod.
        ///
        /// That is exactly what happened: both WithFallback classes threw, and
        /// because the mod used to patch the whole assembly in one call, the
        /// first one to be processed aborted every patch class Harmony had not
        /// yet reached. Matching on arity instead cannot hit that trap.
        /// </summary>
        private static MethodBase GetOrDefaultWithFallback(Type closedOver)
        {
            Type waConfig = AccessTools.TypeByName("WAConfig");
            if (waConfig == null)
            {
                throw new InvalidOperationException(
                    "[WAReborn] WAConfig type not found; cannot patch GetOrDefault.");
            }

            foreach (MethodInfo candidate in AccessTools.GetDeclaredMethods(waConfig))
            {
                if (candidate.Name != "GetOrDefault") continue;
                if (!candidate.IsGenericMethodDefinition) continue;
                if (candidate.GetParameters().Length != 2) continue;
                return candidate.MakeGenericMethod(closedOver);
            }

            throw new InvalidOperationException(
                "[WAReborn] WAConfig.GetOrDefault<T>(string, T) not found. The client's config "
                + "API has changed shape; the URL and feature-flag redirects that hang off it "
                + "need rechecking.");
        }

        [HarmonyPatch()]
        class GetOrDefault_String_WithFallback
        {
            [HarmonyTargetMethod]
            public static MethodBase GetTargetMethod()
            {
                return GetOrDefaultWithFallback(typeof(string));
            }

            [HarmonyPrefix]
            public static bool Prefix( ref string __result, string key )
            {
                ReloadIfStale();
                if (!ForcedString(key, out string forced)) return true;
                __result = forced;
                return false;
            }
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
                return GetOrDefaultWithFallback(typeof(bool));
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

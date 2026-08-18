using System;
using System.Reflection;
using HarmonyLib;
using RSG;
using UnityEngine;
using WorldsAdriftReborn.Config;

namespace WorldsAdriftReborn.Patching.Dynamic.BypassSteam
{
    /// <summary>
    /// Stops the client from ever calling into the Steam API.
    ///
    /// WHY THIS EXISTS ON TOP OF THE UseSteam FLAG. Forcing Bootstrap.UseSteam
    /// false (WAConfig_Patch) takes the client's own no-Steam branch and covers
    /// every call site that asks first. Two do not ask:
    ///
    ///   SteamManagerInit.Start()  -> steamManager.Authenticate(15f)
    ///   IntroScreen.Start()       -> new GameObject("SteamManager", typeof(SteamManager))
    ///                                .GetComponent&lt;SteamManager&gt;().Authenticate(15f)
    ///
    /// Neither consults SteamChecker. SteamManagerInit is a MonoBehaviour that
    /// exists to be dropped on a prefab, and the prefab it sits on is scene data
    /// the decompile cannot show us, so whether it rides on Resources
    /// "Steam/SteamManager" - the one ConnectToNeededServersState instantiates -
    /// is not answerable by reading. Patching SteamManager itself removes the
    /// question.
    ///
    /// WHAT THE ORIGINAL DOES, and why it cannot be allowed to run
    /// (acs/SteamManager.cs:43-91):
    ///
    ///     if (SteamAPI.RestartAppIfNecessary(AppId_t.Invalid)) { Application.Quit(); return; }
    ///     m_bInitialized = SteamAPI.Init();
    ///     if (!m_bInitialized) { throw new SteamWorksInitFailedException(); }
    ///
    /// RestartAppIfNecessary is the classic "relaunch me through Steam" gate,
    /// and Init() is false whenever the store client is not running. But the
    /// failure the player actually hits is one step further on. Their log has
    /// Steam initialising perfectly -
    ///
    ///     [SteamManager] Steam Username: Doubtful Hermit
    ///     [SteamManager] Steam User Id:  76561198002472036
    ///     [SteamManager] Steam App Id:   322780
    ///
    /// - and then, fifteen seconds later, SteamAuthTimeoutException into
    /// UnrecoverableErrorState, then the whole thing again four seconds after
    /// that, forever. RequestAuthSessionTicket waits on a
    /// GetAuthSessionTicketResponse_t callback that never comes for a delisted
    /// appid, HexAuthTicket stays empty and Authenticate rejects. The retry is
    /// not rate-limited because SteamAuthTimeoutException is the one exception
    /// exempted from the 30-second guard in ConnectToNeededServersState.
    ///
    /// So "Steam is running" was never the requirement. "Steam will vouch for
    /// you owning a game it stopped selling in 2019" was, and nothing can make
    /// that true again.
    ///
    /// WHY Initialized IS LEFT FALSE. The tempting shortcut is to fake success:
    /// set m_bInitialized, let SteamManager.Initialized be true and have the
    /// game believe Steam is there. That is worse. Initialized == true is the
    /// guard in front of SteamUtils.GetAppID() in
    /// BossaNetBootstrap.CreateAuthRequestPayload, in UGCManager, in
    /// SteamWorkshopFileList and in IntroScreen.Update, and it is what lets
    /// SteamManager.Update call SteamAPI.RunCallbacks() every frame - all of
    /// them P/Invokes into steam_api64.dll. Leaving it false keeps every one of
    /// those doors shut, and since the original Inject never runs, m_bInitialized
    /// is never set, so Update() and OnEnable() are already no-ops with no patch
    /// of their own.
    ///
    /// This does not redistribute anything of Valve's. steam_api64.dll is simply
    /// never called.
    /// </summary>
    internal static class SteamManager_Patch
    {
        /// <summary>
        /// The auth ticket handed to anything that asks.
        ///
        /// Deliberately the literal the client substitutes for itself when
        /// SteamManager gave it nothing (BossaNetBootstrap.cs:145-149 and
        /// :365-369), so the /authenticate body we put on the wire is
        /// byte-identical to the one the player sends today - where the ticket
        /// is empty for exactly the reason above. Our login server reads only
        /// bossaCredential from that body and ignores steamCredential entirely,
        /// so this value is inert; it exists so nothing null-derefs.
        /// </summary>
        private const string StubAuthTicket = "steamAuthToken";

        private static bool seeded;
        private static bool seedFailed;

        internal static Type SteamManagerType()
        {
            Type t = AccessTools.TypeByName("SteamManager");
            if (t == null)
            {
                throw new InvalidOperationException(
                    "[WAReborn] SteamManager type not found. The Steam bypass cannot be applied "
                    + "and the client will try to talk to Steam.");
            }
            return t;
        }

        /// <summary>
        /// Fills SteamManager's static identity fields with stable values.
        ///
        /// The original Inject sets these from SteamFriends/SteamUser; skipping
        /// it leaves them null, and not every reader guards. BossaNetBootstrap
        /// does, IntroScreen and UGCManager sit behind Initialized, but
        /// AnalyticsProviderBossa and UserAnalytics read UserSteamId straight
        /// out. A null there is a NullReferenceException inside a coroutine,
        /// which is the quiet kind of failure this project has spent a lot of
        /// time digging out of logs.
        ///
        /// Steam_UserId has been sitting in the config since the mod was
        /// written, bound and never read by anything. This is what it was for.
        /// </summary>
        private static void SeedIdentity()
        {
            if (seeded || seedFailed) return;

            try
            {
                Type steamManager = SteamManagerType();

                Set(steamManager, "UserSteamId", ModSettings.steamUserId.Value);
                Set(steamManager, "SteamUsername", Environment.UserName);
                Set(steamManager, "HexAuthTicket", StubAuthTicket);

                seeded = true;
                Debug.Log("[WAReborn] Steam is bypassed. SteamManager identity stubbed as user id '"
                    + ModSettings.steamUserId.Value + "'; the Steam API is never called.");
            }
            catch (Exception e)
            {
                // Loud, and only once. If this ever fires, the symptom downstream
                // would be an NRE in an analytics coroutine with nothing pointing
                // back here.
                seedFailed = true;
                Debug.LogError("[WAReborn] could not stub SteamManager's identity fields; "
                    + "readers of SteamManager.UserSteamId may now see null. " + e);
            }
        }

        private static void Set(Type owner, string property, string value)
        {
            MethodInfo setter = AccessTools.PropertySetter(owner, property);
            if (setter == null)
            {
                throw new InvalidOperationException(
                    "[WAReborn] SteamManager." + property + " has no setter to stub.");
            }
            setter.Invoke(null, new object[] { value });
        }

        /// <summary>
        /// Skips Inject entirely. Nothing calls it but Authenticate, which is
        /// also patched, so this is the backstop rather than the main door - but
        /// it is the one that would catch a call path the audit missed.
        /// </summary>
        [HarmonyPatch]
        internal static class Inject_Patch
        {
            [HarmonyTargetMethod]
            public static MethodBase GetTargetMethod()
            {
                MethodBase m = AccessTools.Method(SteamManagerType(), "Inject",
                    new Type[] { typeof(float) });
                if (m == null)
                {
                    throw new InvalidOperationException(
                        "[WAReborn] SteamManager.Inject(float) not found; Steam bypass incomplete.");
                }
                return m;
            }

            [HarmonyPrefix]
            public static bool Prefix()
            {
                SeedIdentity();
                return false;
            }
        }

        /// <summary>
        /// Resolves immediately instead of asking Steam for an auth ticket.
        ///
        /// The value is discarded by all three callers - ConnectToSteam,
        /// SteamManagerInit.Start and IntroScreen.Start all ignore it - but it
        /// is resolved with the stub ticket rather than an empty string so the
        /// promise's own contract (empty means reject) is not quietly inverted.
        /// </summary>
        [HarmonyPatch]
        internal static class Authenticate_Patch
        {
            [HarmonyTargetMethod]
            public static MethodBase GetTargetMethod()
            {
                MethodBase m = AccessTools.Method(SteamManagerType(), "Authenticate",
                    new Type[] { typeof(float) });
                if (m == null)
                {
                    throw new InvalidOperationException(
                        "[WAReborn] SteamManager.Authenticate(float) not found; the client would "
                        + "hang for 15s and then loop on SteamAuthTimeoutException.");
                }
                return m;
            }

            [HarmonyPrefix]
            public static bool Prefix(ref IPromise<string> __result)
            {
                SeedIdentity();
                __result = Promise<string>.Resolved(StubAuthTicket);
                return false;
            }
        }
    }
}

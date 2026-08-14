using System;
using System.Reflection;
using Framework.Promise;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Startup
{
    /// <summary>
    /// Removes retail's hard dependency on a public UDP NTP server during login.
    ///
    /// The original client refuses to enter PrepareForGameState until
    /// SynchronisedTime.Synchronise resolves. On networks which block UDP/123,
    /// each NTP attempt waits ten seconds and the third failure disconnects the
    /// client before it has received a single world op. Modern Windows already
    /// keeps UTC synchronised, and Wareborn rewrites relay timestamps at the
    /// authority boundary, so the local UTC clock is the correct fail-open
    /// source for this reconstructed server.
    /// </summary>
    [HarmonyPatch]
    internal static class SynchronisedTime_Patch
    {
        private static bool _logged;

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                AccessTools.TypeByName("SynchronisedTime"),
                "Synchronise",
                new[] { typeof(Promise), typeof(bool) });
        }

        [HarmonyPrefix]
        private static bool Prefix(ref Promise __result)
        {
            Type synchronisedTime = AccessTools.TypeByName("SynchronisedTime");
            DateTime epoch = new DateTime(2018, 3, 1, 0, 0, 0, DateTimeKind.Utc);
            double systemNow = (DateTime.UtcNow - epoch).TotalSeconds;

            // These are the only two pieces of state the successful retail NTP
            // callback establishes before resolving the login promise.
            AccessTools.Field(synchronisedTime, "_synced").SetValue(null, true);
            AccessTools.Field(synchronisedTime, "_smoothFixedNow").SetValue(null, systemNow);

            Promise resolved = new Promise();
            resolved.Resolve();
            __result = resolved;

            if (!_logged)
            {
                _logged = true;
                Debug.Log("[WAReborn] time sync: using operating-system UTC; public NTP is optional and cannot block world entry.");
            }

            return false;
        }
    }
}

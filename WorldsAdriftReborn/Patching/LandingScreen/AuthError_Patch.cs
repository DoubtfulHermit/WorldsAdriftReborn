using System;
using System.Reflection;
using HarmonyLib;
using Travellers.UI.InfoPopups;
using UnityEngine;
using LandingScreenType = Travellers.UI.Login.LandingScreen;

namespace WorldsAdriftReborn.Patching.Dynamic.LandingScreen
{
    /// <summary>
    /// A login that cannot reach the server no longer quits the game and sends
    /// the player to Bossa's support site.
    ///
    /// The original (acs/Travellers.UI.Login/LandingScreen.cs:271-278):
    ///
    ///     DialogPopupFacade.ShowOkDialog("Connection Error",
    ///         $"Could not connect to the Worlds Adrift servers. \n\nPlease try again "
    ///       + $"later or visit support.bossastudios.com for more information.({stableHash})",
    ///         Application.Quit, "QUIT");
    ///
    /// Two things wrong with that here. support.bossastudios.com has nothing to
    /// say about a community server, and the only button kills the process - so
    /// a server that is briefly down, or a REST_ServerUrl with a typo in it,
    /// costs the player the whole client and a fresh launch.
    ///
    /// The form is already restored by the time the dialog appears, so
    /// dismissing it hands back a usable login screen. The underlying error
    /// still goes to the log in full; it is just not shown as a hash the player
    /// can do nothing with.
    /// </summary>
    [HarmonyPatch(typeof(LandingScreenType))]
    internal static class AuthError_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch("AuthError")]
        public static bool AuthError_Prefix(LandingScreenType __instance, string error)
        {
            try
            {
                MethodInfo setFormActive = AccessTools.Method(typeof(LandingScreenType),
                    "SetLoginFormActive", new Type[] { typeof(bool) });
                if (setFormActive == null)
                {
                    // Without the form back, an OK button would leave the player
                    // on a blank screen. Let the original run instead: it quits,
                    // which is worse but not a dead end.
                    Debug.LogError("[WAReborn] LandingScreen.SetLoginFormActive(bool) not found; "
                        + "falling back to the client's own quit-on-error dialog.");
                    return true;
                }

                setFormActive.Invoke(__instance, new object[] { true });

                Debug.LogError("[WAReborn] login could not reach the server: " + error);

                DialogPopupFacade.ShowOkDialog(
                    "Could not reach the server",
                    "The Wareborn login server did not answer.\n\n"
                    + "It may be down, or the address in the mod's config may be wrong. "
                    + "Check REST_ServerUrl in WorldsAdriftReborn.cfg, then try again.",
                    null,
                    "OK",
                    false,
                    null);

                return false;
            }
            catch (Exception e)
            {
                Debug.LogError("[WAReborn] could not show the connection-error dialog: " + e);
                return true;
            }
        }
    }
}

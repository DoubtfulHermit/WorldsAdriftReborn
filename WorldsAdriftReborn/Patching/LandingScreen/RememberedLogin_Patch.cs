using System;
using Bossa.Travellers.BossaNet;
using HarmonyLib;
using UnityEngine;
using LandingScreenType = Travellers.UI.Login.LandingScreen;

namespace WorldsAdriftReborn.Patching.Dynamic.LandingScreen
{
    /// <summary>
    /// Reuses a protected server session after the ordinary Steam-only probe has
    /// told the landing screen it has no linked retail account.  Waiting for that
    /// callback avoids racing two authentication requests during ProtectedInit.
    /// </summary>
    [HarmonyPatch(typeof(LandingScreenType))]
    internal static class RememberedLogin_Patch
    {
        private static bool attempted;

        [HarmonyPostfix]
        [HarmonyPatch("NoLinkedAccount")]
        public static void NoLinkedAccount_Postfix(LandingScreenType __instance)
        {
            if (attempted)
            {
                return;
            }
            attempted = true;

            if (!RememberedGameSession.TryLoad(out string username, out string credential))
            {
                return;
            }

            try
            {
                LoginFromForm_Patch.Invoke(__instance, "HideAllInput");
                Debug.Log("[WAReborn] resuming the protected remembered game session.");

                BossaNetBootstrap.Instance.AuthenticateWithBossaNet(
                    username,
                    credential,
                    (BossaNetBootstrap.OnAuthSuccess)delegate(string token)
                    {
                        RememberedGameSession.Save(username, token);
                        LoginFromForm_Patch.Invoke(__instance, "LoginSuccess", token);
                    },
                    (BossaNetBootstrap.OnAuthFail)delegate(string code)
                    {
                        RememberedGameSession.Forget();
                        LoginFromForm_Patch.Invoke(__instance, "LoginFailed", code);
                    },
                    (BossaNetBootstrap.OnAuthError)delegate(string error)
                    {
                        // A network failure does not invalidate a good token.
                        LoginFromForm_Patch.Invoke(__instance, "AuthError", error);
                    },
                    (BossaNetBootstrap.OnAccountNotVerified)delegate(string token)
                    {
                        RememberedGameSession.Forget();
                        LoginFromForm_Patch.Invoke(__instance, "AccountNotVerified", token);
                    });
            }
            catch (Exception e)
            {
                Debug.LogError("[WAReborn] remembered login failed before the request; "
                    + "showing the normal form: " + e);
                // NoLinkedAccount activated the form before this postfix ran,
                // so there is nothing to recover here.  In particular, do not
                // risk throwing a second reflection error from the catch path.
            }
        }
    }
}

using System;
using System.Reflection;
using Bossa.Travellers.BossaNet;
using HarmonyLib;
using UnityEngine;
using LandingScreenType = Travellers.UI.Login.LandingScreen;

namespace WorldsAdriftReborn.Patching.Dynamic.LandingScreen
{
    /// <summary>
    /// Signs the player in when they press LOGIN, instead of first asking them
    /// whether they would like to link a Steam account.
    ///
    /// The original (acs/Travellers.UI.Login/LandingScreen.cs:220-237):
    ///
    ///     public void LoginFromForm()
    ///     {
    ///         if (string.IsNullOrEmpty(_userField.text) || string.IsNullOrEmpty(_passwordField.text))
    ///         { _missingInputWarning.gameObject.SetActive(true); return; }
    ///         string user = _userField.text;
    ///         string pwd  = _passwordField.text;
    ///         HideAllInput();
    ///         DialogPopupFacade.ShowConfirmationDialogAndOverrideSounds("Are you sure?",
    ///             "You need to link your Steam account to your Worlds Adrift account to play.\n"
    ///           + "Would you like to do that now?",
    ///             delegate { BossaNetBootstrap.Instance.AuthenticateWithBossaNet(user, pwd, ...); },
    ///             delegate { SetLoginFormActive(enabled: true); },
    ///             "YES", "NO", useSolidBackground: false);
    ///     }
    ///
    /// Read the YES branch: it is the login call. The dialog is not a warning
    /// about anything, it is retail's consent step for linking a Steam identity
    /// to a Bossa one, and it sits in front of every single sign-in. There is no
    /// Steam here to link to, so the honest answer to "would you like to do that
    /// now" is that there is nothing to do - and being asked "Are you sure?"
    /// after typing a password is a bad enough prompt on its own.
    ///
    /// This reimplements the method with the dialog removed rather than
    /// intercepting the popup by matching its message text. Matching on the
    /// string would leave DialogPopupFacade patched globally, so any future
    /// dialog whose wording drifted close enough would auto-confirm itself -
    /// and auto-confirming an unknown dialog is a much worse failure than the
    /// one being fixed. This way the patch is scoped to exactly one button.
    ///
    /// Everything else is preserved: the empty-field warning, HideAllInput()
    /// while the request is in flight, and the same four callbacks. The result
    /// is what the player gets today if they always press YES.
    /// </summary>
    [HarmonyPatch(typeof(LandingScreenType))]
    internal static class LoginFromForm_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(LandingScreenType.LoginFromForm))]
        public static bool LoginFromForm_Prefix(LandingScreenType __instance)
        {
            try
            {
                UnityEngine.UI.InputField userField = Field<UnityEngine.UI.InputField>(__instance, "_userField");
                UnityEngine.UI.InputField passwordField = Field<UnityEngine.UI.InputField>(__instance, "_passwordField");
                TMPro.TextMeshProUGUI missingInput = Field<TMPro.TextMeshProUGUI>(__instance, "_missingInputWarning");

                if (userField == null || passwordField == null || missingInput == null)
                {
                    // Fall through to the original rather than swallow the click.
                    // The player then gets the Steam dialog back, which is
                    // annoying but still logs them in - far better than a LOGIN
                    // button that does nothing.
                    Debug.LogError("[WAReborn] LandingScreen has changed shape; the login form "
                        + "fields could not be read, so the Steam-link dialog is back.");
                    return true;
                }

                if (string.IsNullOrEmpty(userField.text) || string.IsNullOrEmpty(passwordField.text))
                {
                    missingInput.gameObject.SetActive(true);
                    return false;
                }

                string user = userField.text;
                string pwd = passwordField.text;

                Invoke(__instance, "HideAllInput");

                BossaNetBootstrap.Instance.AuthenticateWithBossaNet(
                    user,
                    pwd,
                    (BossaNetBootstrap.OnAuthSuccess)Handler(__instance, "LoginSuccess",
                        typeof(BossaNetBootstrap.OnAuthSuccess)),
                    (BossaNetBootstrap.OnAuthFail)Handler(__instance, "LoginFailed",
                        typeof(BossaNetBootstrap.OnAuthFail)),
                    (BossaNetBootstrap.OnAuthError)Handler(__instance, "AuthError",
                        typeof(BossaNetBootstrap.OnAuthError)),
                    (BossaNetBootstrap.OnAccountNotVerified)Handler(__instance, "AccountNotVerified",
                        typeof(BossaNetBootstrap.OnAccountNotVerified)));

                return false;
            }
            catch (Exception e)
            {
                // Never leave the player with a dead LOGIN button. Handing the
                // click back to the original costs them one extra dialog.
                Debug.LogError("[WAReborn] could not run the direct login path, falling back to "
                    + "the client's own (which asks about linking a Steam account): " + e);
                return true;
            }
        }

        private static T Field<T>(object instance, string name) where T : class
        {
            FieldInfo field = AccessTools.Field(typeof(LandingScreenType), name);
            return field == null ? null : field.GetValue(instance) as T;
        }

        private static void Invoke(object instance, string name)
        {
            MethodInfo method = AccessTools.Method(typeof(LandingScreenType), name);
            if (method == null)
            {
                throw new InvalidOperationException(
                    "[WAReborn] LandingScreen." + name + "() not found.");
            }
            method.Invoke(instance, null);
        }

        private static Delegate Handler(object instance, string name, Type delegateType)
        {
            MethodInfo method = AccessTools.Method(typeof(LandingScreenType), name);
            if (method == null)
            {
                throw new InvalidOperationException(
                    "[WAReborn] LandingScreen." + name + "() not found; it is one of the four "
                    + "callbacks the login request needs.");
            }
            return Delegate.CreateDelegate(delegateType, instance, method);
        }
    }
}

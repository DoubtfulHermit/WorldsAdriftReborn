using HarmonyLib;
using UI.Components;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Dynamic.BypassSteam
{
    /// <summary>
    /// Opens the landing screen's outbound links in the player's browser instead
    /// of the Steam overlay.
    ///
    /// The original (acs/UI.Components/DevelopmentArea.cs:76-106) tries Steam
    /// first and only falls back to the browser if Steam misbehaves:
    ///
    ///     private void GotoUrl(string url)
    ///     {
    ///         try
    ///         {
    ///             SteamFriends.ActivateGameOverlayToWebPage(url);
    ///             if (checkingOverlay != null) StopCoroutine(checkingOverlay);
    ///             checkingOverlay = StartCoroutine(CheckOverlay(url));
    ///         }
    ///         catch { Application.OpenURL(url); }
    ///     }
    ///
    /// with CheckOverlay then polling SteamUtils.IsOverlayEnabled() for half a
    /// second before giving up. Both of those are P/Invokes into
    /// steam_api64.dll, and with the Steam bypass in place SteamAPI.Init() has
    /// never run - so this is native code called against an uninitialised
    /// library. The managed `catch` around it is no help there; that is the
    /// class of call that takes the process down rather than throwing something
    /// C# can see.
    ///
    /// Even if it survived, the best case is a button that does nothing for half
    /// a second and then works. There is no Steam overlay to open in a build
    /// with no Steam, so go straight to the browser.
    ///
    /// This covers both buttons in the box: FORUMS/MAP via GotoForum, and the
    /// community-video thumbnail via GotoVideo. Under Proton, Application.OpenURL
    /// goes through winebrowser to the host's xdg-open, which is the same path
    /// CREATE ACCOUNT and FORGOT PASSWORD already use.
    /// </summary>
    [HarmonyPatch(typeof(DevelopmentArea))]
    internal static class DevelopmentArea_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch("GotoUrl")]
        public static bool GotoUrl_Prefix(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogWarning("[WAReborn] landing-screen link had no URL; ignoring the click.");
                return false;
            }

            Debug.Log("[WAReborn] opening " + url + " in the browser (no Steam overlay).");
            Application.OpenURL(url);
            return false;
        }
    }
}

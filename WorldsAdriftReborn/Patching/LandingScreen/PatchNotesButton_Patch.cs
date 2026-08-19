using System;
using HarmonyLib;
using UI.Components;
using UnityEngine;
using WorldsAdriftReborn.Config;

namespace WorldsAdriftReborn.Patching.Dynamic.LandingScreen
{
    /// <summary>
    /// Sends the login screen's PATCH NOTES button to our own patch-notes page.
    ///
    /// WHAT THIS BUTTON ACTUALLY IS, because it is not what it looks like.
    /// PATCH NOTES sits next to FORUMS in the DEVELOPMENT BUILD box and looks
    /// like its twin, but FORUMS is a link and this is not. It is a pane toggle:
    ///
    ///     public void TogglePatchNotes(bool show) { patchNotesArea.SetActive(show); }
    ///
    /// (acs/UI.Components/DevelopmentArea.cs:71). The pane it reveals is filled by
    /// ChangeLogLoader, which fetches
    /// WAConfig.Get&lt;string&gt;(ConfigKeys.ClientReleaseNotesUrl) + "/" + buildNumber
    /// and parses the body as Bossa's own changelog markup - splitting on
    /// "&lt;size=14&gt;", reading "version|date" out of each block, and instantiating a
    /// PatchNote prefab per entry.
    ///
    /// So this could NOT be fixed the way CREATE ACCOUNT, FORGOT PASSWORD and
    /// FORUMS were. Those three are each a bare
    /// Application.OpenURL(WAConfig.Get&lt;string&gt;(key)), so redirecting the config
    /// key redirects the button and no patch on the screen is needed. Pointing
    /// ClientReleaseNotesUrl at a web page instead renders that page's raw HTML
    /// into the in-game pane, because the parser above is expecting a format no
    /// web page has. The pane is also fed a placeholder string today - see
    /// ChangeLogLoader_Patch, which replaces Start outright - so there is nothing
    /// working here to preserve.
    ///
    /// Hence: intercept the toggle and open the real page in the browser. The URL
    /// is still a config setting rather than a literal, for the same reason the
    /// others are - an operator running their own instance has their own site.
    ///
    /// SCOPED TO OPENING, NOT CLOSING. Only show == true is intercepted. A
    /// TogglePatchNotes(false) is the pane being hidden - by a close button, or by
    /// the screen tidying up - and swallowing that would leave a stale pane
    /// visible if anything else ever shows it.
    /// </summary>
    [HarmonyPatch(typeof(DevelopmentArea))]
    internal static class PatchNotesButton_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch("TogglePatchNotes")]
        public static bool TogglePatchNotes_Prefix(bool show)
        {
            if (!show)
            {
                return true;
            }

            try
            {
                string url = ModSettings.patchNotesUrl.Value;
                if (string.IsNullOrEmpty(url))
                {
                    Debug.LogWarning("[WAReborn] Links_PatchNotesUrl is blank; showing the "
                        + "client's own patch-notes pane instead.");
                    return true;
                }

                Debug.Log("[WAReborn] PATCH NOTES opening " + url + " in the browser.");
                Application.OpenURL(url);
                return false;
            }
            catch (Exception e)
            {
                // Falling through to the original costs the player a pane of
                // placeholder text, which is strictly better than a dead button.
                Debug.LogError("[WAReborn] could not open the patch notes page, falling back to "
                    + "the client's own pane: " + e);
                return true;
            }
        }
    }
}

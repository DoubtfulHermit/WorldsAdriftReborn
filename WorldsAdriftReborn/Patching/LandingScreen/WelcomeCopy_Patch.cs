using System;
using HarmonyLib;
using TMPro;
using UI.Components;
using UnityEngine;
using WorldsAdriftRebornGameServer.Multiplayer.Config;
using LandingScreenType = Travellers.UI.Login.LandingScreen;

namespace WorldsAdriftReborn.Patching.Dynamic.LandingScreen
{
    /// <summary>
    /// The safety net under SplashScreen_Patch's welcome-copy replacement: finds
    /// Bossa's greeting wherever it is actually drawn, in case it is not only on
    /// the field we can name.
    ///
    /// WHY BOTH. SplashScreen_Patch writes _splashScreenTextMesh, which is a
    /// serialized field, so there is no guessing about which label it is and it
    /// is the right thing to write. What the decompile CANNOT tell us is whether
    /// that field is the only place the greeting appears - the string is baked
    /// into the prefab as well as being served from the localisation table, and
    /// the player who reported this described the scroll as appearing after
    /// login next to a JOIN GAME button, which is the LANDING screen's
    /// logged-in state (LandingScreen.SetLoginFormActive(false) swaps the login
    /// form for _playButtonRoot), not the pre-login splash page.
    ///
    /// Rather than guess between the two and ship a patch that silently does
    /// nothing on the screen that matters - which is precisely what happened to
    /// the landing-screen copy for several releases - this covers both and says
    /// which one it found. "Community-Crafted" is the needle because it is
    /// Bossa's phrasing, appears nowhere in our replacement, and cannot collide
    /// with any other label on either screen.
    ///
    /// This is the same technique as LandingCopy_Patch, which is proven to work
    /// on this UI: a sweep over Resources.FindObjectsOfTypeAll, which unlike a
    /// walk down from a screen root also reaches INACTIVE labels - and the
    /// welcome scroll is inactive until it is shown.
    /// </summary>
    internal static class WelcomeCopy_Patch
    {
        /// <summary>
        /// Bossa's phrasing, and nothing else's. Deliberately not "Greetings
        /// Traveller": our own replacement opens with those words, so matching on
        /// them would make the sweep rewrite its own output every pass.
        /// </summary>
        private const string Needle = "Community-Crafted";

        private static bool reported;

        /// <summary>
        /// Replaces every label still carrying Bossa's greeting.
        ///
        /// Idempotent by construction - our text does not contain the needle, so
        /// a label this has already rewritten cannot match again - which is what
        /// makes it safe to call from several hooks and on every late arrival.
        /// </summary>
        internal static void Sweep(string why)
        {
            try
            {
                string replacement = WelcomeMessageFetcher.Current();
                int replaced = 0;

                TextMeshProUGUI[] labels = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
                foreach (TextMeshProUGUI label in labels)
                {
                    if (label == null || string.IsNullOrEmpty(label.text)) continue;
                    if (label.text.IndexOf(Needle, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    label.text = replacement;
                    replaced++;
                }

                if (replaced > 0)
                {
                    Debug.Log("[WAReborn] found Bossa's welcome greeting on " + replaced
                        + " more label(s) while sweeping after " + why + "; replaced.");
                    reported = true;
                }
                else if (!reported)
                {
                    // Not a warning. On the healthy path SplashScreen_Patch has
                    // already overwritten the only copy through its serialized
                    // field, so finding nothing here is the expected outcome and
                    // saying so proves the sweep ran rather than silently no-oped.
                    Debug.Log("[WAReborn] no leftover copy of Bossa's welcome greeting after "
                        + why + ".");
                    reported = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[WAReborn] could not sweep for Bossa's welcome greeting: " + e);
            }
        }

        /// <summary>
        /// The landing screen as the player first sees it. Catches the greeting
        /// if it lives on that screen rather than on the splash page.
        /// </summary>
        [HarmonyPatch(typeof(LandingScreenType))]
        internal static class LandingScreen_Welcome_Patch
        {
            [HarmonyPostfix]
            [HarmonyPatch("ProtectedInit")]
            public static void ProtectedInit_Postfix()
            {
                Sweep("the landing screen opening");
            }

            /// <summary>
            /// And after a successful sign-in, which is when the screen swaps the
            /// login form for the JOIN GAME button - the state the scroll was
            /// reported in. LoginSuccess is the client's own callback, so this
            /// runs whether the player got there through our direct login path or
            /// through the original one.
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch("LoginSuccess")]
            public static void LoginSuccess_Postfix()
            {
                Sweep("a successful login");
            }
        }

        /// <summary>
        /// The DEVELOPMENT BUILD box's own initialisation, which is the last thing
        /// to write text into that corner of the landing screen. Shares the hook
        /// LandingCopy_Patch uses for the same ordering reason: anything written
        /// from the GameDB has been written by the time this returns.
        /// </summary>
        [HarmonyPatch(typeof(DevelopmentArea))]
        internal static class DevelopmentArea_Welcome_Patch
        {
            [HarmonyPostfix]
            [HarmonyPatch("FillTextComponents")]
            public static void FillTextComponents_Postfix()
            {
                Sweep("the development-build box filling in");
            }
        }
    }
}

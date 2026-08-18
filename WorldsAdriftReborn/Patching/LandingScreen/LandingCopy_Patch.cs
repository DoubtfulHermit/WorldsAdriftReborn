using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UI.Components;
using UnityEngine;
using WorldsAdriftReborn.Config;
using LandingScreenType = Travellers.UI.Login.LandingScreen;

namespace WorldsAdriftReborn.Patching.Dynamic.LandingScreen
{
    /// <summary>
    /// Rewrites the login screen's Bossa-era copy, and turns FORUMS into MAP.
    ///
    /// WHY BY TEXT AND NOT BY FIELD. Only two strings on that screen come from
    /// code. "DEVELOPMENT BUILD", the paragraph under it, "FORUMS",
    /// "PATCH NOTES" and "You need to have a Worlds Adrift account to play" are
    /// all static TextMeshPro text baked into the Unity prefab. They are not in
    /// the localisation table either - that table has 64 keys and none of them
    /// are landing-screen keys - so there is no field to set and no row to edit.
    /// The remaining route is to find the labels at runtime and overwrite them.
    ///
    /// Matching on the displayed string rather than on a serialized field name
    /// is also what makes this safe against the one thing the decompile cannot
    /// tell us: which label is which. DevelopmentArea has a _forumAreaTitle it
    /// fills from the GameDB, and whether that is the box heading or the button
    /// caption is a property of the prefab, not of any code we can read.
    /// Matching what is actually on screen sidesteps the guess entirely.
    ///
    /// Every replacement reports whether it landed. A copy change that silently
    /// does nothing is the failure mode here, so anything still unmatched after
    /// both screens have initialised is logged by name.
    ///
    /// The MAP button's URL is a different matter and is set properly: it is
    /// DevelopmentArea's private _forumLink, read out of the GameDB by
    /// PopulateLinks and passed to GotoUrl on click, so that one is a field
    /// write with no guessing involved.
    /// </summary>
    internal static class LandingCopy_Patch
    {
        private enum Match
        {
            /// <summary>The whole label, trimmed, ignoring case.</summary>
            Whole,

            /// <summary>A distinctive fragment, ignoring case.</summary>
            Fragment
        }

        private sealed class Replacement
        {
            public Match Mode;
            public string Needle;
            public string NewText;
            public string What;
            public bool Landed;
        }

        private static readonly Replacement[] Replacements =
        {
            // The box heading. Sits above the paragraph and the two buttons.
            new Replacement
            {
                Mode = Match.Whole,
                Needle = "DEVELOPMENT BUILD",
                NewText = "WAREBORN",
                What = "the development-build heading",
            },

            // The paragraph under it. Matched on "mailing list", which is the
            // most distinctive thing in it and the least likely to appear
            // anywhere else on the screen.
            //
            // Note this MUST be attempted before the FORUMS caption below in
            // reading order, though it cannot actually collide: the paragraph
            // mentions forums but is matched as a fragment, and the caption is
            // matched as a whole label.
            new Replacement
            {
                Mode = Match.Fragment,
                Needle = "mailing list",
                NewText = "Worlds Adrift shut down in 2019. Wareborn is a fan-run server that puts "
                        + "it back online. Plenty is still missing, and things break.",
                What = "the development-build paragraph",
            },

            // The button itself. Whole-label so it cannot swallow the word
            // "forums" inside the paragraph above.
            new Replacement
            {
                Mode = Match.Whole,
                Needle = "FORUMS",
                NewText = "MAP",
                What = "the FORUMS button caption",
            },

            // Under the login form. There is no Worlds Adrift account system
            // left to have an account with.
            new Replacement
            {
                Mode = Match.Fragment,
                Needle = "Worlds Adrift account to play",
                NewText = "You need a Wareborn account to play",
                What = "the login-screen account line",
            },
        };

        private static int passes;

        /// <summary>
        /// Points the old FORUMS button at the live map, then fixes the copy.
        ///
        /// FillTextComponents is the hook rather than PopulateLinks because it
        /// runs immediately after it in DevelopmentArea.Start() and is where the
        /// GameDB titles are pushed into the labels - so by the time this
        /// returns, everything this patch wants to overwrite has been written
        /// once already.
        /// </summary>
        [HarmonyPatch(typeof(DevelopmentArea))]
        internal static class DevelopmentArea_Copy_Patch
        {
            [HarmonyPostfix]
            [HarmonyPatch("FillTextComponents")]
            public static void FillTextComponents_Postfix(DevelopmentArea __instance)
            {
                try
                {
                    FieldInfo forumLink = AccessTools.Field(typeof(DevelopmentArea), "_forumLink");
                    if (forumLink == null)
                    {
                        Debug.LogError("[WAReborn] DevelopmentArea._forumLink is gone; the MAP "
                            + "button still points at the dead Bossa forums.");
                    }
                    else
                    {
                        forumLink.SetValue(__instance, ModSettings.mapUrl.Value);
                        Debug.Log("[WAReborn] MAP button points at " + ModSettings.mapUrl.Value);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("[WAReborn] could not repoint the MAP button: " + e);
                }

                Apply();
            }
        }

        /// <summary>The other end of the screen, for the account line.</summary>
        [HarmonyPatch(typeof(LandingScreenType))]
        internal static class LandingScreen_Copy_Patch
        {
            [HarmonyPostfix]
            [HarmonyPatch("ProtectedInit")]
            public static void ProtectedInit_Postfix()
            {
                Apply();
            }
        }

        /// <summary>
        /// One sweep over every loaded TextMeshPro label.
        ///
        /// FindObjectsOfTypeAll rather than a walk down from either screen,
        /// because the two boxes and the login form are not guaranteed to share
        /// a parent and inactive labels have to be caught too - the account line
        /// is hidden and shown as the form toggles. It runs at most a handful of
        /// times and stops itself once every replacement has landed.
        /// </summary>
        private static void Apply()
        {
            if (AllLanded())
            {
                return;
            }

            passes++;

            try
            {
                TextMeshProUGUI[] labels = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
                foreach (TextMeshProUGUI label in labels)
                {
                    if (label == null || string.IsNullOrEmpty(label.text)) continue;

                    foreach (Replacement r in Replacements)
                    {
                        if (!Matches(label.text, r)) continue;

                        SetLabel(label, r.NewText);
                        if (!r.Landed)
                        {
                            r.Landed = true;
                            Debug.Log("[WAReborn] replaced " + r.What + ".");
                        }
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[WAReborn] could not rewrite the login-screen copy: " + e);
                return;
            }

            // Both hooks have had a turn by now, so anything still unmatched is
            // not going to match. Say which, by name - a copy change that
            // quietly does nothing is exactly what this project keeps getting
            // caught by.
            if (passes >= 2)
            {
                foreach (Replacement r in Replacements)
                {
                    if (!r.Landed)
                    {
                        Debug.LogWarning("[WAReborn] could not find " + r.What + " (looked for '"
                            + r.Needle + "'); it still shows its original text.");
                    }
                }
            }
        }

        private static bool AllLanded()
        {
            foreach (Replacement r in Replacements)
            {
                if (!r.Landed) return false;
            }
            return true;
        }

        private static bool Matches(string text, Replacement r)
        {
            if (r.Mode == Match.Whole)
            {
                return string.Equals(text.Trim(), r.Needle, StringComparison.OrdinalIgnoreCase);
            }
            return text.IndexOf(r.Needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Writes through the TextStyler when there is one.
        ///
        /// TextStylerTextMeshPro owns the label's text and can rewrite it from
        /// its localisation key; setting TextMeshProUGUI.text behind its back
        /// would leave the two disagreeing and could be undone later.
        /// </summary>
        private static void SetLabel(TextMeshProUGUI label, string newText)
        {
            TextStylerTextMeshPro styler = label.GetComponent<TextStylerTextMeshPro>();
            if (styler != null)
            {
                styler.SetText(newText);
                return;
            }
            label.text = newText;
        }
    }
}

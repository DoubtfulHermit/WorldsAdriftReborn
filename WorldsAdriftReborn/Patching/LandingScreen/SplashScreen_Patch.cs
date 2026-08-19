using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using Travellers.UI.Login;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Dynamic.LandingScreen
{
    /// <summary>
    /// Greys out the PvE card on the two-card server screen, because we do not
    /// run that server. Bossa's own copy on both cards is left untouched.
    ///
    /// WHAT THIS SCREEN ACTUALLY IS. It looks like a chooser and it is not one.
    /// SplashScreen (acs/Travellers.UI.Login/SplashScreen.cs) is a two-page
    /// information panel: a welcome page, then a page with a PvE block and a PvP
    /// block. There is no server identity anywhere in it, no click handler on
    /// either card, and exactly one CONTINUE button. All CONTINUE does is
    ///
    ///     private void OnSplashScreenButtonClicked()
    ///     { _serverInfoParent.SetActive(false); IsReadyToContinue = true; }
    ///
    /// which lets SplashScreenState advance. The real server pick happens much
    /// later, on the character-creation screen, from the list our own
    /// /deploymentStatus answers.
    ///
    /// WHAT WE ACTUALLY RUN. One server.
    /// WorldsAdriftServer/Handlers/ServerStatus/DeploymentStatusHandler.cs
    /// builds a single-entry dictionary, id "community_server", status UP, and
    /// nothing in the mod forces any of the client's PVE.* keys, so hostile ship
    /// interactions and player damage are all live. In Bossa's terms that is the
    /// PvP side. There is no PvE deployment and there never was one here.
    ///
    /// So the screen shows a player two equal-looking options where one is
    /// imaginary. The answer is to grey the PvE card out, and ONLY that.
    ///
    /// WHY THE TEXT IS LEFT ALONE. An earlier version of this patch also rewrote
    /// all six card strings to say which server runs. That was the wrong trade:
    /// it threw away Bossa's copy - which is part of what this project exists to
    /// preserve - to say something the greying already says, and it shipped an
    /// empty PvE bullet that drew a lone diamond glyph with no line after it.
    /// The retail strings come from the GameDB localisation table through
    /// SetTexts(), so the way to have the original text is to not overwrite it.
    /// For the record, decrypted out of
    /// StreamingAssets/GameDB/localization.bytes (AES-256-CBC + LZF, key
    /// "jDbTw6roGtva" / salt "5gucbeCOt2pysjlJx", both read from
    /// GameDBAccessor.ImportFromServer in the decompile), the six keys read:
    ///
    ///     SPLASH_SCREEN_PVE_TITLE    "PvP restricted to The Badlands"
    ///     SPLASH_SCREEN_PVE_BULLET1  "Provides a slower-paced, more peaceful
    ///                                 experience"
    ///     SPLASH_SCREEN_PVE_BULLET2  "Recommended for players who prefer
    ///                                 exploring and building creative ships
    ///                                 with friends over combat"
    ///     SPLASH_SCREEN_PVP_TITLE    "Open PvP combat"
    ///     SPLASH_SCREEN_PVP_BULLET1  "Provides more opportunities for dynamic
    ///                                 stories and piracy, and emphasizes
    ///                                 teamwork"
    ///     SPLASH_SCREEN_PVP_BULLET2  "Recommended for players who want more
    ///                                 thrills and danger; where the line
    ///                                 between friend and foe is blurred"
    ///
    /// so both cards say three things and the third PvE line is not blank. That
    /// is what the client draws now that nothing here writes over it.
    ///
    /// SetTexts is still the right seam for the greying, because both public
    /// entry points, SetProductionText() and SetBetaText(), call it first, so
    /// this lands whichever branch SplashScreenState takes.
    ///
    /// TO TURN PvE BACK ON, delete the DimPveCard(__instance) call in
    /// SetTexts_Postfix. Nothing else in this file touches the PvE card, and its
    /// text is already Bossa's, so that one line is the whole switch.
    /// </summary>
    [HarmonyPatch(typeof(SplashScreen))]
    internal static class SplashScreen_Patch
    {
        /// <summary>How faded the unavailable card is. Readable, clearly off.</summary>
        private const float DisabledAlpha = 0.35f;

        /// <summary>
        /// The label on the FIRST splash page - the parchment scroll headed
        /// "Greetings Traveller," - kept so a welcome message that arrives from
        /// the server after the screen has drawn can still be shown.
        ///
        /// Held as a plain reference rather than looked up again because the
        /// screen is disposed when the player continues, and a destroyed
        /// TextMeshProUGUI compares equal to null under Unity's overloaded
        /// operator, which is exactly the check we want.
        /// </summary>
        private static TextMeshProUGUI liveWelcomeLabel;

        private static bool subscribed;

        /// <summary>
        /// Replaces Bossa's welcome copy on the first splash page.
        ///
        /// WHY THIS METHOD AND NOT SetTexts. SetProductionText and SetBetaText
        /// both call SetTexts FIRST and only then assign
        /// _splashScreenTextMesh.text from the localisation table, so a postfix on
        /// SetTexts would be overwritten a line later - the same ordering trap
        /// that makes the landing-screen copy need two hooks. These two are the
        /// last writers, so this is the seam.
        ///
        /// It is a serialized field, not a label found by matching its text, so
        /// unlike LandingCopy_Patch there is no guessing about which label is
        /// which. The retail string is a Bossa press release: it welcomes the
        /// player to "a Community-Crafted MMO", calls the game "still in the early
        /// stages of development", and invites them to contact Community Managers
        /// who have not existed since 2019.
        ///
        /// The text itself comes from the server so an operator can edit it in the
        /// admin panel, with a baked-in default for the offline case. See
        /// WelcomeMessageFetcher for why that fetch never blocks this screen.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("SetProductionText")]
        public static void SetProductionText_Postfix(SplashScreen __instance)
        {
            ApplyWelcome(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch("SetBetaText")]
        public static void SetBetaText_Postfix(SplashScreen __instance)
        {
            ApplyWelcome(__instance);
        }

        private static void ApplyWelcome(SplashScreen screen)
        {
            try
            {
                TextMeshProUGUI label = Label(screen, "_splashScreenTextMesh");
                if (label == null)
                {
                    Debug.LogWarning("[WAReborn] SplashScreen._splashScreenTextMesh is missing; the "
                        + "welcome page still shows Bossa's original copy.");
                    return;
                }

                // Log what is being REPLACED, not just that something was. The
                // decompile can name this field but it cannot prove which
                // parchment on screen it draws, and "the patch applied but the
                // screen is unchanged" is the exact failure this whole area keeps
                // producing. One line of the old text settles it from a log.
                string before = label.text ?? string.Empty;
                Debug.Log("[WAReborn] welcome page label currently reads: '"
                    + Excerpt(before) + "'.");

                liveWelcomeLabel = label;
                label.text = WelcomeMessageFetcher.Current();
                WelcomeCopy_Patch.Sweep("the splash screen");

                if (!subscribed)
                {
                    // Late answers redraw the screen in place. Subscribed once,
                    // never unsubscribed: this is a static event on a type that
                    // lives as long as the process, and the handler is null-safe
                    // against a disposed screen.
                    subscribed = true;
                    WelcomeMessageFetcher.Arrived += OnWelcomeArrived;
                }

                Debug.Log("[WAReborn] welcome page copy replaced ("
                    + (WelcomeMessageFetcher.Fetched == null ? "built-in text" : "from the server")
                    + ").");
            }
            catch (Exception e)
            {
                Debug.LogError("[WAReborn] could not replace the welcome page copy; it may still "
                    + "show Bossa's original text. " + e);
            }
        }

        /// <summary>A single readable line of a label's text, for the log.</summary>
        private static string Excerpt(string text)
        {
            string oneLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return oneLine.Length <= 70 ? oneLine : oneLine.Substring(0, 70) + "...";
        }

        private static void OnWelcomeArrived()
        {
            // Unity's operator== is what makes this safe once the screen has been
            // disposed: a destroyed object compares equal to null.
            if (liveWelcomeLabel == null)
            {
                return;
            }

            liveWelcomeLabel.text = WelcomeMessageFetcher.Current();
            WelcomeCopy_Patch.Sweep("a late server answer");
            Debug.Log("[WAReborn] welcome page updated with the message that arrived from the server.");
        }

        /// <summary>
        /// Runs after the client has filled both cards from the localisation
        /// table, and only greys the PvE one. The retail copy it just wrote is
        /// left exactly as it is.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("SetTexts")]
        public static void SetTexts_Postfix(SplashScreen __instance)
        {
            try
            {
                // Log what the two cards now say. This screen has a history of
                // patches that "applied" while the player saw something else, and
                // the whole point of this change is that six specific strings are
                // Bossa's again - so prove it from a log line instead of asking
                // someone to squint at a screenshot. Titles only: they are the
                // lines that differed most from the copy we used to write, and
                // two of them fit on one line.
                Debug.Log("[WAReborn] server cards read (retail localisation, not ours) - PvE: '"
                    + Excerpt(TextOf(__instance, "_pveTitle")) + "' / PvP: '"
                    + Excerpt(TextOf(__instance, "_pvpTitle")) + "'.");

                DimPveCard(__instance);
            }
            catch (Exception e)
            {
                // The screen still works if this fails - it is only alpha - so
                // never take the boot down over it, but do say so. The failure
                // mode is a PvE card that looks selectable and is not.
                Debug.LogError("[WAReborn] could not grey out the server screen's PvE card; it "
                    + "may look like a server we run. " + e);
            }
        }

        private static TextMeshProUGUI Label(SplashScreen screen, string fieldName)
        {
            FieldInfo field = AccessTools.Field(typeof(SplashScreen), fieldName);
            return field == null ? null : field.GetValue(screen) as TextMeshProUGUI;
        }

        /// <summary>
        /// A label's text for logging. "&lt;missing&gt;" rather than an empty
        /// string when the field is gone, because a blank in the log would read
        /// like a blank on screen - which is the exact bug this change removes.
        /// </summary>
        private static string TextOf(SplashScreen screen, string fieldName)
        {
            TextMeshProUGUI label = Label(screen, fieldName);
            return label == null ? "<missing>" : label.text ?? string.Empty;
        }

        /// <summary>
        /// Fades the PvE card and stops it taking clicks.
        ///
        /// The card root is not a serialized field, so it is found by walking up
        /// from the PvE title until the parent is _serverInfoParent - that child
        /// is the card. A CanvasGroup there dims the block's art as well as its
        /// text, which text colour alone would not; if the card cannot be found,
        /// dimming the three labels is the fallback so the player still gets a
        /// visible difference rather than nothing.
        /// </summary>
        private static void DimPveCard(SplashScreen screen)
        {
            FieldInfo parentField = AccessTools.Field(typeof(SplashScreen), "_serverInfoParent");
            GameObject serverInfoParent = parentField == null
                ? null
                : parentField.GetValue(screen) as GameObject;
            TextMeshProUGUI pveTitle = Label(screen, "_pveTitle");

            Transform card = null;
            if (serverInfoParent != null && pveTitle != null)
            {
                Transform walk = pveTitle.transform;
                while (walk != null && walk.parent != null && walk.parent != serverInfoParent.transform)
                {
                    walk = walk.parent;
                }
                if (walk != null && walk.parent == serverInfoParent.transform)
                {
                    card = walk;
                }
            }

            if (card != null)
            {
                CanvasGroup group = card.GetComponent<CanvasGroup>()
                    ?? card.gameObject.AddComponent<CanvasGroup>();
                group.alpha = DisabledAlpha;
                group.interactable = false;
                group.blocksRaycasts = false;
                Debug.Log("[WAReborn] PvE card '" + card.name + "' greyed out; it is not a server "
                    + "we run.");
                return;
            }

            Debug.LogWarning("[WAReborn] could not find the PvE card root under _serverInfoParent; "
                + "falling back to fading its text only.");
            FadeLabel(screen, "_pveTitle");
            FadeLabel(screen, "_pveBullet1");
            FadeLabel(screen, "_pveBullet2");
        }

        private static void FadeLabel(SplashScreen screen, string fieldName)
        {
            TextMeshProUGUI label = Label(screen, fieldName);
            if (label == null) return;
            Color faded = label.color;
            faded.a = DisabledAlpha;
            label.color = faded;
        }
    }
}

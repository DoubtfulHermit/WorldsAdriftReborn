using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using Travellers.UI.Login;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Dynamic.LandingScreen
{
    /// <summary>
    /// Tells the truth on the two-card server screen: one of them is not running.
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
    /// So the screen was showing a player two equal-looking options where one is
    /// imaginary and neither is a choice. Worse than a broken button: a broken
    /// button at least looks broken. This rewrites the PvE block to say it is not
    /// running and dims the whole card, and rewrites the PvP block to say it is
    /// the only one and that CONTINUE lands there regardless.
    ///
    /// WHY A RUNTIME OVERWRITE. The card text comes from the GameDB localisation
    /// table via SetTexts(), whose source is
    /// StreamingAssets/GameDB/localization.bytes - obfuscated, and re-serialising
    /// it to change six strings would be a far bigger and more fragile change
    /// than a postfix. SetTexts is the right seam because both public entry
    /// points, SetProductionText() and SetBetaText(), call it first, so this
    /// lands whichever branch SplashScreenState takes.
    /// </summary>
    [HarmonyPatch(typeof(SplashScreen))]
    internal static class SplashScreen_Patch
    {
        private const string PveTitle = "PvE Server (not running)";
        private const string PveBullet1 = "Wareborn hosts one server, and this is not it.";
        private const string PveBullet2 = "";

        private const string PvpTitle = "PvP Server";
        private const string PvpBullet1 = "The only server running. CONTINUE brings you here either way.";
        private const string PvpBullet2 = "Other players can attack you and your ship.";

        /// <summary>How faded the unavailable card is. Readable, clearly off.</summary>
        private const float DisabledAlpha = 0.35f;

        [HarmonyPostfix]
        [HarmonyPatch("SetTexts")]
        public static void SetTexts_Postfix(SplashScreen __instance)
        {
            try
            {
                SetText(__instance, "_pveTitle", PveTitle);
                SetText(__instance, "_pveBullet1", PveBullet1);
                SetText(__instance, "_pveBullet2", PveBullet2);

                SetText(__instance, "_pvpTitle", PvpTitle);
                SetText(__instance, "_pvpBullet1", PvpBullet1);
                SetText(__instance, "_pvpBullet2", PvpBullet2);

                DimPveCard(__instance);
            }
            catch (Exception e)
            {
                // The screen still works if this fails - it is text and alpha -
                // so never take the boot down over it, but do say so.
                Debug.LogError("[WAReborn] could not relabel the server screen; it may still "
                    + "show a PvE server that does not exist. " + e);
            }
        }

        private static TextMeshProUGUI Label(SplashScreen screen, string fieldName)
        {
            FieldInfo field = AccessTools.Field(typeof(SplashScreen), fieldName);
            return field == null ? null : field.GetValue(screen) as TextMeshProUGUI;
        }

        private static void SetText(SplashScreen screen, string fieldName, string value)
        {
            TextMeshProUGUI label = Label(screen, fieldName);
            if (label == null)
            {
                Debug.LogWarning("[WAReborn] SplashScreen." + fieldName + " is missing; that line "
                    + "of the server screen keeps its original text.");
                return;
            }
            label.text = value;
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

using HarmonyLib;
using Travellers.UI.InfoPopups;

namespace WorldsAdriftReborn.Patching.Dynamic.LandingScreen
{
    /// <summary>
    /// The Island Creator button.
    ///
    /// LandingScreen.OpenIslandCreator is
    /// SteamFriends.ActivateGameOverlayToStore(new AppId_t(271920u), ...) - a
    /// store page, in the Steam overlay, for a second delisted app. All three of
    /// those are gone, and with the Steam bypass in place the call is a P/Invoke
    /// into a library that was never initialised.
    ///
    /// The old message told the player to start the Island Creator from their
    /// Steam library instead. That has not been possible since the game was
    /// delisted, so it sent people looking for something they cannot find.
    /// </summary>
    [HarmonyPatch(typeof(Travellers.UI.Login.LandingScreen))]
    internal class LandingScreen_Patch
    {
        private static string noIslandCreator = "The Island Creator is not part of this build.\n\n"
            + "It was a separate Steam app, and it was delisted along with the game. "
            + "Wareborn does not need Steam, so there is no library to start it from.";

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Travellers.UI.Login.LandingScreen.OpenIslandCreator))]
        public static bool OpenIslandCreator()
        {
            DialogPopupFacade.ShowOkDialog("Island Creator", noIslandCreator, null, "OK", true, null);
            return false;
        }
    }
}

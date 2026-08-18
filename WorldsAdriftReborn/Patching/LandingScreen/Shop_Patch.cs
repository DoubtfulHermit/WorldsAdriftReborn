using HarmonyLib;
using Travellers.UI.InfoPopups;

namespace WorldsAdriftReborn.Patching.Dynamic.LandingScreen
{
    /// <summary>
    /// There is no shop.
    ///
    /// Shop.OpenShop opened store.steampowered.com/itemstore/&lt;appid&gt;/ in the
    /// Steam overlay. The store page for a delisted game is gone, the overlay
    /// needs a Steam client this build never talks to, and
    /// SteamUtils.GetAppID() is a P/Invoke into an uninitialised
    /// steam_api64.dll, so the original cannot even fail politely.
    ///
    /// The old message said we were "bypassing Steam" and could not open the
    /// overlay, which invited the player to go and open it themselves. Nothing
    /// is there to open.
    /// </summary>
    [HarmonyPatch(typeof(Shop))]
    internal class Shop_Patch
    {
        private static string noShop = "There is no in-game store.\n\n"
            + "This button opened the Steam item shop, which closed with the game in 2019. "
            + "Nothing here costs money and nothing is for sale.";

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Shop.OpenShop))]
        public static bool OpenShop_Prefix()
        {
            DialogPopupFacade.ShowOkDialog("Store", noShop, null, "OK", true, null);
            return false;
        }
    }
}

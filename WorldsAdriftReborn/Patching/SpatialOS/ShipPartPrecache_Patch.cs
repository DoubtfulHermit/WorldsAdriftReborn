using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;

namespace WorldsAdriftReborn.Patching.SpatialOS
{
    /// <summary>
    /// Appends every loose-ship-part prefab to the client's boot precache list.
    ///
    /// THE BUG THIS KILLS: a runtime-crafted part is only visible if its prefab is
    /// ALREADY in the CachingAssetDatabase when the AddEntityOp is processed. The
    /// game loads entity prefabs through an async Resources coroutine that needs at
    /// least one extra frame (ResourcesGameObjectLoader), and the server sends
    /// AssetLoadRequest + AddEntity back-to-back - so the AddEntity races the load
    /// and a cold prefab loses: GetEntityTemplate throws MissingComponentException,
    /// the entity is never created, and the crafted part is INVISIBLE while the
    /// materials are gone (live case: 'CoreMain', the atlas sky core; hard evidence
    /// in BepInEx/LogOutput.log "Prefab: CoreMain (CoreMain_unityclient) cannot be
    /// found"). The stock list ("client-precached-prefabs", 19 names: Helm01,
    /// Lamp01, the instruments, ShipFrame01/02...) is exactly the set of parts that
    /// crafted fine - every part OUTSIDE it came out invisible on first craft.
    ///
    /// The fix warms the whole loose-part catalogue during the loading screen: a
    /// Harmony postfix on Assets.Scripts.Precache.AssetsToPrecache (the TextAsset
    /// list loader Bootstrap feeds into SpatialOS.AssetsToPrecache, precached
    /// BEFORE ConnectAsync) appending ShipPartClientPrecache.PrefabNames - the
    /// SHARED, unit-tested list the server tests pin against LoosePartCatalogue.
    /// WorkerSpecificAssetDatabaseTemplateProvider_Patch keeps a synchronous
    /// load-on-miss as the safety net for anything not on this list.
    /// </summary>
    [HarmonyPatch]
    internal class ShipPartPrecache_Patch
    {
        /// <summary>The stock client precache TextAsset name Bootstrap loads.</summary>
        private const string ClientPrecacheList = "client-precached-prefabs";

        [HarmonyTargetMethod]
        public static MethodBase GetTargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("Assets.Scripts.Precache"), "AssetsToPrecache");
        }

        [HarmonyPostfix]
        public static void AssetsToPrecache_Postfix( string filename, ref List<string> __result )
        {
            if (filename != ClientPrecacheList)
            {
                // Some other list (e.g. the fsim worker's); leave it alone.
                return;
            }

            int before = __result != null ? __result.Count : 0;
            __result = ShipPartClientPrecache.AppendTo(__result);
            Debug.Log("[WAReborn] precache list '" + filename + "': appended "
                + (__result.Count - before) + " ship-part prefab(s) (" + before + " stock -> "
                + __result.Count + " total) so every crafted part renders on first spawn.");
        }
    }
}

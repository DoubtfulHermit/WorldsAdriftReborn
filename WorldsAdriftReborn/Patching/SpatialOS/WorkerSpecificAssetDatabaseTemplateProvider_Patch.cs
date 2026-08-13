using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Assets.Improbable.Core.TemplateProviders;
using Bossa.Travellers.PrefabExporting.Preprocessors;
using HarmonyLib;
using Improbable.Assets;
using Improbable.Corelibrary.PreProcessor.Global;
using Improbable.Unity;
using Improbable.Unity.Assets;
using Improbable.Unity.Entity;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.SpatialOS
{
    [HarmonyPatch()]
    internal class WorkerSpecificAssetDatabaseTemplateProvider_Patch
    {
        [HarmonyTargetMethod]
        public static MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                                        AccessTools.TypeByName("WorkerSpecificAssetDatabaseTemplateProvider"),
                                        "GetEntityTemplate",
                                        new Type[]
                                        {
                                            typeof(string)
                                        });
        }

        [HarmonyPrefix]
        public static void GetEntityTemplate_Prefix( object __instance, string prefabName )
        {
            // The player seems to miss some components which cant be added through sdk calls that we know of, but they are added by the ExportProcess method which gets invoked when we compile the object
            // not sure if this will call the right one tho (there are multiple different ones) but it seems to produce different error messages when used compared to when not used.
            object assetDatabase = AccessTools.Field(AccessTools.TypeByName("WorkerSpecificAssetDatabaseTemplateProvider"), "AssetDatabase").GetValue(__instance);
            IDictionary<string, GameObject> dic = (IDictionary<string, GameObject>)AccessTools.Field(typeof(CachingAssetDatabase), "cachedGameObjects").GetValue(assetDatabase);
            string key = prefabName + "_unityclient";
            PrefabCompiler p = new PrefabCompiler(WorkerPlatform.UnityClient);
            GameObject gObject;

            // RESCUE-ON-MISS: a runtime-spawned entity (a crafted ship part) reaches
            // GetEntityTemplate the moment its AddEntityOp is processed, but the game
            // loads entity prefabs via an ASYNC Resources coroutine that needs at least
            // one extra frame - so a prefab that was not precached loses the race, the
            // provider throws MissingComponentException, the entity is never created and
            // the crafted part is INVISIBLE (live case: CoreMain, the atlas sky core).
            // The prefabs live in resources.assets under "EntityPrefabs/<name>_unityclient",
            // so on a cache miss we load the SAME asset synchronously and seed the cache
            // the provider is about to read. One-frame hitch on first sight of a new
            // prefab type; ShipPartPrecache_Patch warms the known ship parts at boot so
            // this is a safety net, not the normal path. Names containing '@' (island
            // bundles, context-suffixed travellers) are not Resources assets; skip them.
            if (!dic.TryGetValue(key, out gObject) && !prefabName.Contains("@"))
            {
                GameObject loaded = Resources.Load<GameObject>("EntityPrefabs/" + key);
                if (loaded != null)
                {
                    dic[key] = loaded;
                    gObject = loaded;
                    Debug.LogWarning("[WAReborn] RESCUED prefab '" + key + "' with a synchronous"
                        + " Resources load; the async precache had not finished before AddEntity."
                        + " The entity will render normally.");
                }
            }

            if (gObject != null)
            {
                p.Compile(gObject);
                Debug.LogWarning("COMPILED PLAYER GAMEOBJECT!!!");
            }
            else
            {
                Debug.LogWarning("COMPILE FAILED " + key);
            }
        }
    }
}

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

        // Reflection handles resolved ONCE. This prefix runs for EVERY
        // AddEntityOp the client processes - resolving the type by name (a scan
        // of every loaded assembly) and the FieldInfos on each call was pure
        // per-entity overhead concentrated exactly in the load-in window.
        private static readonly FieldInfo AssetDatabaseField =
            AccessTools.Field(AccessTools.TypeByName("WorkerSpecificAssetDatabaseTemplateProvider"), "AssetDatabase");
        private static readonly FieldInfo CachedGameObjectsField =
            AccessTools.Field(typeof(CachingAssetDatabase), "cachedGameObjects");

        // Templates already run through PrefabCompiler, by instance id. The
        // template is a SHARED cached object that MakeComponent clones per
        // entity; compiling it again for every entity that uses the same prefab
        // (every tree, every deposit, every ship part of a kind) redid the whole
        // ExportProcess component pass per entity for zero change. If the cache
        // entry is ever replaced, the new object has a new instance id and gets
        // its own compile.
        private static readonly HashSet<int> compiledTemplates = new HashSet<int>();

        [HarmonyPrefix]
        public static void GetEntityTemplate_Prefix( object __instance, string prefabName )
        {
            // The player seems to miss some components which cant be added through sdk calls that we know of, but they are added by the ExportProcess method which gets invoked when we compile the object
            // not sure if this will call the right one tho (there are multiple different ones) but it seems to produce different error messages when used compared to when not used.
            object assetDatabase = AssetDatabaseField.GetValue(__instance);
            IDictionary<string, GameObject> dic = (IDictionary<string, GameObject>)CachedGameObjectsField.GetValue(assetDatabase);
            string key = prefabName + "_unityclient";
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
                if (compiledTemplates.Add(gObject.GetInstanceID()))
                {
                    PrefabCompiler p = new PrefabCompiler(WorkerPlatform.UnityClient);
                    p.Compile(gObject);
                    // Plain log (no stack trace), once per template - the old
                    // unconditional LogWarning captured and formatted a full
                    // stack trace per entity checkout.
                    Debug.Log("[WAReborn] compiled entity template '" + key + "'");
                }
            }
            else
            {
                Debug.LogWarning("[WAReborn] entity template compile failed: no cached prefab for " + key);
            }
        }
    }
}

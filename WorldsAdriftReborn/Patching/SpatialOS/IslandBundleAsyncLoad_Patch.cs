using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftReborn.Patching.SpatialOS
{
    /// <summary>
    /// Loads island asset bundles ASYNCHRONOUSLY, so checking out an island no
    /// longer stops the frame.
    ///
    /// THE MEASURED PROBLEM. The stutter probe caught approach hitches shaped
    /// like this:
    ///
    ///     spike dt=503.7ms f=17640 t=391.9s ents+0/ops+0 comps+0 tmpl+0 spatial=480.6ms
    ///
    /// Half a second of frame, 480 ms of it inside ConnectionLifecycle.Update -
    /// and NOTHING was instantiated: no AddEntity op received, no entity made, no
    /// component dispatched, no entity template fetched. That combination rules
    /// out entity instantiation, the deferred creation queue and the visualizer
    /// machinery, and leaves exactly one thing that runs on that call stack and
    /// touches none of those counters: an AssetLoadRequestOp arriving in
    /// View.Process, which DispatchEventHandler.OnAssetLoad turns into a
    /// templateProvider.PrepareTemplate call - and, for islands only, into a
    /// SYNCHRONOUS multi-megabyte AssetBundle read.
    ///
    /// WHY ONLY ISLANDS. Retail's loader chain (decompile: ResourceTemplateProvider
    /// -> ResourcesGameObjectLoader) sends every ordinary entity prefab to
    /// Resources.LoadAsync on a coroutine, and hands ONLY names containing
    /// "@Island" to the asset-bundle loader. Retail's shipped bundle loader was
    /// the STREAMING one (AssetBundleDownloader over WWW), which is asynchronous
    /// end to end. This mod forces AssetDatabaseStrategy.Local so the game runs
    /// offline (InitializeAssetLoader_Patch), and the local strategy's
    /// LocalAssetBundleLoader calls AssetBundle.LoadFromFile - blocking - and
    /// GameObjectFromAssetBundleLoader then calls LoadAsset&lt;GameObject&gt; -
    /// also blocking. The island bundles in Assets/unity average 8 MiB and reach
    /// 46 MiB. That is the hitch, and it is ours, not retail's.
    ///
    /// WHAT THIS DOES. For island names only, it replaces that pair with
    /// AssetBundle.LoadFromFileAsync + AssetBundle.LoadAssetAsync driven from a
    /// coroutine. Unity does the read, the decompression and most of the object
    /// deserialisation on its loading thread and time-slices the main-thread
    /// integration (Application.backgroundLoadingPriority), so the same work is
    /// spread over frames instead of landing in one. The callback contract is
    /// unchanged - the whole IAssetLoader chain is callback-shaped precisely
    /// because retail's streaming loader was asynchronous - so the asset-loaded
    /// reply still goes out exactly when the prefab is genuinely ready, which is
    /// what the server's correlated ack waits for before it sends AddEntity.
    ///
    /// WHAT THIS DELIBERATELY DOES NOT DO. It does not touch LoadBalancing.
    /// Everything AFTER the bundle load already goes through retail's queue,
    /// unpatched by this mod: IslandVisualiser.InitImposter, PopulateStaticPrefabs,
    /// TreeBase, MetalDepositVisualiser all call LoadBalancing.Execute with
    /// Priority.IslandObjects/IslandColliderObjects, and
    /// EntityEventHandler.ProcessDeferred already gates entity creation on
    /// LoadBalancing.FastLoadBalancer.HasBudget(). The queue was never bypassed;
    /// the bundle read simply never went through it in the first place, and it
    /// could not - it is not an Instantiate call, it is file I/O.
    ///
    /// SAFETY. Anything unexpected - the type not resolving, the wrapped loader
    /// not being the local one, the bundle file not being where it should be -
    /// falls through to the original synchronous method, which is the current
    /// behaviour. The failure mode of this patch is "no faster", never "no island".
    /// </summary>
    [HarmonyPatch]
    internal class IslandBundleAsyncLoad_Patch
    {
        // Resolved once, in Prepare. If any of them is null the patch is skipped
        // entirely rather than aborting the mod's whole CreateAndPatchAll pass.
        private static Type _bundleLoaderType;
        private static MethodInfo _target;
        private static FieldInfo _wrappedLoaderField;
        private static FieldInfo _entityPrefabsPathField;

        private static readonly IslandBundleLoadLedger Ledger = new IslandBundleLoadLedger();

        private sealed class Waiter
        {
            public Action<GameObject> OnLoaded;
            public Action<Exception> OnError;
        }

        [HarmonyPrepare]
        public static bool Prepare()
        {
            try
            {
                _bundleLoaderType = AccessTools.TypeByName(
                    "Improbable.Unity.Assets.GameObjectFromAssetBundleLoader");
                Type localLoaderType = AccessTools.TypeByName(
                    "Improbable.Unity.Assets.LocalAssetBundleLoader");
                if (_bundleLoaderType == null || localLoaderType == null)
                {
                    Debug.LogWarning("[WAReborn] island bundles will load synchronously:"
                        + " the asset-bundle loader types were not found.");
                    return false;
                }

                _target = AccessTools.Method(_bundleLoaderType, "LoadAsset");
                _wrappedLoaderField = AccessTools.Field(_bundleLoaderType, "assetBundleLoader");
                _entityPrefabsPathField = AccessTools.Field(localLoaderType, "entityPrefabsPath");
                if (_target == null || _wrappedLoaderField == null || _entityPrefabsPathField == null)
                {
                    Debug.LogWarning("[WAReborn] island bundles will load synchronously:"
                        + " the asset-bundle loader members were not found.");
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] island bundles will load synchronously: " + e.Message);
                return false;
            }
        }

        [HarmonyTargetMethod]
        public static MethodBase GetTargetMethod()
        {
            return _target;
        }

        /// <summary>
        /// Positional parameter names (__0/__1/__2) rather than the decompiled
        /// ones: this signature must keep matching even if the shipped metadata
        /// names differ from the decompiler's rendering of them.
        /// </summary>
        [HarmonyPrefix]
        public static bool LoadAsset_Prefix( object __instance, string __0,
            Action<GameObject> __1, Action<Exception> __2 )
        {
            if (!IslandBundleLoadPolicy.IsIslandBundle(__0))
            {
                return true; // ordinary prefabs never reach here; if one does, leave it alone
            }

            string path;
            IslandBundleLoadHost host;
            try
            {
                path = BundlePath(__instance, __0);
                host = IslandBundleLoadHost.Instance;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] falling back to the synchronous island bundle load for '"
                    + __0 + "': " + e.Message);
                return true;
            }

            // No path, no host, or no file: let the original run, so its own
            // error message (and its own success, if the naming rule ever
            // changes) remains the single source of truth.
            if (path == null || host == null || !File.Exists(path))
            {
                return true;
            }

            Waiter waiter = new Waiter { OnLoaded = __1, OnError = __2 };
            if (!Ledger.BeginOrJoin(__0, waiter, Time.realtimeSinceStartup))
            {
                // A load for this exact bundle is already in flight and will
                // deliver to this waiter too. Starting a second
                // LoadFromFileAsync on one file is a Unity error, not a
                // duplicate - and the server retries an unacknowledged asset
                // request every 5 s, so this DOES happen.
                return false;
            }

            try
            {
                host.StartCoroutine(LoadRoutine(__0, path));
            }
            catch (Exception e)
            {
                // The ledger now holds waiters nobody will ever pay. Settle them
                // immediately: a reported failure is recoverable (the server's
                // ack timeout owns it), a silent hang is not.
                Fail(__0, e);
            }
            return false;
        }

        private static string BundlePath( object bundleLoaderInstance, string prefabName )
        {
            object wrapped = _wrappedLoaderField.GetValue(bundleLoaderInstance);
            if (wrapped == null) return null;
            // Only the LOCAL loader keeps a directory; a streaming loader has no
            // file to point at and must keep its own path.
            if (!_entityPrefabsPathField.DeclaringType.IsInstanceOfType(wrapped)) return null;

            string directory = (string)_entityPrefabsPathField.GetValue(wrapped);
            if (string.IsNullOrEmpty(directory)) return null;
            return Path.Combine(directory, IslandBundleLoadPolicy.BundleFileName(prefabName));
        }

        /// <summary>
        /// The asynchronous twin of GameObjectFromAssetBundleLoader.LoadAsset +
        /// OnAssetBundleLoaded. Every observable step is kept identical,
        /// including Unload(false) immediately after the asset is pulled out and
        /// the exact error routing.
        /// </summary>
        private static IEnumerator LoadRoutine( string prefabName, string path )
        {
            float startedAt = Time.realtimeSinceStartup;
            int startedFrame = Time.frameCount;

            AssetBundleCreateRequest create = null;
            try
            {
                create = AssetBundle.LoadFromFileAsync(path);
            }
            catch (Exception e)
            {
                Fail(prefabName, e);
                yield break;
            }
            if (create == null)
            {
                Fail(prefabName, new Exception("AssetBundle.LoadFromFileAsync returned null for '"
                    + path + "'."));
                yield break;
            }

            yield return create;

            AssetBundle bundle = create.assetBundle;
            if (bundle == null)
            {
                Fail(prefabName, new Exception("Failed to load prefab's '" + prefabName
                    + "' asset bundle from file '" + path + "'.\nAsset is most likely corrupted."));
                yield break;
            }

            AssetBundleRequest load = null;
            try
            {
                load = bundle.LoadAssetAsync<GameObject>(prefabName);
            }
            catch (Exception e)
            {
                bundle.Unload(false);
                Fail(prefabName, e);
                yield break;
            }
            if (load == null)
            {
                bundle.Unload(false);
                Fail(prefabName, new Exception("LoadAssetAsync returned null for '" + prefabName + "'."));
                yield break;
            }

            yield return load;

            GameObject prefab = load.asset as GameObject;
            // Identical to the original: the bundle's container is released as
            // soon as the object is out, the object itself is kept.
            bundle.Unload(false);

            if (prefab == null)
            {
                Fail(prefabName, new Exception("Could not load the game object from asset '"
                    + prefabName + "'."));
                yield break;
            }

            Debug.Log("[WAReborn] island bundle '" + prefabName + "' loaded asynchronously in "
                + Mathf.RoundToInt((Time.realtimeSinceStartup - startedAt) * 1000f) + " ms across "
                + (Time.frameCount - startedFrame) + " frames; the main thread was never blocked"
                + " for the whole read.");
            Succeed(prefabName, prefab);
        }

        private static void Succeed( string prefabName, GameObject prefab )
        {
            IList<object> waiters = Ledger.TakeWaiters(prefabName);
            for (int i = 0; i < waiters.Count; i++)
            {
                Waiter waiter = waiters[i] as Waiter;
                if (waiter == null) continue;
                try
                {
                    if (waiter.OnLoaded != null) waiter.OnLoaded(prefab);
                }
                catch (Exception e)
                {
                    // The original wraps the success callback the same way and
                    // routes anything it throws to onError.
                    SafeError(waiter, e);
                }
            }
        }

        private static void Fail( string prefabName, Exception error )
        {
            Debug.LogError("[WAReborn] loading island asset '" + prefabName + "' failed. " + error);
            IList<object> waiters = Ledger.TakeWaiters(prefabName);
            for (int i = 0; i < waiters.Count; i++)
            {
                SafeError(waiters[i] as Waiter, error);
            }
        }

        private static void SafeError( Waiter waiter, Exception error )
        {
            if (waiter == null || waiter.OnError == null) return;
            try
            {
                waiter.OnError(error);
            }
            catch (Exception e)
            {
                Debug.LogError("[WAReborn] island bundle error callback threw: " + e);
            }
        }
    }

    /// <summary>
    /// The coroutine host for island bundle loads.
    ///
    /// It is its own hidden, DontDestroyOnLoad object on purpose. A load hosted
    /// on a scene behaviour dies with the scene, and a dead loader coroutine is
    /// not a slow island - it is an asset-loaded reply that never arrives, which
    /// is precisely the stall AssetLoadAck was written to survive (see its
    /// header: "the loader coroutine's host can die in the loading-screen
    /// handover"). Nothing about this object is scene-scoped, so nothing may
    /// destroy it.
    /// </summary>
    internal sealed class IslandBundleLoadHost : MonoBehaviour
    {
        private static IslandBundleLoadHost _instance;

        internal static IslandBundleLoadHost Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("WAReborn Island Bundle Loader");
                    go.hideFlags = HideFlags.HideAndDontSave;
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<IslandBundleLoadHost>();
                }
                return _instance;
            }
        }
    }
}

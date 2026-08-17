using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Improbable;
using Improbable.CoreLibrary.CoordinateRemapping;
using Improbable.Math;
using Improbable.Worker;
using UnityEngine;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftReborn.Patching.SpatialOS
{
    /// <summary>
    /// Owns client-only, non-physical island silhouettes. A shell clones only
    /// the terrain object's lowest retail LOD from the already-cached island
    /// bundle. It contains no SpatialOS entity, collider, rigidbody simulation,
    /// resources or static prefabs.
    /// </summary>
    internal static class DistantIslandShells
    {
        private sealed class Shell
        {
            public IslandDistantShellSpec Spec;
            public DistantIslandShellAnchor Anchor;
            public bool PhysicalPresent;
        }

        private static readonly Dictionary<long, Shell> ByTerrainEntity =
            new Dictionary<long, Shell>();

        public static void TemplatePrepared(object dispatchHandler, AssetLoadRequestOp request)
        {
            IslandDistantShellSpec spec;
            if (!IslandDistantShellProtocol.TryParseRequest(request.AssetType, out spec)) return;

            Shell existing;
            if (ByTerrainEntity.TryGetValue(spec.EntityId, out existing)
                && existing.Anchor != null)
            {
                existing.Anchor.SendReadyAgain(request.Name, request.Context);
                return;
            }

            try
            {
                GameObject template = CachedTemplate(dispatchHandler, request.Name);
                GenerateDynamicMaterial source = template == null
                    ? null : template.GetComponentInChildren<GenerateDynamicMaterial>(true);
                if (source == null)
                {
                    Debug.LogWarning("[WAReborn] island shell " + spec.IslandId
                        + ": cached template has no GenerateDynamicMaterial terrain object.");
                    return;
                }

                // Clone the terrain object, not the authoritative island entity
                // root. That excludes IslandVisualiser and all injected entity
                // readers before this object ever becomes active.
                GameObject root = UnityEngine.Object.Instantiate(source.gameObject);
                root.name = "WAReborn Distant Island Shell - " + spec.IslandId;
                root.SetActive(false);

                Renderer[] allRenderers = root.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in allRenderers) renderer.enabled = false;
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
                foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
                {
                    body.isKinematic = true;
                    body.detectCollisions = false;
                }

                GenerateDynamicMaterial generator =
                    root.GetComponentInChildren<GenerateDynamicMaterial>(true);
                foreach (Behaviour behaviour in root.GetComponentsInChildren<Behaviour>(true))
                    if (behaviour != generator) behaviour.enabled = false;

                LODGroup lodGroup = root.GetComponentInChildren<LODGroup>(true);
                Renderer[] lowRenderers = LowestNonEmptyLod(lodGroup);
                if (generator == null || lodGroup == null || lowRenderers.Length == 0)
                {
                    UnityEngine.Object.Destroy(root);
                    Debug.LogWarning("[WAReborn] island shell " + spec.IslandId
                        + ": terrain template has no usable retail LOD group.");
                    return;
                }

                DistantIslandShellAnchor anchor = root.AddComponent<DistantIslandShellAnchor>();
                bool physicalPresent = Improbable.Unity.Core.SpatialOS.Universe.Get(
                    new EntityId(spec.EntityId)) != null;
                var shell = new Shell
                {
                    Spec = spec,
                    Anchor = anchor,
                    PhysicalPresent = physicalPresent,
                };
                ByTerrainEntity[spec.EntityId] = shell;
                anchor.Begin(spec, request.Name, request.Context, generator, lodGroup,
                    lowRenderers, delegate { return shell.PhysicalPresent; });
                root.SetActive(true);
                generator.GenerateMaterial();
                Debug.Log("[WAReborn] preparing non-physical low-LOD shell for "
                    + spec.IslandId + " (terrain entity " + spec.EntityId + ").");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] failed to prepare distant island shell for '"
                    + request.Name + "': " + e);
            }
        }

        public static void SetPhysicalPresence(long terrainEntityId, bool present)
        {
            Shell shell;
            if (!ByTerrainEntity.TryGetValue(terrainEntityId, out shell)
                || shell.Anchor == null) return;
            shell.PhysicalPresent = present;
            shell.Anchor.RefreshVisibility();
        }

        private static GameObject CachedTemplate(object dispatchHandler, string prefabName)
        {
            FieldInfo providerField = AccessTools.Field(dispatchHandler.GetType(), "templateProvider");
            object provider = providerField == null ? null : providerField.GetValue(dispatchHandler);
            if (provider == null) return null;
            MethodInfo getter = AccessTools.Method(provider.GetType(), "GetEntityTemplate",
                new Type[] { typeof(string) });
            return getter == null ? null
                : getter.Invoke(provider, new object[] { prefabName }) as GameObject;
        }

        private static Renderer[] LowestNonEmptyLod(LODGroup group)
        {
            if (group == null) return new Renderer[0];
            LOD[] lods = group.GetLODs();
            for (int i = lods.Length - 1; i >= 0; i--)
                if (lods[i].renderers != null && lods[i].renderers.Length > 0)
                    return lods[i].renderers;
            return new Renderer[0];
        }
    }

    internal sealed class DistantIslandShellAnchor : MonoBehaviour
    {
        private IslandDistantShellSpec spec;
        private string assetName;
        private string assetContext;
        private GenerateDynamicMaterial generator;
        private LODGroup lodGroup;
        private Renderer[] visibleRenderers;
        private Func<bool> physicalPresent;
        private bool materialReady;
        private float nextRemapAt;

        public void Begin(IslandDistantShellSpec value, string name, string context,
            GenerateDynamicMaterial materialGenerator, LODGroup group,
            Renderer[] renderers, Func<bool> isPhysicalPresent)
        {
            spec = value;
            assetName = name;
            assetContext = context;
            generator = materialGenerator;
            lodGroup = group;
            visibleRenderers = renderers;
            physicalPresent = isPhysicalPresent;
            RemapPosition();
            StartCoroutine(WaitForMaterial());
        }

        private IEnumerator WaitForMaterial()
        {
            while (generator != null && !generator.HasMaterialGenerated) yield return null;
            if (generator == null) yield break;

            // The LODGroup served its purpose: GenerateDynamicMaterial assigned
            // the proper packed low-LOD material through it. Disable it before
            // exposing our explicitly selected last retail LOD.
            if (lodGroup != null) lodGroup.enabled = false;
            materialReady = true;
            RefreshVisibility();
            SendReadyAgain(assetName, assetContext);
            Debug.Log("[WAReborn] distant island shell ready for " + spec.IslandId
                + "; renderers=" + visibleRenderers.Length + ".");
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < nextRemapAt) return;
            nextRemapAt = Time.unscaledTime + 0.25f;
            RemapPosition();
        }

        private void RemapPosition()
        {
            if (spec == null) return;
            var globalMetres = new Vector3d(
                (double)spec.X / 4096.0,
                (double)spec.Y / 4096.0,
                (double)spec.Z / 4096.0);
            transform.position = CoordinateRemappingBehaviour.GlobalVectorToUnityPosition(globalMetres);
            transform.rotation = Quaternion.identity;
        }

        public void RefreshVisibility()
        {
            bool show = materialReady && (physicalPresent == null || !physicalPresent());
            if (visibleRenderers == null) return;
            foreach (Renderer renderer in visibleRenderers)
                if (renderer != null) renderer.enabled = show;
        }

        public void SendReadyAgain(string name, string context)
        {
            if (!materialReady || spec == null) return;
            // Re-sends are intentional: the server may retry when the first ready
            // packet was lost. Construction remains idempotent by entity id.
            string marker = IslandDistantShellProtocol.Ready(
                spec.IslandId, spec.EntityId, spec.X, spec.Y, spec.Z);
            Improbable.Unity.Core.SpatialOS.Connection.SendAssetLoadedResponse(
                marker, name, context);
        }
    }

    [HarmonyPatch]
    internal static class DistantIslandShell_AddEntity_Patch
    {
        [HarmonyTargetMethod]
        private static MethodBase Target()
        {
            return AccessTools.Method(AccessTools.TypeByName(
                "Improbable.Unity.Core.DispatchEventHandler"), "AddEntity");
        }

        [HarmonyPostfix]
        private static void Added(AddEntityOp addEntity)
        {
            bool present = Improbable.Unity.Core.SpatialOS.Universe.Get(addEntity.EntityId) != null;
            if (present) DistantIslandShells.SetPhysicalPresence(addEntity.EntityId.Id, true);
        }
    }

    [HarmonyPatch]
    internal static class DistantIslandShell_RemoveEntity_Patch
    {
        [HarmonyTargetMethod]
        private static MethodBase Target()
        {
            return AccessTools.Method(AccessTools.TypeByName(
                "Improbable.Unity.Core.DispatchEventHandler"), "RemoveEntity");
        }

        [HarmonyPostfix]
        private static void Removed(RemoveEntityOp removeEntity)
        {
            DistantIslandShells.SetPhysicalPresence(removeEntity.EntityId.Id, false);
        }
    }
}

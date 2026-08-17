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

        public static void PrepareProcedural(AssetLoadRequestOp request,
            IslandDistantShellSpec spec)
        {
            Shell existing;
            if (ByTerrainEntity.TryGetValue(spec.EntityId, out existing)
                && existing.Anchor != null)
            {
                existing.Anchor.SendReadyAgain(request.Name, request.Context);
                return;
            }
            try
            {
                GameObject root = new GameObject("WAReborn Compact Island Shell - " + spec.IslandId);
                MeshFilter filter = root.AddComponent<MeshFilter>();
                MeshRenderer renderer = root.AddComponent<MeshRenderer>();
                filter.sharedMesh = ProceduralMesh(spec);
                // Submesh 0 is the plateau, submesh 1 the rock beneath it. Both are
                // muted: a distant island is mostly silhouette, and fog does the rest.
                Material[] shellMaterials = new Material[]
                {
                    ShellMaterial(PlateauColour),
                    ShellMaterial(RockColour),
                };
                renderer.sharedMaterials = shellMaterials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                DistantIslandShellAnchor anchor = root.AddComponent<DistantIslandShellAnchor>();
                anchor.hazeMaterials = shellMaterials;
                Haze(shellMaterials, root.transform.position);
                bool physicalPresent = Improbable.Unity.Core.SpatialOS.Universe.Get(
                    new EntityId(spec.EntityId)) != null;
                var shell = new Shell { Spec = spec, Anchor = anchor, PhysicalPresent = physicalPresent };
                ByTerrainEntity[spec.EntityId] = shell;
                anchor.BeginProcedural(spec, request.Name, request.Context,
                    new Renderer[] { renderer }, delegate { return shell.PhysicalPresent; });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] failed compact shell " + spec.IslandId + ": " + e);
            }
        }

        // The underside profile. The outline is a MAXIMUM-radius radial silhouette
        // sampled from the island's walkable top surface, so the rim belongs at the
        // top of the envelope and the mass below it tapers to a keel. These two
        // numbers shape that taper and are the only invented values here; the rim
        // radius, the rim height and the keel depth are all measured.
        private const float KeelRingHeight = .45f;
        private const float KeelRingInset = .72f;

        private static bool loggedShader;

        /// <summary>
        /// A floating-island silhouette: a plateau cap at the measured top, and an
        /// underside tapering to a keel at the measured bottom.
        ///
        /// The previous mesh was a flat-topped, flat-bottomed drum spanning
        /// MinY..MinY+45% - the BOTTOM 45% of the envelope. It therefore drew the
        /// island's underside and omitted the plateau the outline was sampled from,
        /// leaving the silhouette a median 121 m (up to 411 m) below the terrain it
        /// stands in for. Islands appeared to sit too low and then jump when the
        /// physical terrain replaced them.
        /// </summary>
        private static Mesh ProceduralMesh(IslandDistantShellSpec spec)
        {
            int count = spec.Outline.Length;
            float rimY = (float)spec.MaxY;
            float keelY = (float)spec.MinY;
            float ringY = keelY + (rimY - keelY) * KeelRingHeight;

            // The cap ring and the body ring are SEPARATE vertices at the same
            // coordinates. Sharing them let RecalculateNormals average the
            // upward cap normal with the outward wall normal across what should be
            // the island's hard rim, smoothing the one edge that gives the shape
            // its read and leaving something that looked blown from glass.
            Vector3[] vertices = new Vector3[count * 3 + 2];
            int bodyRim = count;
            int keelRing = count * 2;
            int keelApex = count * 3;
            int topCenter = keelApex + 1;
            for (int i = 0; i < count; i++)
            {
                float x = (float)spec.Outline[i].X;
                float z = (float)spec.Outline[i].Z;
                vertices[i] = new Vector3(x, rimY, z);
                vertices[bodyRim + i] = new Vector3(x, rimY, z);
                vertices[keelRing + i] = new Vector3(x * KeelRingInset, ringY, z * KeelRingInset);
            }
            vertices[keelApex] = new Vector3(0, keelY, 0);
            vertices[topCenter] = new Vector3(0, rimY, 0);

            // Two submeshes so the plateau and the rock read differently without a
            // texture: at this distance the top/side break is most of the shape cue.
            List<int> cap = new List<int>(count * 3);
            List<int> body = new List<int>(count * 9);
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                cap.Add(topCenter); cap.Add(next); cap.Add(i);
                // The side walls must face OUTWARD. The original pair wound the
                // other way, so the shell's flanks were backface-culled too and
                // the viewer looked straight through them at the inside of the far
                // wall - which is why it read as blown glass rather than rock.
                body.Add(keelRing + i); body.Add(bodyRim + next); body.Add(keelRing + next);
                body.Add(keelRing + i); body.Add(bodyRim + i); body.Add(bodyRim + next);
                // The keel fan faces DOWN, so it winds opposite to the top cap.
                // Copying the cap's order here pointed the whole underside inward:
                // it was backface-culled, every island rendered with no bottom, and
                // the silhouette read as a shape with a piece missing.
                body.Add(keelApex); body.Add(keelRing + i); body.Add(keelRing + next);
            }

            Mesh mesh = new Mesh();
            mesh.name = "WAReborn compact island silhouette";
            mesh.vertices = vertices;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(cap.ToArray(), 0);
            mesh.SetTriangles(body.ToArray(), 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Fades a shell toward the horizon by distance, because NOTHING ELSE WILL.
        ///
        /// The diagnostic below reported `scene fog=False`: this game does not use
        /// Unity's built-in fog, so choosing a fog-aware shader bought exactly
        /// nothing and the shell rendered at full contrast in front of an
        /// atmosphere it should be dissolving into. The retail terrain shaders do
        /// their own aerial perspective and we cannot borrow theirs without loading
        /// the island bundle the compact shell exists to avoid.
        ///
        /// So the shell hazes itself, from the scene's ambient light rather than a
        /// hardcoded pink: this sky changes colour with the time of day, and a
        /// baked-in tint would be wrong for most of it.
        /// </summary>
        private const float HazeStartMetres = 1500f;
        private const float HazeFullMetres = 7000f;
        private const float HazeMaxStrength = .88f;

        // Darker and less saturated than the first pass. A distant island reads as
        // MASS first, and a pale surface against a pale sky has no silhouette at
        // all - which is most of why the first attempt looked like glass.
        private static readonly Color PlateauColour = new Color(.24f, .28f, .22f, 1f);
        private static readonly Color RockColour = new Color(.20f, .19f, .18f, 1f);

        internal static void Haze(Material[] materials, Vector3 shellPosition)
        {
            if (materials == null) return;
            Camera camera = Camera.main;
            if (camera == null) return;

            float distance = Vector3.Distance(camera.transform.position, shellPosition);
            float t = Mathf.Clamp01((distance - HazeStartMetres) / (HazeFullMetres - HazeStartMetres));
            Color haze = RenderSettings.ambientLight;

            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                Color baseColour = i == 0 ? PlateauColour : RockColour;
                materials[i].color = Color.Lerp(baseColour, haze, t * HazeMaxStrength);
            }
        }

        /// <summary>
        /// A lit material. Unlit/Color ignored scene lighting entirely, so the shell
        /// had no form at all; this at least takes the sun. Falls back through the
        /// shaders this client is known to carry.
        /// </summary>
        private static Material ShellMaterial(Color color)
        {
            Shader shader = Shader.Find("Legacy Shaders/Diffuse")
                ?? Shader.Find("Diffuse")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Unlit/Color");
            // Report which one we actually got, ONCE. Whether the shell sits in the
            // scene's haze or reads as a cut-out pasted on the sky depends entirely
            // on this falling through to something fog-aware, and guessing from a
            // screenshot is exactly what cost a round trip.
            if (!loggedShader)
            {
                loggedShader = true;
                Debug.Log("[WAReborn] compact island shell shader: "
                    + (shader == null ? "<none found>" : shader.name)
                    + "; scene fog=" + RenderSettings.fog
                    + " mode=" + RenderSettings.fogMode
                    + " colour=" + RenderSettings.fogColor);
            }
            Material material = new Material(shader);
            material.color = color;
            return material;
        }

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
                // MonoBehaviour.StartCoroutine is rejected on an inactive
                // GameObject. Renderers and physics are already disabled above,
                // so activation is visually inert; activate first, then arm the
                // material waiter and generator.
                root.SetActive(true);
                anchor.Begin(spec, request.Name, request.Context, generator, lodGroup,
                    lowRenderers, delegate { return shell.PhysicalPresent; });
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

        public void BeginProcedural(IslandDistantShellSpec value, string name, string context,
            Renderer[] renderers, Func<bool> isPhysicalPresent)
        {
            spec = value;
            assetName = name;
            assetContext = context;
            visibleRenderers = renderers;
            physicalPresent = isPhysicalPresent;
            materialReady = true;
            RemapPosition();
            RefreshVisibility();
            SendReadyAgain(name, context);
            Debug.Log("[WAReborn] compact distant shell ready for " + spec.IslandId + ".");
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
            DistantIslandShells.Haze(hazeMaterials, transform.position);
        }

        /// <summary>The compact shell's own materials, or null for a v1 retail shell.</summary>
        internal Material[] hazeMaterials;

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

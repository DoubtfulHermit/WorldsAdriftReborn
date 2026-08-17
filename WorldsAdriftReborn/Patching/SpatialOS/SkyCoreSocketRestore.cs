using System;
using HarmonyLib;
using UnityEngine;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;

namespace WorldsAdriftReborn.Patching.SpatialOS
{
    /// <summary>
    /// Restores the ORPHANED sky-core socket system to the cached entity templates.
    ///
    /// WHAT RETAIL DID: ShipCorePreprocessor (an IPrefabExportProcessor) ran at
    /// prefab-export time and added ShipCoreVisualizer + an Activate interaction to
    /// the core base; the module prefabs carried ShipCoreModuleVisualizer and the
    /// base's socket transforms carried ShipCoreModuleLocator. In THIS build the
    /// preprocessor is attached to NOTHING and all three components are absent from
    /// every GameObject in the shipped assets (full UnityPy census) - but the
    /// SOCKETS themselves are authored: CoreMain_unityclient/CoreMain_LOD0 has all
    /// eight locator transforms (named per module, incl. the shipped typo
    /// "CoreStabiliserLoacotor") plus the visualizer's VFX targets ("Point light"
    /// Light, "Sparks (WhiteSpark)" + "FlockingAtoms (4)" ParticleSystems,
    /// "light_bulb1" MeshRenderer). So the client's own placement path
    /// (PlacementPreview.cs:694-707: aim-parent must have ShipCoreVisualizer, the
    /// phantom must have ShipCoreModuleVisualizer, snap =
    /// GetTransformForModule(ModuleType), dedup by ModuleType) only needs the
    /// components re-attached - which is what this does, at the same effective
    /// point the retail preprocessor ran: on the cached template, before
    /// PrefabCompiler.Compile (WorkerSpecificAssetDatabaseTemplateProvider_Patch).
    /// The phantom is Instantiate(template) (PlacementPreview.CreatePhantom), so
    /// one template mutation covers entity, pool and phantom alike.
    ///
    /// FAIL-OPEN: everything is wrapped; a failure logs and leaves the template
    /// exactly as it was (parts still render/lift, placement shows the normal
    /// invalid feedback instead of snapping - never a brick). A missing socket
    /// child gets a synthesized stand-in at the base root, because
    /// GetTransformForModule returning null would NRE the placement preview.
    /// Components are added DISABLED, the same convention every baked [Require]
    /// visualizer ships with - the visualizer framework enables them on the real
    /// entity once 1236 + 190602 inject (both are in the loose-part seed set), and
    /// on the non-entity phantom they stay disabled so their Update/LateUpdate
    /// never dereferences a never-injected reader.
    /// </summary>
    internal static class SkyCoreSocketRestore
    {
        // ShipCoreVisualizer's serialized color defaults, verbatim from the decompile.
        private static readonly Color NoLoadColor = new Color32(255, 244, 223, 255);
        private static readonly Color FullLoadColor = new Color32(255, 180, 69, 255);
        private static readonly Color OverloadedColor = new Color32(255, 24, 24, 255);

        /// <summary>
        /// Applies the restoration to one cached template, keyed by the bare prefab
        /// name ("CoreMain", "CoreGenerator", ...). Idempotent; safe for every
        /// prefab (non-sky-core names return immediately).
        /// </summary>
        internal static void Apply( string prefabName, GameObject template )
        {
            try
            {
                if (prefabName == SkyCoreSockets.BasePrefabName)
                {
                    RestoreBase(template);
                    return;
                }

                SkyCoreSocketRow row = SkyCoreSockets.ForPrefab(prefabName);
                if (row != null)
                {
                    RestoreModule(template, row);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] sky-core socket restore FAILED for '" + prefabName + "': " + e
                    + "\nCore placement will show the normal invalid feedback instead of snapping;"
                    + " nothing is bricked.");
            }
        }

        /// <summary>
        /// CoreMain: attach a ShipCoreModuleLocator (with its ModuleType) to each of
        /// the eight authored socket transforms, then ShipCoreVisualizer.Create fed
        /// the authored VFX children - LateUpdate dereferences light/particles/sparks
        /// unconditionally, so any missing one gets an inert stand-in.
        /// </summary>
        private static void RestoreBase( GameObject template )
        {
            if (template.GetComponent<ShipCoreVisualizer>() != null)
            {
                return;
            }

            int authored = 0, synthesized = 0;
            foreach (SkyCoreSocketRow row in SkyCoreSockets.Modules)
            {
                Transform socket = FindDeepChild(template.transform, row.LocatorChildName);
                if (socket == null)
                {
                    // GetTransformForModule(type) returning null NREs PlacementPreview,
                    // so a missing socket gets a stand-in at the base root: that one
                    // module places at the core's origin, and the log says why.
                    Debug.LogWarning("[WAReborn] sky-core: authored socket '" + row.LocatorChildName
                        + "' not found under CoreMain; synthesizing a stand-in at the root.");
                    GameObject standIn = new GameObject(row.LocatorChildName);
                    standIn.transform.SetParent(template.transform, false);
                    socket = standIn.transform;
                    synthesized++;
                }
                else
                {
                    authored++;
                }

                if (socket.GetComponent<ShipCoreModuleLocator>() == null)
                {
                    ShipCoreModuleLocator locator = socket.gameObject.AddComponent<ShipCoreModuleLocator>();
                    AccessTools.Field(typeof(ShipCoreModuleLocator), "_moduleType")
                        .SetValue(locator, ParseModuleType(row.ModuleTypeName));
                }
            }

            MeshRenderer[] bulbs = RenderersInDeepChild(template.transform, "light_bulb1");
            Light light = ComponentInDeepChild<Light>(template.transform, "Point light")
                ?? InertStandIn(template).AddComponent<Light>();
            light.enabled = false; // the visualizer drives it; start dark
            ParticleSystem particles = ComponentInDeepChild<ParticleSystem>(template.transform, "FlockingAtoms (4)")
                ?? InertStandIn(template).AddComponent<ParticleSystem>();
            ParticleSystem sparks = ComponentInDeepChild<ParticleSystem>(template.transform, "Sparks (WhiteSpark)")
                ?? InertStandIn(template).AddComponent<ParticleSystem>();

            ShipCoreVisualizer.Create(template, bulbs, NoLoadColor, FullLoadColor, OverloadedColor,
                light, particles, sparks);

            // Baked-visualizer convention: disabled on the template. The framework
            // enables it on the entity once its [Require]d 1236 + 190602 inject; the
            // phantom clone stays disabled and its LateUpdate never runs un-injected.
            ShipCoreVisualizer visualizer = template.GetComponent<ShipCoreVisualizer>();
            if (visualizer != null)
            {
                visualizer.enabled = false;
            }

            Debug.Log("[WAReborn] sky-core: restored ShipCoreVisualizer + " + SkyCoreSockets.Modules.Length
                + " module locators on CoreMain (" + authored + " authored socket(s), " + synthesized
                + " synthesized). Modules now snap to their sockets.");
        }

        /// <summary>
        /// A module prefab: attach ShipCoreModuleVisualizer (internal type, hence
        /// AccessTools) and reflection-set its private serialized _type so the
        /// placement gate can match it to the base's locator and dedup by type.
        /// </summary>
        private static void RestoreModule( GameObject template, SkyCoreSocketRow row )
        {
            Type visualizerType = AccessTools.TypeByName("ShipCoreModuleVisualizer");
            if (visualizerType == null)
            {
                Debug.LogWarning("[WAReborn] sky-core: ShipCoreModuleVisualizer type not found;"
                    + " module '" + row.PrefabName + "' will not snap.");
                return;
            }

            if (template.GetComponent(visualizerType) != null)
            {
                return;
            }

            Behaviour visualizer = (Behaviour)template.AddComponent(visualizerType);
            visualizer.enabled = false; // template convention, see class remarks
            AccessTools.Field(visualizerType, "_type").SetValue(visualizer, ParseModuleType(row.ModuleTypeName));

            Debug.Log("[WAReborn] sky-core: restored ShipCoreModuleVisualizer (" + row.ModuleTypeName
                + ") on module prefab '" + row.PrefabName + "'.");
        }

        private static object ParseModuleType( string moduleTypeName )
        {
            return Enum.Parse(typeof(ShipCoreModuleTypes), moduleTypeName);
        }

        /// <summary>A hidden, inert child for a VFX stand-in the authored prefab lacks.</summary>
        private static GameObject InertStandIn( GameObject template )
        {
            GameObject go = new GameObject("WAReborn_CoreFxStandIn");
            go.transform.SetParent(template.transform, false);
            return go;
        }

        private static Transform FindDeepChild( Transform root, string name )
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }
                Transform hit = FindDeepChild(child, name);
                if (hit != null)
                {
                    return hit;
                }
            }
            return null;
        }

        private static T ComponentInDeepChild<T>( Transform root, string name ) where T : Component
        {
            Transform child = FindDeepChild(root, name);
            return child == null ? null : child.GetComponent<T>();
        }

        private static MeshRenderer[] RenderersInDeepChild( Transform root, string name )
        {
            Transform child = FindDeepChild(root, name);
            return child == null ? new MeshRenderer[0] : child.GetComponentsInChildren<MeshRenderer>(true);
        }
    }
}

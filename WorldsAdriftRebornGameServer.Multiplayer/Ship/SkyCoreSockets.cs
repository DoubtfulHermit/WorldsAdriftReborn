// SHARED SOURCE - compiled into BOTH the BepInEx client mod (net35, C# 7.3) and
// the unit-tested WorldsAdriftRebornGameServer.Multiplayer library (net6.0),
// exactly like ClientRigPolicy / ShipPartClientPrecache. Keep this file
// net35 / C# 7.3 clean: no nullable annotations, no records, no target-typed new.

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>One sky-core module: its prefab, its ShipCoreModuleTypes value, and its socket.</summary>
    public sealed class SkyCoreSocketRow
    {
        public SkyCoreSocketRow(string prefabName, string moduleTypeName, string locatorChildName)
        {
            PrefabName = prefabName;
            ModuleTypeName = moduleTypeName;
            LocatorChildName = locatorChildName;
        }

        /// <summary>The module part's prefab base name ("CoreGenerator").</summary>
        public string PrefabName { get; private set; }

        /// <summary>
        /// The ShipCoreModuleTypes enum MEMBER NAME ("AdvancedGenerator"). A string,
        /// not the enum: this shared file cannot reference the game assembly, so the
        /// client patch parses it with Enum.Parse and SkyCoreSocketsTests pins the
        /// spelling against the decompiled enum's member list.
        /// </summary>
        public string ModuleTypeName { get; private set; }

        /// <summary>
        /// The AUTHORED socket transform's name under the CoreMain prefab's LOD0 -
        /// verbatim from the shipped asset, including its quirks (the lower-case
        /// "coreCoolantSystemLocator" and the typo'd "CoreStabiliserLoacotor").
        /// </summary>
        public string LocatorChildName { get; private set; }
    }

    /// <summary>
    /// The sky-core socket map: which module snaps into which authored socket on
    /// the CoreMain base, under which ShipCoreModuleTypes key.
    ///
    /// WHY THIS EXISTS. The client's own placement path for "coreModule" parts
    /// (PlacementPreview.cs:694-707) demands three components this build ships on
    /// NO prefab (full asset census): ShipCoreVisualizer on the base (whose
    /// GetTransformForModule reads ShipCoreModuleLocator children),
    /// ShipCoreModuleLocator on each socket transform, and
    /// ShipCoreModuleVisualizer (+_type) on each carried module. The SOCKETS
    /// themselves ARE authored - CoreMain_LOD0 carries all eight locator
    /// transforms, named after the module prefabs - only the components were
    /// stripped. The client mod re-attaches them at template-compile time
    /// (SkyCoreSocketRestore) using exactly this map; the retail preprocessor
    /// (ShipCorePreprocessor.ExportProcess, attached to nothing in this build)
    /// did the same job at export time.
    ///
    /// THE TYPE PAIRING IS OURS. Six of the eight pairs are forced by name
    /// (AtlasEnhancer, AirFilter, CoolantSystem, CoreStabiliser,
    /// EfficiencyModule, CircuitryNetwork); the remaining two module parts
    /// (CoreGenerator, CoreComputer) take the remaining two enum values
    /// (AdvancedGenerator by name, EnergyAmplifier by elimination). Because BOTH
    /// sides of the placement check (the locator's _moduleType and the module's
    /// _type) are assigned from THIS map, the pairing only has to be internally
    /// consistent - the enum value is a key, not a behavior.
    /// </summary>
    public static class SkyCoreSockets
    {
        /// <summary>The base of the chain: the prefab that carries the authored sockets.</summary>
        public const string BasePrefabName = "CoreMain";

        /// <summary>The eight modules, one per authored socket on the base.</summary>
        public static readonly SkyCoreSocketRow[] Modules = new SkyCoreSocketRow[]
        {
            new SkyCoreSocketRow("CoreAtlasEnhancer",    "AtlasEnhancer",     "CoreAtlasEnhancerLocator"),
            new SkyCoreSocketRow("CoreGenerator",        "AdvancedGenerator", "CoreGeneratorLocator"),
            new SkyCoreSocketRow("CoreAirfilter",        "AirFilter",         "CoreAirfilterLocator"),
            new SkyCoreSocketRow("CoreCoolantSystem",    "CoolantSystem",     "coreCoolantSystemLocator"),
            new SkyCoreSocketRow("CoreStabiliser",       "CoreStabiliser",    "CoreStabiliserLoacotor"),
            new SkyCoreSocketRow("CoreComputer",         "EnergyAmplifier",   "CoreComputerLocator"),
            new SkyCoreSocketRow("CoreCircuitryNetwork", "CircuitryNetwork",  "CoreCircuitryNetworkLocator"),
            new SkyCoreSocketRow("CoreEfficiencyModule", "EfficiencyModule",  "CoreEfficiencyModuleLocator"),
        };

        /// <summary>The module row for a prefab, or null when the prefab is not a sky-core module.</summary>
        public static SkyCoreSocketRow ForPrefab(string prefabName)
        {
            for (int i = 0; i < Modules.Length; i++)
            {
                if (Modules[i].PrefabName == prefabName)
                {
                    return Modules[i];
                }
            }
            return null;
        }
    }
}

using System.Collections.Generic;

// SHARED SOURCE - compiled into BOTH the BepInEx client mod (net35, C# 7.3) and
// the unit-tested WorldsAdriftRebornGameServer.Multiplayer library (net6.0),
// exactly like ClientRigPolicy. Keep this file net35 / C# 7.3 clean: no
// nullable annotations, no records, no target-typed new.

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The ship-part prefabs the CLIENT must precache at boot, and the pure
    /// merge the client patch applies to the game's own precache list.
    ///
    /// WHY THIS LIST EXISTS. The client instantiates a runtime-spawned entity
    /// the moment the AddEntityOp is processed, but it loads entity prefabs via
    /// an ASYNC Resources coroutine that needs at least one extra frame
    /// (ResourcesGameObjectLoader). The server sends AssetLoadRequest and
    /// AddEntity back-to-back for a crafted part, so the AddEntity always races
    /// the load - and a prefab that is not ALREADY in the CachingAssetDatabase
    /// loses (MissingComponentException, entity never created, part invisible).
    /// The stock game precaches only 19 prefabs ("client-precached-prefabs":
    /// Helm01, Lamp01, the instruments, ShipFrame01/02...), which is precisely
    /// why those parts crafted fine and EVERY new part type came out invisible.
    /// Appending the full loose-part catalogue to that list makes every craft
    /// render deterministically, warmed during the loading screen.
    ///
    /// ShipPartClientPrecacheTests pins this list against LoosePartCatalogue
    /// (every catalogue prefab MUST be here) and against the client census
    /// (every name here MUST be loadable), so a new catalogue row without a
    /// precache entry fails the build, not the player.
    /// </summary>
    public static class ShipPartClientPrecache
    {
        /// <summary>
        /// Every distinct prefab the loose-part catalogue can spawn. Names are
        /// the exact, case-preserved prefab names (the client appends
        /// "_unityclient" itself). Duplicates with the stock precache list are
        /// harmless - <see cref="AppendTo"/> deduplicates.
        /// </summary>
        public static readonly string[] PrefabNames = new string[]
        {
            // Basics
            "Helm01", "Sail01", "Deck01",
            // Procedural engine / wing
            "ModularEngine", "ModularWing",
            // Sky cores
            "CoreMain", "CoreAtlasEnhancer", "CoreGenerator", "CoreAirfilter",
            "CoreCoolantSystem", "CoreStabiliser", "CoreComputer",
            "CoreCircuitryNetwork", "CoreEfficiencyModule",
            // Structural
            "Panel01", "Panel02", "Panel03", "Window01", "Stairs1",
            "RailingStraight", "RailingCorner", "BarPipe", "BarPipeBent",
            // Storage
            "ContainerSmall", "ContainerMount", "ContainerMedium", "ContainerLarge",
            // Decoration
            "Barrel01", "Cupboard", "Horn01", "Lamp01",
            // Instruments
            "Altimeter", "FuelGauge", "HeadingIndicator", "ArtificialHorizon",
            "AirspeedIndicator",
            // Power + reviver
            "PowerGenerator01", "Respawner01",
        };

        /// <summary>
        /// Every OTHER world prefab the server can name in a connect-time or
        /// broadcast AddEntityOp: the global (biome-data) entity and every
        /// placeable deployable's world prefab (Deployables' AssetNames). These hit
        /// the exact same race as the ship parts - the server sends
        /// AssetLoadRequest + AddEntity back-to-back on the placement broadcast,
        /// and a timeout-advanced spawn chain can send AddEntity with no request at
        /// all - so a cold prefab loses and survives only via the synchronous
        /// rescue's frame hitch. Warming them at boot makes the rescue the rare
        /// path it was meant to be. Live case 2026-08-13: 'GlobalEntity' rescued at
        /// AddEntity because nothing had ever precached it.
        ///
        /// Deliberately EXCLUDED: "Trunk" and "MountedBox" - their Deployables rows
        /// are assetVerified:false and the names are NOT in the client census, so
        /// precaching them would log a load error at every boot (and the census
        /// test would fail the build). DeployablePrecacheTests pins this list
        /// against Deployables so a new resolvable deployable cannot be added
        /// without being precached here.
        /// </summary>
        public static readonly string[] WorldPrefabNames = new string[]
        {
            // World-wide data entity (biome table the deposits wait on).
            "GlobalEntity",
            // Stations
            "Shipyard", "CraftingStation",
            // Storage (Cupboard/Barrel01 already covered by the part list)
            "MakeshiftStorage", "ContainerMedium", "ContainerLarge",
            // Utility & lights (Lamp01/PowerGenerator01/Lifter-family overlap is
            // deduplicated by AppendTo)
            "Campfire", "Stove01", "Loom01", "Lifter",
            "KiokiRevivalChamberA", "TerritoryControlBeacon",
        };

        /// <summary>
        /// Appends every ship-part and world prefab not already present to
        /// <paramref name="baseList"/> (case-sensitive match, the same equality
        /// the client's asset DB key uses after suffixing). Returns the same
        /// list instance for the Harmony postfix to hand back. A null base list
        /// yields a fresh list of just our prefabs.
        /// </summary>
        public static List<string> AppendTo(List<string> baseList)
        {
            List<string> result = baseList != null ? baseList : new List<string>();
            for (int i = 0; i < PrefabNames.Length; i++)
            {
                if (!result.Contains(PrefabNames[i]))
                {
                    result.Add(PrefabNames[i]);
                }
            }
            for (int i = 0; i < WorldPrefabNames.Length; i++)
            {
                if (!result.Contains(WorldPrefabNames[i]))
                {
                    result.Add(WorldPrefabNames[i]);
                }
            }
            return result;
        }
    }
}

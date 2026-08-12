using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The facts about a FUEL POD this server can place - the FUEL analogue of the
    /// atlas shard, but HOST-LESS. A fuel pod is a "fuel egg": a single SpatialOS
    /// entity carrying a <c>LodgeableState</c> (2102) that a player frees and picks
    /// up with a native 1211 PickUp, whereupon the server grants the <c>"fuel"</c>
    /// crafting material to their inventory. Acquisition reuses the SHARED
    /// lodgeable-pickup core (<see cref="LodgeablePickupRegistry"/> +
    /// <see cref="LodgeablePickupPolicy"/>) that the atlas shard also uses.
    ///
    /// WHY HOST-LESS. Unlike the atlas shard (a <c>MetalDepositAtlas</c> lodged in a
    /// metal-deposit CORE, freed by mining that core), the fuel pod's authoritative
    /// worker visualiser <c>FuelPodVisualiser_fsim</c> [Require]s ONLY
    /// <c>LodgeableState.Reader</c> (2102) - there is NO 1305 rock-core link, no host
    /// entity, nothing to mine to free it (VERIFIED: acs/FuelPodVisualiser_fsim.cs
    /// lines 9-10, acs/FuelPod.cs). So a pod is registered already RELEASED (directly
    /// pickable), and the shared core's Lodged->Released stage is simply skipped.
    ///
    /// PREFAB. The pod prefab carries <c>FuelPod</c> + a <c>Rigidbody</c> and is set
    /// up by <c>EggPreprocessor</c> (acs/EggPreprocessor.cs: on the worker it adds
    /// FuelPodVisualiser_fsim and sets FuelPod.IsLodged=true; on the CLIENT it swaps
    /// in LiftableVisualizer + RawMaterialBreakOnImpactVisualizer, i.e. a liftable
    /// raw material that breaks into its resource). The best-matching RESOLVABLE
    /// prefab name is <c>Egg</c> - docs/research/loop/data/prefab-names.tsv line 57
    /// with BOTH the client and worker columns "yes" - which is the generic egg the
    /// fuel EggPreprocessor runs on. Sent bare, like every other placed prefab (the
    /// client appends the worker suffix itself). Overridable at runtime with
    /// <c>WAREBORN_FUELPOD_ASSET</c> so the exact name can be corrected without a
    /// rebuild once a live client confirms it.
    ///
    /// HONEST UNKNOWNS (live-capture only, same class as the atlas shard - see
    /// docs/research/findings-combustion-fuel.md §6):
    ///  - the pod prefab's EXACT resolvable name (Egg is the best match, not proven);
    ///  - whether the stock CLIENT renders + lets you PickUp the egg when seeded only
    ///    2102/190602/1210 (its client visualisers [Require] a break/salvage path we
    ///    do not serve, so it renders from baked geometry like the nugget does, and
    ///    the exact client interaction - PickUp vs salvage-break vs lift - is unproven);
    ///  - <see cref="FuelPerPod"/>, the retail fuel yield per pod (reconstructed).
    /// </summary>
    public static class FuelPods
    {
        /// <summary>The default wire prefab name; see the class remarks for the derivation.</summary>
        public const string DefaultAssetName = "Egg";

        /// <summary>
        /// The wire prefab name, from <c>WAREBORN_FUELPOD_ASSET</c> or
        /// <see cref="DefaultAssetName"/>. Runtime-overridable because the exact name
        /// is the primary live-capture unknown; a blank/whitespace value falls back to
        /// the default rather than spawning an unresolvable entity.
        /// </summary>
        public static string AssetName
        {
            get
            {
                string? env = System.Environment.GetEnvironmentVariable("WAREBORN_FUELPOD_ASSET");
                return string.IsNullOrWhiteSpace(env) ? DefaultAssetName : env.Trim();
            }
        }

        /// <summary>
        /// The inventory <c>itemTypeId</c> a collected pod grants and recipes consume.
        /// REAL: <c>"fuel"</c> is an existing <c>itemData.json</c> row (name "Fuel",
        /// category "Fuel", 2x2, stack 99), which <c>ItemHelper</c> treats as a
        /// resource, so <c>InventoryService.Grant</c> accepts and stacks it. Unlike
        /// the atlas shard's id, this one is NOT pending - the row already ships.
        /// (The retail id string itself is WAReborn's own choice, as with every WA
        /// item; the point is it resolves and grants.)
        /// </summary>
        public const string ItemTypeId = "fuel";

        /// <summary>
        /// Fuel units granted per collected pod. RECONSTRUCTED, not retail: the pod's
        /// LodgeableState carries no amount and the retail yield is server refdata lost
        /// with the dead servers (findings-combustion-fuel §6). 5 is a documented knob
        /// - a small stack (fuel stacks to 99) that makes the gather->craft loop
        /// meaningful without pretending to a real number. Tune once a live 1081 delta
        /// after a pickup is captured.
        /// </summary>
        public const int FuelPerPod = 5;

        /// <summary>Registration-key prefix for a placed fuel pod. See <see cref="KeyFor"/>.</summary>
        public const string KeyPrefix = "fuel-pod-";

        /// <summary>The registration key for the fuel pod at a given index.</summary>
        public static string KeyFor(int index) => KeyPrefix + index;

        /// <summary>True if a registration key is a fuel pod's.</summary>
        public static bool IsPodKey(string? key) =>
            key != null && key.StartsWith(KeyPrefix, System.StringComparison.Ordinal);

        /// <summary>
        /// The placement index for a pod key ("fuel-pod-N"), or null if the key is not
        /// a pod's or carries no parseable index.
        /// </summary>
        public static int? IndexOf(string? key)
        {
            if (!IsPodKey(key))
            {
                return null;
            }
            return int.TryParse(key!.Substring(KeyPrefix.Length), out int index) && index >= 0
                ? index
                : (int?)null;
        }

        /// <summary>The 1210 PickUp interaction radius for a fuel pod, metres. Reuses the nugget's.</summary>
        public const float PickUpRadius = MetalNodes.PickUpRadius;

        /// <summary>The 1210 PickUp interaction hold time for a fuel pod, seconds. Reuses the nugget's.</summary>
        public const float PickUpTimeToUse = MetalNodes.PickUpTimeToUse;

        /// <summary>
        /// The 2102 LodgeableState.slotName for a fuel pod. Empty: a host-less pod has
        /// no core slot to name, and no client reader gates on it (it drives only a
        /// SlotNameUpdated callback nothing subscribes to). Empty (not null) is the
        /// benign value the Data struct copies by value.
        /// </summary>
        public const string SlotName = "";

        /// <summary>The island every player spawns on; pods are placed island-local against it.</summary>
        public static readonly FixedPointPosition IslandOrigin = SpawnPolicy.IslandPosition;

        /// <summary>One island-local fuel-pod placement on Haven (island-local metres).</summary>
        public readonly struct Placement
        {
            public Placement(double localX, double localY, double localZ)
            {
                LocalX = localX;
                LocalY = localY;
                LocalZ = localZ;
            }

            public double LocalX { get; }
            public double LocalY { get; }
            public double LocalZ { get; }
        }

        /// <summary>
        /// The fuel-pod placements on Haven, island-local metres - a modest starter set
        /// so fuel is gatherable across the island.
        ///
        /// Each is a MEASURED near-spawn LOD0 surface vertex from the same Haven
        /// surface table the trees and metal nodes draw from (ny&gt;0.90, flat, clear
        /// ground), so a pod rests on real terrain rather than an invented coordinate
        /// that could land underground (this island's pre-TRS tables were once wrong by
        /// a mean of 47.7 m). They are spread from ~30 m to ~120 m of the spawn point.
        ///
        /// STARTER SET, honestly so: for this pass the pods reuse the island's proven
        /// measured vertices, so a pod may share a flat shelf with a distributed tree
        /// or a metal node placed at the same vertex. Dedicated fuel-DEPOSIT placement
        /// - surface-sampled the way the metal-deposit work scatters nodes from the
        /// island surface data - is the flagged follow-up; this pass only needs fuel
        /// reachable and pickable. NOT a placement study.
        /// </summary>
        public static readonly IReadOnlyList<Placement> HavenPlacements = new[]
        {
            new Placement(192.0, 7.13,   8.0), // ~30 m NE of spawn, flat
            new Placement(152.0, 4.71,   0.0), // ~60 m, flat shelf
            new Placement(176.0, 6.39, -16.0), // ~50 m, flat
            new Placement(128.0, 6.12,   0.0), // ~85 m, flat
            new Placement(184.0, 3.10, -32.0), // ~45 m S of spawn, flat
        };

        /// <summary>The world position of the fuel pod at a placement index.</summary>
        public static FixedPointPosition PositionAt(int index)
        {
            Placement p = HavenPlacements[index];
            return MetalNodes.IslandLocalToWorldFixed(IslandOrigin, p.LocalX, p.LocalY, p.LocalZ);
        }

        /// <summary>The number of fuel pods to place, clamped to [1, full table].</summary>
        public static int CountFrom(string? countEnv) =>
            SpawnCountPolicy.CountFrom(countEnv, HavenPlacements.Count);
    }
}

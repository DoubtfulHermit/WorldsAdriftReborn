using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The facts about a FUEL CANISTER (fuel pod) this server can place - the world
    /// source of the <c>"fuel"</c> crafting material.
    ///
    /// IT IS A SALVAGE TARGET, NOT A PICKUP. Retail fuel is obtained by SALVAGING
    /// fuel canisters with the gauntlet salvage tool - the same tool and flow used for
    /// metal and wood - and the canisters "protrude all over islands"
    /// (worldsadrift.fandom.com/wiki/Fuel, /wiki/Resources, /wiki/Mining). It is NOT
    /// an interact/E pickup. An earlier pass modelled it as a lodgeable pickup
    /// (1211 InteractWithObject/PickUp, generalized from the atlas shard); that was
    /// wrong and has been removed. The yield curve is the recovered retail
    /// <see cref="FuelCanisterYield"/>: 3 shots, 8 + 8 + 9 = 25 fuel.
    ///
    /// CLIENT EVIDENCE that a canister is a salvage target:
    ///  - <c>PlayerMultitool.TryDeploySalvager</c> (acs/PlayerMultitool.cs:288-306)
    ///    raycasts, fetches a <c>Salvageable</c> off the hit entity and gates the shot
    ///    on <c>componentInEntity.IsSalvageable()</c>; only then does it raise
    ///    <c>ShotEntity(hitEntity, ...)</c>, which the player's 2106 publishes - the
    ///    exact same path a metal node's shots take.
    ///  - <c>Salvageable</c> (acs/Salvageable.cs:8-9) <c>[Require]</c>s
    ///    <c>SalvageAndRepairStateReader</c> = component <b>1099</b>. So a canister
    ///    must carry 1099 with <c>isSalvageable=true</c> or the beam refuses it
    ///    outright (the same rule the tree's 1099 already documents).
    ///  - The pod prefab's CLIENT export adds <c>RawMaterialBreakOnImpactVisualizer</c>
    ///    (acs/EggPreprocessor.cs:22-27), which <c>[Require]</c>s
    ///    <c>SalvageAndRepairStateReader</c> (1099) and
    ///    <c>DetachFromParentWhenUnderHealthThresholdStateReader</c> (1235), and on
    ///    break plays <c>Play_HarvestImpact_MaterialBreak</c> with the material's SFX
    ///    switch (acs/RawMaterialBreakOnImpactVisualizer.cs:12-31) - i.e. it is a RAW
    ///    MATERIAL that breaks under HARVEST impact, on a health threshold. That is a
    ///    salvage/harvest object, not a pick-up-able item.
    ///  - Its WORKER visualiser <c>FuelPodVisualiser_fsim</c>
    ///    (acs/FuelPodVisualiser_fsim.cs:9-10) <c>[Require]</c>s only
    ///    <c>LodgeableState.Reader</c> (<b>2102</b>), which drives
    ///    <c>FuelPod.IsLodged</c> -> <c>Rigidbody.isKinematic</c> (acs/FuelPod.cs:48-51)
    ///    - so 2102 is still needed, purely as the "sits still" physics flag.
    ///
    /// So the served set is <b>190602</b> (transform) + <b>1099</b> (salvageable, the
    /// gate) + <b>2102</b> (kinematic) + <b>1016</b> (health, already served healthy by
    /// the generic branch, which is what keeps <c>IsDamaged()</c> false and the target
    /// aimable). No 1210 - there is no pickup prompt.
    ///
    /// HONEST UNKNOWNS (live-capture only - see docs/research/findings-combustion-fuel.md §6):
    ///  - the canister prefab's EXACT resolvable name ("Egg" is the best match in the
    ///    verified prefab-names table for the prefab the fuel EggPreprocessor runs on,
    ///    not proven to be the fuel variant); override with WAREBORN_FUELPOD_ASSET;
    ///  - whether the stock CLIENT renders it and accepts the beam with exactly this
    ///    served set (the 1235 break-state is NOT served, so the break VFX/SFX may not
    ///    fire even though the salvage itself works);
    ///  - RELOCATION/RESPAWN: retail fuel-canister locations CHANGE on every island
    ///    resource reset (the ~1.5-2 h "understorm" that also replaces ore nodes and
    ///    scrap piles). This build places them ONCE - see
    ///    <see cref="HavenPlacements"/>. Flagged, not built.
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
        /// The inventory <c>itemTypeId</c> a salvaged canister grants and recipes
        /// consume. REAL: <c>"fuel"</c> is an existing <c>itemData.json</c> row (name
        /// "Fuel", category "Fuel", 2x2, stack 99), which <c>ItemHelper</c> treats as a
        /// resource, so <c>InventoryService.Grant</c> accepts and stacks it. It is also
        /// the 1099 <c>itemTypeId</c> the canister advertises as its salvage material.
        /// </summary>
        public const string ItemTypeId = "fuel";

        /// <summary>Salvage shots to empty a canister: 3 (recovered retail).</summary>
        public static int ShotsToDeplete => FuelCanisterYield.ShotsToDeplete;

        /// <summary>Total fuel one canister is worth: 25 (recovered retail, 8+8+9).</summary>
        public static int TotalFuel => FuelCanisterYield.TotalFuel;

        /// <summary>Registration-key prefix for a placed fuel canister. See <see cref="KeyFor"/>.</summary>
        public const string KeyPrefix = "fuel-pod-";

        /// <summary>The registration key for the fuel canister at a given index.</summary>
        public static string KeyFor(int index) => KeyPrefix + index;

        /// <summary>True if a registration key is a fuel canister's.</summary>
        public static bool IsPodKey(string? key) =>
            key != null && key.StartsWith(KeyPrefix, System.StringComparison.Ordinal);

        /// <summary>
        /// The placement index for a canister key ("fuel-pod-N"), or null if the key is
        /// not a canister's or carries no parseable index.
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

        /// <summary>
        /// The 2102 LodgeableState.slotName for a canister. Empty: a free-standing
        /// canister has no core slot to name, and no client reader gates on it (it
        /// drives only a SlotNameUpdated callback nothing subscribes to). Empty (not
        /// null) is the benign value the Data struct copies by value.
        /// </summary>
        public const string SlotName = "";

        /// <summary>The island every player spawns on; canisters are placed island-local against it.</summary>
        public static readonly FixedPointPosition IslandOrigin = IslandCatalog.Haven.GlobalOrigin;

        /// <summary>One island-local fuel-canister placement on Haven (island-local metres).</summary>
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
        /// The fuel-canister placements on Haven, island-local metres - a modest
        /// starter set so fuel is gatherable across the island ("canisters protrude all
        /// over islands").
        ///
        /// Each is a MEASURED LOD0 surface vertex from the same Haven surface table the
        /// trees and metal nodes draw from (ny&gt;0.90, flat, clear ground), so a
        /// canister rests on real terrain rather than an invented coordinate that could
        /// land underground (this island's pre-TRS tables were once wrong by a mean of
        /// 47.7 m). They spread from ~30 m to ~85 m of the spawn point.
        ///
        /// STATIC, and honestly so: retail canister locations CHANGE on every island
        /// resource reset (the ~1.5-2 h "understorm" that also replaces ore nodes and
        /// scrap piles). This table is a fixed starter placement; relocation belongs in
        /// the same world-wide respawn system the ore nodes will need, and is flagged
        /// as follow-up rather than built here.
        /// </summary>
        public static readonly IReadOnlyList<Placement> HavenPlacements = new[]
        {
            new Placement(192.0, 7.13,   8.0), // ~30 m NE of spawn, flat
            new Placement(152.0, 4.71,   0.0), // ~60 m, flat shelf
            new Placement(176.0, 6.39, -16.0), // ~50 m, flat
            new Placement(128.0, 6.12,   0.0), // ~85 m, flat
            new Placement(184.0, 3.10, -32.0), // ~45 m S of spawn, flat
        };

        /// <summary>The world position of the fuel canister at a placement index.</summary>
        public static FixedPointPosition PositionAt(int index)
        {
            Placement p = HavenPlacements[index];
            return MetalNodes.IslandLocalToWorldFixed(IslandOrigin, p.LocalX, p.LocalY, p.LocalZ);
        }

        /// <summary>The number of fuel canisters to place, clamped to [1, full table].</summary>
        public static int CountFrom(string? countEnv) =>
            SpawnCountPolicy.CountFrom(countEnv, HavenPlacements.Count);
    }
}

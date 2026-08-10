namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The facts about the ANCHORED metal DEPOSIT this server can place - the real
    /// Worlds Adrift ore mechanic, as opposed to the loose <see cref="MetalNodes"/>
    /// nugget. A deposit is a single SpatialOS entity spawned from the
    /// <c>metal_deposit_entity</c> prefab whose salvage beam breaks the outer CRUST
    /// into pieces where it is hit (12283 MetalRockCrustState.shotPoints +
    /// ShotCrustEvent), then depletes the CORE's health (1016 ItemHealthState) over
    /// ~10 shots, and at zero flags the core destroyed and the crust exploded
    /// (2103 MetalRockCoreState.isDestroyed + 12283 exploded). Fully
    /// server-authoritative; the client is stock.
    ///
    /// WHY A SEPARATE MODULE FROM THE NUGGET. The nugget rolls and its "collection"
    /// is a server-invented sink teleport; the deposit is a static prop with the
    /// game's own authored crust/core loop. They share the <see cref="NodeRegistry"/>
    /// ledger (which already carries shotPoints) and the <see cref="MetalHarvest"/>
    /// shot counter, but everything that differs - the prefab, the variant, the
    /// component set, the depletion feedback - lives here.
    ///
    /// EVERYTHING HERE IS EITHER MEASURED/EXTRACTED OR MARKED AS AN ASSUMPTION, per
    /// the standing caveat that a third of this project's confident static
    /// conclusions have been wrong when run.
    /// </summary>
    public static class MetalDeposits
    {
        /// <summary>
        /// The prefab name on the wire, BARE.
        ///
        /// VERIFIED: <c>metal_deposit_entity</c> is line 328 of
        /// docs/research/loop/data/prefab-names.tsv with BOTH the client and worker
        /// columns "yes", and it is the name strings-scanned out of the shipped
        /// resources.assets (<c>metal_deposit_entity_unityclient</c> /
        /// <c>_unityworker</c>). Sent bare for the same reason "Tree"/"MetalNugget"
        /// are: the client appends the worker suffix itself
        /// (WorkerSpecificPrefabName.GetWorkerSpecificPrefabName), so the bare name
        /// is correct and a "_unityclient" suffix would be doubled and resolve to
        /// nothing.
        /// </summary>
        public const string AssetName = "metal_deposit_entity";

        /// <summary>Registration-key prefix for a placed deposit. See <see cref="KeyFor"/>.</summary>
        public const string KeyPrefix = "deposit-";

        /// <summary>The registration key for the deposit at a given index.</summary>
        public static string KeyFor(int index) => KeyPrefix + index;

        /// <summary>Whether a registration key names a placed deposit.</summary>
        public static bool IsDepositKey(string? key) =>
            key != null && key.StartsWith(KeyPrefix, System.StringComparison.Ordinal);

        /// <summary>
        /// The DEFAULT 1255 variantId - a real <c>MetalDepositVisuals</c> asset id.
        ///
        /// VERIFIED by a strings scan of the shipped
        /// <c>sharedassets0.assets</c>: the <c>MetalDepositsByBiome</c>
        /// BiomeSpecificResourceTable lists, under EVERY biome
        /// (<c>MetalDeposits_Biome01</c> through <c>_Biome04</c>), the three
        /// AssetVariant ids <c>metal_deposit_composite_light_01</c> /
        /// <c>_02</c> / <c>_03</c>. SharedResourceData.MetalDepositVariant looks the
        /// id up case-insensitively for the deposit's biome, so this resolves in any
        /// biome Haven turns out to be. A variantId that does NOT resolve leaves
        /// MetalDepositVisualiser disabled and the entity invisible - so this is the
        /// single most important string in the deposit.
        ///
        /// Overridable at runtime with <c>WAREBORN_DEPOSIT_VARIANT</c> so the other
        /// two variants (or a future biome-specific one) can be tried live WITHOUT a
        /// rebuild, matching the WAREBORN_* switch philosophy used across this server.
        /// </summary>
        public const string DefaultVariantId = "metal_deposit_composite_light_01";

        /// <summary>
        /// The 1255 variantId to seed, from <c>WAREBORN_DEPOSIT_VARIANT</c> or the
        /// verified <see cref="DefaultVariantId"/>.
        /// </summary>
        public static string VariantId()
        {
            string? env = System.Environment.GetEnvironmentVariable("WAREBORN_DEPOSIT_VARIANT");
            return string.IsNullOrWhiteSpace(env) ? DefaultVariantId : env.Trim();
        }

        // ------------------------------------------------------------------
        // The depletion sizing. The client's own crust/core feedback is driven by
        // 1016 HealthPct and by the shotPoints cloud - both of which the server
        // authors - so these numbers ARE the mining feel.
        // ------------------------------------------------------------------

        /// <summary>
        /// 1016 ItemHealthState.maxHealth for a deposit core. With
        /// <see cref="SalvageShootDamage"/> = 200 this is <see cref="ShotsToDeplete"/>
        /// = 10 shots, ~7.5 s of held beam at the client's ~0.75 s MinDeployInterval -
        /// the sizing the research doc measured (findings-metal-deposits.md).
        /// </summary>
        public const int MaxHealth = 2000;

        /// <summary>Health removed per salvage shot. 200 * 10 = <see cref="MaxHealth"/>.</summary>
        public const int SalvageShootDamage = 200;

        /// <summary>Salvage shots to empty one deposit. See <see cref="MaxHealth"/>.</summary>
        public const int ShotsToDeplete = 10;

        /// <summary>
        /// Units of metal a deposit frees when the core is destroyed. Fed to
        /// <see cref="MetalHarvest.Place"/> and granted through
        /// <c>HarvestReward.Award</c> on the single deplete transition. Richer than a
        /// nugget's five because a deposit is ten shots of work; invented, like the
        /// nugget's yield, since the authored scrap-spawn path is gone.
        /// </summary>
        public const int YieldUnits = 12;

        /// <summary>
        /// Current core health for a deposit that has taken <paramref name="hits"/>
        /// salvage shots, clamped to [0, <see cref="MaxHealth"/>]. Pure, so both the
        /// live 1016 broadcast and a late joiner's 1016 seed compute the same value
        /// from the shot count without storing a second number.
        /// </summary>
        public static int HealthAfter(int hits)
        {
            if (hits < 0)
            {
                hits = 0;
            }
            int health = MaxHealth - (hits * SalvageShootDamage);
            return health < 0 ? 0 : health;
        }

        /// <summary>The island every player spawns on; deposits are placed island-local against it.</summary>
        public static readonly FixedPointPosition IslandOrigin = SpawnPolicy.IslandPosition;

        /// <summary>
        /// One island-local deposit placement on Haven: a metal type, a quality, and a
        /// surface vertex (island-local metres).
        /// </summary>
        public readonly struct Placement
        {
            public Placement(string metalType, int quality, double localX, double localY, double localZ)
            {
                MetalType = metalType;
                Quality = quality;
                LocalX = localX;
                LocalY = localY;
                LocalZ = localZ;
            }

            public string MetalType { get; }
            public int Quality { get; }
            public double LocalX { get; }
            public double LocalY { get; }
            public double LocalZ { get; }
        }

        /// <summary>
        /// The test deposit placements on Haven, island-local metres.
        ///
        /// Index 0 is the PROVEN-mode deposit: island-local (216.0, 4.57, 8.0) - the
        /// same measured LOD0 surface vertex the nugget's proven node uses (8.9 m from
        /// the spawn point, ny = 0.995, well inside the salvager's 10 m aim reach), so
        /// a tester walks a few paces and aims. The remaining two are a small spread at
        /// other measured near-spawn vertices so the area reads as more than one rock.
        /// A deposit is bigger than a nugget; these are far enough apart (>= 14 m) not
        /// to interpenetrate. Placement is a SEPARATE task - this is just enough to
        /// prove the mining loop.
        /// </summary>
        public static readonly IReadOnlyList<Placement> HavenPlacements = new[]
        {
            new Placement("iron",   6, 216.0, 4.57,   8.0), // PROVEN, 8.9 m from spawn (index 0)
            // Generated from the extracted Haven LOD0 surface table
            // (docs/research/world-data/island-surfaces/1431299145.json): flat ground
            // (upward normal >= 0.92), reachable height y in [1.5,12], each >= 14 m
            // apart and >= 9 m from the distributed trees, so ore and trees never
            // overlap and none spawn buried. Metal/quality cycled for variety.
            new Placement("iron",   5, 200.0, 4.27,   0.0),
            new Placement("copper", 6, 184.0, 7.32,   0.0),
            new Placement("iron",   7, 200.0, 4.98,  16.0),
            new Placement("iron",   5, 184.0, 8.68, -16.0),
            new Placement("copper", 6, 216.0, 5.51,  16.0),
            new Placement("iron",   7, 184.0, 5.47,  24.0),
            new Placement("copper", 5, 228.0, 3.32, -16.0),
            new Placement("iron",   6, 208.0, 6.84,  32.0),
            new Placement("iron",   7, 184.0, 3.10, -32.0),
            new Placement("copper", 5, 168.0, 5.67, -16.0),
            new Placement("iron",   6, 236.0, 3.07,   4.0),
            new Placement("iron",   7, 152.0, 4.71,   0.0),
            new Placement("copper", 5, 160.0, 5.33, -32.0),
            new Placement("iron",   6, 192.0, 1.50,  56.0),
            new Placement("copper", 7, 160.0, 4.08,  40.0),
            new Placement("iron",   5, 136.0, 4.47,   0.0),
            new Placement("iron",   6, 144.0, 3.91, -32.0),
            new Placement("copper", 7, 136.0, 5.64,  32.0),
            new Placement("iron",   5, 152.0, 3.95,  56.0),
            new Placement("iron",   6, 116.0, 7.51,  12.0),
            new Placement("copper", 7, 152.0, 2.36,  72.0),
            new Placement("iron",   5, -32.0, 11.27,  72.0),
        };

        /// <summary>
        /// The deposit for a registration key ("deposit-N"), or null if the key is not
        /// a deposit's. Deterministic: the same key always maps to the same placement,
        /// so the server can recover a deposit's facts from its <see cref="WorldEntity"/>
        /// at registration time without threading the list through the spawn seam.
        /// </summary>
        public static MetalNode? ByKey(string key)
        {
            if (!IsDepositKey(key))
            {
                return null;
            }
            if (!int.TryParse(key.Substring(KeyPrefix.Length), out int index)
                || index < 0 || index >= HavenPlacements.Count)
            {
                return null;
            }
            return NodeAt(index);
        }

        /// <summary>The deposit at a placement index, as a pure <see cref="MetalNode"/> value.</summary>
        public static MetalNode NodeAt(int index)
        {
            Placement p = HavenPlacements[index];
            return new MetalNode(
                KeyFor(index),
                p.MetalType,
                p.Quality,
                MetalNodes.IslandLocalToWorldFixed(IslandOrigin, p.LocalX, p.LocalY, p.LocalZ),
                isDeposit: true,
                variantId: VariantId());
        }

        /// <summary>
        /// The deposits to place on Haven. <paramref name="count"/> is clamped to
        /// [0, the full table]; index 0 (the proven deposit) is kept for any count
        /// &gt;= 1. One is the cautious first-live default - the coordinate and
        /// variant chains have never been in front of a running client.
        /// </summary>
        public static IReadOnlyList<MetalNode> Haven(int count)
        {
            if (count > HavenPlacements.Count)
            {
                count = HavenPlacements.Count;
            }
            List<MetalNode> nodes = new List<MetalNode>();
            for (int i = 0; i < count; i++)
            {
                nodes.Add(NodeAt(i));
            }
            return nodes;
        }
    }
}

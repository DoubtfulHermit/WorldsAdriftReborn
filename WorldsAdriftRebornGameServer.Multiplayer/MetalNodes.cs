namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The facts about the one resource node this server places - the
    /// <c>MetalNugget</c> - and the measured Haven positions it is placed at.
    ///
    /// WHY THE NUGGET AND NOT THE DEPOSIT OR A TREE
    /// (docs/research/gathering/findings-metal-deposits.md). The nugget is the
    /// cheapest thing in the game a player can walk up to, aim at and (eventually)
    /// deplete: ONE entity, geometry BAKED into the prefab so it renders the
    /// instant it spawns with no visualiser initialisation, no parent, no variant
    /// table, no biome GlobalEntity. Metal is also the only resource line where
    /// spawn, aim, hit, deplete and collect all have live client implementations -
    /// trees are a harvesting dead end (TreeFsimVisualizer is UnityWorker-only) and
    /// the full metal deposit needs all five of its components or renders nothing.
    ///
    /// EVERYTHING HERE IS EITHER MEASURED OR VERIFIED, per the standing caveat that
    /// a third of this project's confident static conclusions have been wrong when
    /// run. What is VERIFIED against the decompiled game / extracted data is marked;
    /// what is an ASSUMPTION awaiting a running client is marked too.
    /// </summary>
    public static class MetalNodes
    {
        /// <summary>
        /// The prefab name that goes on the wire, BARE.
        ///
        /// VERIFIED: <c>MetalNugget</c> is line 163 of
        /// docs/research/loop/data/prefab-names.tsv with BOTH the client and worker
        /// columns "yes", so the client can resolve it. Sent bare for the same
        /// reason "Tree" is: the client appends the worker suffix itself in
        /// WorkerSpecificPrefabName.GetWorkerSpecificPrefabName, so "MetalNugget"
        /// is correct and "MetalNugget_unityclient" would be suffixed twice and
        /// resolve to nothing.
        /// </summary>
        public const string AssetName = "MetalNugget";

        /// <summary>Registration-key prefix for a placed node. See <see cref="KeyFor"/>.</summary>
        public const string KeyPrefix = "metal-";

        /// <summary>The registration key for the node at a given index.</summary>
        public static string KeyFor(int index) => KeyPrefix + index;

        // ------------------------------------------------------------------
        // The 1210 InteractiveState seed values (the "E to pick up" prompt).
        //
        // VERIFIED constructor shapes (ilspycmd on Generated.Code.dll):
        //   InteractiveStateData(bool available, EntityId inUseBy,
        //                        List<InteractionEntry> interactions, bool syncSchematics)
        //   InteractionEntry(InteractVerb verb, float radius, bool lockOnUse,
        //                    string activatedByItem, string description,
        //                    string lockedDescription, bool exclusiveUse, float timeToUse)
        //   enum InteractVerb { Default, Activate, PickUp, Man, ... } -> PickUp = 2
        //
        // InteractiveObjectVisualizer.OnEnable does
        // Interactions.FirstOrDefault(i => i.verb == Verb); with NO matching entry
        // the radius and timeToUse fall to 0 and the prompt never appears
        // (findings-metal-deposits.md). So the entry must exist, name PickUp, and
        // carry a non-zero radius.
        // ------------------------------------------------------------------

        /// <summary>1210 InteractionEntry.radius, metres. Non-zero or no prompt appears.</summary>
        public const float PickUpRadius = 3.0f;

        /// <summary>
        /// 1210 InteractionEntry.timeToUse, seconds. A short hold; the actual pickup
        /// (Route B, 1211 TriggerInteractWithObject) is a separate, Phase-3 concern.
        /// </summary>
        public const float PickUpTimeToUse = 0.5f;

        /// <summary>
        /// 1016 ItemHealthState seed for a nugget, both health and maxHealth.
        /// EQUAL and non-zero for the same reason a tree's is: health == 0 paints
        /// every renderer black in SalvageableItemVisualiser.OnEnable, and
        /// health &lt; maxHealth makes IsDamaged() true. The nugget has no depletion
        /// feedback of its own, so this only ever keeps it a legal, undamaged target.
        /// </summary>
        public const int ItemHealth = 100;

        /// <summary>
        /// Haven instance #5's world position - the island every player spawns on.
        /// Nodes are placed island-local against THIS origin so they move with the
        /// island if it is ever re-placed, exactly as the tree is.
        /// </summary>
        public static readonly FixedPointPosition IslandOrigin = SpawnPolicy.IslandPosition;

        /// <summary>
        /// Encodes an island-LOCAL metre offset into a world Q52.12 fixed-point
        /// position: island origin + local x 4096, truncated toward zero on each
        /// axis - the exact arithmetic the tree's and player's positions use, so a
        /// node coordinate and the tree coordinate can be checked against each other.
        /// Truncation (not rounding) matches the client's own encoder
        /// (FixedPointVector3Util: (long)(d * 4096)).
        /// </summary>
        public static FixedPointPosition IslandLocalToWorldFixed(FixedPointPosition islandOrigin, double localX, double localY, double localZ)
        {
            return new FixedPointPosition(
                islandOrigin.X + (long)(localX * FixedPointPosition.UnitsPerMetre),
                islandOrigin.Y + (long)(localY * FixedPointPosition.UnitsPerMetre),
                islandOrigin.Z + (long)(localZ * FixedPointPosition.UnitsPerMetre));
        }

        /// <summary>
        /// One island-local placement: a metal type, a quality, and a MEASURED LOD0
        /// surface vertex on Haven (island 1431299145).
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
        /// The measured, walkable node placements on Haven, island-local metres.
        ///
        /// Every one is a MEASURED LOD0 surface vertex from
        /// docs/research/world-data/island-surfaces/1431299145.json (TRS-corrected,
        /// so on the exact geometry the runtime collides against) with a near-flat
        /// normal (ny > 0.99) and constrained to the reachable ground band
        /// (island-local y in [1, 12] m) within ~90 m of the player's spawn, then
        /// thinned to a 14 m minimum separation. That band matters: the flattest
        /// vertices on this island sit on the metal camp's elevated platforms
        /// (y ~ 40-57 m), which a player cannot walk to - so "flattest" alone would
        /// place nodes where nobody can reach them.
        ///
        /// The FIRST entry is the PROVEN node: (216.0, 4.57, 8.0), 8.9 m from the
        /// spawn point and 8 m from the tree, ny = 0.995. It is the "test ONE
        /// hardcoded coordinate first" node the plan calls for; the rest are the
        /// small spread that makes the area read as populated.
        ///
        /// Metal TYPE is cosmetic for a nugget (it always renders as aluminium); the
        /// assignment here just gives each node a distinct material name for the
        /// future grant. QUALITY is likewise unused by the nugget's rendering.
        /// </summary>
        public static readonly IReadOnlyList<Placement> HavenPlacements = new[]
        {
            new Placement("aluminium", 8, 216.0,  4.57,   8.0), // PROVEN, 8.9 m from spawn
            new Placement("iron",      6, 180.0,  2.48, -40.0),
            new Placement("bronze",    5, 160.0,  3.89,  56.0),
            new Placement("copper",    6, 184.0,  1.03,  64.0),
            new Placement("tin",       4, 168.0,  5.32,  -8.0),
            new Placement("cobalt",    7, 144.0,  4.46,   8.0),
            new Placement("titanium",  7, 144.0,  4.35,  24.0),
            new Placement("aurium",    8, 168.0,  4.47,  24.0),
            new Placement("aluminium", 5, 168.0,  4.19,  40.0),
            new Placement("iron",      6, 144.0,  3.74, -24.0),
        };

        /// <summary>
        /// The node for a registration key ("metal-N"), or null if the key is not a
        /// metal node's. Used by the server to recover a spawned node's facts (metal
        /// type, quality) from its <see cref="WorldEntity"/> when it registers it
        /// into the harvest ledger, without threading the whole list through the
        /// spawn seam. Deterministic: the same key always maps to the same placement.
        /// </summary>
        public static MetalNode? ByKey(string key)
        {
            if (key == null || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
            {
                return null;
            }
            if (!int.TryParse(key.Substring(KeyPrefix.Length), out int index)
                || index < 0 || index >= HavenPlacements.Count)
            {
                return null;
            }
            Placement p = HavenPlacements[index];
            return new MetalNode(
                KeyFor(index),
                p.MetalType,
                p.Quality,
                IslandLocalToWorldFixed(IslandOrigin, p.LocalX, p.LocalY, p.LocalZ));
        }

        /// <summary>
        /// The nodes to place on Haven, as pure <see cref="MetalNode"/> values.
        /// </summary>
        /// <param name="onlyProven">
        /// When true, returns ONLY the first (proven) node. This is the cautious
        /// first-live-test mode the standing caveat calls for: the coordinate chain
        /// has never been validated against a running client, so a single node is
        /// spawned before the whole table is trusted.
        /// </param>
        public static IReadOnlyList<MetalNode> Haven(bool onlyProven = false)
        {
            List<MetalNode> nodes = new List<MetalNode>();
            int count = onlyProven ? 1 : HavenPlacements.Count;
            for (int i = 0; i < count; i++)
            {
                Placement p = HavenPlacements[i];
                nodes.Add(new MetalNode(
                    KeyFor(i),
                    p.MetalType,
                    p.Quality,
                    IslandLocalToWorldFixed(IslandOrigin, p.LocalX, p.LocalY, p.LocalZ)));
            }
            return nodes;
        }
    }
}

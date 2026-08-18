using WorldsAdriftRebornGameServer.Multiplayer.Islands;

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

        // ------------------------------------------------------------------
        // Mining (Route A, salvage beam). A nugget has no health or crust of its
        // own, so how many shots empty it and what that yields is server policy -
        // see Multiplayer.MetalHarvest.
        // ------------------------------------------------------------------

        /// <summary>
        /// Units of metal a nugget frees when the salvage beam empties it. Fed to
        /// <see cref="MetalHarvest.Place"/>; the "Salvaged &lt;metal&gt; xN" toast and
        /// the inventory grant both report this N (via a per-metal
        /// <c>YieldRule(amountPerUnit: 1)</c>). Invented - the nugget's authored
        /// yield spawning system is gone (findings-metal-deposits, "SURFACE
        /// NUGGETS") - and kept modest.
        /// </summary>
        public const int NuggetYieldUnits = 5;

        /// <summary>
        /// Salvage shots to empty one nugget. See <see cref="MetalHarvest.DefaultShotsToDeplete"/>.
        /// </summary>
        public const int NuggetShotsToDeplete = 3;

        /// <summary>
        /// How far, in metres, a depleted nugget is dropped straight down. The
        /// nugget has NO depletion feedback of its own (it never renders as damaged
        /// and stays salvageable client-side forever), and there is no RemoveEntityOp
        /// in this build, so the visible "it's gone" is a 190602 teleport that sinks
        /// it under the terrain (findings-metal-deposits, "SURFACE NUGGETS"). Far
        /// enough to be well below any walkable ground; the salvager's 10 m aim
        /// raycast then misses it, which is also what stops a held beam re-shooting
        /// the husk.
        /// </summary>
        public const double DepletedSinkMetres = 1000.0;

        /// <summary>
        /// A depleted node's position: its live position sunk <see cref="DepletedSinkMetres"/>
        /// straight down. A pure function of the intact position, so the live
        /// depletion broadcast and a late joiner's 190602 seed compute the SAME
        /// place without the server storing a second coordinate - a depleted node
        /// stays in the registry (rule 1) and is therefore still seeded to joiners,
        /// but sunk, so they see it gone exactly as everyone already present does.
        /// </summary>
        public static FixedPointPosition Sink(FixedPointPosition intact)
        {
            return new FixedPointPosition(
                intact.X,
                intact.Y - (long)(DepletedSinkMetres * FixedPointPosition.UnitsPerMetre),
                intact.Z);
        }

        /// <summary>
        /// Haven instance #5's world position - the island every player spawns on.
        /// Nodes are placed island-local against THIS origin so they move with the
        /// island if it is ever re-placed, exactly as the tree is.
        /// </summary>
        public static readonly FixedPointPosition IslandOrigin = IslandCatalog.Haven.GlobalOrigin;

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
            new Placement("aluminium", 8, 216.0,  4.57,   8.0), // PROVEN, 8.9 m from spawn (index 0 = proven-mode node)
            // Distributed across the reachable ground band (farthest-point sampled
            // from the 1431299145 surface table: ny>0.90 flat spots, local y in
            // [1,12] so a player can actually WALK to each - the camp platforms at
            // y~40-57 are excluded, they cannot be reached). ~310x208 m of spread.
            // Iron weighted heavily.
            new Placement("iron", 6, 248.0, 1.88, 0.0),
            new Placement("iron", 5, 16.0, 4.89, -120.0),
            new Placement("iron", 6, 192.0, 1.22, 64.0),
            new Placement("copper", 4, 192.0, 7.13, 8.0),
            // REMOVED 2026-08-19: 20.4 m from the Revival Chamber's axis once the
            // tower was stood up and moved to (156, 20) - i.e. inside the building's
            // own 21.9 m footprint, a nugget on the floor of a sealed drum. Deleted
            // from the table rather than skipped at registration, so the boot
            // resource count tells the truth. Was:
            //   new Placement("bronze", 7, 152.0, 4.71, 0.0)
            new Placement("tin", 5, -40.0, 11.61, 60.0),
            new Placement("aluminium", 8, 136.0, 4.06, -40.0),
            new Placement("cobalt", 7, 176.0, 6.39, -16.0),
            new Placement("titanium", 6, 128.0, 6.12, 0.0),
            new Placement("aurium", 5, 192.0, 5.68, 32.0),
            new Placement("iron", 6, -32.0, 11.33, 80.0),
            // REMOVED 2026-08-18: this node stood 18.0 m from the Revival
            // Chamber's axis, i.e. INSIDE the building. The user asked for the
            // shelf the chamber stands on to be cleared, so it is cleared here -
            // deleted from the table rather than skipped at registration, so the
            // boot resource count tells the truth. Was:
            //   new Placement("iron", 4, 151.7, 4.00, 48.0)
            new Placement("iron", 7, 184.0, 3.10, -32.0),
            new Placement("copper", 5, 160.0, 1.10, 72.0),
            new Placement("bronze", 8, 232.0, 2.74, -16.0),
            new Placement("tin", 7, 208.0, 4.80, 16.0),
            new Placement("aluminium", 6, -56.0, 11.70, 64.0),
            new Placement("cobalt", 5, 152.0, 4.87, -40.0),
            new Placement("titanium", 6, 192.0, 5.65, -8.0),
            new Placement("aurium", 4, 208.0, 6.84, 32.0),
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
                IslandCatalog.Haven.LocalToGlobal(p.LocalX, p.LocalY, p.LocalZ));
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
            return Haven(onlyProven ? 1 : HavenPlacements.Count);
        }

        /// <summary>
        /// The FIRST <paramref name="count"/> nodes to place on Haven, as pure
        /// <see cref="MetalNode"/> values. Placement index 0 is the proven node, so
        /// any count &gt;= 1 keeps it; the count is clamped to [0, the full table]
        /// so an over-large or negative <c>WAREBORN_ORE_COUNT</c> cannot throw.
        /// This is the env-capped variant (see <see cref="SpawnCountPolicy"/>); the
        /// boolean overload is the cautious "proven node only" first-live mode.
        /// </summary>
        public static IReadOnlyList<MetalNode> Haven(int count)
        {
            List<MetalNode> nodes = new List<MetalNode>();
            if (count > HavenPlacements.Count)
            {
                count = HavenPlacements.Count;
            }
            for (int i = 0; i < count; i++)
            {
                Placement p = HavenPlacements[i];
                nodes.Add(new MetalNode(
                    KeyFor(i),
                    p.MetalType,
                    p.Quality,
                    IslandCatalog.Haven.LocalToGlobal(p.LocalX, p.LocalY, p.LocalZ)));
            }
            return nodes;
        }
    }
}

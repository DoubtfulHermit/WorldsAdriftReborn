using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// LOOT CONTAINERS: identity, prefab, grounding and Haven placement.
    ///
    /// The world entity half of docs/plans/loot-containers.md. What goes INSIDE one
    /// is <see cref="Loot.LootTable"/>; how many an island gets is
    /// <see cref="Loot.LootBudget"/>; this file is the where and the what-asset.
    ///
    /// THE PREFAB. <c>LootChest_001</c>, sent BARE. The client appends the worker
    /// suffix itself in <c>WorkerSpecificPrefabName.GetWorkerSpecificPrefabName</c>,
    /// so <c>lootchest_001_unityclient</c> would be doubled and resolve to nothing -
    /// the same trap <see cref="MetalDeposits"/> and <see cref="Databanks"/>
    /// document. The name is in the runtime-validated census
    /// (<c>Ship/client-entity-prefabs.txt</c>), so
    /// <c>ClientEntityPrefabs.CanResolve</c> already answers true for it.
    ///
    /// Retail's <c>ChestContainerLootPreprocessor</c> bakes everything the client
    /// needs onto this prefab at export: an <c>InteractiveObjectVisualizer</c> with
    /// verb <c>Inventory</c>, an <c>InventoryContents</c>, an
    /// <c>InWorldInventoryVisualiser</c>, and - because this prefab has an
    /// <c>Animator</c> - a <c>LootableChestContainerVfxVisualizer</c> that plays the
    /// opening animation for free. Nothing on the server drives that animation.
    ///
    /// The other 47 loot prefabs (<c>LootContainer_001..003</c>,
    /// <c>LootRuinPile1..24</c>, and the <c>_kioki</c> art-set variants of each) are
    /// deliberately left for Phase 3. One prefab proven end to end beats four
    /// half-proven ones.
    ///
    /// HOW IT SITS ON THE GROUND - RECOVERED, and it is the antidote to the
    /// floating-log bug. Retail's own placement pass is
    /// <c>acs/IslandDataBankAndLootableSpawnerVisualizer.cs</c> and it does three
    /// things this server must copy:
    ///
    /// <code>
    ///   :64  spacing:   (a - b).sqrMagnitude &lt; 400f          -> 20 m minimum
    ///   :64  clearance: Physics.CheckBox(p + up*1.75, half 1.6)
    ///   :100 position = surfacePoint - normal * Random(0.15 .. 0.30)
    ///   :101 rotation = Euler(rand(-5,5), rand(0,360), rand(-5,5))
    ///                     * PointTo(Y -> normal, Z -> up)
    /// </code>
    ///
    /// The prop is SUNK 15-30 cm INTO the surface along the normal, and its up-axis
    /// is aligned to that normal before a small tilt jitter. That is precisely what
    /// <c>TreeFall</c> does not do: a felled log keeps its parent's ground-plane
    /// position for its whole life (<c>TreeFall.cs:441-442</c>) and merely rotates
    /// about the entity origin, which puts the trunk's centreline at ground level -
    /// half buried on the flat, hanging in the air on a slope
    /// (<c>TreeFall.cs:62-63</c> admits it). A container that repeated that would
    /// float or clip on every seat that is not perfectly level.
    ///
    /// So: a strict flatness gate, then <see cref="SinkMetres"/> straight down.
    /// Straight down rather than along the normal because
    /// <see cref="Resources.GeneratedPlacement"/> carries only the normal's Y
    /// component, not the full vector - and at the gate this uses
    /// (<see cref="Resources.HavenSurface.LootMinUpwardNormal"/> = 0.97) the normal
    /// is within 14 degrees of up, so the two differ by under a centimetre. Widening
    /// that gate without carrying the full normal would reintroduce the very error
    /// this comment exists to prevent.
    /// </summary>
    public static class LootContainers
    {
        /// <summary>
        /// The client prefab, BARE. See the class remarks for why the
        /// <c>_unityclient</c> suffix must not be here.
        /// </summary>
        public const string AssetName = "LootChest_001";

        /// <summary>
        /// Registration-key prefix. <b>Must</b> stay in sync with
        /// <c>ResourceInterestPolicy.IsStreamedResourceKey</c>: a resource key
        /// outside that allowlist is broadcast eagerly instead of spatially
        /// streamed AND is skipped by <c>ActivateBoundResources</c>, which is the
        /// "renders but does nothing" bug class the handover records.
        /// </summary>
        public const string KeyPrefix = "loot-";

        /// <summary>The registration key for Haven container <paramref name="index"/>.</summary>
        public static string KeyFor(int index) => KeyPrefix + index;

        /// <summary>Whether a registration key names a loot container.</summary>
        public static bool IsLootKey(string? key) =>
            key != null && key.StartsWith(KeyPrefix, StringComparison.Ordinal);

        // ------------------------------------------------------------------
        // THE 1081 GRID
        // ------------------------------------------------------------------

        /// <summary>
        /// Container grid width, cells. The client reads width/height EXACTLY ONCE,
        /// at <c>InWorldInventoryVisualiser.OnEnable</c>, and never calls
        /// <c>Setup</c> again - so a later resize is a lie the server tells itself
        /// and these two numbers are effectively permanent per entity.
        ///
        /// 10x6 = 60 cells. The 60 is INFERRED, from a community note recorded in
        /// docs/research/gathering/findings-interaction.md:135 ("Inventory 60
        /// (containers)"); the 10x6 shape is WAREBORN TUNING chosen so the widest
        /// scrap row in the recovered table (5x3) fits twice across, and so the
        /// panel is the same width as the player's own 10-wide grid.
        /// </summary>
        public const int GridWidth = 10;

        /// <summary>Container grid height, cells. See <see cref="GridWidth"/>.</summary>
        public const int GridHeight = 6;

        /// <summary>
        /// A container has no hotbar belt. <c>hasBelt</c>/<c>beltRow</c> are read by
        /// the same <c>Setup</c> call as the dimensions and are meaningless off a
        /// player.
        /// </summary>
        public const bool HasBelt = false;

        /// <summary>Belt row, unused because <see cref="HasBelt"/> is false.</summary>
        public const int BeltRow = 0;

        // ------------------------------------------------------------------
        // THE 1210 INTERACTION
        // ------------------------------------------------------------------

        /// <summary>
        /// <c>InteractionEntry.radius</c>, metres. Non-zero or the prompt never
        /// appears at all - the <c>MetalNodes.PickUpRadius</c> trap, restated in
        /// <c>PartInteractionPolicy.ActivateRadius</c>. 3 m is arm's reach around a
        /// chest-sized prop; the client re-checks
        /// <c>IsWithinInteractRadius()</c> at open, so a generous radius here only
        /// controls where the prompt appears, never where the panel opens.
        /// </summary>
        public const float InteractRadius = 3f;

        /// <summary>
        /// <c>InteractionEntry.timeToUse</c>, seconds. Zero: opening a chest in
        /// retail was instant, unlike the shipyard console's hold. No shipped
        /// artefact states the value, so zero is the choice that cannot feel wrong -
        /// a hold nobody expects reads as an unresponsive prompt.
        /// </summary>
        public const float InteractTimeToUse = 0f;

        // ------------------------------------------------------------------
        // GROUNDING
        // ------------------------------------------------------------------

        /// <summary>
        /// How far a container sinks into the surface, metres. RECOVERED: retail
        /// used <c>Random(0.15f, 0.30f)</c> along the surface normal
        /// (<c>IslandDataBankAndLootableSpawnerVisualizer.cs:100</c>). This server
        /// takes the midpoint rather than a random draw because placement here is
        /// deterministic by house rule - a container that sank a different distance
        /// on each restart would visibly bob between sessions.
        /// </summary>
        public const double SinkMetres = 0.225;

        /// <summary>The island every Haven container is placed island-local against.</summary>
        public static readonly FixedPointPosition IslandOrigin = IslandCatalog.Haven.GlobalOrigin;

        /// <summary>One island-local container placement, in metres, already sunk.</summary>
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
        /// Haven's container seats, island-local metres, sunk by
        /// <see cref="SinkMetres"/>. Each is a MEASURED LOD0 surface vertex from the
        /// same extracted table the trees, deposits and canisters draw from, so a
        /// chest rests on real terrain rather than an invented coordinate. Haven's
        /// pre-TRS tables were once wrong by a mean of 47.7 m; nothing here is
        /// hand-typed.
        /// </summary>
        public static readonly IReadOnlyList<Placement> HavenPlacements = BuildHavenPlacements();

        private static IReadOnlyList<Placement> BuildHavenPlacements()
        {
            List<Placement> placements = new List<Placement>();
            foreach (Resources.GeneratedPlacement p in Resources.HavenSurface.LootLocals())
            {
                placements.Add(Sink(p.LocalX, p.LocalY, p.LocalZ));
            }
            return placements;
        }

        /// <summary>
        /// Applies retail's sink to a raw surface vertex. Separated out and public so
        /// the release-world seats can be sunk by the SAME arithmetic rather than a
        /// second copy of it - the offline generator emits raw surface points.
        /// </summary>
        public static Placement Sink(double localX, double localY, double localZ) =>
            new Placement(localX, localY - SinkMetres, localZ);

        /// <summary>The world position of the Haven container at a placement index.</summary>
        public static FixedPointPosition PositionAt(int index)
        {
            Placement p = HavenPlacements[index];
            return IslandCatalog.Haven.LocalToGlobal(p.LocalX, p.LocalY, p.LocalZ);
        }

        /// <summary>The number of Haven containers to place, clamped to [1, full table].</summary>
        public static int CountFrom(string? countEnv) =>
            SpawnCountPolicy.CountFrom(countEnv, HavenPlacements.Count);
    }
}

using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// SHIP STORAGE: which crafted parts are containers, how big each one's grid is,
    /// and the numbers its interact prompt carries. The pure half of "bolt a trunk to
    /// your ship and put things in it"; the glue is <c>Game/Ship/ShipContainerStock</c>
    /// and <c>Game/Ship/ShipContainerService</c>.
    ///
    /// THE FOUR ROWS. <c>trunk</c>, <c>mountedBox</c>, <c>storageContainer</c> and
    /// <c>shippingContainer</c> in <see cref="LoosePartCatalogue"/> (prefabs
    /// ContainerSmall / ContainerMount / ContainerMedium / ContainerLarge). They are
    /// keyed on the 1120 itemType, which for a crafted part IS its schematic id -
    /// the catalogue's "storage" column is the schematicData CATEGORY and never
    /// reaches the wire, so keying on it would match nothing.
    ///
    /// WHY EACH ONE NEEDS TWO COMPONENTS AND A VERB, none of which is optional:
    ///
    ///   * <c>InWorldInventoryVisualiser</c> [Require]s <c>1210 + 1081</c>. A Unity
    ///     visualiser does not enable until EVERY [Require] resolves and it fails
    ///     silently, so serving 1210 alone (which is what we did for months) leaves a
    ///     correct-looking, completely dead prop with no log line anywhere. Same
    ///     defect as the loom's unseeded 1264.
    ///   * <c>IsTooDamagedToWorkVisualizer</c> [Require]s <c>1236</c>, and the client's
    ///     own interact gate is <c>verb == Inventory &amp;&amp; !IsTooDamagedToWork</c>.
    ///   * the prefab's BAKED verb is <c>Inventory</c>
    ///     (<c>ShipContainerPreprocessor.SetVerb</c>) and
    ///     <c>InteractiveObjectVisualizer.OnEnable</c> caches
    ///     <c>Interactions.FirstOrDefault(i =&gt; i.verb == Verb)</c> ONCE. Serving the
    ///     generic <c>PickUp</c> entry means that lookup finds NOTHING, the radius
    ///     falls to zero and no prompt can ever appear - the same failure the mounted
    ///     helm hit.
    ///
    /// THE GRID IS PERMANENT PER ENTITY. The client reads width/height/hasBelt/beltRow
    /// exactly once, in <c>InWorldInventoryVisualiser.OnEnable</c>, and never calls
    /// <c>Setup</c> again - so these numbers are a property of CHECKOUT and a later
    /// resize is a lie the server tells itself. Changing a size below only affects
    /// containers checked out after the change.
    /// </summary>
    public static class ShipContainers
    {
        /// <summary>
        /// The widest single item footprint in the recovered scrap table is 5x3
        /// (<c>LootScrapTable</c>), so a container narrower or shorter than that has
        /// cells no item on this server can ever occupy. Every grid below satisfies
        /// it, and a test pins that rather than trusting the table to stay read.
        /// </summary>
        public const int MinimumUsableWidth = 5;

        /// <summary>The height counterpart of <see cref="MinimumUsableWidth"/>.</summary>
        public const int MinimumUsableHeight = 3;

        /// <summary>
        /// A container has no hotbar belt. <c>hasBelt</c>/<c>beltRow</c> are read by
        /// the same one-shot <c>Setup</c> call as the dimensions and are meaningless
        /// off a player.
        /// </summary>
        public const bool HasBelt = false;

        /// <summary>Belt row, unused because <see cref="HasBelt"/> is false.</summary>
        public const int BeltRow = 0;

        /// <summary>
        /// <c>InteractionEntry.radius</c>, metres. Non-zero or the prompt never
        /// appears at all (the <c>MetalNodes.PickUpRadius</c> trap). Matched to
        /// <c>LootContainers.InteractRadius</c> on purpose: a chest is a chest, and a
        /// ship trunk that wants to be approached from further away than a ruin chest
        /// would be an unexplainable inconsistency.
        /// </summary>
        public const float InteractRadius = 3f;

        /// <summary>
        /// <c>InteractionEntry.timeToUse</c>, seconds. Zero - opening storage was
        /// instant in retail, unlike the shipyard console's hold, and a hold nobody
        /// expects reads as an unresponsive prompt.
        /// </summary>
        public const float InteractTimeToUse = 0f;

        /// <summary>
        /// A container's grid, in cells. WAREBORN TUNING: retail's per-item capacity
        /// table lived on the GSim and no artefact in the shipped client states it,
        /// so these are chosen - not recovered - to rank with the prefab sizes
        /// (Mount &lt; Small &lt; Medium &lt; Large) and to keep the widest recovered
        /// 5x3 scrap item placeable in every one of them.
        /// </summary>
        public readonly struct Grid
        {
            internal Grid(int width, int height)
            {
                Width = width;
                Height = height;
            }

            /// <summary>Cells across.</summary>
            public int Width { get; }

            /// <summary>Cells down.</summary>
            public int Height { get; }

            /// <summary>Total cells, the number a player experiences as "capacity".</summary>
            public int Cells => Width * Height;
        }

        private static readonly Dictionary<string, Grid> Grids = new()
        {
            // ContainerMount - the smallest prop of the four, and the only one whose
            // grid is exactly the minimum usable rectangle.
            ["mountedBox"] = new Grid(5, 3),
            // ContainerSmall.
            ["trunk"] = new Grid(6, 4),
            // ContainerMedium.
            ["storageContainer"] = new Grid(8, 5),
            // ContainerLarge - the same 10x6 as a ruin chest, which is the largest
            // grid the client has been observed to lay out cleanly.
            ["shippingContainer"] = new Grid(10, 6),
        };

        /// <summary>
        /// True when this 1120 itemType is one of the four ship storage containers.
        /// The single question every serve, gate and echo asks.
        /// </summary>
        public static bool IsContainer(string? itemType) =>
            itemType != null && Grids.ContainsKey(itemType);

        /// <summary>
        /// This container's grid, or null when the itemType is not a container.
        /// </summary>
        public static Grid? GridFor(string? itemType) =>
            itemType != null && Grids.TryGetValue(itemType, out Grid grid) ? grid : (Grid?)null;

        /// <summary>Every container itemType, for tests and diagnostics.</summary>
        public static IReadOnlyCollection<string> ItemTypes => Grids.Keys;

        /// <summary>
        /// The components a container row MUST seed on top of
        /// <see cref="LoosePartDefinition.BaseShipPartComponents"/>: 1081 InventoryState
        /// and 1236 IsTooDamagedToWorkState. Exposed as data so the catalogue rows and
        /// their test read the same list and cannot drift.
        /// </summary>
        public static readonly IReadOnlyList<uint> RequiredComponents = new uint[] { 1081, 1236 };
    }
}

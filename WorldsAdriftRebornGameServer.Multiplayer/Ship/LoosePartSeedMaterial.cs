using WorldsAdriftRebornGameServer.Multiplayer.Materials;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// WHAT SUBSTANCE A CRAFTED SHIP PART SAYS IT IS MADE OF - the material the
    /// server writes into slot 0 (and every other seeded slot) of a loose part's
    /// <c>1099 SalvageAndRepairState.originalMaterials</c>.
    ///
    /// This looks cosmetic and is not. On a PANEL prefab the material chooses the
    /// MESH, and one of the meshes the client would have to choose DOES NOT EXIST
    /// IN THE SHIPPED BUILD, so the wrong material here makes a crafted part
    /// invisible with nothing but one Unity error to say so.
    ///
    /// THE WINDOW, PROVED FROM SHIPPED BYTES. The chain, all decompiled:
    ///   1. <c>ShipPanelVisualizer.OnEnable</c> -&gt; <c>ShipPanel.Init</c>
    ///      (acs/ShipPanel.cs:84-121) resolves the panel's material as
    ///      <c>MaterialDefinitionFromName(materialsUsed[0].rawMaterial.materialTypeId)
    ///      ?? MaterialDefinitionFromName(_panelMaterial)</c> - i.e. OUR seeded
    ///      slot 0 WINS over the prefab's own default.
    ///   2. <c>PanelArt.MixPanel</c> (acs/PanelArt.cs:92-176) then picks the mesh
    ///      array by (size x window x wood/metal) and, finding an EMPTY array,
    ///      logs <c>"No appropriate mesh found for requested ship panel size!"</c>
    ///      and returns a <c>PanelArtDefinition</c> whose <c>panelFilter</c> holds
    ///      no mesh.
    ///   3. <c>ShipPanel.InitializeMesh</c> (acs/ShipPanel.cs:341-352) then calls
    ///      <c>Instantiate(_originalMesh)</c> on that null mesh, which THROWS out
    ///      of <c>OnEnable</c>. The entity exists, the visualizer is half-enabled,
    ///      and the part has no geometry at all.
    ///   4. The twelve mesh arrays on the shipped <c>ShipPanelDefinitions</c>
    ///      (level0, path_id 1515) were read directly out of the client:
    ///        metalPanelMeshes 1X1/1X2/2X2       = 5 / 4 / 4
    ///        metalWindowPanelMeshes 1X1/1X2/2X2 = 4 / 0 / 0
    ///        woodPanelMeshes 1X1/1X2/2X2        = 2 / 3 / 2
    ///        woodWindowPanelMeshes 1X1/1X2/2X2  = 0 / 0 / 0
    ///      There is exactly ONE window mesh set in the whole game:
    ///      <c>metalWindowPanelMeshes1X1</c>. A WOOD window has no mesh at any
    ///      size.
    ///   5. Window01's own <c>ShipPanel</c> is <c>HasWindow=true</c>,
    ///      <c>_panelSize=onebyone</c>, <c>_panelMaterial="iron"</c> - it is
    ///      authored to be the metal 1X1 window, the one that exists.
    ///
    /// So the uniform Wood seed that <c>1099</c> has always written (chosen for the
    /// helm-freeze fix, and correct for every other part) is exactly what made the
    /// Window invisible. It is CONFIRMED in the maintainer's own client log: two
    /// <c>"No appropriate mesh found"</c> / <c>Instantiate(null)</c> pairs through
    /// <c>ShipPanel.InitializeMesh</c>, against a world state holding exactly two
    /// loose <c>window</c> parts.
    ///
    /// WHY THIS IS KEYED ON itemType AND NOT PERSISTED. A restored part must render
    /// identically to a freshly crafted one, and adding a field to
    /// <c>LoosePartRecord</c> would leave every pre-existing save on the old value.
    /// Deriving it from the itemType the record already carries makes the fix apply
    /// retroactively to the windows already lying in the world, with no migration.
    ///
    /// THE CATEGORY IS NOT FREE. <c>PartGraphicsVariationByMaterial
    /// .GetPrefabFromMaterial</c> (acs:53-58) THROWS on any category that is not
    /// "Wood" or "Metal", so this policy may only ever return those two.
    /// </summary>
    public readonly struct PartSeedMaterial
    {
        public PartSeedMaterial(string materialTypeId, string category)
        {
            MaterialTypeId = materialTypeId;
            Category = category;
        }

        /// <summary>A REAL itemData id, resolved by name through the client's MaterialManager.</summary>
        public string MaterialTypeId { get; }

        /// <summary>"Wood" or "Metal". Never anything else - the client throws.</summary>
        public string Category { get; }
    }

    /// <summary>
    /// The pure itemType -&gt; seeded 1099 material map. See
    /// <see cref="PartSeedMaterial"/> for why a wrong answer here makes a crafted
    /// part invisible rather than merely the wrong colour.
    /// </summary>
    public static class LoosePartSeedMaterial
    {
        /// <summary>
        /// The default every part but the window keeps: the deck's proven-safe Wood
        /// material. It resolves in MaterialManager, and category "Wood" makes
        /// <c>GetPrefabFromMaterial</c> return the part's baked <c>_woodPrefab</c>,
        /// which every ship-part prefab has.
        /// </summary>
        public static readonly PartSeedMaterial Wood =
            new PartSeedMaterial(Deck.MaterialTypeId, Deck.MaterialCategory);

        /// <summary>
        /// The window's material. "iron" is Window01's OWN authored
        /// <c>_panelMaterial</c> default, is one of the six materials the shipped
        /// <c>ShipPanelDefinitions.panelColorDefinitions</c> carries colour sets for
        /// (iron, copper, lead, steel, gold, tin - all metals), and is the only
        /// family with a window mesh. Not a guess: the prefab names it.
        /// </summary>
        public static readonly PartSeedMaterial Iron =
            new PartSeedMaterial("iron", MaterialCategory.Metal);

        /// <summary>
        /// The itemType whose client art has no wooden variant at all. Kept as a
        /// named constant so the test that pins it reads as an assertion about the
        /// window rather than about a string literal.
        /// </summary>
        public const string WindowItemType = "window";

        /// <summary>
        /// The material to seed into every <c>1099</c> slot for a loose part of this
        /// itemType. Unknown/absent itemTypes get <see cref="Wood"/>, which is the
        /// behaviour every part had before the window was fixed.
        /// </summary>
        public static PartSeedMaterial For(string? itemType)
        {
            return itemType == WindowItemType ? Iron : Wood;
        }
    }
}

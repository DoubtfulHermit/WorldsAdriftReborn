namespace WorldsAdriftRebornGameServer.Multiplayer.Crafting
{
    /// <summary>
    /// One recipe, as the server needs it to run a crafting transaction.
    ///
    /// A plain mirror record - NOT the game's Generated.Code SchematicData -
    /// exactly as InventoryItem mirrors ScalaSlottedInventoryItem, and for the
    /// same reason: this project references nothing, which is what lets
    /// <see cref="CraftingPolicy"/> be unit-tested on Linux with no game install.
    ///
    /// Only the fields a transaction reads are declared. The catalogue JSON
    /// carries the full SchematicData field set (SchematicType, baseStats,
    /// hullData, and so on); those are served verbatim to the client over 1097
    /// and never round-tripped through this type, so a System.Text.Json
    /// case-insensitive deserialise simply ignores them here. That is deliberate:
    /// a field the server does not act on is a field the server must not be able
    /// to corrupt.
    ///
    /// The properties are settable because the game-server-side SchematicHelper
    /// deserialises the file into this shape; nothing mutates a record after load.
    /// </summary>
    public sealed class SchematicRecord
    {
        /// <summary>The catalogue key. Falls back to the dictionary key when the JSON omits it.</summary>
        public string SchematicId { get; set; } = "";

        /// <summary>Personal | Clothing | Cooking | ShipParts | Utility | CraftingComponents.</summary>
        public string Category { get; set; } = "";

        /// <summary>The itemTypeId of the item this recipe produces. Must exist in itemData.json.</summary>
        public string ItemType { get; set; } = "";

        /// <summary>How many of <see cref="ItemType"/> one craft yields. Clamped to at least 1 at craft time.</summary>
        public int AmountToCraft { get; set; } = 1;

        /// <summary>Seconds the client shows as a countdown. Phase 1 crafts resolve instantly; this is display only.</summary>
        public int TimeToCraft { get; set; }

        /// <summary>The materials one craft consumes, in slot order.</summary>
        public List<CraftingRequirement> CraftingRequirements { get; set; } = new();
    }

    /// <summary>
    /// One material a recipe needs. Mirrors CraftingItemData's acted-on fields.
    ///
    /// <see cref="Name"/> is the match key and is either a material CATEGORY
    /// ("Metal", "Wood", "Fuel", or the special "Wood/Metal") or a concrete
    /// itemTypeId ("iron", "birch"). The client's own drag-drop matcher
    /// (InventoryItemManager.IsSameMaterialType) compares against exactly this
    /// field, so the server mirrors that rule in <see cref="CraftingPolicy.Matches"/>.
    /// </summary>
    public sealed class CraftingRequirement
    {
        /// <summary>Requirement index / slot number.</summary>
        public int Id { get; set; }

        /// <summary>Material category or itemTypeId this slot accepts.</summary>
        public string Name { get; set; } = "";

        /// <summary>How many units of a matching material this slot needs.</summary>
        public int AmountRequired { get; set; }

        /// <summary>Cosmetic component label (Casing, Aileron, ...). Not acted on server-side.</summary>
        public string Component { get; set; } = "";
    }
}

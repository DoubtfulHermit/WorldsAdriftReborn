namespace WorldsAdriftRebornGameServer.Multiplayer.Inventory
{
    /// <summary>
    /// One row of the 1081 inventory list, mirrored.
    ///
    /// A mirror record rather than the game's ScalaSlottedInventoryItem because
    /// this project deliberately references nothing (see its csproj) - that is
    /// what lets the rules below be unit-tested on Linux with no game install.
    /// The same trick MirrorSendPolicy already uses; conversion happens once, at
    /// the glue boundary in the game server.
    ///
    /// All fourteen wire fields are here, none dropped, because this record is
    /// also what gets serialised into the database: a field left out here is a
    /// field a player loses on relog. In particular <see cref="Meta"/> is the
    /// ONLY place colours and item health live, so a "simplification" that drops
    /// it silently strips every dyed garment in the game.
    /// </summary>
    public sealed record InventoryItem(
        int ItemId,
        string ItemTypeId,
        int Amount,
        string SlotType,
        int UtilitySlotNum,
        int X,
        int Y,
        bool Rotated,
        int HotBarSlotNum,
        int TimeToBuild,
        int Quality,
        bool LockBoxItem,
        IReadOnlyDictionary<string, string> Meta,
        int? Rarity)
    {
        /// <summary>
        /// The one legal "not worn" slot value. Exactly this spelling: the
        /// client does a case-sensitive Enum.Parse with no TryParse and no
        /// try/catch, and the throw lands after AllSlotDataLookup.Clear() has
        /// already run - so one bad value blanks the ENTIRE inventory panel.
        /// </summary>
        public const string NotWorn = "None";

        /// <summary>Sentinel for "not in a hotbar slot" and "not in a utility slot".</summary>
        public const int NoSlot = -1;

        /// <summary>
        /// A worn item is excluded from the grid entirely - its x/y are ignored
        /// by the client - so every geometry rule below has to skip it.
        /// </summary>
        public bool IsWorn => !string.Equals(SlotType, NotWorn, StringComparison.Ordinal);

        /// <summary>
        /// Stash items live in a category list of fixed 2x2 tiles, not in the
        /// grid, so their coordinates are parsed and then never used.
        /// </summary>
        public bool IsStashed => LockBoxItem;

        /// <summary>Whether this item sits in one of the eight hotbar slots.</summary>
        public bool IsOnHotBar => HotBarSlotNum > NoSlot;

        /// <summary>An item that takes no grid space at all: the four gauntlet shells.</summary>
        public bool OccupiesNoSpace(ItemFootprint footprint) => footprint.Width <= 0 || footprint.Height <= 0;

        /// <summary>This item's footprint after <see cref="Rotated"/> is applied.</summary>
        public ItemFootprint Oriented(ItemFootprint footprint)
        {
            return Rotated ? new ItemFootprint(footprint.Height, footprint.Width) : footprint;
        }
    }

    /// <summary>
    /// How much grid an item type takes. NOT a wire field - the client reads it
    /// from the item database it received over 1097, so the server has to look
    /// it up from the same table rather than carry it per item.
    /// </summary>
    public readonly record struct ItemFootprint(int Width, int Height);

    /// <summary>
    /// Resolves an item type's footprint, or returns false for a type the item
    /// database has never heard of.
    ///
    /// A delegate rather than a dictionary parameter so the pure rules never
    /// need the game's ItemHelper, and false rather than a default size because
    /// an unknown itemTypeId is a hard client-side NRE
    /// (InventoryItemManager.LookupItem returns null and the callers dereference
    /// it unguarded) - it must be rejected, not guessed at.
    /// </summary>
    public delegate bool ItemFootprintLookup(string itemTypeId, out ItemFootprint footprint);
}

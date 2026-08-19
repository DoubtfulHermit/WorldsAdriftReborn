using WorldsAdriftRebornGameServer.Multiplayer.Inventory;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Inventory
{
    /// <summary>
    /// A tiny stand-in for the game's item database, plus builders for items.
    ///
    /// Real footprints for the real item types the seed uses, so a test that
    /// says "the glider straddles the belt row" is talking about the actual
    /// glider and not a shape invented to make the assertion pass.
    /// </summary>
    internal static class InventoryTestData
    {
        internal static readonly Dictionary<string, ItemFootprint> Sizes = new()
        {
            // The four gauntlet shells: 0x0, which is why (-1,-1) is legal for them.
            ["gauntlet_salvage"] = new ItemFootprint(0, 0),
            ["gauntlet_repair"] = new ItemFootprint(0, 0),
            ["gauntlet_build"] = new ItemFootprint(0, 0),
            ["gauntlet_scanner"] = new ItemFootprint(0, 0),
            ["glider"] = new ItemFootprint(3, 4),
            ["torso_poncho"] = new ItemFootprint(2, 2),
            ["head_devhat"] = new ItemFootprint(2, 2),
            ["iron"] = new ItemFootprint(3, 2),
            ["birch"] = new ItemFootprint(2, 2),
            ["huge"] = new ItemFootprint(9, 17),
            // Exactly the stock grid above its belt separator: 10 wide by 14
            // tall fills rows 0-13 and leaves the divider and the belt free, so
            // a test can force a placement to choose between them.
            ["backpackFiller"] = new ItemFootprint(10, 14),
        };

        internal static bool Footprints(string itemTypeId, out ItemFootprint footprint)
        {
            return Sizes.TryGetValue(itemTypeId, out footprint);
        }

        internal static InventoryItem Item(
            int itemId,
            string itemTypeId,
            int x = 0,
            int y = 0,
            string slotType = InventoryItem.NotWorn,
            int hotBarSlot = InventoryItem.NoSlot,
            bool rotated = false,
            bool lockBox = false,
            Dictionary<string, string>? meta = null)
        {
            return new InventoryItem(
                itemId,
                itemTypeId,
                1,
                slotType,
                InventoryItem.NoSlot,
                x,
                y,
                rotated,
                hotBarSlot,
                0,
                0,
                lockBox,
                meta ?? new Dictionary<string, string>(),
                null);
        }

        /// <summary>An empty stock grid: 10x18, belt on the bottom three rows.</summary>
        internal static InventoryModel Grid() => InventoryModel.DefaultGrid();

        /// <summary>
        /// The seven items every player is currently seeded with, at the
        /// coordinates ItemHelper.GetDefaultItems actually uses.
        /// </summary>
        internal static InventoryModel Seeded()
        {
            InventoryModel model = Grid();

            model.Add(Item(1, "gauntlet_salvage", -1, -1, hotBarSlot: 0));
            model.Add(Item(2, "gauntlet_repair", -1, -1, hotBarSlot: 1));
            model.Add(Item(3, "gauntlet_build", -1, -1, hotBarSlot: 2));
            model.Add(Item(4, "gauntlet_scanner", -1, -1, hotBarSlot: 3));
            model.Add(Item(1101, "glider"));
            model.Add(Item(1102, "torso_poncho", 0, 4));
            model.Add(Item(1103, "head_devhat", 3, 0));

            return model;
        }
    }
}

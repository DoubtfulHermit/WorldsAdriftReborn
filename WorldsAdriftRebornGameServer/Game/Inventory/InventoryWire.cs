using Bossa.Travellers.Inventory;
using Improbable.Collections;
using WorldsAdriftRebornGameServer.Game.Items;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;

namespace WorldsAdriftRebornGameServer.Game.Inventory
{
    /// <summary>
    /// The one place the pure inventory model meets the game's types.
    ///
    /// It exists because WorldsAdriftRebornGameServer.Multiplayer deliberately
    /// references nothing - that is what lets the rules be unit-tested on Linux
    /// with no game install - so ScalaSlottedInventoryItem cannot be named there.
    /// Exactly the same arrangement MirrorSendPolicy already uses. Every field
    /// crosses here and nowhere else, so a field that stops round-tripping is a
    /// one-file bug rather than a hunt.
    /// </summary>
    internal static class InventoryWire
    {
        /// <summary>
        /// The item database as the pure rules want it: a lookup that says NO for
        /// an unknown type rather than throwing or guessing a size.
        ///
        /// ItemHelper.GetItem is a raw dictionary index and throws
        /// KeyNotFoundException, which on this path would take down a network
        /// handler over one bad item name.
        /// </summary>
        internal static bool Footprints(string itemTypeId, out ItemFootprint footprint)
        {
            footprint = default;

            if (string.IsNullOrEmpty(itemTypeId)
                || !ItemHelper.AllItems.TryGetValue(itemTypeId, out ItemHelper.ValidItem? item))
            {
                return false;
            }

            footprint = new ItemFootprint(item.width, item.height);
            return true;
        }

        /// <summary>
        /// The stack ceiling for an item type, straight from the item database
        /// (itemData.json -> ValidItem.stacksize, served to the client over 1097
        /// as stackingMax). Unknown types and types with no stacksize set read as
        /// the database's own -1 default, which callers treat as "not stackable".
        ///
        /// This is the server side of the same fact the client renders: the count
        /// label is hidden when stackingMax &lt;= 1, so a material must carry a
        /// real ceiling here for "x12" to ever appear.
        /// </summary>
        internal static int StackMaxOf(string itemTypeId)
        {
            return ItemHelper.AllItems.TryGetValue(itemTypeId, out ItemHelper.ValidItem? item)
                ? item.stacksize
                : -1;
        }

        /// <summary>
        /// An item type's material category ("Metal", "Wood", "Fuel", ...), or
        /// false for a type the database has never heard of. Shaped as a
        /// <see cref="Multiplayer.Crafting.MaterialCategoryLookup"/> so the pure
        /// CraftingPolicy can match a slotted material against a recipe
        /// requirement without ever naming ItemHelper.
        /// </summary>
        internal static bool CategoryLookup(string itemTypeId, out string category)
        {
            if (!string.IsNullOrEmpty(itemTypeId)
                && ItemHelper.AllItems.TryGetValue(itemTypeId, out ItemHelper.ValidItem? item))
            {
                category = item.category ?? "";
                return true;
            }

            category = "";
            return false;
        }

        /// <summary>An item type's material category, or the empty string when unknown.</summary>
        internal static string CategoryOf(string itemTypeId)
        {
            return CategoryLookup(itemTypeId, out string category) ? category : "";
        }

        /// <summary>
        /// The character slot an item type is worn in, or "None" when it is not
        /// wearable. Straight from the item database, which is also what the
        /// client reads.
        /// </summary>
        internal static string CharacterSlotOf(string itemTypeId)
        {
            return ItemHelper.AllItems.TryGetValue(itemTypeId, out ItemHelper.ValidItem? item)
                ? item.characterSlot
                : InventoryItem.NotWorn;
        }

        /// <summary>One wire item as the pure model sees it.</summary>
        internal static InventoryItem FromWire(ScalaSlottedInventoryItem item)
        {
            Dictionary<string, string> meta = new Dictionary<string, string>();

            if (item.meta != null)
            {
                foreach (KeyValuePair<string, string> pair in item.meta)
                {
                    meta[pair.Key] = pair.Value;
                }
            }

            return new InventoryItem(
                item.itemId,
                item.itemTypeId,
                item.amount,
                item.slotType ?? InventoryItem.NotWorn,
                item.utilitySlotNum,
                item.xPosition,
                item.yPosition,
                item.rotated,
                item.hotBarSlotNum,
                item.timeToBuild,
                item.quality,
                item.lockBoxItem,
                meta,
                item.rarity.HasValue ? item.rarity.Value : (int?)null);
        }

        /// <summary>One model item as the wire wants it.</summary>
        internal static ScalaSlottedInventoryItem ToWire( InventoryItem item )
        {
            Map<string, string> meta = new Map<string, string>();

            if (item.Meta != null)
            {
                foreach (KeyValuePair<string, string> pair in item.Meta)
                {
                    meta[pair.Key] = pair.Value;
                }
            }

            return new ScalaSlottedInventoryItem(
                item.ItemId,
                item.ItemTypeId,
                item.Amount,
                item.SlotType,
                item.UtilitySlotNum,
                item.X,
                item.Y,
                item.Rotated,
                item.HotBarSlotNum,
                item.TimeToBuild,
                item.Quality,
                item.LockBoxItem,
                meta,
                item.Rarity.HasValue ? new Option<int>(item.Rarity.Value) : new Option<int>());
        }

        /// <summary>
        /// A whole inventory as a fresh wire list.
        ///
        /// FRESH, every time, per destination. Handing the same
        /// Improbable.Collections.List to two peers' stored Data would make them
        /// share one mutable list, which is the exact class of bug that made a
        /// second push built from a stale copy erase the first.
        /// </summary>
        internal static Improbable.Collections.List<ScalaSlottedInventoryItem> ToWireList( InventoryModel model )
        {
            Improbable.Collections.List<ScalaSlottedInventoryItem> list = new Improbable.Collections.List<ScalaSlottedInventoryItem>();

            foreach (InventoryItem item in model.Items)
            {
                if (item.IsStashed)
                {
                    // Stash items ride on lockBoxItems, not inventoryList.
                    continue;
                }

                list.Add(ToWire(item));
            }

            return list;
        }

        /// <summary>The stash half of the same model.</summary>
        internal static Improbable.Collections.List<ScalaSlottedInventoryItem> ToStashList( InventoryModel model )
        {
            Improbable.Collections.List<ScalaSlottedInventoryItem> list = new Improbable.Collections.List<ScalaSlottedInventoryItem>();

            foreach (InventoryItem item in model.Items)
            {
                if (item.IsStashed)
                {
                    list.Add(ToWire(item));
                }
            }

            return list;
        }

        /// <summary>
        /// The model a brand new player starts with: the same seven items
        /// ItemHelper has always handed out, plus the stash, read through the
        /// same conversion as everything else so the defaults are subject to the
        /// same rules as a granted item.
        /// </summary>
        internal static InventoryModel DefaultModel()
        {
            InventoryModel model = InventoryModel.DefaultGrid();

            foreach (ScalaSlottedInventoryItem item in ItemHelper.GetDefaultItems())
            {
                model.Add(FromWire(item));
            }

            foreach (ScalaSlottedInventoryItem item in ItemHelper.GetStashItems(true, true))
            {
                model.Add(FromWire(item));
            }

            return model;
        }
    }
}

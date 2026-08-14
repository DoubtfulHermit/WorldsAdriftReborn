using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;

namespace WorldsAdriftRebornGameServer.Multiplayer.Crafting
{
    /// <summary>The outcome of an <see cref="ShipBlueprintTransaction.AddItem"/>.</summary>
    public enum AddItemOutcome
    {
        /// <summary>Reserved: the item left the inventory and now fills the slot.</summary>
        Added,

        /// <summary>The blueprint is mid-craft; no material may be loaded.</summary>
        WhileCrafting,

        /// <summary>The schematic/material index does not address a real slot.</summary>
        NoSuchSlot,

        /// <summary>No inventory item with that id belongs to the player.</summary>
        ItemNotFound,

        /// <summary>The item's type/quality does not match the slot's requirement.</summary>
        Mismatch,

        /// <summary>The slot is already satisfied; nothing more is accepted.</summary>
        SlotFull,
    }

    /// <summary>The outcome of a <see cref="ShipBlueprintTransaction.ReturnItem"/>.</summary>
    public enum ReturnItemOutcome
    {
        Returned,
        WhileCrafting,
        NoSuchSlot,
        NothingToReturn,
    }

    /// <summary>The outcome of a <see cref="ShipBlueprintTransaction.StartCraft"/>.</summary>
    public enum StartCraftOutcome
    {
        /// <summary>Every enabled row is filled; the timer may start.</summary>
        Started,

        /// <summary>The blueprint is already crafting.</summary>
        AlreadyCrafting,

        /// <summary>No row is enabled - there is nothing to build.</summary>
        NothingEnabled,

        /// <summary>An enabled row is not fully filled - the craft is blocked.</summary>
        MissingMaterials,

        /// <summary>
        /// The target shipyard already holds a built/docked ship. A shipyard's 1205
        /// DockedShipId is singular, so it may build only ONE ship at a time; the
        /// current one must be removed before another is built.
        /// </summary>
        ShipyardOccupied,
    }

    /// <summary>
    /// The server-authoritative state machine that moves items between a player's
    /// <see cref="InventoryModel"/> and a <see cref="ShipBlueprintBuild"/>. It is the
    /// ONE place a material is reserved, returned, or consumed, so the inventory and
    /// the blueprint can never disagree.
    ///
    /// Every method mutates the pure inventory and build in place and reports what
    /// happened; the thin game handler turns that into an authoritative
    /// <c>InventoryPush</c> and a 1271 re-push. No client-supplied amount is ever
    /// trusted - the caller passes an item id, and the amount is read from the
    /// server's own inventory record.
    /// </summary>
    public static class ShipBlueprintTransaction
    {
        /// <summary>
        /// AddItemToShipBlueprint: reserve one inventory item into a material slot.
        /// The whole item (its full stack) is removed from the inventory and held in
        /// the slot. Refused, with no inventory change, if the blueprint is crafting,
        /// the slot does not exist, the item is not the player's, the item does not
        /// match the requirement, or the slot is already full.
        /// </summary>
        public static AddItemOutcome AddItem(
            ShipBlueprintBuild build, InventoryModel inventory,
            int schematicSlotIndex, int materialSlotIndex, int itemId)
        {
            if (build.IsCrafting)
            {
                return AddItemOutcome.WhileCrafting;
            }

            MaterialSlot? slot = build.SlotAt(schematicSlotIndex, materialSlotIndex);
            if (slot == null)
            {
                return AddItemOutcome.NoSuchSlot;
            }

            InventoryItem? item = inventory.ById(itemId);
            if (item == null)
            {
                return AddItemOutcome.ItemNotFound;
            }

            if (slot.IsSatisfied)
            {
                return AddItemOutcome.SlotFull;
            }

            if (!MaterialMatch.Matches(slot.Required, item))
            {
                return AddItemOutcome.Mismatch;
            }

            inventory.Remove(itemId);
            slot.Load(item);
            return AddItemOutcome.Added;
        }

        /// <summary>
        /// ReturnItemFromShipBlueprint: empty a material slot back into the inventory.
        /// Every item reserved into the slot is added back exactly as it was (same id,
        /// stack, position). Refused if crafting, the slot does not exist, or nothing
        /// is loaded.
        /// </summary>
        public static ReturnItemOutcome ReturnItem(
            ShipBlueprintBuild build, InventoryModel inventory,
            int schematicSlotIndex, int materialSlotIndex)
        {
            if (build.IsCrafting)
            {
                return ReturnItemOutcome.WhileCrafting;
            }

            MaterialSlot? slot = build.SlotAt(schematicSlotIndex, materialSlotIndex);
            if (slot == null)
            {
                return ReturnItemOutcome.NoSuchSlot;
            }

            if (!slot.HasLoaded)
            {
                return ReturnItemOutcome.NothingToReturn;
            }

            foreach (InventoryItem item in slot.DrainLoaded())
            {
                inventory.Add(item);
            }
            return ReturnItemOutcome.Returned;
        }

        /// <summary>
        /// AutoFillBlueprint: for every enabled row and every unfilled slot, pull
        /// matching items out of the inventory until the slot is satisfied or no more
        /// matching items remain. Returns the number of items reserved. Worn and
        /// stashed items are never touched. Refused (returns 0) while crafting.
        /// </summary>
        public static int AutoFill(ShipBlueprintBuild build, InventoryModel inventory)
        {
            if (build.IsCrafting)
            {
                return 0;
            }

            int loaded = 0;
            foreach (SchematicRowBuild row in build.Rows)
            {
                if (!row.IsEnabled)
                {
                    continue;
                }
                foreach (MaterialSlot slot in row.Slots)
                {
                    while (!slot.IsSatisfied)
                    {
                        InventoryItem? match = FindMatch(inventory, slot.Required);
                        if (match == null)
                        {
                            break;
                        }
                        inventory.Remove(match.ItemId);
                        slot.Load(match);
                        loaded++;
                    }
                }
            }
            return loaded;
        }

        /// <summary>
        /// ReturnAllItems: empty every slot on the blueprint back into the inventory.
        /// Returns the number of items returned. Refused (returns 0) while crafting.
        /// </summary>
        public static int ReturnAll(ShipBlueprintBuild build, InventoryModel inventory)
        {
            if (build.IsCrafting)
            {
                return 0;
            }

            int returned = 0;
            foreach (InventoryItem item in build.DrainAllLoaded())
            {
                inventory.Add(item);
                returned++;
            }
            return returned;
        }

        /// <summary>
        /// StartCraftingShipBlueprint: gate the craft. It starts only when at least one
        /// row is enabled and every enabled row is fully filled. On success the
        /// reserved materials are consumed for real (they are already out of the
        /// inventory; setting IsCrafting makes them non-returnable) and the timer may
        /// begin. On failure nothing changes and the caller emits an error + clears busy.
        ///
        /// <paramref name="shipyardOccupied"/> gates ONE ship per shipyard: when the
        /// target yard already holds a built/docked ship the craft is refused with
        /// <see cref="StartCraftOutcome.ShipyardOccupied"/> and nothing is consumed. It
        /// is checked FIRST (before materials) so an occupied yard never burns the
        /// player's loaded materials. The occupancy is passed in rather than looked up
        /// here so this stays a pure, engine-free policy the tests drive directly.
        /// </summary>
        public static StartCraftOutcome StartCraft(ShipBlueprintBuild build, bool shipyardOccupied = false)
        {
            if (shipyardOccupied)
            {
                return StartCraftOutcome.ShipyardOccupied;
            }
            if (build.IsCrafting)
            {
                return StartCraftOutcome.AlreadyCrafting;
            }
            if (!build.AnyEnabledRow())
            {
                return StartCraftOutcome.NothingEnabled;
            }
            if (!build.AllEnabledRowsFilled())
            {
                return StartCraftOutcome.MissingMaterials;
            }

            // The reserved materials were removed from inventory at add time; flipping
            // IsCrafting is the point of no return - ReturnItem/ReturnAll now refuse.
            // The slots stay populated so the panel keeps showing full progress; they
            // are physically cleared on timer completion.
            build.IsCrafting = true;
            return StartCraftOutcome.Started;
        }

        /// <summary>
        /// After a successful StartCraft, trim every reserved slot to exactly its recipe
        /// requirement and return the excess to inventory. Whole stacks were reserved so
        /// their item ids/metadata could round-trip; this is the point where a partial
        /// stack is split authoritatively. Disabled rows consume nothing and are returned
        /// in full. Returns the total material AMOUNT refunded (not stack count).
        /// </summary>
        public static int RefundExcess(ShipBlueprintBuild build, InventoryModel inventory)
        {
            if (!build.IsCrafting) return 0;

            int refunded = 0;
            foreach (SchematicRowBuild row in build.Rows)
            {
                foreach (MaterialSlot slot in row.Slots)
                {
                    int remaining = row.IsEnabled ? slot.Required.Amount : 0;
                    List<InventoryItem> reserved = slot.DrainLoaded();
                    foreach (InventoryItem item in reserved)
                    {
                        int consumed = System.Math.Min(remaining, item.Amount);
                        if (consumed > 0)
                        {
                            slot.Load(item with { Amount = consumed });
                            remaining -= consumed;
                        }
                        int excess = item.Amount - consumed;
                        if (excess > 0)
                        {
                            inventory.Add(item with { Amount = excess });
                            refunded += excess;
                        }
                    }
                }
            }
            return refunded;
        }

        /// <summary>
        /// The first inventory item that matches a requirement and is free to reserve
        /// (not worn, not stashed), or null.
        /// </summary>
        private static InventoryItem? FindMatch(InventoryModel inventory, MaterialRequirement required)
        {
            foreach (InventoryItem item in inventory.Items)
            {
                if (item.IsWorn || item.IsStashed)
                {
                    continue;
                }
                if (MaterialMatch.Matches(required, item))
                {
                    return item;
                }
            }
            return null;
        }
    }
}

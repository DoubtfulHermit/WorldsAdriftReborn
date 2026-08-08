namespace WorldsAdriftRebornGameServer.Multiplayer.Inventory
{
    /// <summary>
    /// Every decision about an inventory mutation, and every invariant the wire
    /// demands. No I/O, no game types, no side effects beyond the model handed
    /// in.
    ///
    /// The reason this is a policy module and not a handful of ifs inside the
    /// 1082 handler: the client validates NOTHING the server sends it, and
    /// several of its failure modes are silent (an overlapping item just renders
    /// on top of another) or catastrophic and unattributable (one bad slotType
    /// string blanks the entire panel). The rules therefore have to be enforced
    /// in one place that a test can point at, rather than distributed over
    /// fifteen event branches that each got them slightly right.
    /// </summary>
    public static class InventoryPolicy
    {
        /// <summary>
        /// The slotType values the client's Enum.Parse accepts. Case-sensitive,
        /// no TryParse, no try/catch, and the throw escapes after the panel's
        /// lookup table has already been cleared.
        /// </summary>
        public static readonly IReadOnlyList<string> LegalSlotTypes = new[]
        {
            "None", "Head", "Body", "Feet", "UtilityHead", "Utility", "UtilityFeet",
            "Face", "FacialHair", "Tool", "UtilityHand", "Pet",
        };

        /// <summary>
        /// Whether a slotType string is one the client can parse. A value that is
        /// merely mis-cased ("none") is as fatal as a nonsense one.
        /// </summary>
        public static bool IsLegalSlotType(string? slotType)
        {
            if (slotType == null)
            {
                return false;
            }

            for (int i = 0; i < LegalSlotTypes.Count; i++)
            {
                if (string.Equals(LegalSlotTypes[i], slotType, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Everything wrong with this inventory, in terms a log line can carry,
        /// or an empty list when it is safe to put on the wire.
        ///
        /// Returning problems rather than throwing because the caller's correct
        /// response is never "crash the server": it is to log loudly and push
        /// the last good state, so the player's panel unsticks even when the
        /// server has a bug.
        /// </summary>
        public static IReadOnlyList<string> ValidateForWire(InventoryModel model, ItemFootprintLookup footprints)
        {
            List<string> problems = new();

            HashSet<int> seenIds = new();
            List<GridRect> placed = new();

            foreach (InventoryItem item in model.Items)
            {
                if (!seenIds.Add(item.ItemId))
                {
                    // Same id twice: the client's indexer insert makes one of
                    // them silently disappear, and RemoveByItemId then deletes
                    // BOTH.
                    problems.Add("duplicate itemId " + item.ItemId);
                }

                if (string.IsNullOrEmpty(item.ItemTypeId))
                {
                    problems.Add("item " + item.ItemId + " has no itemTypeId");
                    continue;
                }

                if (!IsLegalSlotType(item.SlotType))
                {
                    problems.Add("item " + item.ItemId + " has slotType '" + item.SlotType
                        + "', which blanks the whole panel");
                }

                if (item.Meta == null)
                {
                    // TryGetValue is called on meta unguarded on every icon
                    // update, so null is an NRE per frame, not a one-off.
                    problems.Add("item " + item.ItemId + " has null meta");
                }

                if (item.TimeToBuild != 0)
                {
                    problems.Add("item " + item.ItemId + " has timeToBuild " + item.TimeToBuild
                        + ", which greys it out");
                }

                if (item.HotBarSlotNum >= InventoryModel.HotBarSlots)
                {
                    problems.Add("item " + item.ItemId + " claims hotbar slot " + item.HotBarSlotNum
                        + ", which the client logs and drops");
                }

                if (!footprints(item.ItemTypeId, out ItemFootprint footprint))
                {
                    // An itemTypeId the item database has never heard of is an
                    // unguarded null dereference on the client.
                    problems.Add("item " + item.ItemId + " has unknown itemTypeId '" + item.ItemTypeId + "'");
                    continue;
                }

                if (item.IsWorn || item.IsStashed)
                {
                    continue;
                }

                ItemFootprint oriented = item.Oriented(footprint);

                if (!InventoryGeometry.InBounds(item.X, item.Y, oriented.Width, oriented.Height, model.Width, model.Height))
                {
                    problems.Add("item " + item.ItemId + " at (" + item.X + "," + item.Y + ") "
                        + oriented.Width + "x" + oriented.Height + " is out of bounds");
                    continue;
                }

                if (oriented.Width > 0 && oriented.Height > 0)
                {
                    foreach (GridRect other in placed)
                    {
                        if (InventoryGeometry.Overlaps(
                                item.X, item.Y, oriented.Width, oriented.Height,
                                other.X, other.Y, other.Width, other.Height))
                        {
                            problems.Add("item " + item.ItemId + " overlaps item " + other.ItemId);
                            break;
                        }
                    }

                    placed.Add(new GridRect(item.ItemId, item.X, item.Y, oriented.Width, oriented.Height));
                }
            }

            return problems;
        }

        /// <summary>
        /// Moves an item to (x,y), optionally rotating it. Returns false and
        /// leaves the model untouched when the destination is out of bounds or
        /// occupied.
        ///
        /// A rejected move is NOT an error path that can be skipped: the caller
        /// must still push 1081 afterwards, because the client set
        /// IsWaitingForServer before it sent the request and only a 1081 clears
        /// it. Pushing the unchanged state is what makes the item snap back
        /// instead of the panel greying out forever.
        /// </summary>
        public static bool TryMove(InventoryModel model, int itemId, int x, int y, bool rotate, ItemFootprintLookup footprints)
        {
            InventoryItem? item = model.ById(itemId);

            if (item == null || item.IsStashed)
            {
                return false;
            }

            if (!footprints(item.ItemTypeId, out ItemFootprint footprint))
            {
                return false;
            }

            ItemFootprint oriented = rotate
                ? new ItemFootprint(footprint.Height, footprint.Width)
                : footprint;

            if (!InventoryGeometry.Fits(x, y, oriented.Width, oriented.Height, model.Width, model.Height,
                    model.OccupiedRects(footprints, exceptItemId: itemId)))
            {
                return false;
            }

            // A move puts the item in the grid, so it is by definition no longer
            // worn. Leaving slotType alone here is how an item ends up both in a
            // grid cell and on the character's body.
            return model.Replace(item with
            {
                X = x,
                Y = y,
                Rotated = rotate,
                SlotType = InventoryItem.NotWorn,
            });
        }

        /// <summary>
        /// Puts an item on a hotbar slot. Slots 0-3 are refused: they are the
        /// four gauntlet shells, which InteractAgentObserver hardcodes and never
        /// reads the inventory for, so an item assigned there is invisible to
        /// the tool system and merely displaces a gauntlet in the UI.
        ///
        /// Hotbar membership is orthogonal to grid position - the item keeps its
        /// cells - so this changes nothing but the slot number, and any previous
        /// occupant is evicted rather than left to fight over the slot.
        /// </summary>
        public static bool TryAssignToHotBar(InventoryModel model, int itemId, int slotIndex)
        {
            if (slotIndex < InventoryModel.FirstAssignableHotBarSlot || slotIndex >= InventoryModel.HotBarSlots)
            {
                return false;
            }

            InventoryItem? item = model.ById(itemId);

            if (item == null || item.IsStashed)
            {
                return false;
            }

            InventoryItem? occupant = model.OnHotBar(slotIndex);

            if (occupant != null && occupant.ItemId != itemId)
            {
                model.Replace(occupant with { HotBarSlotNum = InventoryItem.NoSlot });
            }

            return model.Replace(item with { HotBarSlotNum = slotIndex });
        }

        /// <summary>
        /// Clears a hotbar slot. Refuses 0-3 for the same reason as assignment:
        /// the gauntlets are not really there to be removed.
        /// </summary>
        public static bool TryRemoveFromHotBar(InventoryModel model, int slotIndex)
        {
            if (slotIndex < InventoryModel.FirstAssignableHotBarSlot || slotIndex >= InventoryModel.HotBarSlots)
            {
                return false;
            }

            InventoryItem? occupant = model.OnHotBar(slotIndex);

            if (occupant == null)
            {
                return false;
            }

            return model.Replace(occupant with { HotBarSlotNum = InventoryItem.NoSlot });
        }

        /// <summary>
        /// Marks an item as worn in a character slot.
        ///
        /// The slot string comes from the item database, and an item type whose
        /// characterSlot is "None" is not wearable at all - equipping it would
        /// write a legal-but-meaningless value and leave the item in the grid
        /// while the client believes it is on the body.
        /// </summary>
        public static bool TryEquip(InventoryModel model, int itemId, string slotType)
        {
            if (!IsLegalSlotType(slotType) || string.Equals(slotType, InventoryItem.NotWorn, StringComparison.Ordinal))
            {
                return false;
            }

            InventoryItem? item = model.ById(itemId);

            if (item == null || item.IsStashed)
            {
                return false;
            }

            // One garment per slot. Without this the wearable arrays on 1280 end
            // up with two items claiming Head and the visualiser renders whichever
            // it reaches first.
            //
            // Collected first, then replaced: Replace writes through the list
            // indexer, which bumps List<T>'s version counter, so displacing an
            // occupant while enumerating Items throws.
            List<InventoryItem> displaced = new();

            foreach (InventoryItem other in model.Items)
            {
                if (other.ItemId != itemId
                    && string.Equals(other.SlotType, slotType, StringComparison.Ordinal))
                {
                    displaced.Add(other);
                }
            }

            foreach (InventoryItem other in displaced)
            {
                model.Replace(other with { SlotType = InventoryItem.NotWorn });
            }

            return model.Replace(item with { SlotType = slotType });
        }

        /// <summary>
        /// Takes an item off the body and back into the grid.
        ///
        /// Unequipping has to find the item a real cell, because a worn item's
        /// x/y were ignored while it was worn and are very likely stale or
        /// overlapping something placed since. With no free cell the unequip is
        /// refused - which is the honest answer, and better than dropping the
        /// garment through the floor of the data model.
        /// </summary>
        public static bool TryUnequip(InventoryModel model, int itemId, ItemFootprintLookup footprints)
        {
            InventoryItem? item = model.ById(itemId);

            if (item == null || !item.IsWorn)
            {
                return false;
            }

            if (!footprints(item.ItemTypeId, out ItemFootprint footprint))
            {
                return false;
            }

            ItemFootprint oriented = item.Oriented(footprint);
            IReadOnlyList<GridRect> occupied = model.OccupiedRects(footprints, exceptItemId: itemId);

            (int X, int Y)? spot = InventoryGeometry.Fits(item.X, item.Y, oriented.Width, oriented.Height,
                    model.Width, model.Height, occupied)
                ? (item.X, item.Y)
                : InventoryGeometry.FirstFree(oriented.Width, oriented.Height, model.Width, model.Height, occupied);

            if (spot == null)
            {
                return false;
            }

            return model.Replace(item with
            {
                SlotType = InventoryItem.NotWorn,
                X = spot.Value.X,
                Y = spot.Value.Y,
            });
        }

        /// <summary>
        /// Places a newly created item in the first free cell, or returns null
        /// when the inventory is full.
        ///
        /// This is the shape a grant needs (harvest, loot, admin), and it is
        /// here rather than in the harvesting workstream because "where does it
        /// go" is a property of the container, not of what filled it.
        /// </summary>
        public static InventoryItem? TryGrant(
            InventoryModel model,
            int itemId,
            string itemTypeId,
            int amount,
            int quality,
            IReadOnlyDictionary<string, string> meta,
            int? rarity,
            ItemFootprintLookup footprints)
        {
            if (model.ById(itemId) != null)
            {
                return null;
            }

            if (!footprints(itemTypeId, out ItemFootprint footprint))
            {
                return null;
            }

            (int X, int Y)? spot = InventoryGeometry.FirstFree(
                footprint.Width, footprint.Height, model.Width, model.Height, model.OccupiedRects(footprints));

            if (spot == null)
            {
                return null;
            }

            InventoryItem item = new InventoryItem(
                itemId,
                itemTypeId,
                amount,
                InventoryItem.NotWorn,
                InventoryItem.NoSlot,
                spot.Value.X,
                spot.Value.Y,
                false,
                InventoryItem.NoSlot,
                0,
                quality,
                false,
                meta ?? new Dictionary<string, string>(),
                rarity);

            return model.Add(item) ? item : null;
        }
    }
}

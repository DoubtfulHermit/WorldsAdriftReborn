using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;

namespace WorldsAdriftRebornGameServer.Multiplayer.Crafting
{
    /// <summary>
    /// Whether an inventory item is an acceptable fill for a blueprint material slot.
    ///
    /// The client renders whatever the server writes into 1271; the server is the
    /// sole authority on what counts.
    ///
    /// THE WIDENING (this is the "documented widen-later" the Phase 2 comment
    /// promised). Retail's hull slots asked for a FAMILY at a minimum quality -
    /// the shipyard row literally reads "Q3+ Metal", printed by
    /// <c>ShipBlueprintMaterialUI</c> whenever the row's category is "Metal" or
    /// "Wood" (acs/ShipBlueprintMaterialUI.cs:81-86, VERIFIED) - and the player
    /// chose which metal. So a requirement now accepts ANY material of its
    /// category, unless it opts out with
    /// <c>AcceptsAnyInCategory: false</c> (an atlas shard is not "any metal").
    ///
    /// The quality floor is unchanged and still applies to every candidate, so
    /// widening the SUBSTANCE never widens the STANDARD.
    /// </summary>
    public static class MaterialMatch
    {
        public static bool Matches(MaterialRequirement required, InventoryItem item)
        {
            return Matches(required, item.ItemTypeId, item.Quality);
        }

        public static bool Matches(MaterialRequirement required, string itemTypeId, int quality)
        {
            if (required == null || quality < required.Quality)
            {
                return false;
            }

            if (string.Equals(required.MaterialTypeId, itemTypeId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return required.AcceptsAnyInCategory
                && Materials.MaterialCatalog.Satisfies(required.Category, itemTypeId);
        }
    }

    /// <summary>
    /// One material slot inside a schematic row: the requirement it displays and the
    /// inventory items reserved into it so far.
    ///
    /// RESERVATION MODEL. When a player drags an item in, the WHOLE inventory item
    /// (its full stack) is removed from the inventory and held here. That keeps the
    /// item id round-tripping - a return puts back the exact same item - and means the
    /// authoritative inventory the client waits on always agrees with what the slot
    /// shows. <see cref="EquivalentAmount"/> is the sum of the held stacks; the slot is
    /// <see cref="IsSatisfied"/> once that reaches the required amount. Overfill is
    /// allowed (dropping a stack of 5 into a slot needing 3 shows 5/3 and satisfies it)
    /// - splitting stacks is a later refinement, not a Phase 2 requirement.
    /// </summary>
    public sealed class MaterialSlot
    {
        private readonly List<InventoryItem> _loaded = new List<InventoryItem>();

        public MaterialSlot(MaterialRequirement required)
        {
            Required = required;
        }

        /// <summary>The requirement this slot displays (type, quality, amount).</summary>
        public MaterialRequirement Required { get; }

        /// <summary>The inventory items reserved into this slot, in load order.</summary>
        public IReadOnlyList<InventoryItem> Loaded => _loaded;

        /// <summary>Whether anything is reserved here.</summary>
        public bool HasLoaded => _loaded.Count > 0;

        /// <summary>The total reserved amount - the 1271 equivalentMaterialAmount.</summary>
        public int EquivalentAmount
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < _loaded.Count; i++)
                {
                    sum += _loaded[i].Amount;
                }
                return sum;
            }
        }

        /// <summary>Filled once the reserved amount reaches the requirement.</summary>
        public bool IsSatisfied => EquivalentAmount >= Required.Amount;

        /// <summary>Whether this item may be reserved here: it matches and the slot is not already full.</summary>
        public bool Accepts(InventoryItem item) => !IsSatisfied && MaterialMatch.Matches(Required, item);

        /// <summary>Reserve an item into the slot (the caller has already removed it from inventory).</summary>
        public void Load(InventoryItem item) => _loaded.Add(item);

        /// <summary>
        /// Empty the slot and hand back every reserved item so the caller can return
        /// them to the inventory (a return) or drop them (a craft consume).
        /// </summary>
        public List<InventoryItem> DrainLoaded()
        {
            List<InventoryItem> drained = new List<InventoryItem>(_loaded);
            _loaded.Clear();
            return drained;
        }
    }

    /// <summary>
    /// One schematic row's live build state: its identity, whether it is enabled, and
    /// its material slots. The two mandatory ids (shipFrame / deck01) are
    /// non-disableable - the client greys their toggle - so <see cref="SetEnabled"/>
    /// refuses to change them, mirroring
    /// <c>acs/ShipBlueprintSchematicUI.cs:58</c>.
    /// </summary>
    public sealed class SchematicRowBuild
    {
        private readonly List<MaterialSlot> _slots = new List<MaterialSlot>();

        public SchematicRowBuild(SchematicRow row, bool isMandatory)
        {
            SchematicId = row.SchematicId;
            NodeCount = row.NodeCount;
            CraftingTime = row.CraftingTime;
            IsMandatory = isMandatory;
            IsEnabled = row.IsEnabled;
            foreach (MaterialRequirement req in row.Materials)
            {
                _slots.Add(new MaterialSlot(req));
            }
        }

        public string SchematicId { get; }
        public int NodeCount { get; }
        public int CraftingTime { get; }

        /// <summary>shipFrame/deck01: always present, cannot be disabled.</summary>
        public bool IsMandatory { get; }

        /// <summary>Whether this row is included in the craft.</summary>
        public bool IsEnabled { get; private set; }

        public IReadOnlyList<MaterialSlot> Slots => _slots;

        /// <summary>Every slot on this row is satisfied.</summary>
        public bool IsFilled
        {
            get
            {
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (!_slots[i].IsSatisfied)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// SetSchematicEnabled: toggle the row. A mandatory row is non-disableable, so
        /// the call is refused (returns false, no state change); the handler still
        /// clears busy so the client is never stuck.
        /// </summary>
        public bool SetEnabled(bool enabled)
        {
            if (IsMandatory)
            {
                return false;
            }
            IsEnabled = enabled;
            return true;
        }
    }

    /// <summary>
    /// The live build state of one selected ship blueprint on one shipyard, for one
    /// player: the expanded schematic rows with their material slots, and whether the
    /// build timer is running.
    ///
    /// It is the single source of truth that survives across the many 1270 events a
    /// build takes (select -> add/return/autofill -> craft). The game handler maps it
    /// straight into a 1271 <c>ShipBlueprintCraftingState</c> after every change, and
    /// the mapping is the only thing the client ever sees.
    ///
    /// Engine-free: it is built from an engine-free <see cref="ShipBlueprintRecipe"/>
    /// and mutates an engine-free <see cref="InventoryModel"/>, so the whole
    /// add/return/autofill/craft state machine is unit-tested on Linux with no install.
    /// </summary>
    public sealed class ShipBlueprintBuild
    {
        private readonly List<SchematicRowBuild> _rows = new List<SchematicRowBuild>();

        public ShipBlueprintBuild(string blueprintId, ShipBlueprintRecipe recipe)
        {
            BlueprintId = blueprintId;
            CraftingTime = recipe.CraftingTime;
            foreach (SchematicRow row in recipe.Rows)
            {
                bool mandatory =
                    string.Equals(row.SchematicId, ShipBlueprintRecipe.ShipFrameSchematicId, StringComparison.Ordinal)
                    || string.Equals(row.SchematicId, ShipBlueprintRecipe.Deck01SchematicId, StringComparison.Ordinal);
                _rows.Add(new SchematicRowBuild(row, mandatory));
            }
        }

        /// <summary>The selected blueprint's id/name (the 1271 blueprintId).</summary>
        public string BlueprintId { get; }

        /// <summary>
        /// WHAT THIS BUILD IS ACTUALLY MADE OF - the dominant wood and metal among
        /// every item the player reserved into an ENABLED row.
        ///
        /// The craft has always known this: <see cref="MaterialSlot.Loaded"/> holds
        /// the real <see cref="InventoryItem"/>s, complete with their itemTypeId and
        /// quality. It simply never asked them what they were, so a copper hull was
        /// indistinguishable from an iron one the moment the timer finished. This is
        /// the accessor that carries the player's CHOICE onto the output.
        ///
        /// Disabled rows are excluded: a row the player switched off contributes no
        /// substance to the ship.
        /// </summary>
        public Materials.HullMaterials LoadedMaterials()
        {
            var consumed = new List<(string ItemTypeId, int Amount, int Quality)>();
            foreach (SchematicRowBuild row in _rows)
            {
                if (!row.IsEnabled)
                {
                    continue;
                }
                foreach (MaterialSlot slot in row.Slots)
                {
                    foreach (InventoryItem item in slot.Loaded)
                    {
                        consumed.Add((item.ItemTypeId, item.Amount, item.Quality));
                    }
                }
            }
            return Materials.HullMaterials.FromConsumed(consumed);
        }

        /// <summary>The whole-blueprint craft time in seconds (the 1271 craftingTime).</summary>
        public int CraftingTime { get; }

        /// <summary>
        /// Whether the build timer is running. While true the materials are
        /// CONSUMED FOR REAL - they were already removed from inventory when reserved,
        /// and now cannot be returned - so every mutating transaction is refused.
        /// </summary>
        public bool IsCrafting { get; set; }

        public IReadOnlyList<SchematicRowBuild> Rows => _rows;

        /// <summary>The row at an index, or null if out of range.</summary>
        public SchematicRowBuild? RowAt(int schematicIndex)
        {
            if (schematicIndex < 0 || schematicIndex >= _rows.Count)
            {
                return null;
            }
            return _rows[schematicIndex];
        }

        /// <summary>The slot at (schematic, material), or null if either index is out of range.</summary>
        public MaterialSlot? SlotAt(int schematicIndex, int materialSlotIndex)
        {
            SchematicRowBuild? row = RowAt(schematicIndex);
            if (row == null || materialSlotIndex < 0 || materialSlotIndex >= row.Slots.Count)
            {
                return null;
            }
            return row.Slots[materialSlotIndex];
        }

        /// <summary>At least one enabled row exists (a craft with nothing enabled is nonsense).</summary>
        public bool AnyEnabledRow()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsEnabled)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Every ENABLED row is fully filled - the craft gate.</summary>
        public bool AllEnabledRowsFilled()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsEnabled && !_rows[i].IsFilled)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Empty every slot and return all reserved items (a craft consume, or a clear).</summary>
        public List<InventoryItem> DrainAllLoaded()
        {
            List<InventoryItem> all = new List<InventoryItem>();
            for (int r = 0; r < _rows.Count; r++)
            {
                IReadOnlyList<MaterialSlot> slots = _rows[r].Slots;
                for (int s = 0; s < slots.Count; s++)
                {
                    all.AddRange(slots[s].DrainLoaded());
                }
            }
            return all;
        }
    }
}

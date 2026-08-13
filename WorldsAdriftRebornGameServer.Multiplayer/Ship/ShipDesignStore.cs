using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// One saved FRAME DESIGN slot: the wire fields of one
    /// <c>ShipHullSchematicData</c> the server serves in 1207
    /// <c>ShipHullAgentState.field1_schematics</c>. Pure data.
    /// </summary>
    public sealed class ShipDesignSlot
    {
        public ShipDesignSlot(byte[] data, string name, float beamsLength,
            int numberOfDecks, string clientSchematicsIdJson, string uuid)
        {
            Data = data;
            Name = name;
            BeamsLength = beamsLength;
            NumberOfDecks = numberOfDecks;
            ClientSchematicsIdJson = clientSchematicsIdJson;
            Uuid = uuid;
        }

        /// <summary>field1_data - the ShipPlan geometry blob.</summary>
        public byte[] Data { get; set; }

        /// <summary>field2_name.</summary>
        public string Name { get; set; }

        /// <summary>field3_beams_length.</summary>
        public float BeamsLength { get; set; }

        /// <summary>field4_number_of_decks.</summary>
        public int NumberOfDecks { get; set; }

        /// <summary>field5_client_schematics_id - the SchematicData JSON.</summary>
        public string ClientSchematicsIdJson { get; set; }

        /// <summary>field6_uuid - MUST equal the JSON's uUID (StarterFrame doc).</summary>
        public string Uuid { get; set; }
    }

    /// <summary>
    /// One player's live ship-design state, held in memory and keyed by their player
    /// entity id - the same shape as <c>PlayerProgression</c> /
    /// <c>InventoryService</c>. It is BOTH the source of truth the 1207 serve branch
    /// reads (so a re-checkout re-serves the CURRENT designs, not a static seed) AND
    /// the little state machine the 1208 command handler drives: load a slot into the
    /// editor, apply the client's edited blob, save it back, reset, unload.
    ///
    /// Pure and engine-free so the whole command-&gt;ack machine is unit-tested on
    /// Linux with no install. The game handler is thin glue that turns the booleans
    /// here into 1207/1206 component updates.
    ///
    /// EVERY client-supplied blob goes through <see cref="ShipPlanModel.TryDecode"/> -
    /// <see cref="ApplyEditedHull"/> never throws and rejects a malformed design
    /// rather than storing it, so a modified client can never take the server down or
    /// poison a saved slot.
    ///
    /// Seeded lazily on first touch with exactly one <see cref="StarterFrame"/>, so an
    /// untouched player always has a non-empty FRAME DESIGNS list. In-session only;
    /// disk persistence is a documented follow-on.
    /// </summary>
    public sealed class PlayerShipDesigns
    {
        /// <summary>Sentinel <see cref="LoadedSlot"/> value: nothing is loaded.</summary>
        public const int NoSlot = -1;

        public PlayerShipDesigns()
        {
            Slots.Add(new ShipDesignSlot(
                StarterFrame.HullBlob(),
                StarterFrame.Title,
                StarterFrame.BeamsLength,
                StarterFrame.NumberOfDecks,
                StarterFrame.ClientSchematicsIdJson(),
                StarterFrame.Uuid));
        }

        /// <summary>The saved FRAME DESIGN slots, index == wire slot.</summary>
        public List<ShipDesignSlot> Slots { get; } = new List<ShipDesignSlot>();

        /// <summary>True once a slot is loaded into the editor (1206 Active).</summary>
        public bool Active { get; private set; }

        /// <summary>Unsaved edits pending on <see cref="WorkingHull"/> (1206 Modified).</summary>
        public bool Modified { get; private set; }

        /// <summary>The slot currently loaded, or <see cref="NoSlot"/>.</summary>
        public int LoadedSlot { get; private set; } = NoSlot;

        /// <summary>
        /// The hull blob currently in the editor - a copy of the loaded slot's data,
        /// then whatever the client's periodic UpdateShip has sent. Null when nothing
        /// is loaded.
        /// </summary>
        public byte[]? WorkingHull { get; private set; }

        /// <summary>The shipyard entity currently being edited, or 0.</summary>
        public long EditingShipyardEntityId { get; private set; }

        /// <summary>
        /// The shipyard whose build console this player most recently had open - the
        /// last editorId seen on any 1208 command or the last console this player opened.
        /// STICKY (not cleared when editing stops), because a FRAME DESIGNS rename
        /// (TriggerRenameSchematic) carries NO editorId of its own, yet needs the
        /// shipyard id to re-emit the console-open signal that rebuilds the list with the
        /// new name. Zero until the player first touches a shipyard console.
        /// </summary>
        public long LastConsoleShipyardEntityId { get; private set; }

        /// <summary>
        /// Record the shipyard whose build console this player is working with, so a
        /// later rename (which carries no editorId) can address the right yard. Idempotent;
        /// ignores 0 so a clear-on-exit does not erase the sticky value.
        /// </summary>
        public void NoteConsole(long shipyardEntityId)
        {
            if (shipyardEntityId != 0)
            {
                LastConsoleShipyardEntityId = shipyardEntityId;
            }
        }

        /// <summary>Whether a slot index addresses a real saved design.</summary>
        public bool IsValidSlot(int slot) => slot >= 0 && slot < Slots.Count;

        /// <summary>
        /// TriggerLoadSchematic(slot): make the slot the editor's working hull and mark
        /// the editor Active. Returns false (no state change) for an out-of-range slot,
        /// so the ack the handler sends still resolves the client's pending request but
        /// nothing is loaded.
        /// </summary>
        public bool LoadSlot(int slot)
        {
            if (!IsValidSlot(slot))
            {
                return false;
            }

            LoadedSlot = slot;
            WorkingHull = (byte[])Slots[slot].Data.Clone();
            Active = true;
            Modified = false;
            return true;
        }

        /// <summary>
        /// TriggerUpdateShip(data): the client's periodic push of the edited hull while
        /// the editor is open. The blob is validated with
        /// <see cref="ShipPlanModel.TryDecode"/>; a malformed one is dropped (returns
        /// false) and the last good working hull is kept. Marks Modified when it
        /// actually changes the geometry.
        /// </summary>
        public bool ApplyEditedHull(byte[]? data)
        {
            if (!Active)
            {
                return false;
            }
            if (!ShipPlanModel.TryDecode(data, out _, out _))
            {
                return false;
            }

            WorkingHull = (byte[])data!.Clone();
            Modified = true;
            return true;
        }

        /// <summary>
        /// TriggerSaveSchematic(slot): persist the working hull back into the slot. Only
        /// valid when that slot is the loaded one and the editor is active. Clears
        /// Modified. Returns false with no change otherwise.
        /// </summary>
        public bool Save(int slot)
        {
            if (!Active || !IsValidSlot(slot) || WorkingHull == null)
            {
                return false;
            }

            Slots[slot].Data = (byte[])WorkingHull.Clone();
            Modified = false;
            return true;
        }

        /// <summary>
        /// TriggerResetSchematic(slot): throw away edits and reload the slot's saved
        /// geometry into the working hull. Marks Modified false (working == saved).
        /// </summary>
        public bool Reset(int slot)
        {
            if (!Active || !IsValidSlot(slot))
            {
                return false;
            }

            LoadedSlot = slot;
            WorkingHull = (byte[])Slots[slot].Data.Clone();
            Modified = false;
            return true;
        }

        /// <summary>
        /// TriggerUnloadSchematic(): clear the editor. Active-&gt;false so the client's
        /// HasShipLoaded() goes false and Edit disables.
        /// </summary>
        public bool Unload()
        {
            Active = false;
            Modified = false;
            LoadedSlot = NoSlot;
            WorkingHull = null;
            EditingShipyardEntityId = 0;
            return true;
        }

        /// <summary>TriggerStartEditingSchematic(shipyardId): enter the mesh editor.</summary>
        public void StartEditing(long shipyardEntityId)
        {
            EditingShipyardEntityId = shipyardEntityId;
            NoteConsole(shipyardEntityId);
        }

        /// <summary>TriggerStopEditingSchematic(): leave the mesh editor (design stays loaded).</summary>
        public void StopEditing()
        {
            EditingShipyardEntityId = 0;
        }

        /// <summary>TriggerRenameSchematic(slot, name): rename a saved design.</summary>
        public bool Rename(int slot, string name)
        {
            if (!IsValidSlot(slot))
            {
                return false;
            }

            Slots[slot].Name = name ?? "";
            return true;
        }
    }

    /// <summary>Process-global registry of per-player ship designs, keyed by player entity id.</summary>
    public static class ShipDesignStore
    {
        private static readonly Dictionary<long, PlayerShipDesigns> ByEntity =
            new Dictionary<long, PlayerShipDesigns>();

        /// <summary>The player's designs, created (seeded with the starter frame) on first touch.</summary>
        public static PlayerShipDesigns For(long entityId)
        {
            if (!ByEntity.TryGetValue(entityId, out PlayerShipDesigns? d))
            {
                d = new PlayerShipDesigns();
                ByEntity[entityId] = d;
            }
            return d;
        }

        /// <summary>Drop a player's designs when their entity leaves.</summary>
        public static void Forget(long entityId) => ByEntity.Remove(entityId);

        /// <summary>
        /// Whether ANY player is currently inside the frame-design mesh editor on
        /// this shipyard (<c>TriggerStartEditingSchematic</c> without its matching
        /// stop). The station-pickup busy gate: packing the yard mid-edit would
        /// pull the editor's shipyard out from under the editing client. Design
        /// contexts are per-player and few, so a scan per pickup EVENT is nothing.
        /// </summary>
        public static bool AnyEditingAt(long shipyardEntityId)
        {
            foreach (PlayerShipDesigns designs in ByEntity.Values)
            {
                if (designs.EditingShipyardEntityId == shipyardEntityId && shipyardEntityId != 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

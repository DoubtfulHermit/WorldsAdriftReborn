using System;
using System.Collections.Generic;
using Bossa.Travellers.Items;
using Improbable;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 1208 ShipHullAgentClientState - the FRAME DESIGNS command channel on the PLAYER.
     *
     * This is the SELECT -> EDIT -> SAVE half of the placed-shipyard build UI. The
     * client's ShipHullAgentVisualizer holds the 1208 WRITER and sends one Trigger*
     * command per user action, each carrying a client-generated requestId; it then
     * WAITS for a ShipHullAgentRequestResponse(requestId, success) on its 1207 READER
     * before proceeding (acs/ShipHullAgentVisualizer.cs:116-171). So every command we
     * accept MUST be acked on 1207 or the client's button stays spinning forever.
     *
     * The commands (verified, acs/gencode):
     *   TriggerLoadSchematic(slot, editorId, id)   - load a saved frame into the editor.
     *   TriggerUpdateShip(beams, decks, data, editorId, id) - the periodic (~3s) push of
     *                                                the edited hull blob while editing.
     *   TriggerSaveSchematic(slot, editorId, id)   - persist the working hull to the slot.
     *   TriggerResetSchematic(slot, editorId, id)  - discard edits, reload the slot.
     *   TriggerUnloadSchematic(editorId, id)       - clear the editor.
     *   TriggerRenameSchematic(slot, name, id)     - rename a saved frame.
     *   TriggerStartEditingSchematic(editorId)     - enter the mesh editor (no ack).
     *   TriggerStopEditingSchematic(editorId)      - leave the mesh editor (no ack).
     *
     * editorId is the SHIPYARD entity id (the ShipHullEditorVisualizer's EntityId is the
     * shipyard). 1206 ShipHullEditorState lives on THAT entity; the client reads Active
     * (HasShipLoaded) to enable Edit, and HullData to rebuild the mesh. So on load we
     * push a 1206 update to the shipyard with Active=true + the working hull, and ack on
     * 1207. All command state is a per-player in-memory PlayerShipDesigns (ShipDesignStore).
     * Every client-supplied blob goes through ShipPlanModel.TryDecode inside the store,
     * so a malformed design is dropped, never stored, and never throws here.
     *
     * MULTIPLAYER SAFETY: event-driven and per-player. Every reply is sent ONLY to the
     * peer that issued the command. The 1207 acks/schematics ride that peer's own player
     * entity. The 1206 update rides the SHARED shipyard entity, but is addressed to the
     * ISSUING peer ALONE - never broadcast - so two players editing never clobber each
     * other's view. Rate is a user click, or the client's ~3s UpdateShip; nothing is
     * per-frame and nothing is relayed cross-entity. 1208 is filtered out of the raw
     * relay (MirrorSendPolicy), same as 1270.
     */
    [RegisterComponentUpdateHandler]
    internal class ShipHullAgentClientState_Handler
        : IComponentUpdateHandler<ShipHullAgentClientState,
            ShipHullAgentClientState.Update, ShipHullAgentClientState.Data>
    {
        public ShipHullAgentClientState_Handler() { Init(1208); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            ShipHullAgentClientState.Update clientComponentUpdate,
            ShipHullAgentClientState.Data serverComponentData)
        {
            // Only the sender's OWN entity: 1208 rides the player's own hull-agent writer.
            ulong peerId = PeerIdentity.IdOf(player);
            if (!WorldsAdriftRebornGameServer.Players.Owns(peerId, entityId))
            {
                Console.WriteLine("[warning] 1208 update for entity " + entityId + " from a peer that owns "
                    + WorldsAdriftRebornGameServer.Players.EntityOf(peerId) + ", ignoring.");
                return;
            }

            PlayerShipDesigns designs = ShipDesignStore.For(entityId);

            // Order mirrors the client's own ordering guarantees loosely; each list is a
            // batch of same-kind events. Editing lifecycle (start/stop) carries no ack.
            if (clientComponentUpdate.startEditingSchematic != null)
            {
                foreach (StartEditingSchematic ev in clientComponentUpdate.startEditingSchematic)
                {
                    long shipyardId = ev.editorId.Id;
                    designs.StartEditing(shipyardId);
                    // 1207 editorId tells ShipHullAgentVisualizer to enter editor input mode;
                    // 1206 editorId makes ShipHullEditorVisualizer.BeingEdited true (zoom in).
                    Send1207(player, entityId, u => u.SetEditorId(new EntityId(shipyardId)));
                    Send1206(player, shipyardId, u => u.SetEditorId(new EntityId(shipyardId)));
                    Console.WriteLine("[info] 1208: entity " + entityId + " started editing shipyard " + shipyardId + ".");
                }
            }

            if (clientComponentUpdate.loadSchematic != null)
            {
                foreach (LoadSchematic ev in clientComponentUpdate.loadSchematic)
                {
                    bool ok = designs.LoadSlot(ev.slot);
                    long shipyardId = ev.editorId.Id;
                    if (ok)
                    {
                        PushEditorState(player, shipyardId, designs);
                    }
                    Ack(player, entityId, ev.id, ok);
                    Console.WriteLine("[info] 1208: entity " + entityId + " load slot " + ev.slot
                        + " on shipyard " + shipyardId + " -> " + ok + ".");
                }
            }

            if (clientComponentUpdate.updateShip != null)
            {
                foreach (UpdateShip ev in clientComponentUpdate.updateShip)
                {
                    int len = ev.data?.Length ?? 0;
                    bool ok = designs.ApplyEditedHull(ev.data);
                    long shipyardId = ev.editorId.Id;
                    if (ok)
                    {
                        PushEditorState(player, shipyardId, designs);
                    }
                    Ack(player, entityId, ev.id, ok);
                    Console.WriteLine("[info] 1208: entity " + entityId + " updateShip on shipyard " + shipyardId
                        + " (" + len + " bytes, modified=" + designs.Modified + ") -> " + ok + ".");
                }
            }

            if (clientComponentUpdate.saveSchematic != null)
            {
                foreach (SaveSchematic ev in clientComponentUpdate.saveSchematic)
                {
                    bool ok = designs.Save(ev.slot);
                    long shipyardId = ev.editorId.Id;
                    if (ok)
                    {
                        // the saved slot's geometry changed -> re-serve the schematics list
                        // so the FRAME DESIGNS row reflects it, and clear Modified on 1206.
                        PushSchematics(player, entityId, designs);
                        PushEditorState(player, shipyardId, designs);
                    }
                    Ack(player, entityId, ev.id, ok);
                    Console.WriteLine("[info] 1208: entity " + entityId + " save slot " + ev.slot + " -> " + ok + ".");
                }
            }

            if (clientComponentUpdate.resetSchematic != null)
            {
                foreach (ResetSchematic ev in clientComponentUpdate.resetSchematic)
                {
                    bool ok = designs.Reset(ev.slot);
                    long shipyardId = ev.editorId.Id;
                    if (ok)
                    {
                        PushEditorState(player, shipyardId, designs);
                    }
                    Ack(player, entityId, ev.id, ok);
                    Console.WriteLine("[info] 1208: entity " + entityId + " reset slot " + ev.slot + " -> " + ok + ".");
                }
            }

            if (clientComponentUpdate.renameSchematic != null)
            {
                foreach (RenameSchematic ev in clientComponentUpdate.renameSchematic)
                {
                    // Rename a saved FRAME DESIGN (the pencil icon in the list). The client
                    // sends the slot it resolved from the row's UUID plus the new name; we
                    // persist it into the per-player store, re-serve the FRAME DESIGNS list
                    // on 1207 so the row shows the new name (and a re-checkout re-serves it,
                    // ComponentsSerializer 1207 reads the same store), and ack so the client's
                    // pending reply resolves and it fires SchematicsUpdated. NOTE: this is the
                    // HULL FRAME rename on 1208 - NOT the ship-blueprint RenameBlueprint (1270).
                    bool ok = designs.Rename(ev.slot, ev.name);
                    if (ok)
                    {
                        PushSchematics(player, entityId, designs);

                        // LIVE-REFRESH THE FRAME DESIGNS LIST. Re-pushing 1207 (above)
                        // updates the schematic system but does NOT re-run the panel's
                        // Activate/rebuild, so the renamed row does not visibly change
                        // until the panel is re-opened - the SAME display-rebuild gap the
                        // Done-exit fix closed. So we re-emit the console-open signal (1005
                        // PlayerStartCrafting) exactly as StopEditing/Done does, which is
                        // the only path that re-runs ShipCraftingUI.Activate and re-reads
                        // the just-pushed 1207, so the new name shows immediately.
                        //
                        // RenameSchematic carries NO editorId, so the shipyard is resolved
                        // from the player's tracked console (the last yard they opened /
                        // were editing). One echo per rename - a discrete user action, not
                        // a per-frame loop; a re-open does not itself trigger a rename, so
                        // there is no re-open loop.
                        long renameShipyardId = designs.EditingShipyardEntityId != 0
                            ? designs.EditingShipyardEntityId
                            : designs.LastConsoleShipyardEntityId;
                        bool relisted = renameShipyardId != 0
                            && WorldsAdriftRebornGameServer.Placement.OpenShipyardConsole(
                                player, entityId, renameShipyardId);
                        Console.WriteLine("[info] 1208: entity " + entityId + " rename slot " + ev.slot
                            + " -> '" + ev.name + "' -> " + ok + "; "
                            + (relisted
                                ? "re-emitted 1005 on shipyard " + renameShipyardId
                                    + " so the FRAME DESIGNS list shows the new name immediately."
                                : "no open console tracked / feature off, list refreshes on next open."));
                        Ack(player, entityId, ev.id, ok);
                        continue;
                    }
                    Ack(player, entityId, ev.id, ok);
                    Console.WriteLine("[info] 1208: entity " + entityId + " rename slot " + ev.slot
                        + " -> '" + ev.name + "' -> " + ok + " (no such slot).");
                }
            }

            if (clientComponentUpdate.unloadSchematic != null)
            {
                foreach (UnloadSchematic ev in clientComponentUpdate.unloadSchematic)
                {
                    long shipyardId = ev.editorId.Id;
                    designs.Unload();
                    // Active=false so HasShipLoaded() goes false and Edit disables.
                    Send1206(player, shipyardId, u =>
                    {
                        u.SetActive(false);
                        u.SetModified(false);
                        u.SetEditorId(new EntityId(0));
                    });
                    Ack(player, entityId, ev.id, true);
                    Console.WriteLine("[info] 1208: entity " + entityId + " unload on shipyard " + shipyardId + ".");
                }
            }

            if (clientComponentUpdate.stopEditingSchematic != null)
            {
                foreach (StopEditingSchematic ev in clientComponentUpdate.stopEditingSchematic)
                {
                    long shipyardId = ev.editorId.Id;
                    designs.StopEditing();
                    // Leave editor input mode: clear editorId on both the player's 1207
                    // (ShipHullAgentVisualizer exits editor mode) and the shipyard's 1206
                    // (BeingEdited false -> the mesh zooms back out).
                    Send1207(player, entityId, u => u.SetEditorId(new EntityId(0)));
                    Send1206(player, shipyardId, u => u.SetEditorId(new EntityId(0)));

                    // Refresh the state the restored panel READS, so the console echo below
                    // rebuilds from current data: 1207 = the FRAME DESIGNS list, 1206 =
                    // Active + working hull + owner (StopEditing leaves the design LOADED, so
                    // Active stays true -> HasShipLoaded() true -> EDIT enabled + hull preview).
                    PushSchematics(player, entityId, designs);
                    PushEditorState(player, shipyardId, designs);

                    // REPOPULATE THE PANEL ON "Done" - the REAL fix.
                    //
                    // Done -> ShipHullEditorScreen.CloseScreen -> PopState<ShipyardEditorUIState>.
                    // That editor state lives on the SAME window layer (TabbedInventoryInterface)
                    // as the ship-build panel's MainInventoryUIState, so opening the editor POPS
                    // that state (MainInventoryUIState.ShouldPopState: same layer -> true), tearing
                    // the ShipCraftingUI down (Deactivate -> _leftPanelRoot inactive). Popping the
                    // editor does NOT re-push it, so the panel comes back with its left panel and
                    // content ROOTS still inactive = the empty brown panel.
                    //
                    // The exit's LoadSchematic ack only reaches ShipCraftingUIHelper.DoUpdateSchematic
                    // -> UpdateSchematicsList, which NEVER re-activates _leftPanelRoot nor re-runs
                    // ShipCraftingUI.Activate - that is why re-pushing 1207 alone (the previous fix)
                    // did nothing visible. The ONLY path that fully rebuilds the panel is
                    // CraftingStationBehaviour.OnStartInteraction (1005 PlayerStartCrafting) ->
                    // PushState(MainInventoryUIState.ShipCraft) -> ShowCraftingModule ->
                    // SetCraftingDataTemplate + Activate (leftPanelRoot on, FRAME DESIGNS + hull
                    // preview). That is EXACTLY what the Tab + re-interact workaround triggers.
                    //
                    // So we reproduce the interact-open signal server-side: re-emit the shipyard's
                    // 1005 PlayerStartCrafting to THIS player only. OpenShipyardConsole is the same
                    // call the interact handler uses (InteractAgentState_Handler), so Done now
                    // restores a fully populated panel without the manual workaround.
                    bool reopened = WorldsAdriftRebornGameServer.Placement.OpenShipyardConsole(
                        player, entityId, shipyardId);
                    Console.WriteLine("[info] 1208: entity " + entityId + " stopped editing shipyard " + shipyardId
                        + "; re-pushed " + designs.Slots.Count + " FRAME DESIGN(s) and "
                        + (reopened ? "re-emitted 1005 PlayerStartCrafting so the panel fully repopulates."
                                    : "could NOT re-open the console (not a placed shipyard / feature off)."));
                }
            }
        }

        /// <summary>
        /// Push the full editor state onto the shipyard's 1206 for the acting peer:
        /// Active + the current working hull + slot + owner. This is what turns
        /// HasShipLoaded() true and feeds the mesh.
        /// </summary>
        private static void PushEditorState(ENetPeerHandle player, long shipyardId, PlayerShipDesigns designs)
        {
            byte[] hull = designs.WorkingHull ?? new byte[0];
            // ownerPlayerId MUST equal the client's LocalPlayer.PlayerId (1086 PlayerName
            // field2, served from LocalPlayerIdentity) or SAVE/RESET stay greyed
            // (ShipCraftingUIHelper gates them on GetOwnerId() == LocalPlayer.PlayerId).
            // The 1206 is pushed per-peer to the acting player, so treating the editing
            // player as the owner is correct.
            string ownerId = LocalPlayerIdentity.PlayerId;
            int slot = designs.LoadedSlot < 0 ? 0 : designs.LoadedSlot;
            Send1206(player, shipyardId, u =>
            {
                u.SetActive(designs.Active);
                u.SetModified(designs.Modified);
                u.SetHullData(hull);
                u.SetSlotId(slot);
                u.SetHasDirectAccess(true);
                u.SetOwnerPlayerId(ownerId);
            });
        }

        /// <summary>Re-serve the player's FRAME DESIGNS list on 1207 (after a save/rename).</summary>
        private static void PushSchematics(ENetPeerHandle player, long playerEntityId, PlayerShipDesigns designs)
        {
            Improbable.Collections.List<ShipHullSchematicData> list =
                new Improbable.Collections.List<ShipHullSchematicData>();
            foreach (ShipDesignSlot slot in designs.Slots)
            {
                list.Add(new ShipHullSchematicData(
                    (byte[])slot.Data.Clone(), slot.Name, slot.BeamsLength,
                    slot.NumberOfDecks, slot.ClientSchematicsIdJson, slot.Uuid));
            }
            Send1207(player, playerEntityId, u => u.SetSchematics(list));
        }

        /// <summary>Ack a requestId on 1207 so the client's pending reply resolves.</summary>
        private static void Ack(ENetPeerHandle player, long playerEntityId, int requestId, bool success)
        {
            Send1207(player, playerEntityId,
                u => u.AddRequestResponse(new ShipHullAgentRequestResponse(requestId, success)));
        }

        private static void Send1207(ENetPeerHandle player, long playerEntityId,
            Action<ShipHullAgentState.Update> build)
        {
            ShipHullAgentState.Update update = new ShipHullAgentState.Update();
            build(update);
            SendOPHelper.SendComponentUpdateOp(player, playerEntityId,
                new List<uint> { 1207 }, new List<object> { update });
        }

        private static void Send1206(ENetPeerHandle player, long shipyardEntityId,
            Action<ShipHullEditorState.Update> build)
        {
            ShipHullEditorState.Update update = new ShipHullEditorState.Update();
            build(update);
            SendOPHelper.SendComponentUpdateOp(player, shipyardEntityId,
                new List<uint> { 1206 }, new List<object> { update });
        }
    }
}

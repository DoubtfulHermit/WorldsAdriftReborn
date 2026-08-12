using System;
using System.Collections.Generic;
using Bossa.Travellers.Craftingstation;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Crafting;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 1270 PlayerShipBlueprintInteractionState - "refresh the ship-build lists".
     *
     * This is the reply half that KILLS THE LOADING SPINNER on the placed-shipyard
     * build UI. The FRAME DESIGNS + SHIP BLUEPRINTS panel sits under a full-panel
     * LoadingInputBlocker (ShipSchematicsList._loadingInputBlocker) bound to the 1274
     * GsimShipBlueprintInteractionState.Busy property. On open,
     * ShipCraftingUI.Activate() -> TriggerShipBlueprintsRefresh() sets Busy TRUE
     * locally and publishes a RefreshBlueprints event on THIS component (1270), the
     * player's own client->server writer. Real SpatialOS answered on 1274 with the
     * catalogue and Busy=false; our server had no reply, so the blocker spun forever.
     *
     * BUSY IS PER-COMMAND, NOT PER-REFRESH. The client wraps EVERY 1270 command in
     * LockOnBusyState: it sets the client-local BusyModel TRUE and waits for a 1274
     * BusyUpdated event to clear it. Both LoadingInputBlockers (the left list and the
     * centre overlay) bind that same BusyModel. So a reply is owed to ANY command, not
     * only RefreshBlueprints. The bug this fixes: selecting a hull frame fires
     * TriggerSetBlueprintId(None) (hulls and blueprints are mutually exclusive), which
     * locked BusyModel; with no reply it stayed true forever and ate EDIT/SAVE/etc. Now
     * every command earns a Busy=false. The blueprint LIST is only re-seeded (empty) on
     * an actual refresh, so a non-refresh command clears Busy without churning the list.
     *
     * WIRE DETAIL: BusyUpdated fires only if field tag 2 is PRESENT
     * (Field2BusySpecified => HasValue). SetBusy(false) sets the nullable so tag 2 is
     * emitted; a protobuf default-dropped false would be silently ignored.
     *
     * 1270 is granted authoritative on + injected into the player only when the
     * shipyard build UI is wired (MirrorSendPolicy.ShipBuildUi*, gated with placement),
     * so this handler only ever runs for a peer that legitimately holds the 1270 writer.
     *
     * MULTIPLAYER SAFETY: per-player and event-driven. The reply goes ONLY to the
     * peer that sent the command, ONLY about that peer's own player entity, and ONLY
     * when a command arrives - never per frame. 1270 (and 1208) are filtered out of the
     * raw cross-entity relay (MirrorSendPolicy.IsRelayedToOtherPlayers), so nothing is
     * re-addressed to another player's mirror. No high-rate reliably-relayed component
     * is introduced.
     */
    [RegisterComponentUpdateHandler]
    internal class PlayerShipBlueprintInteractionState_Handler
        : IComponentUpdateHandler<PlayerShipBlueprintInteractionState,
            PlayerShipBlueprintInteractionState.Update, PlayerShipBlueprintInteractionState.Data>
    {
        public PlayerShipBlueprintInteractionState_Handler() { Init(1270); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            PlayerShipBlueprintInteractionState.Update clientComponentUpdate,
            PlayerShipBlueprintInteractionState.Data serverComponentData)
        {
            // Only the sender's OWN entity: 1270 rides the player's own blueprint
            // writer, so entityId must be the player. Without this a modified client
            // could address a 1270 to another avatar's entity.
            ulong peerId = PeerIdentity.IdOf(player);
            if (!WorldsAdriftRebornGameServer.Players.Owns(peerId, entityId))
            {
                Console.WriteLine("[warning] 1270 update for entity " + entityId + " from a peer that owns "
                    + WorldsAdriftRebornGameServer.Players.EntityOf(peerId) + ", ignoring.");
                return;
            }

            ShipBlueprintInteraction.BlueprintCommandCounts counts =
                new ShipBlueprintInteraction.BlueprintCommandCounts(
                    clientComponentUpdate.addItem?.Count ?? 0,
                    clientComponentUpdate.returnItem?.Count ?? 0,
                    clientComponentUpdate.startCrafting?.Count ?? 0,
                    clientComponentUpdate.setBlueprintId?.Count ?? 0,
                    clientComponentUpdate.refreshBlueprints?.Count ?? 0,
                    clientComponentUpdate.saveBlueprint?.Count ?? 0,
                    clientComponentUpdate.renameBlueprint?.Count ?? 0,
                    clientComponentUpdate.deleteBlueprint?.Count ?? 0,
                    clientComponentUpdate.autofillBlueprint?.Count ?? 0,
                    clientComponentUpdate.returnAllItems?.Count ?? 0,
                    clientComponentUpdate.setSchematicEnabled?.Count ?? 0);

            if (!ShipBlueprintInteraction.ShouldReplyBusyFalse(counts))
            {
                // An empty 1270 update carries no LockOnBusyState command, so there is no
                // BusyModel to clear. Nothing to do.
                return;
            }

            bool isRefresh = ShipBlueprintInteraction.ShouldReplyToRefresh(counts.RefreshBlueprints);

            // ---- SAVE: add each saved design to the player's blueprint catalogue so the
            // SHIP BLUEPRINTS list grows. SaveBlueprint(targetShipyard, newId) - newId is
            // the blueprint name. The list is (re-)pushed below on save (or refresh).
            Multiplayer.Crafting.PlayerShipBlueprints catalog =
                Multiplayer.Crafting.ShipBlueprintCatalogStore.For(entityId);
            if (counts.SaveBlueprint > 0 && clientComponentUpdate.saveBlueprint != null)
            {
                foreach (SaveBlueprint save in clientComponentUpdate.saveBlueprint)
                {
                    if (catalog.Save(save.newId))
                    {
                        Console.WriteLine("[info] 1270: entity " + entityId
                            + " saved blueprint \"" + save.newId + "\".");
                    }
                }
            }

            // ---- SELECT: populate 1271 on the TARGET SHIPYARD with the selected
            // blueprint's cost bill. SetShipBlueprint(targetEntity=shipyard, blueprintId).
            // A blueprintId with a value -> push the recipe's schematic rows; the None
            // case (the client fires SetBlueprintId(None) when a hull frame is selected,
            // hulls and blueprints being mutually exclusive) -> push an EMPTY 1271 so a
            // previously selected blueprint's cost does not linger. 1271 lives on the
            // shipyard, which the player has interest in while the build UI is open, so
            // the update reaches this peer and drives ShipBlueprintCraftingBehaviour's
            // SchematicsUpdated. Per-shipyard, event-driven, sent only to this peer.
            if (counts.SetBlueprintId > 0 && clientComponentUpdate.setBlueprintId != null)
            {
                foreach (SetShipBlueprint select in clientComponentUpdate.setBlueprintId)
                {
                    long shipyardId = select.targetEntity.Id;

                    // TEARDOWN GUARD (bug F3): re-selecting or clearing a blueprint drops
                    // the current build. If that build had materials LOADED they were
                    // physically removed from the bag at reserve time, so they must be
                    // returned BEFORE the build is replaced/cleared or they are lost - the
                    // server may not trust the client's own can't-switch guard (a modified
                    // or desynced client bypasses it). A build MID-CRAFT is not switchable:
                    // its materials are consumed for real and abandoning it would orphan the
                    // running timer, so the switch is refused (mirrors the client).
                    ShipBlueprintBuild? existing = ShipBlueprintBuildStore.Get(shipyardId, entityId);
                    if (existing != null && existing.IsCrafting)
                    {
                        Console.WriteLine("[warning] 1270 SetBlueprintId on shipyard " + shipyardId
                            + " while a build is CRAFTING; refusing the switch so the running craft"
                            + " is not abandoned (entity " + entityId + ").");
                        continue;
                    }
                    if (existing != null)
                    {
                        int drained = ShipBuildTeardown.DrainBuildBackToInventory(entityId, existing);
                        if (drained > 0)
                        {
                            Console.WriteLine("[info] 1270 SetBlueprintId on shipyard " + shipyardId
                                + ": returned " + drained + " loaded material(s) to entity " + entityId
                                + " before switching/clearing the blueprint.");
                        }
                    }

                    if (select.blueprintId.HasValue)
                    {
                        // TEST recipe (ShipBlueprintRecipe.TestMakeshiftShip) - authored
                        // conservative bill; real numbers get swapped in the recipe module.
                        // Phase 2: a LIVE build is created and stored under (shipyard, this
                        // player) so the subsequent add/return/autofill/craft events on the
                        // shared shipyard find THIS player's fill state, not another's.
                        ShipBlueprintRecipe recipe = ShipBlueprintRecipe.TestMakeshiftShip();
                        ShipBlueprintBuild build = new ShipBlueprintBuild(select.blueprintId.Value, recipe);
                        ShipBlueprintBuildStore.Set(shipyardId, entityId, build);
                        PushCrafting(player, shipyardId, build);
                        Console.WriteLine("[info] 1270: entity " + entityId + " selected blueprint \""
                            + select.blueprintId.Value + "\"; stored build + pushed 1271 on shipyard " + shipyardId
                            + " with " + build.Rows.Count + " schematic row(s).");
                    }
                    else
                    {
                        // Blueprint cleared (hull frame selected): drop the build and push
                        // an empty resting 1271 so a previous cost does not linger.
                        ShipBlueprintBuildStore.Clear(shipyardId, entityId);
                        ShipBlueprintCraftingState.Update crafting = new ShipBlueprintCraftingState.Update();
                        crafting.SetBlueprintId(new Improbable.Collections.Option<string>());
                        crafting.SetSchematics(
                            new Improbable.Collections.List<ShipBlueprintSchematic>());
                        crafting.SetCraftingTime(0);
                        crafting.SetIsCrafting(false);
                        SendOPHelper.SendComponentUpdateOp(player, shipyardId,
                            new List<uint> { 1271 },
                            new List<object> { crafting });
                        Console.WriteLine("[info] 1270: entity " + entityId
                            + " cleared blueprint selection; pushed empty 1271 on shipyard " + shipyardId + ".");
                    }
                }
            }

            // ---- MATERIAL LOADING + CRAFT (Phase 2). Each event targets the shipyard;
            // the build lives per (shipyard, this player). Every transaction is
            // server-authoritative: the client sends only an item id, the amount is read
            // from the server's own inventory. After a change we push the authoritative
            // inventory (the client un-greys only on a 1081 InventoryPush) and re-push
            // 1271 to THIS peer so the shipyard's material view updates for this player
            // alone. Busy is cleared for all of them by the single 1274 reply below.

            // ADD: drag an inventory item into a material slot.
            if (counts.AddItem > 0 && clientComponentUpdate.addItem != null)
            {
                foreach (AddItemToShipBlueprint add in clientComponentUpdate.addItem)
                {
                    long shipyardId = add.targetEntity.Id;
                    ShipBlueprintBuild? build = ShipBlueprintBuildStore.Get(shipyardId, entityId);
                    if (build == null)
                    {
                        Console.WriteLine("[warning] 1270 AddItem on shipyard " + shipyardId
                            + " but entity " + entityId + " has no selected blueprint; ignoring.");
                        continue;
                    }
                    InventoryModel inventory = InventoryService.ForEntity(entityId);
                    AddItemOutcome outcome = ShipBlueprintTransaction.AddItem(
                        build, inventory, add.schematicSlotIndex, add.materialSlotIndex, add.itemId);
                    if (outcome == AddItemOutcome.Added)
                    {
                        InventoryPush.Push(entityId, "reserved item " + add.itemId + " into ship blueprint");
                        PushCrafting(player, shipyardId, build);
                    }
                    else
                    {
                        // The client greys its inventory optimistically the instant the
                        // item is dragged out, and un-greys ONLY when an authoritative
                        // 1081 inventory push arrives. A rejected item (type/quality
                        // mismatch, slot full, not owned) must STILL echo the unchanged
                        // inventory back, or the whole inventory panel hangs blacked-out.
                        InventoryPush.Push(entityId, "rejected item " + add.itemId
                            + " into ship blueprint (" + outcome + ")");
                    }
                    Console.WriteLine("[info] 1270 AddItem(item=" + add.itemId + ", row="
                        + add.schematicSlotIndex + ", slot=" + add.materialSlotIndex + ") on shipyard "
                        + shipyardId + " -> " + outcome + ".");
                }
            }

            // RETURN: click a filled slot with an empty hand to give the materials back.
            if (counts.ReturnItem > 0 && clientComponentUpdate.returnItem != null)
            {
                foreach (ReturnItemFromShipBlueprint ret in clientComponentUpdate.returnItem)
                {
                    long shipyardId = ret.targetEntity.Id;
                    ShipBlueprintBuild? build = ShipBlueprintBuildStore.Get(shipyardId, entityId);
                    if (build == null)
                    {
                        continue;
                    }
                    InventoryModel inventory = InventoryService.ForEntity(entityId);
                    ReturnItemOutcome outcome = ShipBlueprintTransaction.ReturnItem(
                        build, inventory, ret.schematicSlotIndex, ret.materialSlotIndex);
                    if (outcome == ReturnItemOutcome.Returned)
                    {
                        InventoryPush.Push(entityId, "returned ship-blueprint materials");
                        PushCrafting(player, shipyardId, build);
                    }
                    Console.WriteLine("[info] 1270 ReturnItem(row=" + ret.schematicSlotIndex + ", slot="
                        + ret.materialSlotIndex + ") on shipyard " + shipyardId + " -> " + outcome + ".");
                }
            }

            // AUTOFILL: pull matching items for every enabled row until satisfied/empty.
            if (counts.AutofillBlueprint > 0 && clientComponentUpdate.autofillBlueprint != null)
            {
                foreach (AutoFillBlueprint fill in clientComponentUpdate.autofillBlueprint)
                {
                    long shipyardId = fill.targetShipyard.Id;
                    ShipBlueprintBuild? build = ShipBlueprintBuildStore.Get(shipyardId, entityId);
                    if (build == null)
                    {
                        continue;
                    }
                    InventoryModel inventory = InventoryService.ForEntity(entityId);
                    int loaded = ShipBlueprintTransaction.AutoFill(build, inventory);
                    if (loaded > 0)
                    {
                        InventoryPush.Push(entityId, "autofilled " + loaded + " ship-blueprint material(s)");
                        PushCrafting(player, shipyardId, build);
                    }
                    Console.WriteLine("[info] 1270 AutoFill on shipyard " + shipyardId + " -> reserved "
                        + loaded + " item(s).");
                }
            }

            // RETURN ALL: empty every slot back into the inventory.
            if (counts.ReturnAllItems > 0 && clientComponentUpdate.returnAllItems != null)
            {
                foreach (ReturnAllItems all in clientComponentUpdate.returnAllItems)
                {
                    long shipyardId = all.targetShipyard.Id;
                    ShipBlueprintBuild? build = ShipBlueprintBuildStore.Get(shipyardId, entityId);
                    if (build == null)
                    {
                        continue;
                    }
                    InventoryModel inventory = InventoryService.ForEntity(entityId);
                    int returned = ShipBlueprintTransaction.ReturnAll(build, inventory);
                    if (returned > 0)
                    {
                        InventoryPush.Push(entityId, "returned all " + returned + " ship-blueprint material(s)");
                        PushCrafting(player, shipyardId, build);
                    }
                    Console.WriteLine("[info] 1270 ReturnAll on shipyard " + shipyardId + " -> returned "
                        + returned + " item(s).");
                }
            }

            // ENABLE/DISABLE a schematic row (mandatory shipFrame/deck01 are refused).
            if (counts.SetSchematicEnabled > 0 && clientComponentUpdate.setSchematicEnabled != null)
            {
                foreach (SetSchematicEnabled toggle in clientComponentUpdate.setSchematicEnabled)
                {
                    long shipyardId = toggle.targetEntity.Id;
                    ShipBlueprintBuild? build = ShipBlueprintBuildStore.Get(shipyardId, entityId);
                    if (build == null)
                    {
                        continue;
                    }
                    SchematicRowBuild? row = build.RowAt(toggle.schematicSlotIndex);
                    bool changed = row != null && row.SetEnabled(toggle.enabled);
                    if (changed)
                    {
                        PushCrafting(player, shipyardId, build);
                    }
                    Console.WriteLine("[info] 1270 SetSchematicEnabled(row=" + toggle.schematicSlotIndex
                        + ", enabled=" + toggle.enabled + ") on shipyard " + shipyardId
                        + " -> " + (changed ? "applied" : "refused (mandatory or no such row)") + ".");
                }
            }

            // CRAFT: gate on all enabled rows filled, then start the timed build.
            if (counts.StartCrafting > 0 && clientComponentUpdate.startCrafting != null)
            {
                foreach (StartCraftingShipBlueprint craft in clientComponentUpdate.startCrafting)
                {
                    long shipyardId = craft.targetEntity.Id;
                    ShipBlueprintBuild? build = ShipBlueprintBuildStore.Get(shipyardId, entityId);
                    if (build == null)
                    {
                        Console.WriteLine("[warning] 1270 StartCrafting on shipyard " + shipyardId
                            + " but entity " + entityId + " has no selected blueprint; ignoring.");
                        continue;
                    }
                    // ONE SHIP PER SHIPYARD: refuse the craft if this yard already holds a
                    // built/docked ship (its 1205 DockedShipId is singular). Checked here
                    // so no materials are consumed and no timer starts; the current ship
                    // must be removed (undock trigger) before another can be built.
                    bool shipyardOccupied = Game.Crafting.BuiltShips.IsShipyardOccupied(shipyardId);
                    StartCraftOutcome outcome = ShipBlueprintTransaction.StartCraft(build, shipyardOccupied);
                    if (outcome == StartCraftOutcome.Started)
                    {
                        // isCrafting=true -> atomizer VFX on; start the server timer.
                        PushCrafting(player, shipyardId, build);
                        ShipBuildTimerService.Start(player, shipyardId, entityId, build);
                        Console.WriteLine("[info] 1270 StartCrafting on shipyard " + shipyardId
                            + " -> STARTED (" + build.CraftingTime + "s).");
                    }
                    else
                    {
                        // Not buildable: tell the client why (1274 error) and clear busy
                        // (the tail below clears busy for the whole batch anyway).
                        string message = outcome == StartCraftOutcome.MissingMaterials
                            ? "Blueprint is missing materials."
                            : outcome == StartCraftOutcome.NothingEnabled
                                ? "No schematics are enabled."
                                : outcome == StartCraftOutcome.ShipyardOccupied
                                    ? "This shipyard already has a ship docked. Remove it before building another."
                                    : "Blueprint is already crafting.";
                        GsimShipBlueprintInteractionState.Update err = new GsimShipBlueprintInteractionState.Update();
                        err.SetBusy(ShipBlueprintInteraction.RepliedBusy);
                        err.AddError(new ShipBlueprintErrorEvent(message));
                        SendOPHelper.SendComponentUpdateOp(player, entityId,
                            new List<uint> { 1274 }, new List<object> { err });
                        Console.WriteLine("[info] 1270 StartCrafting on shipyard " + shipyardId
                            + " -> BLOCKED (" + outcome + "): " + message);
                    }
                }
            }

            // Clear the spinner for ANY command. The client wraps every 1270 command in
            // LockOnBusyState (BusyModel=true) and clears it ONLY on a 1274 BusyUpdated
            // event, which fires ONLY if field tag 2 (Busy) is PRESENT on the wire
            // (Field2BusySpecified => HasValue). SetBusy(false) sets the nullable, so tag 2
            // is emitted - a protobuf default-dropped false would be silently ignored and
            // the blocker would never lift. Both LoadingInputBlockers (left list + centre
            // overlay) bind the same BusyModel, so this one reply clears both.
            //
            // The blueprint LIST is re-pushed on a refresh (panel open) OR a save (a new
            // entry must appear). Any other command - e.g. SetBlueprintId - clears Busy
            // WITHOUT touching the list model, so selecting a frame/blueprint does not
            // churn the list.
            bool pushList = ShipBlueprintInteraction.ShouldReplyWithBlueprintList(counts);
            GsimShipBlueprintInteractionState.Update reply = new GsimShipBlueprintInteractionState.Update();
            reply.SetBusy(ShipBlueprintInteraction.RepliedBusy);
            if (pushList)
            {
                Improbable.Collections.List<string> available = new Improbable.Collections.List<string>();
                foreach (string blueprintId in catalog.Available)
                {
                    available.Add(blueprintId);
                }
                reply.SetShipBlueprintList(new Improbable.Collections.Option<ShipBlueprintList>(
                    new ShipBlueprintList(available)));
            }

            SendOPHelper.SendComponentUpdateOp(player, entityId,
                new List<uint> { 1274 },
                new List<object> { reply });

            Console.WriteLine("[info] 1270: entity " + entityId + " command batch (locking="
                + counts.Locking + ", refresh=" + counts.RefreshBlueprints + ", setBlueprintId="
                + counts.SetBlueprintId + ", save=" + counts.SaveBlueprint + "); replied 1274 Busy="
                + ShipBlueprintInteraction.RepliedBusy
                + (pushList ? (" + ship-blueprint list (" + catalog.Available.Count + " entr(y/ies)).") : "."));

            if (!isRefresh)
            {
                return;
            }

            // TASK 4 - guarantee FRAME DESIGNS populates. A RefreshBlueprints fires when
            // the build UI OPENS (ShipCraftingUI.Activate -> TriggerShipBlueprintsRefresh),
            // so it is the reliable hook to (re)push the player's 1207 schematics list. The
            // client's SchematicsUpdated is a PropertyCallbackHandler and it is UNVERIFIED
            // whether it fires on the initial 1207 checkout value alone; pushing the list
            // here as an explicit UPDATE makes the event fire deterministically every time
            // the panel opens, so the FRAME DESIGNS list is never empty when it should not
            // be. Same entity (the player), same peer, event-driven - no relay, no per-frame.
            Bossa.Travellers.Items.ShipHullAgentState.Update schematicsPush =
                new Bossa.Travellers.Items.ShipHullAgentState.Update();
            Improbable.Collections.List<Bossa.Travellers.Items.ShipHullSchematicData> schematics =
                new Improbable.Collections.List<Bossa.Travellers.Items.ShipHullSchematicData>();
            Multiplayer.Ship.PlayerShipDesigns designs = Multiplayer.Ship.ShipDesignStore.For(entityId);
            foreach (Multiplayer.Ship.ShipDesignSlot slot in designs.Slots)
            {
                schematics.Add(new Bossa.Travellers.Items.ShipHullSchematicData(
                    (byte[])slot.Data.Clone(), slot.Name, slot.BeamsLength,
                    slot.NumberOfDecks, slot.ClientSchematicsIdJson, slot.Uuid));
            }
            schematicsPush.SetSchematics(schematics);
            SendOPHelper.SendComponentUpdateOp(player, entityId,
                new List<uint> { 1207 },
                new List<object> { schematicsPush });

            Console.WriteLine("[info] 1270: entity " + entityId + " pushed " + designs.Slots.Count
                + " FRAME DESIGN(s) on 1207 so SchematicsUpdated fires.");
        }

        /// <summary>
        /// Push a build's current material bill as a 1271 update on the shipyard, to
        /// THIS peer only. The shipyard is a shared entity, so the update must not be
        /// broadcast: each player's fill view is their own (keyed per player in the
        /// build store), and a broadcast would show one player another's materials.
        /// </summary>
        private static void PushCrafting(ENetPeerHandle player, long shipyardId, ShipBlueprintBuild build)
        {
            ShipBlueprintCraftingState.Update crafting = new ShipBlueprintCraftingState.Update();
            crafting.SetBlueprintId(new Improbable.Collections.Option<string>(build.BlueprintId));
            crafting.SetSchematics(Game.Crafting.ShipBlueprintSchematicMapper.ToSchematics(build));
            crafting.SetCraftingTime(build.CraftingTime);
            crafting.SetIsCrafting(build.IsCrafting);
            SendOPHelper.SendComponentUpdateOp(player, shipyardId,
                new List<uint> { 1271 },
                new List<object> { crafting });
        }
    }
}

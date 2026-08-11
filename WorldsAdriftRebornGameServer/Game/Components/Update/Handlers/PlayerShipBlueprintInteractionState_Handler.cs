using System;
using System.Collections.Generic;
using Bossa.Travellers.Craftingstation;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
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

            // Clear the spinner for ANY command. The client wraps every 1270 command in
            // LockOnBusyState (BusyModel=true) and clears it ONLY on a 1274 BusyUpdated
            // event, which fires ONLY if field tag 2 (Busy) is PRESENT on the wire
            // (Field2BusySpecified => HasValue). SetBusy(false) sets the nullable, so tag 2
            // is emitted - a protobuf default-dropped false would be silently ignored and
            // the blocker would never lift. Both LoadingInputBlockers (left list + centre
            // overlay) bind the same BusyModel, so this one reply clears both.
            //
            // The blueprint LIST is re-seeded (to empty) ONLY on an actual refresh (panel
            // open). A non-refresh command - e.g. the SetBlueprintId(None) the client fires
            // when a hull frame is selected, hulls and blueprints being mutually exclusive -
            // clears Busy WITHOUT touching the list model, so selecting a frame does not
            // churn the (empty) blueprint list.
            GsimShipBlueprintInteractionState.Update reply = new GsimShipBlueprintInteractionState.Update();
            reply.SetBusy(ShipBlueprintInteraction.RepliedBusy);
            if (isRefresh)
            {
                reply.SetShipBlueprintList(new Improbable.Collections.Option<ShipBlueprintList>());
            }

            SendOPHelper.SendComponentUpdateOp(player, entityId,
                new List<uint> { 1274 },
                new List<object> { reply });

            Console.WriteLine("[info] 1270: entity " + entityId + " command batch (locking="
                + counts.Locking + ", refresh=" + counts.RefreshBlueprints + ", setBlueprintId="
                + counts.SetBlueprintId + "); replied 1274 Busy=" + ShipBlueprintInteraction.RepliedBusy
                + (isRefresh ? " + empty ship-blueprint list." : "."));

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
    }
}

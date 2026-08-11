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
     * So on a RefreshBlueprints event we send the sender ONE 1274 update: Busy=false
     * + an empty ShipBlueprintList. Empty is the CORRECT list for a new player - the
     * spinner lifting onto an empty (but interactive) panel is the whole Phase 1 goal.
     * The other 1270 events (add/return item, save/rename/delete/autofill blueprint,
     * set-schematic-enabled) are later milestones and are intentionally ignored here.
     *
     * 1270 is granted authoritative on + injected into the player only when the
     * shipyard build UI is wired (MirrorSendPolicy.ShipBuildUi*, gated with placement),
     * so this handler only ever runs for a peer that legitimately holds the 1270 writer.
     *
     * MULTIPLAYER SAFETY: per-player and event-driven. The reply goes ONLY to the
     * peer that sent the refresh, ONLY about that peer's own player entity, and ONLY
     * when a RefreshBlueprints event arrives - never per frame. 1270 (and 1208) are
     * filtered out of the raw cross-entity relay (MirrorSendPolicy.IsRelayedToOtherPlayers),
     * so nothing is re-addressed to another player's mirror. No high-rate reliably-
     * relayed component is introduced.
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

            int refreshCount = clientComponentUpdate.refreshBlueprints?.Count ?? 0;
            if (!ShipBlueprintInteraction.ShouldReplyToRefresh(refreshCount))
            {
                // A non-refresh 1270 update (item add/return, save/rename/delete, ...)
                // is a later milestone; nothing to reply with, so drop it quietly.
                return;
            }

            // Clear the spinner: Busy=false + an empty (None) ShipBlueprintList. Setting
            // the list to None makes the client's serializer clear field 1, so the panel
            // renders an explicitly-empty catalogue rather than a stale one.
            GsimShipBlueprintInteractionState.Update reply = new GsimShipBlueprintInteractionState.Update();
            reply.SetBusy(ShipBlueprintInteraction.RepliedBusy);
            reply.SetShipBlueprintList(new Improbable.Collections.Option<ShipBlueprintList>());

            SendOPHelper.SendComponentUpdateOp(player, entityId,
                new List<uint> { 1274 },
                new List<object> { reply });

            Console.WriteLine("[info] 1270: entity " + entityId + " requested a blueprint refresh; replied 1274 Busy="
                + ShipBlueprintInteraction.RepliedBusy + " with an empty ship-blueprint list.");

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

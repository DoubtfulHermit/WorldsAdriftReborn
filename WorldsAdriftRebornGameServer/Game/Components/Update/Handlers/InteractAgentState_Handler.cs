using Bossa.Travellers.Interact;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Game.Placement;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 1211 InteractAgentState - the player's per-frame "what am I looking at, which
     * hotbar slot is selected, is the use key down" state.
     *
     * This handler exists for ONE reason: to be the NATIVE deploy trigger for
     * placement. The decompiled client has no dedicated "start placing" command -
     * the original game started placement from server-side item-use logic that is
     * not in the client. The closest observable client signal is 1211's
     * UseItemKeyPressed event: it fires exactly once on use-key-DOWN and carries the
     * selected hotbar slot (itemSlot == CurrentItemSlot, 0-7). So: when the player
     * has a shipyard on the selected hotbar slot and presses use, that is the deploy
     * trigger, and we send them 1019 StartPlacingItemEvent (via PlacementService).
     *
     * WHY THIS IS SAFE despite 1211 being a per-frame stream:
     *   - The handler reads ONLY the useItemKeyPressed event list and early-returns
     *     when it is empty, which is every frame except a key-down. The per-frame
     *     LookingAt/slot DATA is ignored (same shape as MultitoolSalvagerState's
     *     shotEvent handling). So the per-frame cost is one null/empty check.
     *   - It never relays anything and never mutates inventory; it only starts a
     *     preview on the sender's OWN client.
     *   - It is a no-op unless WAREBORN_PLACEMENT=1.
     *
     * RESIDUAL RISK (needs a live client to settle, documented in the report): that
     * UseItemKeyPressed actually fires for a placeable non-tool hotbar item, and that
     * the client's CurrentItemSlot indexing matches the server's HotBarSlotNum. If it
     * does not, the debug file trigger (WAREBORN_PLACEMENT_FILE) drives the exact
     * same StartPlacing path and proves the rest of the pipeline regardless.
     */
    [RegisterComponentUpdateHandler]
    internal class InteractAgentState_Handler : IComponentUpdateHandler<InteractAgentState, InteractAgentState.Update, InteractAgentState.Data>
    {
        public InteractAgentState_Handler() { Init(1211); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            InteractAgentState.Update clientComponentUpdate, InteractAgentState.Data serverComponentData)
        {
            if (!WorldsAdriftRebornGameServer.Placement.Enabled)
            {
                return;
            }

            // A DELTA: the vast majority of 1211 packets carry only look/slot data and
            // no event. Read the event list straight off the update and get out fast
            // when there is nothing to act on - this runs at frame rate.
            Improbable.Collections.List<UseItemKeyPressed>? presses = clientComponentUpdate.useItemKeyPressed;
            if (presses == null || presses.Count == 0)
            {
                return;
            }

            // Only the sender's OWN entity: 1211 is the player's own interact state.
            ulong peerId = PeerIdentity.IdOf(player);
            if (!WorldsAdriftRebornGameServer.Players.Owns(peerId, entityId))
            {
                return;
            }

            // Already mid-placement: ignore further use-presses until it resolves or
            // the session times out (PlacementService guards this too, but skipping
            // the inventory lookup keeps the frame cheap).
            if (WorldsAdriftRebornGameServer.Placement.IsPlacing(entityId))
            {
                return;
            }

            foreach (UseItemKeyPressed press in presses)
            {
                int slot = press.itemSlot; // CurrentItemSlot, the selected hotbar slot 0-7
                InventoryModel model = InventoryService.ForEntity(entityId);
                InventoryItem? selected = model.OnHotBar(slot);

                if (selected != null
                    && Multiplayer.Placement.Deployables.IsDeployable(selected.ItemTypeId))
                {
                    Console.WriteLine("[info] placement: entity " + entityId + " pressed use on a '"
                        + selected.ItemTypeId + "' in hotbar slot " + slot + " (item " + selected.ItemId
                        + "); starting placement.");
                    WorldsAdriftRebornGameServer.Placement.StartPlacing(player, entityId, selected.ItemId, selected.ItemTypeId);
                    return;
                }
            }
        }
    }
}

using Bossa.Travellers.Inventory;
using Improbable.Worker.Internal;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * The inventory request bus: fifteen client events, one server-owned model,
     * one push.
     *
     * WHAT THIS USED TO DO, AND WHY IT MATTERED. Exactly one of the fifteen
     * events (equipWearable) mutated anything; six logged their fields and
     * dropped the request; eight were not mentioned at all. Every one of them
     * then fell through to a 1082 echo. That echo is not an answer: the client
     * sets IsWaitingForServer before it sends and clears it in exactly one
     * place, inside LoadInventory, which runs only off a 1081 update. There is
     * no timeout and no rollback. So THE FIRST TIME A PLAYER DRAGGED AN ITEM
     * THEIR INVENTORY PANEL GREYED OUT PERMANENTLY - not on an edge case, on the
     * first thing anybody does.
     *
     * The fix is structural rather than per-event: this method cannot return
     * without pushing 1081 if the client asked for anything. Handled events
     * mutate the model first; unhandled ones push the unchanged state, which
     * makes the item snap back and the panel come alive. A future event
     * implementation is then a mutation, not a mutation plus a reminder to
     * answer.
     *
     * equipWearable was the template for all of this and is kept, moved behind
     * the same seam so that its 1280 write-back is derived rather than
     * hand-written (the hand-written single-element array is why equipping a
     * second garment used to replace the first).
     */
    [RegisterComponentUpdateHandler]
    internal class InventoryModificationState_Handler : IComponentUpdateHandler<InventoryModificationState, InventoryModificationState.Update, InventoryModificationState.Data>
    {
        public InventoryModificationState_Handler() { Init(1082); }
        protected override void Init( uint ComponentId )
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate( ENetPeerHandle player, long entityId, InventoryModificationState.Update clientComponentUpdate, InventoryModificationState.Data serverComponentData)
        {
            clientComponentUpdate.ApplyTo(serverComponentData);

            InventoryModificationState.Update serverComponentUpdate = (InventoryModificationState.Update)serverComponentData.ToUpdate();

            // Ownership gate. Without it one client can rearrange another
            // player's inventory by naming their entity id, and the whole
            // inbound update path has no such check of its own.
            if (!WorldsAdriftRebornGameServer.Players.Owns(PeerIdentity.IdOf(player), entityId))
            {
                Console.WriteLine("[warning] 1082 request for entity " + entityId
                    + " from a peer that does not own it, ignoring.");
                return;
            }

            InventoryModel model = InventoryService.ForEntity(entityId);

            int requests = 0;

            requests += HandleEquipWearable(clientComponentUpdate, model);
            requests += HandleUnequipWearable(clientComponentUpdate, model);
            requests += HandleMoveItem(clientComponentUpdate, model);
            requests += HandleAssignToHotBar(clientComponentUpdate, model);
            requests += HandleRemoveFromHotBar(clientComponentUpdate, model);
            requests += LogUnimplemented(clientComponentUpdate);

            // The 1082 echo the old code ended on. Kept because the client's
            // event stream is its own, and harmless - but it answers nothing, so
            // it is no longer the last thing that happens.
            SendOPHelper.SendComponentUpdateOp(player, entityId, new List<uint> { ComponentId }, new List<object> { serverComponentUpdate });

            if (requests == 0)
            {
                return;
            }

            // THE LINE THAT KEEPS THE PANEL ALIVE. Unconditional on any request
            // arriving, accepted or refused.
            InventoryPush.Push(entityId, requests + " inventory request(s)");
        }

        private static int HandleEquipWearable( InventoryModificationState.Update update, InventoryModel model )
        {
            for (int j = 0; j < update.equipWearable.Count; j++)
            {
                int itemId = update.equipWearable[j].itemId;
                InventoryItem? item = model.ById(itemId);

                if (item == null)
                {
                    Console.WriteLine("[warning] equipWearable for unknown item " + itemId + ", refusing.");
                    continue;
                }

                // The slot comes from the item database, exactly as the original
                // handler took it from ItemHelper.GetItem(...).characterSlot -
                // the client cannot be trusted to name the slot, and an item
                // whose type is not wearable has characterSlot "None", which
                // InventoryPolicy refuses.
                string slot = InventoryWire.CharacterSlotOf(item.ItemTypeId);

                if (!InventoryPolicy.TryEquip(model, itemId, slot))
                {
                    Console.WriteLine("[info] refused equipWearable of item " + itemId
                        + " (" + item.ItemTypeId + ") into slot '" + slot + "'.");
                }
            }

            return update.equipWearable.Count;
        }

        private static int HandleUnequipWearable( InventoryModificationState.Update update, InventoryModel model )
        {
            // Not handled at all before, which made gear a ONE-WAY DOOR: a
            // player could put a hat on and never take it off.
            for (int j = 0; j < update.unequipWearable.Count; j++)
            {
                int itemId = update.unequipWearable[j].itemId;

                if (!InventoryPolicy.TryUnequip(model, itemId, InventoryWire.Footprints))
                {
                    Console.WriteLine("[info] refused unequipWearable of item " + itemId
                        + " (not worn, or no free cell to put it in).");
                }
            }

            return update.unequipWearable.Count;
        }

        private static int HandleMoveItem( InventoryModificationState.Update update, InventoryModel model )
        {
            for (int j = 0; j < update.moveItem.Count; j++)
            {
                MoveItem move = update.moveItem[j];

                if (move.isLockboxItem)
                {
                    // The stash is a category list of fixed tiles, not a grid;
                    // its x/y are parsed and never used, so a move within it is
                    // meaningless. Answered by the push below like everything
                    // else.
                    Console.WriteLine("[info] ignoring moveItem inside the stash (item " + move.itemId + ").");
                    continue;
                }

                if (!InventoryPolicy.TryMove(model, move.itemId, move.xPos, move.yPos, move.rotate, InventoryWire.Footprints))
                {
                    // Refused, not dropped: the push that follows re-states the
                    // authoritative position and the item visibly snaps back.
                    Console.WriteLine("[info] refused moveItem of item " + move.itemId
                        + " to (" + move.xPos + "," + move.yPos + ") rotate=" + move.rotate + ".");
                }
            }

            return update.moveItem.Count;
        }

        private static int HandleAssignToHotBar( InventoryModificationState.Update update, InventoryModel model )
        {
            for (int j = 0; j < update.assignToHotBar.Count; j++)
            {
                AssignToHotBar assign = update.assignToHotBar[j];

                if (!InventoryPolicy.TryAssignToHotBar(model, assign.itemId, assign.slotIndex))
                {
                    Console.WriteLine("[info] refused assignToHotBar of item " + assign.itemId
                        + " to slot " + assign.slotIndex + " (0-3 are the fixed gauntlets).");
                }
            }

            return update.assignToHotBar.Count;
        }

        private static int HandleRemoveFromHotBar( InventoryModificationState.Update update, InventoryModel model )
        {
            for (int j = 0; j < update.removeFromHotBar.Count; j++)
            {
                RemoveFromHotBar remove = update.removeFromHotBar[j];

                if (!InventoryPolicy.TryRemoveFromHotBar(model, remove.slotIndex))
                {
                    Console.WriteLine("[info] refused removeFromHotBar of slot " + remove.slotIndex + ".");
                }
            }

            return update.removeFromHotBar.Count;
        }

        /// <summary>
        /// The events this server does not implement yet.
        ///
        /// They are counted, not silently skipped, because the count is what
        /// triggers the push - and the push is what stops an unimplemented event
        /// from bricking the panel. Each of these needs a system that does not
        /// exist yet (a second inventory to move between, a crafting model, a
        /// world entity to drop an item onto), so refusing them is honest;
        /// refusing them SILENTLY was not.
        /// </summary>
        private static int LogUnimplemented( InventoryModificationState.Update update )
        {
            int count = 0;

            count += Note(update.removeItem.Count, "removeItem",
                "there is no dropped-item entity to move it to, so honouring it would delete the item");
            count += Note(update.craftItem.Count, "craftItem", "no crafting model");
            count += Note(update.crossInventoryMoveItem.Count, "crossInventoryMoveItem",
                "no second inventory exists yet");
            count += Note(update.splitItemStack.Count, "splitItemStack", "no stacking model");
            count += Note(update.moveAll.Count, "moveAll", "no second inventory exists yet");
            count += Note(update.equipTool.Count, "equipTool", "tool slots are hardcoded client-side");
            count += Note(update.tryToConsume.Count, "tryToConsume", "no consumable effects");
            count += Note(update.tryToLearn.Count, "tryToLearn", "no schematic model");
            count += Note(update.installCipher.Count, "installCipher", "no cipher model");
            count += Note(update.destroyCipher.Count, "destroyCipher", "no cipher model");

            return count;
        }

        private static int Note( int count, string name, string why )
        {
            if (count > 0)
            {
                Console.WriteLine("[info] refusing " + count + " " + name + " request(s): " + why
                    + ". The inventory will be re-pushed so the panel does not stick.");
            }

            return count;
        }
    }
}

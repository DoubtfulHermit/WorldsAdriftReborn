using Bossa.Travellers.Inventory;
using Improbable.Worker.Internal;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Game.Crafting;
using WorldsAdriftRebornGameServer.Game.Loot;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Loot;
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
            requests += HandleCrossInventoryMove(clientComponentUpdate, entityId);
            requests += HandleMoveAll(clientComponentUpdate, entityId);
            requests += HandleTryToConsume(clientComponentUpdate, entityId);
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
        /// TAKING LOOT OUT OF A CHEST, and putting something back in.
        ///
        /// The event carries BOTH entity ids, so the player's own entity is only one
        /// end of it. That is why this method takes <paramref name="playerEntityId"/>
        /// separately from the ids on the wire: the ownership gate at the top of
        /// HandleUpdate proves the SENDER owns the entity the 1082 arrived on, and
        /// this method then proves that entity is one of the two ends of every move
        /// it performs. Without that second check a peer could name two inventories
        /// neither of which is theirs and launder items between other people's chests.
        ///
        /// The OTHER end must be a loot container. Not "any entity with an
        /// inventory": <c>InventoryService.ForEntity</c> will happily conjure a
        /// starter-kit inventory for any id ever passed to it, so an unchecked id
        /// here is an infinite item source. A container is the only non-player
        /// inventory this server has, and it says so out loud.
        ///
        /// Both models are pushed afterwards by the caller for the player's side; the
        /// CONTAINER's 1081 is pushed here, because the caller only knows about the
        /// sender's entity and a chest whose 1081 is not re-pushed keeps showing the
        /// item that just left it.
        /// </summary>
        private static int HandleCrossInventoryMove(
            InventoryModificationState.Update update, long playerEntityId )
        {
            for (int j = 0; j < update.crossInventoryMoveItem.Count; j++)
            {
                CrossInventoryMoveItem move = update.crossInventoryMoveItem[j];
                long source = move.srcInventoryEntityId.Id;
                long destination = move.destInventoryEntityId.Id;

                if (!IsPlayerAndContainer(playerEntityId, source, destination, out long containerId))
                {
                    continue;
                }

                LootStock.Ensure(containerId);
                // The ship-container twin. Whichever kind this is, the other call is
                // a no-op - and one of the two MUST run before ForEntity below, or a
                // container that has never been checked out binds to the player
                // starter kit and the "move" lands in a bag of gauntlets.
                ShipContainerService.Ensure(containerId);

                bool takingOut = source == containerId;
                InventoryModel from = InventoryService.ForEntity(source);
                InventoryModel to = InventoryService.ForEntity(destination);

                CrossMoveOutcome outcome = CrossInventoryPolicy.TryMove(
                    from, to, move.srcItemId, () => InventoryService.NextItemId(destination),
                    move.xPos, move.yPos, move.rotate, InventoryWire.Footprints);

                Console.WriteLine("[loot] cross-inventory move of item " + move.srcItemId
                    + " " + (takingOut ? "OUT OF" : "INTO") + " container " + containerId
                    + " -> " + outcome + ".");

                if (outcome == CrossMoveOutcome.Moved)
                {
                    // The container's own panel. The player's is pushed by the
                    // unconditional push at the end of HandleUpdate.
                    InventoryPush.Push(containerId, "cross-inventory move");
                }
            }

            return update.crossInventoryMoveItem.Count;
        }

        /// <summary>
        /// The "take all" button. Same gates as the single move, and deliberately
        /// tolerant of a full bag: it moves what fits and leaves the rest, because a
        /// player who runs out of room mid-transfer wants the half they got, not a
        /// refusal.
        /// </summary>
        private static int HandleMoveAll(
            InventoryModificationState.Update update, long playerEntityId )
        {
            for (int j = 0; j < update.moveAll.Count; j++)
            {
                MoveAll all = update.moveAll[j];
                long source = all.srcInventoryEntityId.Id;
                long destination = all.destInventoryEntityId.Id;

                if (!IsPlayerAndContainer(playerEntityId, source, destination, out long containerId))
                {
                    continue;
                }

                LootStock.Ensure(containerId);
                // The ship-container twin. Whichever kind this is, the other call is
                // a no-op - and one of the two MUST run before ForEntity below, or a
                // container that has never been checked out binds to the player
                // starter kit and the "move" lands in a bag of gauntlets.
                ShipContainerService.Ensure(containerId);

                InventoryModel from = InventoryService.ForEntity(source);
                InventoryModel to = InventoryService.ForEntity(destination);

                int moved = CrossInventoryPolicy.MoveAll(
                    from, to, () => InventoryService.NextItemId(destination), InventoryWire.Footprints);

                Console.WriteLine("[loot] moveAll " + source + " -> " + destination
                    + " (container " + containerId + "): " + moved + " item(s) moved.");

                if (moved > 0)
                {
                    InventoryPush.Push(containerId, "moveAll");
                }
            }

            return update.moveAll.Count;
        }

        /// <summary>
        /// SALVAGE. The client draws the button on any item whose id starts with
        /// <c>scrapItem-</c> (<c>InventoryTooltipPopup.cs:113</c>), and pressing it
        /// sends exactly this event - <c>TryToConsume(inventoryEntityId, itemId)</c>
        /// from <c>InventoryItemSlot.Use()</c>, which also greys the panel until a
        /// 1081 arrives. It arrived here for months and was refused with "no
        /// consumable effects", so 409 chests' worth of scrap did nothing.
        ///
        /// The event carries an inventory entity id, and it is CHECKED rather than
        /// used. The client only ever sends the player's own - it refuses to salvage
        /// from anything but PlayerInventory (<c>InventoryTooltipPopup.cs:241-247</c>)
        /// - and honouring a foreign id here would let a peer consume items out of a
        /// chest, or out of somebody else's bag, from a hand-built packet. The
        /// ownership gate at the top of HandleUpdate proves the SENDER owns
        /// <paramref name="playerEntityId"/>; this proves the request names it.
        ///
        /// tryToConsume is still the event for eating food and opening a Steam
        /// bundle. Those have no model, so anything that is not scrap falls through
        /// to the same honest refusal as before, inside
        /// <see cref="ScrapSalvageService"/>.
        /// </summary>
        private static int HandleTryToConsume(
            InventoryModificationState.Update update, long playerEntityId )
        {
            for (int j = 0; j < update.tryToConsume.Count; j++)
            {
                TryToConsume consume = update.tryToConsume[j];

                if (consume.inventoryEntityId.Id != playerEntityId)
                {
                    Console.WriteLine("[warning] refusing tryToConsume naming inventory "
                        + consume.inventoryEntityId.Id + " from player " + playerEntityId
                        + ": an item may only be salvaged out of the sender's own inventory.");
                    continue;
                }

                ScrapSalvageService.TrySalvage(playerEntityId, consume.itemId);
            }

            return update.tryToConsume.Count;
        }

        /// <summary>
        /// The gate both cross-inventory paths share: exactly one end must be the
        /// SENDER's own entity and the other must be a registered loot container.
        /// Anything else is refused with a named reason.
        ///
        /// Refusing loudly matters more here than elsewhere in this file. Every other
        /// event in it can only rearrange things the player already owns; these two
        /// move items between entities, so a gap is not a stuck panel but an item
        /// duplicator.
        /// </summary>
        private static bool IsPlayerAndContainer(
            long playerEntityId, long source, long destination, out long containerId )
        {
            containerId = 0;

            if (source == destination)
            {
                Console.WriteLine("[info] refusing a cross-inventory move with the same source and"
                    + " destination (" + source + "); an in-grid move is moveItem, not this.");
                return false;
            }

            if (source == playerEntityId && IsServerContainer(destination))
            {
                containerId = destination;
                return true;
            }

            if (destination == playerEntityId && IsServerContainer(source))
            {
                containerId = source;
                return true;
            }

            Console.WriteLine("[warning] refusing a cross-inventory move between " + source
                + " and " + destination + " for player " + playerEntityId
                + ": one end must be the sender's own entity and the other a container"
                + " (a ruin chest or a mounted ship trunk). Another player's bag is not"
                + " servable.");
            return false;
        }

        /// <summary>
        /// The two kinds of non-player inventory this server has: a rolled ruin chest
        /// and a crafted ship storage container bolted to a ship.
        ///
        /// This must stay a CLOSED list of ledger memberships and never become "any
        /// entity with an inventory". <c>InventoryService.ForEntity</c> will happily
        /// conjure a starter-kit inventory for any id ever passed to it, so an
        /// unchecked id at this seam is not a stuck panel - it is an item duplicator.
        ///
        /// A ship container must additionally be MOUNTED. An unmounted one is not
        /// openable (<c>PartInteractionPolicy.IsSeededInteractionAvailable</c>), so a
        /// move naming one came from a client that should not have had a panel open;
        /// accepting it would let a player stash items into a trunk they are about to
        /// lift away.
        /// </summary>
        private static bool IsServerContainer( long entityId )
        {
            if (LootContainerLedger.IsContainer(entityId))
            {
                return true;
            }
            return ShipContainerService.IsContainer(entityId)
                && MountedParts.Is(entityId);
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

            count += Note(update.splitItemStack.Count, "splitItemStack", "no stacking model");

            count += Note(update.equipTool.Count, "equipTool", "tool slots are hardcoded client-side");
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

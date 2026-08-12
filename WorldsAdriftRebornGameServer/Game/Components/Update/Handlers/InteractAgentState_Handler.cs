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
     * This handler does TWO event-driven jobs, both on the sender's OWN player entity
     * and both gated behind WAREBORN_PLACEMENT=1:
     *
     *   1. DEPLOY TRIGGER (useItemKeyPressed): the decompiled client has no dedicated
     *      "start placing" command. The closest observable signal is 1211's
     *      UseItemKeyPressed event, which fires once on use-key-DOWN and carries the
     *      selected hotbar slot. When the player has a deployable on that slot and
     *      presses use, we send them 1019 StartPlacingItemEvent (via PlacementService).
     *
     *   2. INTERACT-OPEN (interactWithObject): when the player completes a timed
     *      interaction on a world object, the client fires
     *      TriggerInteractWithObject(target, verb) on its own 1211 - VERIFIED at
     *      InteractAgentObserver.IssueInteraction. For a placed shipyard's centre
     *      console the verb is Craft. The ship-build UI does NOT open off that event
     *      directly: CraftingStationBehaviour opens it only when the SHIPYARD's 1005
     *      emits PlayerStartCrafting for the local player. So we translate the Craft
     *      interaction into that 1005 echo (PlacementService.OpenShipyardConsole).
     *
     * WHY THIS IS SAFE despite 1211 being a per-frame stream:
     *   - The handler early-returns when BOTH event lists are empty, which is every
     *     frame except a key-down or an interaction completion. The per-frame
     *     LookingAt/slot DATA is ignored. So the per-frame cost is two empty checks.
     *   - It never relays anything and never mutates inventory; it only starts a
     *     preview or opens a UI on the sender's OWN client.
     *   - It is a no-op unless WAREBORN_PLACEMENT=1.
     *
     * RESIDUAL RISK (needs a live client to settle, documented in the report):
     *   - that UseItemKeyPressed fires for a placeable non-tool hotbar item and that
     *     CurrentItemSlot matches HotBarSlotNum (the debug file trigger proves the
     *     rest of the pipeline regardless);
     *   - that the client actually SENDS the Craft interactWithObject: the client
     *     gates the interact behind _isShipBuildingAware (InteractAgentObserver), so a
     *     player who is not yet shipbuilding-aware is shown a "no crafting yet" chat
     *     message and NO 1211 event is sent - the server never sees it and cannot
     *     open the UI. This is a client onboarding gate, not a missing seed.
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
            // A DELTA: the vast majority of 1211 packets carry only look/slot data and
            // no event. Read both event lists straight off the update and get out fast
            // when there is nothing to act on - this runs at frame rate.
            Improbable.Collections.List<UseItemKeyPressed>? presses = clientComponentUpdate.useItemKeyPressed;
            Improbable.Collections.List<InteractWithObject>? interacts = clientComponentUpdate.interactWithObject;
            bool noPress = presses == null || presses.Count == 0;
            bool noInteract = interacts == null || interacts.Count == 0;
            if (noPress && noInteract)
            {
                return;
            }

            // Only the sender's OWN entity: 1211 is the player's own interact state
            // (rule 6). This ownership fact is what the atlas pickup policy is handed and
            // what the placement paths below require.
            ulong peerId = PeerIdentity.IdOf(player);
            bool ownsPlayer = WorldsAdriftRebornGameServer.Players.Owns(peerId, entityId);

            // ATLAS SHARD PICKUP is ALWAYS active - NOT gated behind WAREBORN_PLACEMENT -
            // because the acquisition loop must work in a plain deposit session. When the
            // client completes a PickUp interaction on a released shard it fires
            // TriggerInteractWithObject(shard, PickUp) here (findings-atlas-shards §2
            // Phase C); the server validates + grants in the pure-policy transaction
            // WorldsAdriftRebornGameServer.TryCollectAtlasShard. Ownership and verb are
            // handed to that policy rather than short-circuited here, so the single gate
            // is the policy. Other verbs/targets fall through to the placement paths.
            if (!noInteract)
            {
                foreach (InteractWithObject pickup in interacts!)
                {
                    if (pickup.verb != InteractVerb.PickUp)
                    {
                        continue;
                    }
                    long shardTarget = pickup.target.Id;
                    if (!WorldsAdriftRebornGameServer.AtlasShards.IsShard(shardTarget))
                    {
                        continue;
                    }
                    Multiplayer.AtlasPickupOutcome outcome =
                        WorldsAdriftRebornGameServer.TryCollectAtlasShard(
                            entityId, shardTarget, ownsPlayer, verbIsPickUp: true);
                    if (outcome != Multiplayer.AtlasPickupOutcome.Grant)
                    {
                        Console.WriteLine("[info] atlas shard PickUp by entity " + entityId
                            + " on " + shardTarget + " not granted: " + outcome + ".");
                    }
                }
            }

            // Everything below is placement-only and gated behind WAREBORN_PLACEMENT.
            if (!WorldsAdriftRebornGameServer.Placement.Enabled)
            {
                return;
            }

            // The placement paths act only on the sender's OWN entity.
            if (!ownsPlayer)
            {
                return;
            }

            // INTERACT-OPEN: the player completed an interaction on a world object. For
            // a placed shipyard's console (verb Craft) echo 1005 PlayerStartCrafting so
            // their client opens the ship-build UI. Other verbs/targets are ignored here.
            if (!noInteract)
            {
                foreach (InteractWithObject interact in interacts!)
                {
                    if (interact.verb == InteractVerb.Craft)
                    {
                        // Both a placed shipyard and a placed Assembly Station bake the
                        // Craft verb, and BOTH open off the same 1005 PlayerStartCrafting
                        // echo - the prefab's baked category decides ship-build vs parts.
                        // Try the shipyard first (it also notes the frame-design console);
                        // it returns false when the target is not a shipyard we placed, so
                        // fall through to the crafting station. Each guard checks its own
                        // ledger, so only the matching one answers.
                        long target = interact.target.Id;
                        bool opened = WorldsAdriftRebornGameServer.Placement.OpenShipyardConsole(
                            player, entityId, target);
                        if (!opened)
                        {
                            WorldsAdriftRebornGameServer.Placement.OpenCraftingStationConsole(
                                player, entityId, target);
                        }
                    }
                }
            }

            if (noPress)
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

            foreach (UseItemKeyPressed press in presses!)
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

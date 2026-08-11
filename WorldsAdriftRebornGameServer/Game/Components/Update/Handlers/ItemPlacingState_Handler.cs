using Bossa.Travellers.Items;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Game.Placement;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 1017 ItemPlacingState - "I have finished positioning the preview; place it HERE".
     *
     * The CONFIRM half of deployable placement, and the one inventory-mutating path
     * where the CLIENT chooses the transform: it raycasts, previews, rotates and
     * validates locally, then publishes ONE PlaceItemEvent. So this handler trusts
     * nothing - it re-checks item identity, ownership, source, parent and transform
     * (PlacementPolicy, pure + unit-tested) before it consumes or spawns anything.
     *
     * The flow that leads here:
     *   1. The player selects the shipyard on the hotbar and presses use, OR the
     *      debug file trigger fires -> PlacementService.StartPlacing sends 1019
     *      StartPlacingItemEvent and the client enters preview.
     *   2. The client holds use until timeToPlace, then publishes 1017 PlaceItemEvent
     *      with placeableItemId == the 1019 PlacingItemId the server chose (the real
     *      inventory id), sourceEntity == the player, globalPosition/globalRotation ==
     *      the chosen transform.
     *   3. This handler validates, spawns the shipyard as a shared world entity every
     *      peer sees, and removes the item from the AUTHORITATIVE inventory (the
     *      client's own local removal on ConsumeItemOnPlacement is cosmetic - only the
     *      server 1081 push prevents a duplicate on relog).
     *
     * MULTIPLAYER SAFETY: 1017 is event-on-confirm, not per-frame; it is filtered out
     * of the raw relay (MirrorSendPolicy.IsRelayedToOtherPlayers) so it is never
     * re-addressed cross-entity to another rig; the shipyard's 190602 is a one-time
     * seed (a static structure - never republished per frame). 1017 is granted
     * authoritative on and injected into the player only when WAREBORN_PLACEMENT=1,
     * so this handler only ever runs for an opted-in server.
     */
    [RegisterComponentUpdateHandler]
    internal class ItemPlacingState_Handler : IComponentUpdateHandler<ItemPlacingState, ItemPlacingState.Update, ItemPlacingState.Data>
    {
        public ItemPlacingState_Handler() { Init(1017); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            ItemPlacingState.Update clientComponentUpdate, ItemPlacingState.Data serverComponentData)
        {
            if (!WorldsAdriftRebornGameServer.Placement.Enabled)
            {
                return;
            }

            // Only the sender's OWN entity: 1017 rides the player's own placement
            // writer, so entityId is the placer. Without this a modified client could
            // publish a 1017 addressed to another avatar and spawn on their behalf.
            ulong peerId = PeerIdentity.IdOf(player);
            if (!WorldsAdriftRebornGameServer.Players.Owns(peerId, entityId))
            {
                Console.WriteLine("[warning] 1017 update for entity " + entityId + " from a peer that owns "
                    + WorldsAdriftRebornGameServer.Players.EntityOf(peerId) + ", ignoring.");
                return;
            }

            Improbable.Collections.List<PlaceItemEvent>? events = clientComponentUpdate.placeItemEvent;
            if (events == null || events.Count == 0)
            {
                return;
            }

            foreach (PlaceItemEvent placement in events)
            {
                HandleOnePlacement(player, entityId, placement);
            }
        }

        private static void HandleOnePlacement(ENetPeerHandle player, long entityId, PlaceItemEvent placement)
        {
            InventoryModel model = InventoryService.ForEntity(entityId);
            InventoryItem? item = model.ById(placement.placeableItemId);

            // parent.Id > 0 means the client aimed at a real entity; terrain placement
            // of a deployable must be parentless. sourceEntity must be the placer.
            bool hasParent = placement.parent.Id > 0;
            bool sourceMatches = placement.sourceEntity.Id == entityId;

            // Which deployable is this? The item's OWN type is the expected type - the
            // policy still runs every other check (real, mine, source, parentless,
            // finite) - but the type gate is now "is this a REGISTERED deployable"
            // rather than "is this the shipyard". An item that is not a deployable (or
            // was already consumed, so null) fails the WrongItemType / ItemNotInInventory
            // reject exactly as before.
            string? itemType = item?.ItemTypeId;
            bool isDeployable = Deployables.TryGet(itemType, out DeployableDef def);
            string expectedType = isDeployable ? itemType! : PlacementService.NotADeployable;

            // A generic crafting station (the Assembly Station) is legitimately placed
            // ONTO a ship deck: its 1017 carries that deck/hull entity as the parent
            // because the client preview raycast snapped to that surface. The server
            // still spawns it parentless at the event's GLOBAL position, so it accepts
            // the parent and then ignores it. The shipyard stays strictly parentless -
            // it is a ground structure, and that invariant is what stops a client
            // spawning one welded to a ship.
            bool parentAllowed = isDeployable && def.IsCraftingStation;

            PlacementDecision decision = PlacementPolicy.Evaluate(
                itemType,
                expectedType,
                sourceMatches,
                hasParent,
                placement.globalPosition.X,
                placement.globalPosition.Y,
                placement.globalPosition.Z,
                parentAllowed: parentAllowed);

            if (!decision.Ok)
            {
                Console.WriteLine("[warning] placement: rejected 1017 for entity " + entityId
                    + " item " + placement.placeableItemId + ": " + decision.Outcome
                    + " (type=" + (itemType ?? "<none>") + ", source=" + placement.sourceEntity.Id
                    + ", parent=" + placement.parent.Id + ").");
                // A definitive reject must ALSO tell the client to leave placement mode,
                // not just drop the server session: the client entered placement on send
                // and only exits on the 1019 StopPlacing clear. Calling EndSession alone
                // (server-only) left the player stuck in preview with every tool locked -
                // the tool-lock bug. StopPlacing sends the clear AND ends the session.
                if (PlacementPolicy.ClientMustLeavePlacing(decision.Outcome))
                {
                    WorldsAdriftRebornGameServer.Placement.StopPlacing(player, entityId);
                }
                else
                {
                    WorldsAdriftRebornGameServer.Placement.EndSession(entityId);
                }
                return;
            }

            FixedPointPosition position = FixedPointPosition.FromMetres(
                placement.globalPosition.X,
                placement.globalPosition.Y,
                placement.globalPosition.Z);

            // Pack the player-chosen rotation; a non-finite/degenerate one falls back
            // to identity inside Encode rather than throwing.
            uint packedRotation = Quaternion32Packing.Encode(
                placement.globalRotation.w,
                placement.globalRotation.x,
                placement.globalRotation.y,
                placement.globalRotation.z);

            // Owner is left empty for this milestone (Phase A = visible + deployed for
            // everyone). Per-owner dome/registration is the Phase B+ follow-on.
            long? spawned = WorldsAdriftRebornGameServer.Placement.SpawnPlacedDeployable(def, position, packedRotation, "");

            if (!spawned.HasValue)
            {
                Console.WriteLine("[warning] placement: entity " + entityId + " item " + placement.placeableItemId
                    + " (" + def.ItemTypeId + ") validated but could not be spawned; NOT consuming the item.");
                WorldsAdriftRebornGameServer.Placement.StopPlacing(player, entityId);
                return;
            }

            // Consume AUTHORITATIVELY, then push 1081. Once removed, a duplicate
            // PlaceItemEvent for the same id finds no item (ItemNotInInventory) and is
            // rejected - the idempotency guard.
            if (model.Remove(placement.placeableItemId))
            {
                InventoryPush.Push(entityId, "consumed " + def.ItemTypeId + " item " + placement.placeableItemId + " on placement");
            }
            else
            {
                Console.WriteLine("[warning] placement: " + def.ItemTypeId + " " + spawned.Value + " spawned but item "
                    + placement.placeableItemId + " was already gone from entity " + entityId + "'s inventory.");
            }

            // Tell the client to leave placement mode + drop the preview ghost (which
            // otherwise stays stuck to the player after a successful placement).
            WorldsAdriftRebornGameServer.Placement.StopPlacing(player, entityId);

            Console.WriteLine("[info] placement: entity " + entityId + " placed a " + def.ItemTypeId + " (item "
                + placement.placeableItemId + ") -> world entity " + spawned.Value + ".");
        }
    }
}

using Bossa.Travellers.Items;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Crafting;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 1239 PlacementToolPlayerState - "I picked up / dropped / placed a ship part".
     *
     * THE CARRY TRACKER. The part-mount commit (1070 PlacePart) carries no part id, so
     * the server has to know which part the player is holding some other way. This is
     * that way: the client's PlayerPlacementToolBehaviour publishes PickedUpEntityEvent
     * (naming the lifted entity) on its OWN authoritative 1239 writer the moment the
     * lift succeeds, and DroppedEntityEvent when the carry ends. Recording
     * player -> partEntityId here is what lets BuilderState_Handler resolve the part a
     * later PlacePart refers to.
     *
     * EVENT-ONLY, and a DELTA: most 1239 packets carry no events. PlacedEntityEvent is
     * a notification that the mount happened - deliberately IGNORED, because the actual
     * mount is committed by the 1070 handler, and clearing the carry here could race
     * ahead of the 1070 PlacePart and leave it with nothing to resolve. Only a genuine
     * DROP clears the carry; a completed mount clears it in the 1070 commit.
     *
     * 1239 is granted authoritative on and injected into the player (MirrorSendPolicy,
     * under the placement flag), so it is in the player's ComponentMap and dispatched
     * here. Nothing is relayed to other players.
     */
    [RegisterComponentUpdateHandler]
    internal class PlacementToolPlayerState_Handler : IComponentUpdateHandler<PlacementToolPlayerState, PlacementToolPlayerState.Update, PlacementToolPlayerState.Data>
    {
        public PlacementToolPlayerState_Handler() { Init(1239); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            PlacementToolPlayerState.Update clientComponentUpdate, PlacementToolPlayerState.Data serverComponentData)
        {
            // Only the sender's OWN entity (rule 6): 1239 rides the player's own lift
            // tool. Without this a modified client could set another player's carry.
            ulong peerId = PeerIdentity.IdOf(player);
            if (!WorldsAdriftRebornGameServer.Players.Owns(peerId, entityId))
            {
                Console.WriteLine("[warning] 1239 update for entity " + entityId + " from a peer that owns "
                    + WorldsAdriftRebornGameServer.Players.EntityOf(peerId) + ", ignoring.");
                return;
            }

            if (clientComponentUpdate.pickedUpEntityEvent != null)
            {
                foreach (PickedUpEvent pickup in clientComponentUpdate.pickedUpEntityEvent)
                {
                    long liftedPartId = pickup.entityId.Id;
                    MountedParts.SetCarried(entityId, liftedPartId);

                    // Lifting a part that is currently MOUNTED detaches it: drop its mount
                    // record so it reads as loose-and-mountable again and a subsequent
                    // PlacePart can re-position it. Without this the re-lifted part stays
                    // mounted in the ledger and its next place is refused PartAlreadyMounted -
                    // exactly "I can't move a part I already placed".
                    // Capture the mount BEFORE removing it, so its owner survives into the
                    // loose re-persist below.
                    MountedParts.Mount? priorMount = MountedParts.MountFor(liftedPartId);
                    bool wasMounted = MountedParts.Unmount(liftedPartId);

                    // Complete the DETACH on the wire (findings-mount-placement.md section 2):
                    // clearing the ledger alone left the client still holding the mounted 8066/
                    // 190602/1120 truth from the last checkout, so carry state contradicted the
                    // server and a re-place was non-deterministic. Broadcast the authoritative
                    // reverse - 8066 no-ship, 190602 loose global, 1120 attached=false - the
                    // instant the part is lifted, using the record captured before it was removed.
                    if (wasMounted && priorMount.HasValue)
                    {
                        PartMountService.BroadcastDetach(liftedPartId, priorMount.Value);
                    }

                    // Keep the SAVE consistent: a lifted part is loose again, so move its
                    // persisted state from MountedParts[] back to LooseParts[] (same PartUid).
                    if (wasMounted)
                    {
                        LoosePartSpawner.RepersistLiftedAsLoose(
                            liftedPartId, priorMount?.OwnerCharacterUid ?? "");
                    }

                    Console.WriteLine("[info] 1239: player entity " + entityId
                        + " picked up part " + liftedPartId
                        + (wasMounted ? " (was mounted; detached for re-placement)." : " (now carrying)."));
                }
            }

            if (clientComponentUpdate.droppedEntityEvent != null && clientComponentUpdate.droppedEntityEvent.Count > 0)
            {
                MountedParts.ClearCarried(entityId);
                Console.WriteLine("[info] 1239: player entity " + entityId + " dropped its carried part.");
            }

            // placedEntityEvent is intentionally not consumed here - see the class note.
        }
    }
}

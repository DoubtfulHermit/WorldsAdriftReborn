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
                    MountedParts.SetCarried(entityId, pickup.entityId.Id);
                    Console.WriteLine("[info] 1239: player entity " + entityId
                        + " picked up part " + pickup.entityId.Id + " (now carrying).");
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

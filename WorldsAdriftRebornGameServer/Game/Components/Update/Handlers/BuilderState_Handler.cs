using Bossa.Travellers.Player;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 1070 BuilderState - the CLIENT->SERVER commit of the part-mount flow.
     *
     * EVENT-ONLY. BuilderStateData is an empty struct and Update.ApplyTo is a no-op,
     * so unlike TreeCutterState there is nothing to merge: the handler acts purely on
     * the three decoded event lists the client sends -
     *   placePart       - the player placed the carried part on a ship (the mount);
     *   cancelPlacePart  - the player cancelled the carry (dropped the lift);
     *   teleportPart     - the player dropped the part in the world (still loose).
     *
     * The client drives carry locally (PlayerScannerTool) and, on place, fires ONE
     * PlacePart on its OWN authoritative 1070 writer, then Cancel()s without waiting -
     * so this handler is the only server-bound signal that a mount happened. PlacePart
     * carries NO part id; the part being carried is resolved from the 1239
     * PlacementToolPlayerState pickup notifications (PlacementToolPlayerState_Handler).
     *
     * 1070 is granted authoritative on and injected into the player entity
     * (MirrorSendPolicy.PartMount*Components, under the placement flag), so it is in
     * the player's ComponentMap and dispatched here.
     *
     * MULTIPLAYER SAFETY: event-driven and per-player. One event in; the commit writes
     * a handful of value-updates on the PART entity (PartMountService). Nothing is
     * relayed to other players (MirrorSendPolicy.IsRelayedToOtherPlayers filters 1070).
     */
    [RegisterComponentUpdateHandler]
    internal class BuilderState_Handler : IComponentUpdateHandler<BuilderState, BuilderState.Update, BuilderState.Data>
    {
        public BuilderState_Handler() { Init(1070); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            BuilderState.Update clientComponentUpdate, BuilderState.Data serverComponentData)
        {
            // Only the sender's OWN entity (docs/multiplayer.md rule 6): 1070 rides the
            // player's own builder writer, so entityId is that player. Without this a
            // modified client could commit a mount "as" another player.
            ulong peerId = PeerIdentity.IdOf(player);
            if (!WorldsAdriftRebornGameServer.Players.Owns(peerId, entityId))
            {
                Console.WriteLine("[warning] 1070 update for entity " + entityId + " from a peer that owns "
                    + WorldsAdriftRebornGameServer.Players.EntityOf(peerId) + ", ignoring.");
                return;
            }

            if (clientComponentUpdate.placePart != null)
            {
                foreach (PlacePart placePart in clientComponentUpdate.placePart)
                {
                    PartMountService.HandlePlacePart(ownsPlayerEntity: true, entityId, placePart);
                }
            }

            if (clientComponentUpdate.cancelPlacePart != null)
            {
                for (int i = 0; i < clientComponentUpdate.cancelPlacePart.Count; i++)
                {
                    PartMountService.HandleCancelPlacePart(entityId);
                }
            }

            // teleportPart (drop-in-world inside the yard) leaves the part LOOSE at a new
            // pose. For this first inert part we clear the carry so a subsequent lift
            // starts clean; re-broadcasting the loose 190602 at the dropped global pose is
            // the documented follow-on (the part re-seeds loose on its next checkout).
            if (clientComponentUpdate.teleportPart != null && clientComponentUpdate.teleportPart.Count > 0)
            {
                Console.WriteLine("[info] 1070: player entity " + entityId
                    + " dropped the carried part in the world (teleportPart); clearing carry.");
                PartMountService.HandleCancelPlacePart(entityId);
            }
        }
    }
}

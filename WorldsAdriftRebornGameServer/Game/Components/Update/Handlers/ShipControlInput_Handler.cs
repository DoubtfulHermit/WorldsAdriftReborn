using Bossa.Travellers.Ship;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 1111 ShipControlInput - the pilot's throttle/vertical/ship-axes stream, the
     * INPUT half of helm flight.
     *
     * WHO SENDS IT AND WHEN (VERIFIED in the decompile): ShipControlsBehaviour on
     * the local player sends it ONLY while 1109 PilotState.DrivingEntityId is
     * valid (IsDriving), at most every 0.05 s, and the generated updater
     * diff-suppresses frames where nothing changed (FinishAndSend_ResolveDiff) -
     * so a held stick is SILENT, and "no packet" must be read as "unchanged",
     * never "released". That is why this handler merges deltas into held input
     * (ShipFlightService.OnControlInput) instead of treating each packet as the
     * whole state.
     *
     * MULTIPLAYER SAFETY:
     *   - CONSUME-ONLY. The update is eaten here and turned into the hull's 1130
     *     control points by the flight integrator; it is never relayed
     *     (MirrorSendPolicy.IsRelayedToOtherPlayers filters 1111/1112), because
     *     relaying the one pilot's up-to-20 Hz stream to every peer reliably is
     *     the exact congestion spiral the relay filter exists to prevent.
     *   - OWN ENTITY ONLY. 1111 lives on the sender's own player entity; an
     *     update addressed elsewhere is dropped with a log.
     *   - Cheap when idle: nobody piloting means no client is sending 1111 at
     *     all (the writer only runs while driving).
     */
    [RegisterComponentUpdateHandler]
    internal class ShipControlInput_Handler : IComponentUpdateHandler<ShipControlInput, ShipControlInput.Update, ShipControlInput.Data>
    {
        public ShipControlInput_Handler() { Init(1111); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            ShipControlInput.Update clientComponentUpdate, ShipControlInput.Data serverComponentData)
        {
            if (!ShipFlightService.Enabled)
            {
                return;
            }

            ulong peerId = PeerIdentity.IdOf(player);
            if (!WorldsAdriftRebornGameServer.Players.Owns(peerId, entityId))
            {
                Console.WriteLine("[warning] 1111 ShipControlInput for entity " + entityId
                    + " from a peer that does not own it; dropped.");
                return;
            }

            // A DELTA: each field is an Option, absent = unchanged. Hand exactly
            // that shape to the service - the merge semantics live in one place
            // (FlightControlInput.Merge) where they are unit-tested.
            //
            // FUEL sits between the two: it mirrors the same delta (a held stick is
            // SILENT on this wire, so burning on packet arrival would let a pilot fly
            // for free), and returns the throttle flight should actually be given -
            // zero while the hull this player pilots is dry. It never touches the
            // flight service's own state; see Game.ShipFuelService for the seam.
            float? throttle = WorldsAdriftRebornGameServer.ShipFuel.OnControlInput(
                entityId,
                clientComponentUpdate.throttle.HasValue ? clientComponentUpdate.throttle.Value : (float?)null,
                clientComponentUpdate.vertical.HasValue ? clientComponentUpdate.vertical.Value : (float?)null,
                clientComponentUpdate.shipAxes.HasValue ? clientComponentUpdate.shipAxes.Value.X : (float?)null,
                clientComponentUpdate.shipAxes.HasValue ? clientComponentUpdate.shipAxes.Value.Y : (float?)null,
                clientComponentUpdate.shipAxes.HasValue ? clientComponentUpdate.shipAxes.Value.Z : (float?)null);

            WorldsAdriftRebornGameServer.Flight.OnControlInput(
                entityId,
                throttle,
                clientComponentUpdate.vertical.HasValue ? clientComponentUpdate.vertical.Value : (float?)null,
                clientComponentUpdate.shipAxes.HasValue ? clientComponentUpdate.shipAxes.Value.X : (float?)null,
                clientComponentUpdate.shipAxes.HasValue ? clientComponentUpdate.shipAxes.Value.Y : (float?)null,
                clientComponentUpdate.shipAxes.HasValue ? clientComponentUpdate.shipAxes.Value.Z : (float?)null);
        }
    }
}

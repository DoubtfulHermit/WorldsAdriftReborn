using Bossa.Travellers.Player;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * Watches 1073 ClientAuthoritativePlayerState for the TELEPORT ACK, and
     * nothing else.
     *
     * TeleportTransformVisualizer is the only 190607 consumer that can enable on
     * this server, and when it applies a teleport it writes the executed request
     * number into this component's lastExecutedRequest field. That write is the
     * server's only evidence a teleport actually happened - there is no other
     * channel, and the server never sees the client's transform except as
     * opaque relayed bytes.
     *
     * It is also why the parentless path is the cheap one: the ack lands on a
     * component the client is ALREADY granted authority over
     * (MirrorSendPolicy.AuthoritativeComponents), so teleport needs no new
     * authority grant at all. The expensive path acks on 190606, which we do not
     * grant and would have to.
     *
     * HOT PATH. ClientAuthoritativePlayerMovement republishes 1073 every tick
     * with the player's bone and relative-position data. It never touches
     * lastExecutedRequest, and a generated Update only carries the fields that
     * were set, so the Option below is empty on every one of those ticks and
     * this handler returns immediately. Registering it costs nothing: the
     * reflection work in ComponentUpdateManager.HandleComponentUpdate already
     * ran for every 1073 update before this existed, and merely failed to find a
     * handler at the end of it.
     *
     * Deliberately does NOT call ApplyTo: the server's stored 1073 is only ever
     * used to re-seed the component, where the client's live bone bytes and
     * relative position would be stale noise.
     */
    [RegisterComponentUpdateHandler]
    internal class ClientAuthoritativePlayerState_Handler : IComponentUpdateHandler<ClientAuthoritativePlayerState, ClientAuthoritativePlayerState.Update, ClientAuthoritativePlayerState.Data>
    {
        public ClientAuthoritativePlayerState_Handler() { Init(1073); }

        protected override void Init( uint ComponentId )
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate( ENetPeerHandle player, long entityId,
            ClientAuthoritativePlayerState.Update clientComponentUpdate, ClientAuthoritativePlayerState.Data serverComponentData )
        {
            if (!clientComponentUpdate.lastExecutedRequest.HasValue)
            {
                return;
            }

            // Ownership gate (docs/multiplayer.md rule 6): a client may only
            // speak for its OWN entity. Without this, a peer could ack another
            // player's teleport and push that player's request counter forward,
            // which would make the victim's next real teleport a silent no-op.
            if (!WorldsAdriftRebornGameServer.Players.Owns(PeerIdentity.IdOf(player), entityId))
            {
                return;
            }

            WorldsAdriftRebornGameServer.Teleports.OnAck(entityId, clientComponentUpdate.lastExecutedRequest.Value);
        }
    }
}

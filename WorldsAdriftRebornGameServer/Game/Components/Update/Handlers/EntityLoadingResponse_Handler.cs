using Improbable.Corelib.Worker.Checkout;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 190001 EntityLoadingResponse - "the initial world set you named is now ready".
     *
     * The client half of WA's shipped loading barrier. The server seeds
     * 190000 EntityLoadingControl = Requested with the initial entity-id list and
     * holds 190002 Activated = false, keeping the loading screen up. The player
     * prefab's BossaEntityLoadingChecker waits until every named entity exists and
     * is active on the client, then publishes loaded=true on this component - which
     * it can only do because setup granted the client authority over 190001.
     *
     * This handler is the server's side of that handshake: on the first loaded=true
     * for a peer's own player, it releases the barrier (pushes 190002 IsActive=true),
     * which is what finally lets PlayerActivationVisualiser fade the loading screen.
     * The release is exactly-once (LoadBarrierTracker.Complete), so a client that
     * publishes loaded twice, or that is also caught by the timeout sweep, is
     * activated once.
     *
     * MULTIPLAYER SAFETY: a rare one-shot per join, reliable-ordered, targeting only
     * the joining peer's own entity. It relays nothing to other players.
     */
    [RegisterComponentUpdateHandler]
    internal class EntityLoadingResponse_Handler : IComponentUpdateHandler<EntityLoadingResponse, EntityLoadingResponse.Update, EntityLoadingResponse.Data>
    {
        public EntityLoadingResponse_Handler() { Init(190001); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            EntityLoadingResponse.Update clientComponentUpdate, EntityLoadingResponse.Data serverComponentData)
        {
            // A delta: most updates would carry no loaded field, and only loaded=true
            // is the "ready" edge. false (the checker resetting when it receives a new
            // list) is not a release.
            if (!clientComponentUpdate.loaded.HasValue || !clientComponentUpdate.loaded.Value)
            {
                return;
            }

            // Only the sender's OWN player entity may release the sender's barrier. A
            // modified client publishing 190001 on someone else's avatar must not
            // activate that other player.
            ulong peerId = PeerIdentity.IdOf(player);
            if (!WorldsAdriftRebornGameServer.Players.Owns(peerId, entityId))
            {
                Console.WriteLine("[load-barrier] 190001 loaded=true for entity " + entityId
                    + " from a peer that owns " + WorldsAdriftRebornGameServer.Players.EntityOf(peerId)
                    + ", ignoring.");
                return;
            }

            // Exactly-once: Complete returns true only if this peer was still holding
            // the barrier. A second signal, or a race with the timeout sweep, is a
            // no-op here.
            if (WorldsAdriftRebornGameServer.LoadBarriers.Complete(peerId))
            {
                WorldsAdriftRebornGameServer.ReleaseLoadBarrier(player, entityId, "client signalled ready (190001)");
            }
        }
    }
}

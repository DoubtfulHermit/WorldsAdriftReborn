using Bossa.Travellers.Materials;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 1037 TreeCutterState - "my salvage beam is resting on this tree section".
     *
     * IT IS A LATCH, NOT A PULSE, and this handler exists mainly to make that
     * survivable. TreeCuttingBehaviour.Update writes the component every frame,
     * but the writer's FinishAndSend suppresses a send when nothing changed, so
     * what arrives here is ONE packet when the beam moves onto a section and ONE
     * when it moves off. There is no chop event to count and no repetition to
     * throttle: if this handler cut a section per packet, a player would get
     * exactly one section per aim and holding the beam would do nothing at all.
     *
     * So the handler does not cut. It records where the beam is pointing and lets
     * TreeHarvest's timer decide when that becomes damage - see
     * WorldsAdriftRebornGameServer.TickTreeHarvest.
     *
     * THE UPDATE IS A DELTA. The client sends only the fields that changed, so an
     * update that keeps aiming at the same tree may carry sectionId alone. Reading
     * clientComponentUpdate directly would therefore see treeEntityId as absent
     * and conclude the beam had left the tree. ApplyTo merges the delta into the
     * stored component first, and the latch is read off the MERGED state.
     */
    [RegisterComponentUpdateHandler]
    internal class TreeCutterState_Handler : IComponentUpdateHandler<TreeCutterState, TreeCutterState.Update, TreeCutterState.Data>
    {
        public TreeCutterState_Handler() { Init(1037); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            TreeCutterState.Update clientComponentUpdate, TreeCutterState.Data serverComponentData)
        {
            // Only the sender's OWN entity, rule 6 in docs/multiplayer.md. Without
            // this, one client could latch a chop onto another player's avatar and
            // the wood - once wood is granted at all - would be credited to the
            // wrong person.
            ulong peerId = PeerIdentity.IdOf(player);
            if (!WorldsAdriftRebornGameServer.Players.Owns(peerId, entityId))
            {
                Console.WriteLine("[warning] 1037 update for entity " + entityId + " from a peer that owns "
                    + WorldsAdriftRebornGameServer.Players.EntityOf(peerId) + ", ignoring.");
                return;
            }

            clientComponentUpdate.ApplyTo(serverComponentData);

            TreeCutSignal signal = new TreeCutSignal(
                serverComponentData.Value.treeEntityId.Id,
                serverComponentData.Value.sectionId,
                serverComponentData.Value.aboveOrBelow);

            // OnCutSignal is where "is that even a tree" is decided: the beam rests
            // on rocks, hulls and other players too, and a signal naming any of
            // them simply disengages. Nothing here validates and then acts twice.
            if (WorldsAdriftRebornGameServer.Harvest.OnCutSignal(entityId, signal))
            {
                Console.WriteLine("[info] entity " + entityId + " " + signal + ".");
            }
        }
    }
}

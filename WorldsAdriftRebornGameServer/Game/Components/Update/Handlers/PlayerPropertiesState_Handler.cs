using Bossa.Travellers.Player;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * Receives a client's PlayerPropertiesState (1088) update and records its
     * customisation map - the player's appearance - so that remote mirrors of
     * this player are seeded with the real look instead of defaults.
     *
     * The client mod publishes this update once at spawn (the game itself never
     * writes 1088 client-side; the mod uses the component Impl's Writer, whose
     * Send has no authority gate). The verbatim relay forwards the same update
     * to already-connected clients live; this handler covers everyone who joins
     * AFTERWARDS.
     *
     * Only the sender's OWN entity is recorded (rule 6 in docs/multiplayer.md):
     * anything else would let one client rewrite another player's appearance.
     */
    [RegisterComponentUpdateHandler]
    internal class PlayerPropertiesState_Handler : IComponentUpdateHandler<PlayerPropertiesState, PlayerPropertiesState.Update, PlayerPropertiesState.Data>
    {
        public PlayerPropertiesState_Handler() { Init(1088); }
        protected override void Init( uint ComponentId )
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate( ENetPeerHandle player, long entityId,
            PlayerPropertiesState.Update clientComponentUpdate, PlayerPropertiesState.Data serverComponentData )
        {
            if (!clientComponentUpdate.customisation.HasValue)
            {
                return;
            }

            long? ownEntity = WorldsAdriftRebornGameServer.Players.EntityOf(PeerIdentity.IdOf(player));
            if (ownEntity != entityId)
            {
                Console.WriteLine("[warning] 1088 update for entity " + entityId + " from a peer that owns " + ownEntity + ", ignoring.");
                return;
            }

            Dictionary<string, string> map = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> pair in clientComponentUpdate.customisation.Value)
            {
                map[pair.Key] = pair.Value;
            }

            WorldsAdriftRebornGameServer.Appearances.Record(entityId, map);
            Console.WriteLine("[info] recorded appearance for entity " + entityId + " (" + map.Count + " keys).");

            // Keep the server-side stored component in sync so later re-serves of
            // this entity's 1088 (interest requests) also carry the real data.
            clientComponentUpdate.ApplyTo(serverComponentData);
        }
    }
}

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

            if (!WorldsAdriftRebornGameServer.Players.Owns(PeerIdentity.IdOf(player), entityId))
            {
                Console.WriteLine("[warning] 1088 update for entity " + entityId + " from a peer that owns "
                    + WorldsAdriftRebornGameServer.Players.EntityOf(PeerIdentity.IdOf(player)) + ", ignoring.");
                return;
            }

            Dictionary<string, string> map = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> pair in clientComponentUpdate.customisation.Value)
            {
                map[pair.Key] = pair.Value;
            }

            WorldsAdriftRebornGameServer.Appearances.Record(entityId, map);
            Console.WriteLine("[info] recorded appearance for entity " + entityId + " (" + map.Count + " keys).");

            // PROBE: does the client actually publish its character identity?
            // The whole persistence design assumes the selected character's uid
            // rides along in this 1088 update, but that has only ever been
            // verified by reading the decompiled client, never observed running.
            // The seed carries 4 cosmetic keys, so 5 keys means identity arrived.
            string identity;
            if (map.TryGetValue(Multiplayer.Inventory.CharacterIdentity.CharacterDataKey, out identity) && identity != null)
            {
                string flat = identity.Replace("\r", "").Replace("\n", " ");
                if (flat.Length > 240) { flat = flat.Substring(0, 240); }
                Console.WriteLine("[probe] IDENTITY entity=" + entityId + " -> " + flat);
            }
            else
            {
                Console.WriteLine("[probe] IDENTITY entity=" + entityId + " -> MISSING (keys: "
                    + string.Join(",", map.Keys.ToArray()) + ")");
            }

            // THIS IS WHERE THE INVENTORY GETS A DURABLE NAME, and the only
            // place it can: no packet on the ENet wire carries an account, so
            // the character uid inside the map above is the sole crossing point
            // between the login server's identity and this process.
            //
            // It happens AFTER checkout, which is why every player begins on a
            // volatile session key: 1081 was already seeded when the interest
            // request arrived. BindIdentity rebinds onto the character key,
            // loads whatever the database holds, and returns false - loudly -
            // when the uid did not arrive, in which case the session keeps
            // working and simply never saves.
            //
            // The push afterwards is not optional. Loading a stored inventory
            // changes what the player owns, and a 1081 update is the only thing
            // that makes the client re-read it.
            bool durable = Game.Inventory.InventoryService.BindIdentity(entityId, map);

            // Keep the server-side stored component in sync so later re-serves of
            // this entity's 1088 (interest requests) also carry the real data.
            //
            // BEFORE the push below, not after: the push re-sends the stored 1088
            // alongside 1081, and sending the pre-publish copy would hand the
            // client back the default look it just replaced.
            clientComponentUpdate.ApplyTo(serverComponentData);

            if (durable)
            {
                Game.Inventory.InventoryPush.Push(entityId, "character identity arrived");
            }
        }
    }
}

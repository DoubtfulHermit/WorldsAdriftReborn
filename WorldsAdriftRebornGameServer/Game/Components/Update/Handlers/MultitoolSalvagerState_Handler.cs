using Bossa.Travellers.Salvaging;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 2106 MultitoolSalvagerState - "my salvage beam just fired at this entity".
     *
     * THIS IS THE METAL ANALOGUE OF 1037 TreeCutterState, but the two carry their
     * hit in opposite ways, and the difference is the whole reason this handler is
     * short:
     *
     *   - 1037 is a LATCH (state): one packet when the beam arrives on a section,
     *     one when it leaves. It names WHERE the beam rests; a server TIMER
     *     (TreeHarvest) turns "resting" into repeated cuts.
     *   - 2106 carries an EVENT: a ShotEvent in the update's `shotEvent` list, one
     *     per deploy. The client rate-limits itself (MultitoolSalvageController.
     *     TryDeploy honours MinDeployInterval ≈ 0.75 s) and publishes exactly one
     *     ShotEvent per deploy, so each event is already ONE discrete hit. There is
     *     no cadence for the server to supply - it only has to count, which is what
     *     MetalHarvest does.
     *
     * VERIFIED against the decompiled client (Assembly-CSharp + Generated.Code):
     *   PlayerMultitool.TryDeploySalvager raycasts (10 m), and on a salvageable hit
     *   raises ShotEntity(hitEntity, point, dir, 1f). PlayerMultitoolVisualizer.
     *   OnPlayerShotSalvage subscribes and publishes on the player's OWN authoritative
     *   2106 writer:
     *       _multitoolSalvagingState.Update
     *           .TriggerShotEvent(entity.EntityId, shotCoords, shotDirection).FinishAndSend();
     *   MultitoolSalvagerState.Updater.TriggerShotEvent(EntityId entity_id,
     *       Coordinates shot_coordinate, Vector3d shot_direction) appends a ShotEvent
     *   to Update.shotEvent, whose runtime struct is
     *       ShotEvent { EntityId entityId; Coordinates shotCoordinate; Vector3d shotDirection; }.
     *   entityId is the TARGET (the node), the analogue of 1037's treeEntityId.
     *
     * 2106 is granted authoritative on and injected into the player entity
     * (MirrorSendPolicy.AuthoritativeComponents / InjectedComponents), so it is in
     * the player's ComponentMap and ComponentUpdateManager will dispatch here.
     */
    [RegisterComponentUpdateHandler]
    internal class MultitoolSalvagerState_Handler : IComponentUpdateHandler<MultitoolSalvagerState, MultitoolSalvagerState.Update, MultitoolSalvagerState.Data>
    {
        public MultitoolSalvagerState_Handler() { Init(2106); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            MultitoolSalvagerState.Update clientComponentUpdate, MultitoolSalvagerState.Data serverComponentData)
        {
            // Only the sender's OWN entity, rule 6 in docs/multiplayer.md and the
            // node-relay hardening (findings-node-relay.md): 2106 rides the player's
            // own multitool, so entityId is the shooter. Without this, a modified
            // client could publish a 2106 addressed to someone else's avatar and
            // credit their salvage - or, worse, drive a node's depletion from an
            // entity it does not own.
            ulong peerId = PeerIdentity.IdOf(player);
            if (!WorldsAdriftRebornGameServer.Players.Owns(peerId, entityId))
            {
                Console.WriteLine("[warning] 2106 update for entity " + entityId + " from a peer that owns "
                    + WorldsAdriftRebornGameServer.Players.EntityOf(peerId) + ", ignoring.");
                return;
            }

            // The update is a DELTA: most 2106 packets carry only isJammed/isEngaged
            // and no events, so the shot list is routinely null or empty. Nothing to
            // merge - a ShotEvent is transient, not stored state - so it is read
            // straight off the incoming update.
            Improbable.Collections.List<ShotEvent>? shots = clientComponentUpdate.shotEvent;
            if (shots == null || shots.Count == 0)
            {
                return;
            }

            foreach (ShotEvent shot in shots)
            {
                long targetEntityId = shot.entityId.Id;

                // OnSalvageShot is where "is that even a node" is decided: the beam
                // rests on trees, hulls, players and already-depleted nodes too, and
                // a shot naming any of them simply does nothing. Nothing here
                // validates and then acts twice.
                WorldsAdriftRebornGameServer.OnSalvageShot(entityId, targetEntityId);
            }
        }
    }
}

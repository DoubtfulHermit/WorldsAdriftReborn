using Bossa.Travellers.Islands;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Gathering;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 1011 IslandResourceSpawnerClientState - the CLIENT's REPLY to the island
     * resource-placement request. This is the consumer half the retail worker
     * provided and this build lost (docs/research/gathering/findings-resource-placement.md).
     *
     * It carries an EVENT, exactly like 2106's ShotEvent: the client's
     * IslandProxyVisualizer, having sampled its OWN island mesh for on-ground,
     * physics-clear points (acs/IslandProxyVisualizer.cs:195-231, IslandSurfaceData.
     * FindPlace), publishes them on its authoritative 1011 writer via
     *   resourceSpawnerClientState.Update.TriggerSpawnResourcesReply(list).FinishAndSend()
     * which appends a SpawnResourcesReply{ List<SpawnResourceRequest> } to
     * Update.spawnResourcesReply (gencode .../IslandResourceSpawnerClientState.cs:398,
     * :645). Each SpawnResourceRequest is a FabricTransform (position = a SpatialOS
     * global Coordinates in metres, metadata = "MetalDeposit") + a variant string.
     *
     * The reply events are transient, not stored state (like 2106's shotEvent), so
     * they are read straight off the incoming update - no ApplyTo/merge. All the
     * trust (metal-only, dedup, clamp to the requested count, one deposit per
     * position) and the actual spawn live behind IslandResourceService.OnReply.
     *
     * WHY THIS DISPATCHES: 1011 is served AND granted authoritative on the ISLAND
     * entity by IslandResourceService.OnIslandInterest, so it is in
     * ComponentMap[peer][island][1011] and ComponentUpdateManager routes here.
     */
    [RegisterComponentUpdateHandler]
    internal class IslandResourceSpawnerClientState_Handler
        : IComponentUpdateHandler<IslandResourceSpawnerClientState, IslandResourceSpawnerClientState.Update, IslandResourceSpawnerClientState.Data>
    {
        public IslandResourceSpawnerClientState_Handler() { Init(1011); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            IslandResourceSpawnerClientState.Update clientComponentUpdate,
            IslandResourceSpawnerClientState.Data serverComponentData)
        {
            // entityId is the ISLAND (1011 is only ever served there). No ownership
            // check against the sender's player entity - unlike 2106/1037, 1011 does
            // not ride the sender's avatar - but OnReply gates on the peer having a
            // real player entity and the target being a registered island, and the
            // ledger clamps/dedups whatever the reply claims.
            IslandResourceService.OnReply(player, entityId, clientComponentUpdate);
        }
    }
}

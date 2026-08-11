using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bossa.Travellers.Craftingstation;
using Bossa.Travellers.Items;
using Improbable;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Placement
{
    /// <summary>
    /// The whole server side of deployable placement, in one seam. It is DATA-DRIVEN
    /// by <see cref="Deployables"/>: it names no item type of its own, so every
    /// deployable in that table (shipyard, chest, campfire, ...) rides the exact same
    /// path. It does three jobs and nothing else:
    ///
    ///   1. START a placement: write 1019 StartPlacingItemEvent onto a player so
    ///      their client enters the native placement preview with the DEPLOYABLE'S OWN
    ///      asset, and PRE-WARM that bundle on every connected peer (seconds before the
    ///      confirm, so the runtime AddEntity at confirm time never races a load).
    ///   2. SPAWN the confirmed deployable as a shared world entity every connected
    ///      peer sees, seeded with the deployable's own component set.
    ///   3. Offer a DEBUG file-poll trigger (WAREBORN_PLACEMENT_FILE), the same shape
    ///      as the ship/teleport triggers, so the pipeline is testable even if the
    ///      native 1211 use-press trigger does not fire on a live client.
    ///
    /// The 1017 confirm handler and the 1211 use-press trigger both call in here; the
    /// pure validation, the deployable table and rotation packing live in the
    /// Multiplayer assembly.
    ///
    /// Gated behind WAREBORN_PLACEMENT=1: when off, every entry point returns
    /// immediately, so an un-flagged server behaves exactly as before.
    /// </summary>
    internal sealed class PlacementService
    {
        /// <summary>
        /// A sentinel "expected type" the 1017 handler passes to PlacementPolicy when
        /// the placed item is NOT a registered deployable, so the policy's type check
        /// fails with WrongItemType. It can never equal a real item type.
        /// </summary>
        public const string NotADeployable = "<not-a-deployable>";

        private const string PlacementType = "Terrain";
        private const string AssetContext = "notNeeded?";
        private const float TimeToPlace = 1.0f;

        private const uint ItemPlacementAgentStateComponentId = 1019;

        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private const string DefaultTriggerFile = "/tmp/wareborn-place";

        /// <summary>
        /// A cancelled placement (the player pressed use, then walked away without
        /// confirming) leaves a session behind. After this long, a new use-press is
        /// allowed to start a fresh one rather than being locked out until relog.
        /// </summary>
        private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(30);

        private readonly Dictionary<long, (int ItemId, DateTime Started)> _sessions = new();
        private readonly Stopwatch _sinceLastPoll = Stopwatch.StartNew();
        private readonly string _triggerFile;

        /// <summary>
        /// A never-reused suffix for a placed deployable's registration key. The key
        /// is what the world-entity registry allocates a shared entity id from, so it
        /// must be unique for the life of the process; a monotonic counter is the
        /// simplest thing that cannot collide across deployable kinds.
        /// </summary>
        private int _placedSequence;

        public PlacementService()
        {
            Enabled = Environment.GetEnvironmentVariable("WAREBORN_PLACEMENT") == "1";
            string? configured = Environment.GetEnvironmentVariable("WAREBORN_PLACEMENT_FILE");
            _triggerFile = string.IsNullOrWhiteSpace(configured) ? DefaultTriggerFile : configured.Trim();
        }

        /// <summary>Whether the feature is switched on (WAREBORN_PLACEMENT=1).</summary>
        public bool Enabled { get; }

        /// <summary>The debug trigger file path, for the startup banner.</summary>
        public string TriggerFile => _triggerFile;

        // -----------------------------------------------------------------
        // 1. START placement
        // -----------------------------------------------------------------

        /// <summary>
        /// Puts a player's client into placement preview for the deployable of type
        /// <paramref name="itemTypeId"/> with inventory id <paramref name="itemId"/>:
        /// writes the 1019 state + fires StartPlacingItemEvent (naming the deployable's
        /// OWN asset) to that peer, and pre-warms that bundle everywhere. Returns true
        /// if the start event was sent. A no-op when disabled, when the type is not a
        /// known deployable, when the player is already mid-placement, or on send
        /// failure.
        /// </summary>
        public bool StartPlacing(ENetPeerHandle peer, long playerEntityId, int itemId, string itemTypeId)
        {
            if (!Enabled)
            {
                return false;
            }

            if (!Deployables.TryGet(itemTypeId, out DeployableDef def))
            {
                Console.WriteLine("[warning] placement: entity " + playerEntityId + " tried to place '"
                    + itemTypeId + "' which is not a registered deployable; ignoring.");
                return false;
            }

            if (IsPlacing(playerEntityId))
            {
                return false;
            }

            // The server SETS the 1019 state (Placing / PlacingItemId / type / consume)
            // AND fires the StartPlacingItemEvent in one update. The state's
            // PlacingItemId is what the client echoes back as PlaceItemEvent
            // .placeableItemId on confirm, so it MUST be the real inventory id or the
            // server cannot consume the right item. The asset is the deployable's own.
            ItemPlacementAgentState.Update update = new ItemPlacementAgentState.Update();
            update.SetPlacing(true);
            update.SetPlacingItemId(itemId);
            update.SetPlacingType(PlacementType);
            update.SetConsumeItemOnPlacement(true);
            update.AddStartPlacingItemEvent(
                new StartPlacingItemEvent(itemId, def.AssetName, PlacementType, TimeToPlace));

            if (!SendOPHelper.SendComponentUpdateOp(
                    peer, playerEntityId,
                    new List<uint> { ItemPlacementAgentStateComponentId },
                    new List<object> { update }))
            {
                Console.WriteLine("[warning] placement: failed to send 1019 StartPlacingItemEvent to entity "
                    + playerEntityId + " (is 1019 injected? WAREBORN_PLACEMENT must be set at connect).");
                return false;
            }

            _sessions[playerEntityId] = (itemId, DateTime.UtcNow);

            // Pre-warm the bundle on every peer NOW, well before the confirm.
            PrewarmAsset(def.AssetName);

            Console.WriteLine("[info] placement: entity " + playerEntityId + " is now placing '" + def.ItemTypeId
                + "' item " + itemId + " (prefab '" + def.AssetName + "', type '" + PlacementType + "', "
                + TimeToPlace + "s to place). Position the preview and hold use to place it.");
            return true;
        }

        /// <summary>Whether a player currently has a live (non-expired) placement session.</summary>
        public bool IsPlacing(long playerEntityId)
        {
            if (!_sessions.TryGetValue(playerEntityId, out var session))
            {
                return false;
            }

            if (DateTime.UtcNow - session.Started > SessionTimeout)
            {
                _sessions.Remove(playerEntityId);
                return false;
            }

            return true;
        }

        /// <summary>Clears a player's placement session (on confirm, or on a definitive reject).</summary>
        public void EndSession(long playerEntityId)
        {
            _sessions.Remove(playerEntityId);
        }

        /// <summary>
        /// Tells the CLIENT to leave placement mode and drop the preview ghost after a
        /// placement completes (or is cancelled server-side): pushes 1019 with
        /// Placing=false + PlacingItemId=0 and a StopPlacingItemEvent. Without this the
        /// client's ItemPlacingBehaviour stays in preview mode (Placing still true) and
        /// the green ghost stays stuck to the player. Also clears the server session.
        /// </summary>
        public void StopPlacing(ENetPeerHandle peer, long playerEntityId)
        {
            ItemPlacementAgentState.Update update = new ItemPlacementAgentState.Update();
            update.SetPlacing(false);
            update.SetPlacingItemId(0);
            update.AddStopPlacingItemEvent(default(StopPlacingItemEvent));

            SendOPHelper.SendComponentUpdateOp(
                peer, playerEntityId,
                new List<uint> { ItemPlacementAgentStateComponentId },
                new List<object> { update });

            EndSession(playerEntityId);
        }

        // -----------------------------------------------------------------
        // 1b. OPEN a placed shipyard's ship-build UI (the interact-open path)
        // -----------------------------------------------------------------

        /// <summary>
        /// Opens the ship-build UI on the interacting player's client after they
        /// complete the "Craft" interaction on a placed shipyard's centre console.
        ///
        /// The client fired 1211 <c>TriggerInteractWithObject(shipyard, Craft)</c>, but
        /// the ship-build UI does NOT open off that event. CraftingStationBehaviour
        /// opens it only when the SHIPYARD entity's 1005 CraftingStationClientState
        /// emits <c>PlayerStartCrafting</c> whose <c>playerId</c> equals the LOCAL
        /// player's own entity id (VERIFIED: CraftingStationBehaviour.OnStartInteraction
        /// early-returns when <c>playerId != PlayerCraftingStationData.CraftingEntityId</c>,
        /// which resolves to the local player entity). So the server echoes that event
        /// back on the shipyard, addressed to the player who interacted, carrying an
        /// EMPTY schematic id (open the UI; do not start a real craft).
        ///
        /// MULTIPLAYER-SAFE: this is a single, event-driven echo in response to one
        /// client interaction - no per-frame state, no relay, no shared mutable
        /// structure. The shipyard's interaction components are one-time seeds.
        ///
        /// A no-op when the feature is off or the target is not a placed shipyard.
        /// Returns true when the echo was sent.
        /// </summary>
        public bool OpenShipyardConsole(ENetPeerHandle peer, long playerEntityId, long shipyardEntityId)
        {
            if (!Enabled)
            {
                return false;
            }

            if (!PlacedShipyards.IsPlacedShipyard(shipyardEntityId))
            {
                // The player interacted with something that is not a shipyard we
                // placed (a helm, a rock, a world prop) - not our event to answer.
                return false;
            }

            // Remember which yard this player has a console open on, so a later FRAME
            // DESIGNS rename (which carries no editorId) can re-emit this same open
            // signal to rebuild the list. Single choke point for every console open.
            Multiplayer.Ship.ShipDesignStore.For(playerEntityId).NoteConsole(shipyardEntityId);

            // playerId = the interacting player's OWN entity id, NOT the shipyard's.
            CraftingStationClientState.Update update = new CraftingStationClientState.Update();
            update.AddPlayerStartCrafting(new PlayerStartCrafting(new EntityId(playerEntityId), ""));

            bool ok = SendOPHelper.SendComponentUpdateOp(
                peer, shipyardEntityId,
                new List<uint> { Deployables.CraftingStationClientStateComponentId },
                new List<object> { update });

            if (ok)
            {
                Console.WriteLine("[info] placement: shipyard console " + shipyardEntityId
                    + " opened the ship-build UI for player " + playerEntityId + ".");
            }
            else
            {
                Console.WriteLine("[warning] placement: failed to echo 1005 PlayerStartCrafting for shipyard "
                    + shipyardEntityId + " to player " + playerEntityId
                    + " (is 1005 checked out on that client? the shipyard must have seeded it).");
            }

            return ok;
        }

        private void PrewarmAsset(string assetName)
        {
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                SendOPHelper.SendAssetLoadRequestOP(peer, "notNeeded?", assetName, AssetContext);
            }
        }

        // -----------------------------------------------------------------
        // 2. SPAWN the confirmed deployable to every connected peer
        // -----------------------------------------------------------------

        /// <summary>
        /// Registers and spawns ONE deployed world entity of <paramref name="def"/> at
        /// the placed transform, broadcasting AssetLoadRequest -> AddEntity -> seed(s)
        /// to every currently-connected peer. Returns the allocated shared entity id,
        /// or null if nothing could be sent.
        ///
        /// The entity is added to the world-entity registry so its 190602 (and, for a
        /// shipyard, 1205) seeds resolve through the normal serializer path; late
        /// joiners in the SAME session are a documented gap (the connect-time spawn
        /// plan is built once and not re-walked), so this reaches the peers present at
        /// confirm time.
        /// </summary>
        public long? SpawnPlacedDeployable(
            DeployableDef def,
            FixedPointPosition position,
            uint packedRotation,
            string ownerCharacterUid)
        {
            if (!Enabled)
            {
                return null;
            }

            int sequence = _placedSequence++;
            string key = def.KeyPrefix + ":" + sequence;

            WorldEntity registration =
                new WorldEntity(
                    key,
                    def.AssetName,
                    AssetContext,
                    position,
                    seedComponents: def.SeedComponents.ToArray(),
                    order: SpawnOrder.AfterPlayer,
                    packedRotation: packedRotation);

            WorldsAdriftRebornGameServer.WorldEntities.Register(registration);
            long entityId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(registration);

            // A shipyard also carries 1205 ShipyardState, seeded from the placed-
            // structure ledger by ComponentsSerializer's 1205 branch. Record it there
            // so that branch renders it deployed rather than an inert prop. Other
            // deployables seed 190602 only and need no ledger entry (the 190602 branch
            // reads their transform straight from the world-entity registry).
            if (def.SeedComponents.Contains(Deployables.ShipyardStateComponentId))
            {
                PlacedShipyards.Register(entityId, ownerCharacterUid);
            }

            int reached = 0;
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                if (BroadcastToPeer(peer, entityId, registration))
                {
                    reached++;
                }
            }

            Console.WriteLine("[info] placement: DEPLOYED '" + def.ItemTypeId + "' as entity " + entityId
                + " (asset '" + def.AssetName + "'" + (def.AssetVerified ? "" : ", UNVERIFIED asset - may render invisible")
                + ") at " + position + " (packed rot " + packedRotation + ", owner '" + ownerCharacterUid
                + "'), sent to " + reached + " peer(s).");

            if (reached == 0)
            {
                Console.WriteLine("[warning] placement: deployable " + entityId
                    + " was registered but reached no fully-connected peer.");
            }

            return entityId;
        }

        private bool BroadcastToPeer(
            ENetPeerHandle peer,
            long entityId,
            WorldEntity registration)
        {
            // Asset request first (idempotent - already pre-warmed at start), then
            // AddEntity, then the all-or-nothing seed push. Mirrors AddWorldEntity's
            // body, fanned out to the live peer set instead of one plan-walking peer.
            SendOPHelper.SendAssetLoadRequestOP(peer, "notNeeded?", registration.AssetName, registration.AssetContext);

            if (!SendOPHelper.SendAddEntityOP(peer, entityId, registration.AssetName, registration.AssetContext))
            {
                Console.WriteLine("[error] placement: failed to send AddEntityOp for deployable " + entityId + " to a peer.");
                return false;
            }

            List<Structs.Structs.InterestOverride> seeds = registration.SeedComponents
                .Select(id => new Structs.Structs.InterestOverride(id, 1))
                .ToList();

            if (!SendOPHelper.SendAddComponentOp(peer, entityId, seeds, true))
            {
                Console.WriteLine("[error] placement: deployable " + entityId
                    + " was created on a peer but its seed components were dropped; it will render inert.");
            }

            return true;
        }

        // -----------------------------------------------------------------
        // 3. DEBUG file-poll trigger
        // -----------------------------------------------------------------

        /// <summary>
        /// Reads and consumes the trigger file, starting placement for a player's
        /// hotbar (or any) deployable. The file may name a player entity id; empty
        /// means "the first connected player". The same file-not-keypress shape as the
        /// ship/teleport triggers, because the server runs headless. Cheap when idle; a
        /// no-op when disabled.
        /// </summary>
        public void PollTrigger()
        {
            if (!Enabled)
            {
                return;
            }

            if (_sinceLastPoll.Elapsed < PollInterval)
            {
                return;
            }
            _sinceLastPoll.Restart();

            string text;
            try
            {
                if (!File.Exists(_triggerFile))
                {
                    return;
                }

                text = File.ReadAllText(_triggerFile);
                File.Delete(_triggerFile);
            }
            catch (Exception e)
            {
                Console.WriteLine("[warning] placement: could not read " + _triggerFile + ": " + e.Message);
                return;
            }

            long? requestedEntity = null;
            string trimmed = text.Trim();
            if (trimmed.Length > 0 && long.TryParse(trimmed, out long parsed))
            {
                requestedEntity = parsed;
            }

            TryStartForDebug(requestedEntity);
        }

        private void TryStartForDebug(long? requestedEntity)
        {
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                ulong peerId = PeerIdentity.IdOf(peer);
                long? entityId = WorldsAdriftRebornGameServer.Players.EntityOf(peerId);
                if (!entityId.HasValue)
                {
                    continue;
                }

                if (requestedEntity.HasValue && entityId.Value != requestedEntity.Value)
                {
                    continue;
                }

                InventoryItem? deployable = FindDeployable(entityId.Value);
                if (deployable == null)
                {
                    Console.WriteLine("[warning] placement: entity " + entityId.Value
                        + " has no deployable item to place (craft one - e.g. a shipyard - and put it on the hotbar first).");
                    if (requestedEntity.HasValue)
                    {
                        return;
                    }
                    continue;
                }

                StartPlacing(peer, entityId.Value, deployable.ItemId, deployable.ItemTypeId);
                return;
            }

            Console.WriteLine("[warning] placement: debug trigger found no eligible player"
                + (requestedEntity.HasValue ? " for entity " + requestedEntity.Value : "") + ".");
        }

        // -----------------------------------------------------------------
        // Shared helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// The deployable item to place: one on the hotbar first (that is what the
        /// player selects), otherwise any deployable in the bag. Any registered
        /// deployable type, not just a shipyard.
        /// </summary>
        internal static InventoryItem? FindDeployable(long entityId)
        {
            InventoryModel model = InventoryService.ForEntity(entityId);

            for (int slot = 0; slot < InventoryModel.HotBarSlots; slot++)
            {
                InventoryItem? onBar = model.OnHotBar(slot);
                if (onBar != null && Deployables.IsDeployable(onBar.ItemTypeId))
                {
                    return onBar;
                }
            }

            foreach (InventoryItem item in model.Items)
            {
                if (Deployables.IsDeployable(item.ItemTypeId))
                {
                    return item;
                }
            }

            return null;
        }

        private static IEnumerable<ENetPeerHandle> ConnectedPeers()
        {
            return PeerManager.Instance.playerState.Keys.ToList();
        }
    }
}

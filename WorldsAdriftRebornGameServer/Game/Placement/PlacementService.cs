using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bossa.Travellers.Items;
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
    /// The whole server side of deployable placement, in one seam. It does three
    /// jobs and nothing else:
    ///
    ///   1. START a placement: write 1019 StartPlacingItemEvent onto a player so
    ///      their client enters the native placement preview, and PRE-WARM the
    ///      Shipyard bundle on every connected peer (seconds before the confirm, so
    ///      the runtime AddEntity at confirm time never races a bundle load).
    ///   2. SPAWN the confirmed shipyard as a shared world entity every connected
    ///      peer sees (there is no existing runtime-AddEntity broadcast on this
    ///      server - spawning was connect-time only - so this is that broadcast).
    ///   3. Offer a DEBUG file-poll trigger (WAREBORN_PLACEMENT_FILE), the same
    ///      shape as the ship/teleport triggers, so the pipeline is testable even
    ///      if the native 1211 use-press trigger does not fire on a live client.
    ///
    /// The 1017 confirm handler and the 1211 use-press trigger both call in here;
    /// the pure validation and rotation packing live in the Multiplayer assembly.
    ///
    /// Gated behind WAREBORN_PLACEMENT=1: when off, every entry point returns
    /// immediately, so an un-flagged server behaves exactly as before.
    /// </summary>
    internal sealed class PlacementService
    {
        internal const string ShipyardAsset = "Shipyard";
        internal const string ShipyardItemType = "shipyard";
        private const string PlacementType = "Terrain";
        private const string AssetContext = "notNeeded?";
        private const float TimeToPlace = 1.0f;

        private const uint ItemPlacementAgentStateComponentId = 1019;
        private const uint TransformStateComponentId = 190602;
        private const uint ShipyardStateComponentId = 1205;

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
        /// Puts a player's client into placement preview for the shipyard with
        /// inventory id <paramref name="itemId"/>: writes the 1019 state + fires
        /// StartPlacingItemEvent to that peer, and pre-warms the bundle everywhere.
        /// Returns true if the start event was sent. A no-op when disabled, when the
        /// player is already mid-placement, or when the send fails.
        /// </summary>
        public bool StartPlacing(ENetPeerHandle peer, long playerEntityId, int itemId)
        {
            if (!Enabled)
            {
                return false;
            }

            if (IsPlacing(playerEntityId))
            {
                return false;
            }

            // The server SETS the 1019 state (Placing / PlacingItemId / type /
            // consume) AND fires the StartPlacingItemEvent in one update. The state's
            // PlacingItemId is what the client echoes back as PlaceItemEvent.
            // placeableItemId on confirm, so it MUST be the real inventory id or the
            // server cannot consume the right item.
            ItemPlacementAgentState.Update update = new ItemPlacementAgentState.Update();
            update.SetPlacing(true);
            update.SetPlacingItemId(itemId);
            update.SetPlacingType(PlacementType);
            update.SetConsumeItemOnPlacement(true);
            update.AddStartPlacingItemEvent(
                new StartPlacingItemEvent(itemId, ShipyardAsset, PlacementType, TimeToPlace));

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
            PrewarmShipyardAsset();

            Console.WriteLine("[info] placement: entity " + playerEntityId + " is now placing shipyard item "
                + itemId + " (prefab '" + ShipyardAsset + "', type '" + PlacementType + "', "
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

        private void PrewarmShipyardAsset()
        {
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                SendOPHelper.SendAssetLoadRequestOP(peer, "notNeeded?", ShipyardAsset, AssetContext);
            }
        }

        // -----------------------------------------------------------------
        // 2. SPAWN the confirmed shipyard to every connected peer
        // -----------------------------------------------------------------

        /// <summary>
        /// Registers and spawns ONE deployed shipyard world entity at the placed
        /// transform, broadcasting AssetLoadRequest -> AddEntity -> seed(190602,1205)
        /// to every currently-connected peer. Returns the allocated shared entity id,
        /// or null if nothing could be sent.
        ///
        /// The entity is added to the world-entity registry so its 190602/1205 seeds
        /// resolve through the normal serializer path; late joiners in the SAME
        /// session are a documented gap (the connect-time spawn plan is built once
        /// and not re-walked), so this reaches the peers present at confirm time.
        /// </summary>
        public long? SpawnPlacedShipyard(
            FixedPointPosition position,
            uint packedRotation,
            string ownerCharacterUid)
        {
            if (!Enabled)
            {
                return null;
            }

            int sequence = PlacedShipyards.NextSequence();
            string key = "placed-shipyard:" + sequence;

            WorldEntity registration =
                new WorldEntity(
                    key,
                    ShipyardAsset,
                    AssetContext,
                    position,
                    seedComponents: new uint[] { TransformStateComponentId, ShipyardStateComponentId },
                    order: SpawnOrder.AfterPlayer,
                    packedRotation: packedRotation);

            WorldsAdriftRebornGameServer.WorldEntities.Register(registration);
            long entityId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(registration);

            PlacedShipyards.Register(entityId, ownerCharacterUid);

            int reached = 0;
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                if (BroadcastShipyardToPeer(peer, entityId, registration))
                {
                    reached++;
                }
            }

            Console.WriteLine("[info] placement: DEPLOYED shipyard '" + key + "' as entity " + entityId
                + " at " + position + " (packed rot " + packedRotation + ", owner '" + ownerCharacterUid
                + "'), sent to " + reached + " peer(s).");

            if (reached == 0)
            {
                Console.WriteLine("[warning] placement: shipyard " + entityId
                    + " was registered but reached no fully-connected peer.");
            }

            return entityId;
        }

        private bool BroadcastShipyardToPeer(
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
                Console.WriteLine("[error] placement: failed to send AddEntityOp for shipyard " + entityId + " to a peer.");
                return false;
            }

            List<Structs.Structs.InterestOverride> seeds = registration.SeedComponents
                .Select(id => new Structs.Structs.InterestOverride(id, 1))
                .ToList();

            if (!SendOPHelper.SendAddComponentOp(peer, entityId, seeds, true))
            {
                Console.WriteLine("[error] placement: shipyard " + entityId
                    + " was created on a peer but its 190602/1205 seed components were dropped; it will render inert.");
            }

            return true;
        }

        // -----------------------------------------------------------------
        // 3. DEBUG file-poll trigger
        // -----------------------------------------------------------------

        /// <summary>
        /// Reads and consumes the trigger file, starting placement for a player's
        /// hotbar (or any) shipyard. The file may name a player entity id; empty
        /// means "the first connected player". The same file-not-keypress shape as
        /// the ship/teleport triggers, because the server runs headless. Cheap when
        /// idle; a no-op when disabled.
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

                InventoryItem? shipyard = FindShipyard(entityId.Value);
                if (shipyard == null)
                {
                    Console.WriteLine("[warning] placement: entity " + entityId.Value
                        + " has no '" + ShipyardItemType + "' item to place (craft one and put it on the hotbar first).");
                    if (requestedEntity.HasValue)
                    {
                        return;
                    }
                    continue;
                }

                StartPlacing(peer, entityId.Value, shipyard.ItemId);
                return;
            }

            Console.WriteLine("[warning] placement: debug trigger found no eligible player"
                + (requestedEntity.HasValue ? " for entity " + requestedEntity.Value : "") + ".");
        }

        // -----------------------------------------------------------------
        // Shared helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// The shipyard item to place: one on the hotbar first (that is what the
        /// quest asks the player to select), otherwise any shipyard in the bag.
        /// </summary>
        internal static InventoryItem? FindShipyard(long entityId)
        {
            InventoryModel model = InventoryService.ForEntity(entityId);

            for (int slot = 0; slot < InventoryModel.HotBarSlots; slot++)
            {
                InventoryItem? onBar = model.OnHotBar(slot);
                if (onBar != null && string.Equals(onBar.ItemTypeId, ShipyardItemType, StringComparison.Ordinal))
                {
                    return onBar;
                }
            }

            foreach (InventoryItem item in model.Items)
            {
                if (string.Equals(item.ItemTypeId, ShipyardItemType, StringComparison.Ordinal))
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

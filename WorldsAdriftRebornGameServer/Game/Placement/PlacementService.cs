using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bossa.Travellers.Craftingstation;
using Bossa.Travellers.Items;
using Bossa.Travellers.Ship.Lock;
using Improbable;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Crafting;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Game.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
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

        /// <summary>1219 ShipyardVisitorState - the player's own "which shipyard do I have build access to".</summary>
        private const uint ShipyardVisitorStateComponentId = 1219;

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

            // GRANT SHIP-BUILD ACCESS. Interacting with a shipyard console is exactly the
            // moment the client expects to "gain access": PlayerScannerTool.DeployItem
            // refuses the crafted-part lift with "Interact with shipyard to gain access."
            // whenever its Shipyard is null (PlayerScannerTool.cs:455-457), and that
            // Shipyard resolves purely from the player's own 1219 ShipyardVisitorState
            // .ShipyardId being a valid entity id (ShipyardVisitorVisualizer.cs:130-133).
            // So record the grant (the 1219 serve branch reports it on every re-checkout)
            // and PUSH a live 1219 update now so the player's client resolves the yard
            // without waiting for a re-checkout. Per-player, event-driven, one field.
            GrantShipyardBuildAccess(peer, playerEntityId, shipyardEntityId);

            return EmitPlayerStartCrafting(peer, playerEntityId, shipyardEntityId, "shipyard", "ship-build UI",
                resetToIdle: false);
        }

        /// <summary>
        /// Records the per-player build-access grant and pushes a live 1219
        /// <c>ShipyardVisitorState</c> update carrying the shipyard's entity id to the
        /// interacting player, so their client's <c>ShipyardVisitorVisualizer</c> resolves
        /// the yard (<c>ShipyardVisitorVisualizer.cs:130-133</c>) and
        /// <c>PlayerScannerTool.Shipyard</c> becomes non-null - clearing the "Interact
        /// with shipyard to gain access." refusal on the crafted-part lift. The ledger
        /// makes the grant survive a re-checkout of the player's own 1219 (the serve
        /// branch reads it), and the push makes it take effect immediately.
        ///
        /// The 1219 component is already present on the player (served on request as an
        /// <c>EntityId(0)</c> stub in ComponentsSerializer), so this update lands on a
        /// component the client holds. Best-effort: a dropped push is corrected by the
        /// ledger-backed serve on the next checkout.
        /// </summary>
        private void GrantShipyardBuildAccess(ENetPeerHandle peer, long playerEntityId, long shipyardEntityId)
        {
            Multiplayer.Placement.ShipyardBuildAccess.Shared.Grant(playerEntityId, shipyardEntityId);

            ShipyardVisitorState.Update update = new ShipyardVisitorState.Update();
            update.SetShipyardId(new EntityId(shipyardEntityId));

            if (SendOPHelper.SendComponentUpdateOp(
                    peer, playerEntityId,
                    new List<uint> { ShipyardVisitorStateComponentId },
                    new List<object> { update }))
            {
                Console.WriteLine("[info] placement: granted shipyard build access to player " + playerEntityId
                    + " for shipyard " + shipyardEntityId + " (pushed 1219 ShipyardId); the crafted-part lift"
                    + " no longer reports \"Interact with shipyard to gain access.\".");
            }
            else
            {
                Console.WriteLine("[warning] placement: recorded build-access grant for player " + playerEntityId
                    + " -> shipyard " + shipyardEntityId + " but the live 1219 push failed; it will still apply"
                    + " on the next checkout via the ledger-backed serve.");
            }
        }

        /// <summary>
        /// Opens the GENERIC PARTS crafting UI on the interacting player's client after
        /// they complete the "Craft" interaction on a placed Assembly Station.
        ///
        /// This is the crafting-station twin of <see cref="OpenShipyardConsole"/> and
        /// sends the IDENTICAL signal - a 1005 CraftingStationClientState.PlayerStartCrafting
        /// echoed back on the station entity, addressed to the interacting player. What
        /// differs is NOT the server signal but the PREFAB: CraftingStationBehaviour's
        /// OnStartInteraction switches on the prefab-baked _craftingCategory (VERIFIED in
        /// the decompile), so the AssemblyStation prefab (category CraftingStation) opens
        /// MainInventoryUIState.ItemCraft (the parts tab) where the Shipyard prefab opens
        /// ShipCraft. So one server path serves both; the entity's asset decides the UI.
        /// It does NOT touch ShipDesignStore - a parts station has no frame-design console.
        ///
        /// MULTIPLAYER-SAFE: a single, event-driven, per-player echo in response to one
        /// client interaction - no per-frame state, no relay. A no-op when the feature is
        /// off or the target is not a placed crafting station. Returns true when sent.
        /// </summary>
        public bool OpenCraftingStationConsole(ENetPeerHandle peer, long playerEntityId, long stationEntityId)
        {
            if (!Enabled)
            {
                return false;
            }

            if (!PlacedCraftingStations.IsPlacedCraftingStation(stationEntityId))
            {
                return false;
            }

            // resetToIdle: the parts-crafting UI (CraftingStationCraftingUI /
            // CraftingStationSchematicList) is fragile against a STALE loaded schematic on
            // the station's 1005. On open, CraftingStationBehaviour.OnStartInteraction runs
            // RefreshCraftingData and CheckIfSchematicLoaded against whatever
            // CraftingStationClientState the station currently holds. If clientSchematicId
            // is non-empty (e.g. left over from a previous station craft's 1005 push) the
            // client re-selects that schematic - CraftingStationSchematicList.SelectSchematic
            // calls CategoryPressed(schematic.CraftingCategoryEnum) and dereferences the
            // returned slot with NO null check (VERIFIED CraftingStationSchematicList.cs:365-385).
            // If that category has no slot in the tab it just built, that is an uncaught NRE
            // that blanks the whole Crafting tab. Its sibling SyncCraftingItems indexes the
            // slotted-materials list up to the loaded schematic's requirement count with no
            // bounds check (CraftingStationData.cs:283-285), an ArgumentOutOfRangeException
            // when the two are momentarily inconsistent. Re-asserting the idle seed shape
            // (empty schematic + empty slots + closed countdown) alongside the open echo
            // makes the station always open in the crash-safe NoSchematic state: an empty
            // clientSchematicId means LoadedSchematic is null, so SelectSchematic is never
            // reached and SyncCraftingItems early-returns. The client picking a recipe then
            // drives real values back through the 1003 handler exactly as before.
            //
            // BUT the interact-open fires REPEATEDLY while the player stands at the console
            // (the client re-sends the Craft interaction), so an UNCONDITIONAL idle reset
            // wipes an in-progress multi-slot craft's recipe + slots mid-fill - the client's
            // OnSchematicChanged("") clears LoadedSchematic and the empty slotted list blanks
            // the panel, stranding every slot after the first. So reset ONLY on a fresh open
            // (no craft in progress bound to THIS station); when this player already has an
            // active craft here, re-echo WITHOUT the reset - the station's 1005 already holds
            // the live slot state (the per-slot 1003 fills now push to the station), so the
            // re-open preserves it. A stale schematic on a genuine first open is still reset,
            // because a fresh session has no schematic and no station binding.
            CraftSession session = CraftSessions.For(playerEntityId, stationEntityId);
            bool resetToIdle = StationCraftRouting.ShouldResetToIdleOnOpen(
                session.SchematicId, session.StationEntityId, stationEntityId);

            return EmitPlayerStartCrafting(peer, playerEntityId, stationEntityId, "crafting station", "parts crafting UI",
                resetToIdle);
        }

        /// <summary>
        /// Echoes the 1005 CraftingStationClientState.PlayerStartCrafting event back on a
        /// placed crafting-station entity (<paramref name="stationEntityId"/>), addressed
        /// to the interacting player (<paramref name="playerEntityId"/>) with an EMPTY
        /// schematic id (open the UI; do not start a real craft). The single choke point
        /// both the shipyard console and the parts-station console open through, so the
        /// on-the-wire signal is byte-identical and the prefab's baked category is the
        /// only thing that decides which UI the client opens.
        /// </summary>
        private bool EmitPlayerStartCrafting(
            ENetPeerHandle peer, long playerEntityId, long stationEntityId, string kind, string uiName,
            bool resetToIdle)
        {
            // playerId = the interacting player's OWN entity id, NOT the station's.
            CraftingStationClientState.Update update = new CraftingStationClientState.Update();
            update.AddPlayerStartCrafting(new PlayerStartCrafting(new EntityId(playerEntityId), ""));

            if (resetToIdle)
            {
                // Re-assert the crash-safe idle seed shape in the SAME update that opens the
                // UI, so the parts station can never open on a stale loaded schematic (see
                // OpenCraftingStationConsole). Empty schematic id + empty slotted list + a
                // closed (-1) countdown is exactly what componentId==1005 seeds; clientSchematicId
                // "" makes LoadedSchematic null, which is the branch the client renders safely.
                // The shipyard path does NOT reset (resetToIdle:false) so the ship-build UI's
                // existing open behaviour is unchanged.
                update.SetClientSchematicId("");
                update.SetSchematicOwner("");
                update.SetSlottedMaterials(new Improbable.Collections.List<SlottedMaterial>());
                update.SetItemReadyInSeconds(-1);
                update.SetCurrentWeight(0f);
            }

            bool ok = SendOPHelper.SendComponentUpdateOp(
                peer, stationEntityId,
                new List<uint> { Deployables.CraftingStationClientStateComponentId },
                new List<object> { update });

            if (ok)
            {
                Console.WriteLine("[info] placement: " + kind + " console " + stationEntityId
                    + " opened the " + uiName + " for player " + playerEntityId + ".");
            }
            else
            {
                Console.WriteLine("[warning] placement: failed to echo 1005 PlayerStartCrafting for " + kind + " "
                    + stationEntityId + " to player " + playerEntityId
                    + " (is 1005 checked out on that client? the station must have seeded it).");
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

            (WorldEntity registration, long entityId) =
                RegisterDeployable(def, position, packedRotation, ownerCharacterUid, livePlacement: true);

            // PERSIST BEFORE BROADCASTING. A crash between here and the last peer send
            // still leaves the record on disk, so the deployable reappears next boot;
            // recording one that reaches no peer is harmless, because the boot restore
            // is what makes it universal anyway.
            WorldStatePersistence.RecordPlacedDeployable(
                def.ItemTypeId, position, packedRotation, ownerCharacterUid);

            int reached = 0;
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                if (BroadcastToPeer(peer, entityId, registration))
                {
                    reached++;
                }
            }

            // SHIPYARD FOLD-OUT (3.1): a live placement was seeded deployed=false (above,
            // via RegisterDeployable) so the placer's ShipyardVisualizer plays the
            // Shipyard_Deploy panel/leg fold-out. After the clip has played, flip the 1205
            // seed to deployed=true and push it, so any LATER checkout snaps instead of
            // re-animating. One-shot, per-entity, drained on the main loop - not a relay.
            if (def.SeedComponents.Contains(Deployables.ShipyardStateComponentId))
            {
                DeployableDeployFlip.ScheduleShipyardFoldOut(entityId);
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

        /// <summary>
        /// Registers ONE deployed world entity of <paramref name="def"/> and seeds its
        /// ledger, allocating its shared entity id - the part common to a runtime
        /// placement and a boot restore, so the two cannot build a different entity.
        /// The WorldEntity itself is built by <see cref="PlacedDeployableSpawnPlan"/>,
        /// the single source of truth for a placed deployable's asset + seed set. Does
        /// NOT broadcast: the caller sends (runtime) or leaves it to the spawn plan (boot).
        /// </summary>
        private (WorldEntity Registration, long EntityId) RegisterDeployable(
            DeployableDef def,
            FixedPointPosition position,
            uint packedRotation,
            string ownerCharacterUid,
            bool livePlacement)
        {
            WorldEntity registration =
                PlacedDeployableSpawnPlan.WorldEntityFor(def, _placedSequence++, position, packedRotation);

            WorldsAdriftRebornGameServer.WorldEntities.Register(registration);
            long entityId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(registration);
            if (livePlacement)
            {
                LocalDomainOwnership.MoveToIsland(
                    WorldsAdriftRebornGameServer.DomainHost, entityId, registration.Position);
            }

            // A shipyard also carries 1205 ShipyardState, seeded from the placed-
            // structure ledger by ComponentsSerializer's 1205 branch. Record it there
            // so that branch renders it deployed rather than an inert prop. Other
            // deployables seed 190602 only and need no ledger entry (the 190602 branch
            // reads their transform straight from the world-entity registry).
            if (def.SeedComponents.Contains(Deployables.ShipyardStateComponentId))
            {
                // FOLD-OUT SEED (3.1): a LIVE placement is seeded deployed=false so the
                // client plays the Shipyard_Deploy clip; a BOOT RESTORE (already deployed
                // last session) is seeded deployed=true so it snaps. The live case's flip
                // back to true is scheduled by SpawnPlacedDeployable after broadcast.
                bool deployed = Multiplayer.Placement.ShipyardDeployPolicy.InitialDeployed(livePlacement);
                PlacedShipyards.Register(entityId, ownerCharacterUid, deployed: deployed);
            }

            // A generic crafting station (the Assembly Station) records its placed id in
            // its own ledger so the 1210 verb branch seeds Craft (not PickUp) and the
            // interact handler answers with the parts-UI 1005 echo. It seeds no ledger-
            // sourced state - its 1004/1005 are entity-agnostic idle defaults - so unlike
            // the shipyard the ledger is pure membership. Runtime placement and boot
            // restore both reach here, so a restored station is recognised identically.
            else if (def.IsCraftingStation)
            {
                PlacedCraftingStations.Register(entityId, ownerCharacterUid);
            }

            return (registration, entityId);
        }

        /// <summary>
        /// Re-creates ONE persisted deployable at boot: resolves its
        /// <see cref="DeployableDef"/> from the stored item type and registers it via
        /// the SAME <see cref="RegisterDeployable"/> core a runtime placement uses, so it
        /// is byte-identical and the spawn plan serves it to every joining client.
        /// Returns the allocated entity id, or null when placement is off (nothing to
        /// interact with) or the stored item type is no longer a known deployable.
        ///
        /// Does not broadcast (there are no peers at boot) and does not re-persist (the
        /// record it came from is already on disk).
        /// </summary>
        public long? RestorePlacedDeployable(PlacedDeployableRecord record)
        {
            if (!Enabled)
            {
                Console.WriteLine("[info] placement: NOT restoring persisted deployable '"
                    + record.ItemTypeId + "' because placement is off (set WAREBORN_PLACEMENT=1).");
                return null;
            }

            if (!Deployables.TryGet(record.ItemTypeId, out DeployableDef def))
            {
                Console.WriteLine("[warning] placement: persisted deployable '" + record.ItemTypeId
                    + "' is no longer a registered deployable; skipping its restore.");
                return null;
            }

            (_, long entityId) = RegisterDeployable(
                def, record.Position(), record.PackedRotation, record.OwnerCharacterUid, livePlacement: false);

            Console.WriteLine("[info] placement: RESTORED '" + def.ItemTypeId + "' as entity " + entityId
                + " at " + record.Position() + " (owner '" + record.OwnerCharacterUid
                + "'); it will be served to every joining client via the spawn plan.");

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
            WorldsAdriftRebornGameServer.SentEntities.MarkSent(peer, entityId);

            List<Structs.Structs.InterestOverride> seeds = registration.SeedComponents
                .Select(id => new Structs.Structs.InterestOverride(id, 1))
                .ToList();

            // Record the seed push in the served-component ledger, exactly as the
            // connect-time spawn plan (AddWorldEntity) now does, so this peer's later
            // interest re-checkout does not re-ADD the shipyard's already-delivered
            // 1205/1210/1004/1005/1206 - the duplicate AddComponent that crashes the
            // client. This is the LIVE placer's path (the joiner's is the spawn plan);
            // both must mark served or the re-checkout re-seeds and the entity store
            // throws "component already exists".
            List<uint> seedServed = new List<uint>();
            if (!SendOPHelper.SendAddComponentOp(peer, entityId, seeds, true, seedServed))
            {
                Console.WriteLine("[error] placement: deployable " + entityId
                    + " was created on a peer but its seed components were dropped; it will render inert.");
            }
            WorldsAdriftRebornGameServer.ServedComponents.MarkServed(peer, entityId, seedServed);

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

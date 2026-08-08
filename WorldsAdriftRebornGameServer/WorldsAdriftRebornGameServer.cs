using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Improbable.Worker;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game;
using WorldsAdriftRebornGameServer.Game.Components;
using WorldsAdriftRebornGameServer.Game.Components.Update;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;
using WorldsAdriftRebornGameServer.Multiplayer;
using static WorldsAdriftRebornGameServer.DLLCommunication.EnetLayer;

namespace WorldsAdriftRebornGameServer
{
    internal class WorldsAdriftRebornGameServer
    {
        private static bool keepRunning = true;
        [PInvoke(typeof(EnetLayer.ENet_Poll_Callback))]
        private unsafe static void OnNewClientConnected(IntPtr peer )
        {
            ENetPeerHandle ePeer = new ENetPeerHandle(peer);
            if (!ePeer.IsInvalid)
            {
                // Track before anything else: every later lookup resolves through
                // PeerIdentity so only one handle per peer is ever alive.
                ePeer = PeerIdentity.Instance.Track(peer, ePeer);

                if (!PeerManager.Instance.playerState.ContainsKey(ePeer))
                {
                    PeerManager.Instance.playerState.Add(ePeer, new Dictionary<int, PlayerSyncStatus> { { 0, new PlayerSyncStatus() } });
                }
                Console.WriteLine("[info] " + Describe(peer) + " connected. players now: " + PeerManager.Instance.playerState.Count);
            }
        }
        [PInvoke(typeof(EnetLayer.ENet_Poll_Callback))]
        private unsafe static void OnClientDisconnected(IntPtr peer )
        {
            ENetPeerHandle? ePeer = PeerIdentity.Instance.Resolve(peer);
            if (ePeer == null)
            {
                Console.WriteLine("[warning] a disconnect arrived for untracked " + Describe(peer) + ", ignoring.");
                return;
            }

            long? ownEntity = ForgetPeer(ePeer);

            Console.WriteLine("[info] " + Describe(peer) + " disconnected"
                + (ownEntity.HasValue ? " (entity " + ownEntity.Value + ")" : " (never spawned)")
                + ". players now: " + PeerManager.Instance.playerState.Count + ".");
        }

        /// <summary>
        /// Drops EVERY piece of per-peer state, in one place.
        ///
        /// It exists because per-peer state was spread over ten collections and
        /// the disconnect path cleaned five. The mirror's five bookkeeping
        /// dictionaries were never touched, which leaked for the lifetime of the
        /// process and - worse - could misattribute a stale batch to whoever ENet
        /// later handed the recycled peer slot to. Those five are now two records
        /// inside <see cref="MirrorSchedule"/>, whose <c>Forget</c> is unit-tested
        /// to empty all of them; anything new keyed by peer belongs in here.
        ///
        /// Returns the entity the peer owned, or null if it never spawned one.
        /// </summary>
        private static long? ForgetPeer(ENetPeerHandle peer)
        {
            ulong peerId = PeerIdentity.IdOf(peer);
            long? ownEntity = Players.EntityOf(peerId);

            // Unregister first: this is what actually matters, because it stops
            // relaying updates to and from a peer that is gone.
            //
            // The despawn intents cannot be sent yet. There is no wire message for
            // entity removal: ENetChannel has no such channel, no RemoveEntityOp
            // proto exists, and the SDK's RegisterRemoveEntityCallback is still an
            // unimplemented TODO in Exports.cpp. Until that exists a departed
            // player leaves a stale avatar behind, which is cosmetic rather than
            // blocking.
            IReadOnlyList<MirrorIntent> despawns = Mirror.OnLeave(peerId);
            if (despawns.Count > 0)
            {
                Console.WriteLine("[warning] " + despawns.Count + " avatar(s) of entity "
                    + (ownEntity.HasValue ? ownEntity.Value.ToString() : "?")
                    + " cannot be despawned: entity removal is not implemented on the wire. Stale avatar(s) will remain.");
            }

            // Parked and pending-resend mirror ops for a peer that is gone: there
            // is nobody left to send them to.
            Schedule.Forget(peerId);

            // Drop the departed player's stored appearance so the store does not
            // grow across reconnects (entity ids are handed out monotonically, so
            // a stale record is never re-read, only wasted memory).
            if (ownEntity.HasValue)
            {
                Appearances.Forget(ownEntity.Value);

                // Save, then drop, the departed player's inventory. The save is
                // the last chance a session gets: every mutation already wrote
                // through the push seam, but a server-side grant that happened
                // between the last push and the disconnect would otherwise be
                // lost. It is a no-op for a player whose character uid never
                // arrived - those inventories are session-scoped by design and
                // deliberately unsaveable. Do this BEFORE dropping any other
                // per-player state, so a save can never race a teardown.
                Game.Inventory.InventoryService.Forget(ownEntity.Value);

                Teleports.Forget(ownEntity.Value);
            }

            // Drop this peer's slice of the component map. ForgetPeer's own
            // docblock claims to clean every piece of per-peer state and this
            // one was never in it, so a departed peer's stored component
            // references stayed live for the lifetime of the process - and the
            // inventory push seam iterates exactly this map to decide who to
            // send to, so a stale entry is a send to a peer that is gone.
            GameState.Instance.ComponentMap.Remove(peer);

            PeerManager.Instance.playerState.Remove(peer);
            PeerManager.Instance.clientSetupState.Remove(peer);

            // Last, because every lookup above resolves through it.
            PeerIdentity.Instance.Forget(peer.DangerousGetHandle());

            return ownEntity;
        }

        /// <summary>
        /// A peer's identity as it appears in the log. The raw ENetPeer* is the
        /// only identity the server has, so logging it is what makes two
        /// concurrent players' lines tellable apart.
        /// </summary>
        private static string Describe(IntPtr peer)
        {
            return "peer 0x" + ((ulong)peer.ToInt64()).ToString("x");
        }

        private static string Describe(ulong peerId)
        {
            return "peer 0x" + peerId.ToString("x");
        }

        /// <summary>
        /// Registers a freshly spawned player and spawns their avatar on every
        /// other client, and every other player's avatar on theirs.
        ///
        /// A remote avatar is seeded with TransformState only, so it appears in
        /// the right place. The client then asks for whatever else it wants, and
        /// the existing SEND_COMPONENT_INTEREST path serves it; logs confirm both
        /// clients do request components for each other's entities unprompted.
        ///
        /// It must NOT be sent the local player's component set. Doing that gave
        /// each client two entities carrying player state and detached the
        /// camera to a top-down view with neither avatar drawn. Components like
        /// 1073 ClientAuthoritativePlayerState and 1072 CharacterControlsData
        /// specifically mean "this is the character you control".
        ///
        /// Authority is never granted here either. Only a peer's own entity may
        /// be made authoritative, or the client would try to drive another player.
        ///
        /// See docs/component-ids.md for what the numbers mean.
        /// </summary>
        private static void MirrorNewPlayer(ENetPeerHandle peer, long entityId)
        {
            IReadOnlyList<MirrorIntent> intents = Mirror.OnJoin(PeerIdentity.IdOf(peer), entityId);
            if (intents.Count == 0)
            {
                Console.WriteLine("[info] first player in the world, nobody to mirror.");
                return;
            }

            // Two-phase mirror. The client only instantiates an entity whose
            // prefab asset it has LOADED: the local player flow sends an
            // AssetLoadRequestOp and waits for the ack before AddEntityOp. When
            // this mirror sent AddEntityOp("Traveller","Default") directly, the
            // plain Traveller asset had never been requested and no rig ever
            // appeared in the scene (verified by the client-side rig inventory).
            //
            // So: request the asset now, park the entity ops per target peer, and
            // flush them when that peer's next asset-loaded ack arrives.
            foreach (MirrorIntent intent in intents)
            {
                ENetPeerHandle? target = PeerIdentity.Instance.Resolve(new IntPtr((long)intent.TargetPeer));
                if (target == null)
                {
                    Console.WriteLine("[warning] mirror target " + Describe(intent.TargetPeer) + " vanished, skipping.");
                    continue;
                }

                // The schedule reports whether this is the first op parked for the
                // peer, which is exactly when the asset it will wait on must be
                // requested.
                if (Schedule.Park(intent.TargetPeer, intent))
                {
                    if (SendOPHelper.SendAssetLoadRequestOP(target, "notNeeded?", MirrorSendPolicy.PrefabName, MirrorSendPolicy.RemotePrefabContext))
                    {
                        Console.WriteLine("[info] mirror: requested plain Traveller asset load for " + Describe(intent.TargetPeer) + ".");
                    }
                }
            }
        }

        /// <summary>
        /// Decides WHEN parked mirror ops are force-flushed and resent, and holds
        /// every per-peer mirror record.
        ///
        /// It is driven by a real clock. This loop used to count its own
        /// iterations and call 40 of them "~2s", which assumed each iteration
        /// blocks for its 50 ms ENet_Poll timeout - but enet_host_service returns
        /// the instant an event is already queued, so the loop turns once per
        /// EVENT. See MirrorSchedule's own remarks for what that cost.
        /// </summary>
        private static readonly MirrorSchedule Schedule = new MirrorSchedule(new MonotonicClock());

        /// <summary>
        /// Force-flushes any parked mirror ops older than the timeout, so an idle
        /// already-in-world player still receives a newly joined player's rig even
        /// though it never sends the asset-load ack the primary flush waits for.
        /// </summary>
        private static void FlushStaleMirrors()
        {
            foreach (ulong peerId in Schedule.DueForFlush())
            {
                Console.WriteLine("[info] mirror: fallback flush for idle " + Describe(peerId) + " (no asset ack seen).");
                FlushPendingMirrors(peerId);
            }
        }

        /// <summary>
        /// Resends mirror ops a few times after the initial flush. A client that
        /// was still loading the "Traveller"/"Default" prefab silently drops the
        /// AddEntity, so the newly joined player's rig never spawns for it - the
        /// joining client in particular, which is busy with its own spawn. Rather
        /// than parse asset acks, simply resend a few times; the client tolerates
        /// duplicate adds, and once the asset is loaded the rig appears.
        /// </summary>
        private static void ResendMirrors()
        {
            foreach (MirrorResend batch in Schedule.DueForResend())
            {
                ENetPeerHandle? target = PeerIdentity.Instance.Resolve(new IntPtr((long)batch.PeerId));
                if (target == null)
                {
                    Console.WriteLine("[warning] mirror: resend target " + Describe(batch.PeerId) + " vanished, dropping its ops.");
                    Schedule.Forget(batch.PeerId);
                    continue;
                }

                foreach (MirrorIntent intent in batch.Ops)
                {
                    // AddEntity ONLY. Never resend AddComponents: it re-seeds the
                    // default TransformState onto a live player and teleports them
                    // into the sky. The client tolerates a duplicate AddEntity, and
                    // the components from the original flush still apply.
                    // The rule itself lives in MirrorSendPolicy so it is testable.
                    if (MirrorSendPolicy.MayResend(intent.Op))
                    {
                        SendOPHelper.SendAddEntityOP(target, intent.EntityId, MirrorSendPolicy.PrefabName, MirrorSendPolicy.RemotePrefabContext);
                    }
                }

                Console.WriteLine("[info] mirror: resent ops to " + Describe(batch.PeerId) + " (" + batch.AttemptsLeft + " attempts left).");
            }
        }

        /// <summary>
        /// Sends the parked mirror ops for a peer, called when that peer acks an
        /// asset load. Uses the peer's FIRST ack after queuing: the ack payload is
        /// not parsed anywhere in this server, so this can fire one asset early if
        /// the peer was still mid-spawn - if rigs intermittently fail to appear
        /// for the JOINING client, parse the ack payload and match the asset.
        /// </summary>
        private static void FlushPendingMirrors(ulong peerId)
        {
            // Taking the batch also arms the resends: a client that was still
            // loading the prefab asset silently drops the AddEntity, and the rig
            // never appears (observed: the joining client saw nothing while the
            // already-in-world client saw it fine). Resends are safe - the client
            // tolerates duplicate entity/component adds with a warning.
            IReadOnlyList<MirrorIntent> queue = Schedule.TakeParked(peerId);
            if (queue.Count == 0)
            {
                return;
            }

            ENetPeerHandle? target = PeerIdentity.Instance.Resolve(new IntPtr((long)peerId));
            if (target == null)
            {
                Console.WriteLine("[warning] mirror: flush target " + Describe(peerId) + " vanished, dropping its ops.");
                Schedule.Forget(peerId);
                return;
            }

            // Context "Default", NOT "Player": "Player" selects Traveller@Player,
            // the full local rig whose duplication caused every camera/identity
            // regression. "Default" selects the plain Traveller remote rig.
            List<Structs.Structs.InterestOverride> remoteComponents =
                RemoteSeed.Select(id => new Structs.Structs.InterestOverride(id, 1)).ToList();

            foreach (MirrorIntent intent in queue)
            {
                bool ok = intent.Op switch
                {
                    MirrorOp.AddEntity => SendOPHelper.SendAddEntityOP(target, intent.EntityId, MirrorSendPolicy.PrefabName, MirrorSendPolicy.RemotePrefabContext),
                    MirrorOp.AddComponents => SendOPHelper.SendAddComponentOp(target, intent.EntityId, remoteComponents),
                    _ => true,
                };

                Console.WriteLine((ok ? "[success] " : "[error] failed: ") + "mirror(flush) " + intent);
            }
        }

        /// <summary>
        /// Forwards one player's component update to every other connected player.
        /// Copies the bytes out of native memory first, because the packet is
        /// destroyed as soon as this poll iteration ends.
        /// </summary>
        private static unsafe void RelayToOtherPlayers(ENetPeerHandle sender, uint componentId, byte* data, int dataLength)
        {
            if (data == null || dataLength <= 0)
            {
                return;
            }

            byte[] payload = new byte[dataLength];
            Marshal.Copy(new IntPtr(data), payload, 0, dataLength);

            // Diagnostic: decode a sample of transform payloads so the log shows
            // what senders actually publish (Parent? global or relative position?).
            TransformSampleLogger.MaybeLog(componentId, data, dataLength);

            ulong senderId = PeerIdentity.IdOf(sender);
            foreach (MirrorIntent intent in Mirror.OnComponentUpdate(senderId, componentId, payload))
            {
                ENetPeerHandle? target = PeerIdentity.Instance.Resolve(new IntPtr((long)intent.TargetPeer));
                if (target == null)
                {
                    continue;
                }

                if (SendOPHelper.SendRawComponentUpdateOp(target, intent.EntityId, intent.ComponentId, intent.Payload!))
                {
                    ServerLog.Trace("[relay] component ", intent.ComponentId + " of entity " + intent.EntityId
                        + ": " + Describe(senderId) + " -> " + Describe(intent.TargetPeer));
                }
            }
        }

        private static readonly EnetLayer.ENet_Poll_Callback callbackC = new EnetLayer.ENet_Poll_Callback(OnNewClientConnected);
        private static readonly EnetLayer.ENet_Poll_Callback callbackD = new EnetLayer.ENet_Poll_Callback(OnClientDisconnected);
        // A client only PUBLISHES components it has authority over. 190602
        // TransformState is granted so the client sends its position; 1073
        // ClientAuthoritativePlayerState is granted so ClientAuthoritativePlayerMovement's
        // Writer enables and the client publishes its skeleton's bone bytes every
        // tick (that writer is authority-gated - without the grant it never runs
        // and remote avatars stay in T-pose). The grant only ever applies to the
        // sender's OWN entity (isSendersOwnEntity gate below).
        // The set itself lives in MirrorSendPolicy so it is testable.
        private static readonly List<uint> authoritativeComponents = new List<uint>(MirrorSendPolicy.AuthoritativeComponents);

        /// <summary>
        /// How many clients the ENet host accepts. Was 1, which made the server
        /// single-player by construction.
        /// </summary>
        private const int MaxPlayers = 8;

        /// <summary>
        /// TransformState: a player's position and rotation. This is what has to
        /// reach other clients for them to see anyone move.
        /// See docs/component-ids.md.
        /// </summary>
        private const uint TransformStateComponentId = MirrorSendPolicy.TransformStateComponentId;

        /// <summary>
        /// ClientAuthoritativePlayerState: carries the player's bone/animation
        /// bytes (and relative-position fields). Seeded on remote rigs so
        /// BoneAnimationReader binds and animates, and granted to the owner so its
        /// movement writer publishes. See docs/component-ids.md.
        /// </summary>
        private const uint ClientAuthoritativePlayerStateComponentId = MirrorSendPolicy.ClientAuthoritativePlayerStateComponentId;

        /// <summary>
        /// UtilitySlotActivatedState: whether the head/body/feet utility slot is
        /// active. The glider is a body utility; deploying it flips this, and
        /// UtilitySlotActivatedVisualizer on the remote rig opens/closes the wings.
        /// Granted so the owner publishes it, seeded so the remote reader binds.
        /// 1109 PilotState is deliberately NOT used - it steals the PilotVisualizer
        /// singleton and pokes LocalPlayer. See docs/component-ids.md.
        /// </summary>
        private const uint UtilitySlotActivatedStateComponentId = MirrorSendPolicy.UtilitySlotActivatedStateComponentId;

        /// <summary>
        /// RopeControlPoints: the grapple rope's control points. Granted so the
        /// owner's RopeObserver publishes the live rope, seeded so the remote rig
        /// carries the data; a mod component (RemoteGrappleLine) reads it by
        /// component id and draws the line. See docs/component-ids.md.
        /// </summary>
        private const uint RopeControlPointsComponentId = MirrorSendPolicy.RopeControlPointsComponentId;

        /// <summary>
        /// Components seeded on a mirrored remote avatar: TransformState (position),
        /// 1086 PlayerName, the two [Require]s of CharacterCustomisationVisualizer
        /// (1081 InventoryState, 1088 PlayerPropertiesState) which builds the body,
        /// and 1073 ClientAuthoritativePlayerState which drives BoneAnimationReader.
        /// Kept minimal: the full second-stage set enabled visualizers against
        /// default data and their OnEnable subscriptions threw. Seeding 1073 also
        /// enables the game's native PlayerVisualizer positioner (which
        /// RemoteRigMover now yields to). See docs/component-ids.md.
        /// </summary>
        // The set itself lives in MirrorSendPolicy so it is testable.
        private static readonly IReadOnlyList<uint> RemoteSeed = MirrorSendPolicy.RemoteSeedComponents;

        /// <summary>Who owns which player entity. Internal: the component update
        /// handlers validate entity ownership against it.</summary>
        internal static readonly PlayerRegistry Players = new PlayerRegistry();

        /// <summary>Published appearance per player entity; read by the 1088
        /// serializer branch, written by PlayerPropertiesState_Handler.</summary>
        internal static readonly AppearanceStore Appearances = new AppearanceStore();

        /// <summary>
        /// Moves players around the world by 190607 TeleportRequestState, and
        /// watches 1073 for the ack. Internal because
        /// ClientAuthoritativePlayerState_Handler reports acks to it.
        ///
        /// This is the authentic Act 1 exit: the game's own tutorial ended by
        /// teleporting the player out of the Revival Chamber, and it is the only
        /// way off Haven that exists before ships do. See TeleportService for how
        /// a human fires one.
        /// </summary>
        internal static readonly TeleportService Teleports = new TeleportService();

        /// <summary>Decides which ops go to which peers so players can see each other.</summary>
        private static readonly RemotePlayerMirror Mirror = new RemotePlayerMirror(Players);

        /// <summary>
        /// Entity id source. Pure policy so the "one shared island id, ids never
        /// reused" rule is unit-testable; see EntityIdAllocator.
        /// </summary>
        private static readonly EntityIdAllocator EntityIds = new EntityIdAllocator();

        /// <summary>
        /// EVERY non-player thing this server puts in the world, and the one
        /// entity id each is known by on every client.
        ///
        /// This replaced two hardcoded facts: that the only such thing is the
        /// island, and that its asset name is a constant. Adding a tree or a ship
        /// hull is now a registration in Multiplayer.WorldEntities - no change to
        /// the spawn state machine, the id allocator, or the component
        /// serializer's dispatch.
        ///
        /// Internal because ComponentsSerializer asks it for each entity's own
        /// 190602 position: the question "where does this entity go" used to have
        /// exactly two possible answers and now has one per registration.
        /// </summary>
        internal static readonly WorldEntityRegistry WorldEntities =
            Multiplayer.WorldEntities.Default(EntityIds, SpawnProofIsland);

        /// <summary>
        /// Whether to also spawn the second Haven (see
        /// Multiplayer.WorldEntities.ProofIsland). OFF unless
        /// WAREBORN_SPAWN_PROOF_ISLAND=1.
        ///
        /// It exists to exercise the world-entity seam against a real client
        /// without seeding a single new component, and it is opt-in because it has
        /// never been in front of one: no game was launched for this change, and a
        /// second island is a visible change to what players see.
        /// </summary>
        private static bool SpawnProofIsland =>
            Environment.GetEnvironmentVariable("WAREBORN_SPAWN_PROOF_ISLAND") == "1";

        /// <summary>
        /// The island's entity id, or null if it has not been handed out yet.
        ///
        /// It deliberately does NOT allocate. Asking the question must never be
        /// what creates the island id, or the answer would depend on which entity
        /// happened to be serialized first.
        /// </summary>
        internal static long? IslandEntityId =>
            EntityIds.IslandAllocated ? EntityIds.SharedIslandEntityId : null;

        public static long NextEntityId => EntityIds.Next();

        /// <summary>
        /// Wire plumbing for one <see cref="SpawnPlanStep"/>. The policy decides
        /// WHAT happens, to what, and in what order; this decides only which ENet
        /// op says it.
        ///
        /// There are exactly four shapes because there are two ops and two kinds
        /// of subject - a registered world entity, or the joining peer's own
        /// avatar. A fifth world entity adds no case here.
        /// </summary>
        private static Action<object> ActionFor(SpawnPlanStep step)
        {
            if (step.IsPlayer)
            {
                return step.Op == SpawnOp.RequestAsset ? RequestPlayerAsset() : AddPlayerEntity();
            }

            return step.Op == SpawnOp.RequestAsset
                ? RequestWorldEntityAsset(step.Entity!)
                : AddWorldEntity(step.Entity!);
        }

        private static Action<object> RequestPlayerAsset()
        {
            return (object o) =>
            {
                Console.WriteLine("[info] requesting the game to load the player asset...");

                if (SendOPHelper.SendAssetLoadRequestOP((ENetPeerHandle)o, "notNeeded?", MirrorSendPolicy.PrefabName, MirrorSendPolicy.LocalPrefabContext))
                {
                    Console.WriteLine("[info] successfully serialized and queued AssetLoadRequestOp.");
                }
                else
                {
                    Console.WriteLine("[error] failed to serialize and queue AssetLoadRequestOp.");
                }
            };
        }

        /// <summary>
        /// Asks the client to load one world entity's bundle.
        ///
        /// It is a separate step from creating the entity, and it always precedes
        /// it, because the client only instantiates an entity whose prefab asset
        /// it has LOADED - an AddEntityOp for an unloaded prefab is dropped in
        /// silence. That cost a full debugging round on the remote-player mirror,
        /// where AddEntityOp("Traveller") without a preceding request produced no
        /// rig and no error.
        /// </summary>
        private static Action<object> RequestWorldEntityAsset(WorldEntity entity)
        {
            return (object o) =>
            {
                Console.WriteLine("[info] requesting the game to load " + entity.AssetName + " for world entity '" + entity.Key + "'...");

                if (SendOPHelper.SendAssetLoadRequestOP((ENetPeerHandle)o, "notNeeded?", entity.AssetName, entity.AssetContext))
                {
                    Console.WriteLine("[info] successfully serialized and queued AssetLoadRequestOp.");
                }
                else
                {
                    Console.WriteLine("[error] failed to serialize and queue AssetLoadRequestOp for '" + entity.Key + "'.");
                }
            };
        }

        /// <summary>
        /// Creates one world entity on this client.
        ///
        /// Every client gets the SAME entity id for it - that is what
        /// WorldEntityRegistry.EntityIdFor guarantees, and it is not a nicety. A
        /// remote player's rig positions itself by PARENTING: the client publishes
        /// TransformState with Parent = its island's entity id, and the receiving
        /// client's RelativeParentTransformChildHierarchyBehaviour looks that id up
        /// LOCALLY. With per-client ids (0 for one client, 2 for the next) the
        /// lookup found nothing and every remote avatar stayed frozen at the seed
        /// position, ~90 km off-island.
        ///
        /// Calling EntityIdFor here is also what ALLOCATES the id, on the first
        /// client to reach this step, which is why nothing that merely asks a
        /// question is allowed to call it.
        ///
        /// Seeded components, if the registration lists any, go out immediately
        /// after - all-or-nothing, so an id with no branch in ComponentsSerializer
        /// leaves a rendered but inert entity. SendOPHelper now prints the full
        /// requested list next to the failure for exactly this reason.
        /// </summary>
        private static Action<object> AddWorldEntity(WorldEntity entity)
        {
            return (object o) =>
            {
                ENetPeerHandle peer = (ENetPeerHandle)o;
                long entityId = WorldEntities.EntityIdFor(entity);

                Console.WriteLine("[success] asset loaded for '" + entity.Key + "'. creating entity " + entityId + " at " + entity.Position + "...");

                if (!SendOPHelper.SendAddEntityOP(peer, entityId, entity.AssetName, entity.AssetContext))
                {
                    Console.WriteLine("[error] failed to serialize and queue AddEntityOp for '" + entity.Key + "'.");
                    return;
                }

                Console.WriteLine("[info] successfully serialized and queued AddEntityOp for world entity '"
                    + entity.Key + "' (" + entityId + ").");

                if (entity.SeedComponents.Count == 0)
                {
                    return;
                }

                List<Structs.Structs.InterestOverride> seeds = entity.SeedComponents
                    .Select(id => new Structs.Structs.InterestOverride(id, 1))
                    .ToList();

                if (!SendOPHelper.SendAddComponentOp(peer, entityId, seeds, true))
                {
                    Console.WriteLine("[error] '" + entity.Key + "' (" + entityId
                        + ") was created but its seeded components were dropped. It will render and do nothing.");
                }
            };
        }

        private static Action<object> AddPlayerEntity()
        {
            return (object o) =>
            {
                ENetPeerHandle peer = (ENetPeerHandle)o;
                Console.WriteLine("[info] client ack'ed the previous spawning instruction (info by sdk, does not mean it truly spawned). requesting to spawn player...");

                // Capture this peer's own entity id in a local. Reading a shared
                // playerEntityIDs.Last() was only safe while a single client
                // could ever connect; with several spawning at once it could
                // return someone else's entity. That list is gone: nothing ever
                // read it again, and it grew for the life of the process.
                long playerEntityId = NextEntityId;

                if (SendOPHelper.SendAddEntityOP(peer, playerEntityId, MirrorSendPolicy.PrefabName, MirrorSendPolicy.LocalPrefabContext))
                {
                    Console.WriteLine("[info] successfully serialized and queued AddEntityOp for player entity " + playerEntityId + ".");
                    MirrorNewPlayer(peer, playerEntityId);
                }
                else
                {
                    Console.WriteLine("[error] failed to serialize and queue AddEntityOp.");
                }
            };
        }

        /// <summary>
        /// Translates the policy's ack into the sync loop's own enum. Two names
        /// for one idea, kept apart only because GameState.NextStateRequirement
        /// lives in the ENet-bound assembly and the policy must not depend on it.
        /// </summary>
        private static GameState.NextStateRequirement RequirementFor(SpawnAck ack)
        {
            return ack switch
            {
                SpawnAck.AssetLoaded => GameState.NextStateRequirement.ASSET_LOADED_RESPONSE,
                SpawnAck.EntityAdded => GameState.NextStateRequirement.ADDED_ENTITY_RESPONSE,
                _ => throw new ArgumentOutOfRangeException(nameof(ack)),
            };
        }

        static unsafe void Main( string[] args )
        {
            Console.CancelKeyPress += delegate ( object? sender, ConsoleCancelEventArgs e )
            {
                keepRunning = false;
            };

            if (EnetLayer.ENet_Initialize() < 0)
            {
                Console.WriteLine("[error] failed to initialize ENet.");
                return;
            }

            Console.WriteLine("[info] successfully initialized ENet.");
            // Port is configurable so the server can be hosted somewhere that
            // already uses 7777 (e.g. a VPS running another game server).
            // Override with WAREBORN_GAME_PORT; defaults to the stock 7777.
            int gamePort = 7777;
            string portEnv = Environment.GetEnvironmentVariable("WAREBORN_GAME_PORT");
            if (!string.IsNullOrWhiteSpace(portEnv) && int.TryParse(portEnv, out int parsedPort))
            {
                gamePort = parsedPort;
            }
            Console.WriteLine("[info] game server listening on UDP " + gamePort + ".");

            ENetHostHandle server = EnetLayer.ENet_Create_Host(gamePort, MaxPlayers, 5, 0, 0);

            if (server.IsInvalid)
            {
                Console.WriteLine("[error] failed to create host and listen on network interface.");

                EnetLayer.ENet_Deinitialize(new IntPtr(0));
                return;
            }

            Console.WriteLine("[info] successfully initialized networking, now waiting for connections and data.");
            PeerManager.Instance.SetENetHostHandle(server);

            // Said once, at start-up, because "my stuff does not save" is
            // otherwise discovered several sessions later and blamed on the
            // wrong thing.
            Game.Inventory.InventoryService.ReportPersistenceState();

            // Say it once, at startup, because a feature nobody can find is not a
            // feature. Destinations come from TeleportPolicy so this list cannot
            // go stale.
            Console.WriteLine("[info] teleport: write a destination to " + Teleports.TriggerFile
                + " to move players. Destinations: " + string.Join(", ", TeleportPolicy.Names)
                + ". Add an entity id to move just one, e.g. `echo '"
                + TeleportPolicy.SafeDestination.Name + " 3' > " + Teleports.TriggerFile + "`.");


            // define initial world state for first chunk
            //
            // Built FROM SpawnPlan - i.e. derived from what is REGISTERED -
            // rather than typed out as lambdas in whatever order felt natural.
            // The order is load-bearing and silent when wrong: an entity the
            // player stands on has to have its AddEntity, and so its colliders,
            // land before the player's transform is published, or the player is
            // placed over geometry that does not exist yet and falls forever
            // (this server writes no HealthState, so there is no fall damage to
            // end it, and WorldEdgePushback never runs because we never send
            // world bounds). Expressing the order as data means SpawnPlanTests
            // asserts it instead of a human re-deriving it.
            //
            // Each step is also gated on the client ACKing the previous one,
            // which is the only throttle on bundle loading anywhere in the
            // system - the client's asset loader is synchronous and unbudgeted.
            //
            // The plan is computed ONCE, not per peer, so every client walks an
            // identical sequence and every world entity's id is allocated by
            // whichever client reaches its step first and then reused verbatim.
            IReadOnlyList<SpawnPlanStep> plan = SpawnPlan.For(WorldEntities);

            Console.WriteLine("[info] spawn plan (" + plan.Count + " steps): "
                + string.Join(" -> ", plan.Select(s => s.ToString())));

            GameState.Instance.WorldState[0] = plan
                .Select(step => new SyncStep(RequirementFor(step.Ack), ActionFor(step)))
                .ToList();

            while (keepRunning)
            {
                // Fallback flush for parked mirror ops. The ack-driven flush only
                // fires when the target sends an asset-load ack - which an ALREADY-
                // IN-WORLD, idle player never does (it finished loading), so its
                // mirror of a newly joined player never spawned. After a short
                // delay (the asset request has had time to load) flush anyway.
                FlushStaleMirrors();
                // Resend ONLY AddEntity (never AddComponents). A peer that was
                // still loading the prefab drops the AddEntity and never spawns the
                // other player - the one-way visibility bug. Resending AddComponents
                // was what caused the sky-teleport: it re-applied the DEFAULT seeded
                // TransformState (0,100,0) to a live player. AddEntity alone carries
                // no component data, so it cannot move anyone.
                ResendMirrors();
                // The only way a human can currently ask for anything: a file.
                // There is no command channel (SendCommandRequest is a TODO stub
                // in the SDK), so a client cannot request a teleport at all. Self-
                // throttled to twice a second, because this loop turns once per
                // ENet EVENT rather than once per poll timeout.
                Teleports.PollTrigger();

                EnetLayer.ENetPacket_Wrapper* packet = EnetLayer.ENet_Poll(server, 50, Marshal.GetFunctionPointerForDelegate(callbackC), Marshal.GetFunctionPointerForDelegate(callbackD));
                if(packet != null)
                {
                    // Resolve who actually sent this packet. Before the peer field
                    // existed the server could not tell, so it applied every packet
                    // to every client, which is why it only worked with one.
                    ENetPeerHandle? sender = PeerIdentity.Instance.Resolve(packet->Peer);

                    if (sender == null || !PeerManager.Instance.playerState.ContainsKey(sender))
                    {
                        // Normal during connect and teardown races: drop the packet
                        // rather than letting one client's state abort the loop.
                        Console.WriteLine("[warning] packet on channel " + packet->Channel + " from unknown "
                            + Describe(packet->Peer) + ", dropping.");
                        EnetLayer.ENet_Destroy_Packet(new IntPtr(packet));
                        continue;
                    }

                    {
                        // work on packets that are relevant to progress in sync state
                        int currentChunkIndex = 0;
                        int currentPlayerSyncIndex = PeerManager.Instance.playerState[sender][currentChunkIndex].SyncStepPointer;

                        if (currentPlayerSyncIndex != GameState.Instance.WorldState[currentChunkIndex].Count - 1)
                        {
                            GameState.NextStateRequirement nextStateRequirement = GameState.Instance.WorldState[currentChunkIndex][currentPlayerSyncIndex].NextStateRequirement;

                            if(packet->Channel == (int)EnetLayer.ENetChannel.ASSET_LOAD_REQUEST_OP && nextStateRequirement == GameState.NextStateRequirement.ASSET_LOADED_RESPONSE)
                            {
                                PeerManager.Instance.playerState[sender][currentChunkIndex].SyncStepPointer++;
                            }
                            else if(packet->Channel == (int)EnetLayer.ENetChannel.ADD_ENTITY_OP && nextStateRequirement == GameState.NextStateRequirement.ADDED_ENTITY_RESPONSE)
                            {
                                PeerManager.Instance.playerState[sender][currentChunkIndex].SyncStepPointer++;
                            }
                        }
                    }

                    // A peer's asset-loaded ack releases any mirror ops parked for
                    // it (the two-phase remote-player mirror; see MirrorNewPlayer).
                    if (packet->Channel == (int)EnetLayer.ENetChannel.ASSET_LOAD_REQUEST_OP)
                    {
                        FlushPendingMirrors(PeerIdentity.IdOf(sender));
                    }

                    // work on packets that are not relevant for progress of sync state
                    //
                    // do/while(false) so the existing 'continue' error paths still mean
                    // "stop processing this packet". A bare block would send them to the
                    // next while iteration and skip ENet_Destroy_Packet below, leaking it.
                    do
                    {
                        KeyValuePair<ENetPeerHandle, Dictionary<int, PlayerSyncStatus>> keyValuePair =
                            new KeyValuePair<ENetPeerHandle, Dictionary<int, PlayerSyncStatus>>(sender, PeerManager.Instance.playerState[sender]);

                        if (packet->Channel == (int)EnetLayer.ENetChannel.SEND_COMPONENT_INTEREST)
                        {
                            long entityId = 0;
                            uint interestCount = 0;
                            Structs.Structs.InterestOverride* interests = (Structs.Structs.InterestOverride*)new IntPtr(0);

                            if (EnetLayer.PB_EXP_SendComponentInterest_Deserialize(packet->Data, (int)packet->DataLength, &entityId, &interests, &interestCount))
                            {
                                Console.WriteLine("[info] game requests components for entity id: " + entityId);

                                // The first-time setup (component injection + AUTHORITY grant)
                                // must only ever run against the sender's OWN player entity.
                                // The old check - "is this any player entity" - would run the
                                // full setup, authority included, against ANOTHER player's
                                // entity if the client happened to request the mirrored remote
                                // entity's components first. Request ordering has been lucky so
                                // far; this removes the dice roll.
                                bool isSendersOwnEntity = Players.Owns(PeerIdentity.IdOf(sender), entityId);
                                if(isSendersOwnEntity && !PeerManager.Instance.clientSetupState.Contains(keyValuePair.Key))
                                {
                                    // a player entity requests components for the first time, we need to setup a few things to make him work properly
                                    // some of this might not be needd anymore in the future once we sorted out a few things.
                                    //
                                    // we can make use of the fact that the game requests components for players in two stages, where the second one will terminate the loading screen of the client.
                                    // the second stage needs a few components setup properly, for this we need to inject one component and call auth changed for a few others once.

                                    // Some components are needed in the first stage and need to be injected.
                                    //
                                    // PlayerExternalDataVisualizer null-guards its reader in IsAlive() but NOT in
                                    // IsDriving() (1109 PilotState) or IsEditingShip() (1207 ShipHullAgentState).
                                    // PlayerExternalData.CanMove() evaluates them left to right with &&, so injecting
                                    // only 1109 did not fix the crash - it moved it from IsDriving to IsEditingShip.
                                    // The client log shows exactly that: 1,264 throws from the first, then 1,366 from
                                    // the second, in strictly disjoint line ranges.
                                    //
                                    // This is not cosmetic. The NullReferenceException escapes
                                    // UserControlCharacter.Update() and GrapplingHookNew.Update(), so Unity aborts the
                                    // whole Update for that frame: no movement, no jump, no grapple for ~25 seconds
                                    // after the world appears. Under real SpatialOS the components arrived with the
                                    // entity and the window was zero frames; our packet-driven delivery widens it.
                                    List<Structs.Structs.InterestOverride> injectedEarly = new List<Structs.Structs.InterestOverride>
                                    {
                                        new Structs.Structs.InterestOverride(1109, 1),
                                        new Structs.Structs.InterestOverride(1207, 1)
                                    };

                                    if (!SendOPHelper.SendAddComponentOp(keyValuePair.Key, entityId, injectedEarly, true))
                                    {
                                        continue;
                                    }

                                    // then send what the game requested
                                    if (!SendOPHelper.SendAddComponentOp(keyValuePair.Key, entityId, interests, interestCount, true))
                                    {
                                        continue;
                                    }

                                    // for some reason the game does not always request component 1080 (SchematicsLearnerGSimState), but its reader is required in InventoryVisualiser
                                    List<Structs.Structs.InterestOverride> injected = new List<Structs.Structs.InterestOverride> { new Structs.Structs.InterestOverride(1080, 1) };
                                    // also inject other required components for the inventory
                                    injected.AddRange(authoritativeComponents.Select(p => new Structs.Structs.InterestOverride(p, 1)));

                                    if (!SendOPHelper.SendAddComponentOp(keyValuePair.Key, entityId, injected, true))
                                    {
                                        continue;
                                    }

                                    // now send auth change
                                    if(!SendOPHelper.SendAuthorityChangeOp(keyValuePair.Key, entityId, authoritativeComponents))
                                    {
                                        continue;
                                    }

                                    // now add player to clientSetupState
                                    PeerManager.Instance.clientSetupState.Add(keyValuePair.Key);

                                    // Teleport last, and on its own. 190607 is the third
                                    // [Require] of TeleportTransformVisualizer and the client does
                                    // not reliably ask for it, so it has to be injected - but it
                                    // is NOT worth the spawn path for. Every send above is
                                    // failOnComponentInitError:true and 'continue's out of the
                                    // whole setup on failure; folding teleport into one of them
                                    // would mean an unexpected serializer miss costs a player
                                    // their inventory, their authority grants and their loading
                                    // screen. Separate call, non-fatal, after the flag is set.
                                    Teleports.SeedOn(keyValuePair.Key, entityId);
                                }
                                else
                                {
                                    // player already setup or another entity requested components, so just process them
                                    if (!SendOPHelper.SendAddComponentOp(keyValuePair.Key, entityId, interests, interestCount, true))
                                    {
                                        continue;
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine("[error] failed to deserialize ComponentInterest message from game.");
                            }
                        }
                        else if(packet->Channel == (int)EnetLayer.ENetChannel.COMPONENT_UPDATE_OP)
                        {
                            long entityId = 0;
                            uint updateCount = 0;
                            Structs.Structs.ComponentUpdateOp* update = (Structs.Structs.ComponentUpdateOp*)new IntPtr(0);

                            if (EnetLayer.PB_EXP_ComponentUpdateOp_Deserialize(packet->Data, (int)packet->DataLength, &entityId, &update, &updateCount) && updateCount > 0)
                            {
                                ServerLog.Trace("[info] game requests ", updateCount, " ComponentUpdate's for entity id ", entityId);

                                for(int i = 0; i < updateCount; i++)
                                {
                                    ComponentUpdateManager.Instance.HandleComponentUpdate(keyValuePair.Key, entityId, update[i].ComponentId, update[i].ComponentData, update[i].DataLength);

                                    // Forward this player's update to everyone else so they
                                    // can see it. Relayed verbatim: the server cannot
                                    // deserialize most component ids anyway.
                                    RelayToOtherPlayers(sender, update[i].ComponentId, update[i].ComponentData, update[i].DataLength);
                                }
                            }
                            else
                            {
                                Console.WriteLine("[error] failed to deserialize ComponentUpdate message from game, or empty message.");
                            }
                        }
                    }
                    while (false);

                    EnetLayer.ENet_Destroy_Packet(new IntPtr(packet));
                }

                // dont wait for GetOplist and then for the Dispatch call as we are the ones who would dispatch the work anyways.
                // sync up players
                foreach (KeyValuePair<ENetPeerHandle, Dictionary<int, PlayerSyncStatus>> keyValuePair in PeerManager.Instance.playerState)
                {
                    int currentChunkIndex = 0;
                    PlayerSyncStatus pStatus = keyValuePair.Value[currentChunkIndex];
                    SyncStep step = GameState.Instance.WorldState[currentChunkIndex][pStatus.SyncStepPointer];

                    if (!pStatus.Performed)
                    {
                        step.Step(keyValuePair.Key);
                        pStatus.Performed = true;
                    }
                }
            }

            server.Dispose();

            Console.WriteLine("[info] shutting down.");
        }
    }
}

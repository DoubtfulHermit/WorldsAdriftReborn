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
            ENetPeerHandle ePeer = new ENetPeerHandle(peer, new ENetHostHandle());
            if (!ePeer.IsInvalid)
            {
                // Track before anything else: every later lookup resolves through
                // PeerIdentity so only one handle per peer is ever alive.
                ePeer = PeerIdentity.Instance.Track(peer, ePeer);

                if (!PeerManager.Instance.playerState.ContainsKey(ePeer))
                {
                    PeerManager.Instance.playerState.Add(ePeer, new Dictionary<int, PlayerSyncStatus> { { 0, new PlayerSyncStatus() } });
                }
                Console.WriteLine("[info] got a connection. players now: " + PeerManager.Instance.playerState.Count);
            }
        }
        [PInvoke(typeof(EnetLayer.ENet_Poll_Callback))]
        private unsafe static void OnClientDisconnected(IntPtr peer )
        {
            ENetPeerHandle? ePeer = PeerIdentity.Instance.Forget(peer);
            if (ePeer == null)
            {
                Console.WriteLine("[warning] a disconnect arrived for an untracked peer, ignoring.");
                return;
            }

            // Unregister first: this is what actually matters, because it stops
            // relaying updates to and from a peer that is gone.
            //
            // The despawn intents cannot be sent yet. There is no wire message for
            // entity removal: ENetChannel has no such channel, no RemoveEntityOp
            // proto exists, and the SDK's RegisterRemoveEntityCallback is still an
            // unimplemented TODO in Exports.cpp. Until that exists a departed
            // player leaves a stale avatar behind, which is cosmetic rather than
            // blocking.
            long? ownEntity = Players.EntityOf(PeerIdentity.IdOf(ePeer));

            IReadOnlyList<MirrorIntent> despawns = Mirror.OnLeave(PeerIdentity.IdOf(ePeer));
            if (despawns.Count > 0)
            {
                Console.WriteLine("[warning] " + despawns.Count + " avatar(s) cannot be despawned: entity removal is not implemented on the wire. Stale avatar(s) will remain.");
            }

            // Drop the departed player's stored appearance so the store does not
            // grow across reconnects (entity ids are handed out monotonically, so
            // a stale record is never re-read, only wasted memory).
            if (ownEntity.HasValue)
            {
                Appearances.Forget(ownEntity.Value);
            }

            PeerManager.Instance.playerState.Remove(ePeer);
            PeerManager.Instance.clientSetupState.Remove(ePeer);
            Console.WriteLine("[info] a client disconnected. players now: " + PeerManager.Instance.playerState.Count);
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
                    Console.WriteLine("[warning] mirror target peer vanished, skipping.");
                    continue;
                }

                if (!pendingMirrors.TryGetValue(target, out List<MirrorIntent> queue))
                {
                    queue = new List<MirrorIntent>();
                    pendingMirrors[target] = queue;
                    pendingMirrorTick[target] = loopTick;

                    if (SendOPHelper.SendAssetLoadRequestOP(target, "notNeeded?", "Traveller", "Default"))
                    {
                        Console.WriteLine("[info] mirror: requested plain Traveller asset load for a peer.");
                    }
                }

                queue.Add(intent);
            }
        }

        /// <summary>Entity ops per target peer, waiting for that peer's asset-loaded ack.</summary>
        private static readonly Dictionary<ENetPeerHandle, List<MirrorIntent>> pendingMirrors = new Dictionary<ENetPeerHandle, List<MirrorIntent>>();

        /// <summary>Loop tick at which each peer's mirror ops were parked, for the fallback flush.</summary>
        private static readonly Dictionary<ENetPeerHandle, long> pendingMirrorTick = new Dictionary<ENetPeerHandle, long>();

        /// <summary>Main-loop tick counter (one per ENet poll iteration, ~50ms).</summary>
        private static long loopTick = 0;

        /// <summary>Ticks to wait before force-flushing parked mirror ops (~2s at 50ms/iter).</summary>
        private const long MirrorFlushTimeoutTicks = 40;

        /// <summary>
        /// Force-flushes any parked mirror ops older than the timeout, so an idle
        /// already-in-world player still receives a newly joined player's rig even
        /// though it never sends the asset-load ack the primary flush waits for.
        /// </summary>
        private static void FlushStaleMirrors()
        {
            if (pendingMirrors.Count == 0)
            {
                return;
            }

            List<ENetPeerHandle> due = null;
            foreach (KeyValuePair<ENetPeerHandle, long> entry in pendingMirrorTick)
            {
                if (loopTick - entry.Value >= MirrorFlushTimeoutTicks)
                {
                    (due ??= new List<ENetPeerHandle>()).Add(entry.Key);
                }
            }

            if (due == null)
            {
                return;
            }

            foreach (ENetPeerHandle target in due)
            {
                Console.WriteLine("[info] mirror: fallback flush for an idle peer (no asset ack seen).");
                FlushPendingMirrors(target);
            }
        }

        /// <summary>Mirror ops kept for resending, per peer.</summary>
        private static readonly Dictionary<ENetPeerHandle, List<MirrorIntent>> mirrorResends = new Dictionary<ENetPeerHandle, List<MirrorIntent>>();
        private static readonly Dictionary<ENetPeerHandle, long> mirrorResendTick = new Dictionary<ENetPeerHandle, long>();
        private static readonly Dictionary<ENetPeerHandle, int> mirrorResendsLeft = new Dictionary<ENetPeerHandle, int>();

        /// <summary>How many times to resend mirror ops, and the gap between them (~3s at 50ms/iter).</summary>
        private const int MirrorResendAttempts = 3;
        private const long MirrorResendIntervalTicks = 60;

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
            if (mirrorResends.Count == 0)
            {
                return;
            }

            List<ENetPeerHandle> due = null;
            foreach (KeyValuePair<ENetPeerHandle, long> entry in mirrorResendTick)
            {
                if (loopTick - entry.Value >= MirrorResendIntervalTicks)
                {
                    (due ??= new List<ENetPeerHandle>()).Add(entry.Key);
                }
            }
            if (due == null)
            {
                return;
            }

            List<Structs.Structs.InterestOverride> remoteComponents =
                RemoteSeed.Select(id => new Structs.Structs.InterestOverride(id, 1)).ToList();

            foreach (ENetPeerHandle target in due)
            {
                int left = mirrorResendsLeft.TryGetValue(target, out int l) ? l : 0;
                if (left <= 0 || !mirrorResends.TryGetValue(target, out List<MirrorIntent> ops))
                {
                    mirrorResends.Remove(target);
                    mirrorResendTick.Remove(target);
                    mirrorResendsLeft.Remove(target);
                    continue;
                }

                foreach (MirrorIntent intent in ops)
                {
                    switch (intent.Op)
                    {
                        case MirrorOp.AddEntity:
                            SendOPHelper.SendAddEntityOP(target, intent.EntityId, "Traveller", "Default");
                            break;
                        case MirrorOp.AddComponents:
                            SendOPHelper.SendAddComponentOp(target, intent.EntityId, remoteComponents);
                            break;
                    }
                }

                mirrorResendsLeft[target] = left - 1;
                mirrorResendTick[target] = loopTick;
                Console.WriteLine("[info] mirror: resent ops to a peer (" + (left - 1) + " attempts left).");
            }
        }

        /// <summary>
        /// Sends the parked mirror ops for a peer, called when that peer acks an
        /// asset load. Uses the peer's FIRST ack after queuing: the ack payload is
        /// not parsed anywhere in this server, so this can fire one asset early if
        /// the peer was still mid-spawn - if rigs intermittently fail to appear
        /// for the JOINING client, parse the ack payload and match the asset.
        /// </summary>
        private static void FlushPendingMirrors(ENetPeerHandle target)
        {
            // Keep a copy so the ops can be RESENT: a client that was still
            // loading the prefab asset silently drops the AddEntity, and the rig
            // never appears (observed: the joining client saw nothing while the
            // already-in-world client saw it fine). Resends are safe - the client
            // tolerates duplicate entity/component adds with a warning.
            if (pendingMirrors.TryGetValue(target, out List<MirrorIntent> toRepeat) && toRepeat.Count > 0)
            {
                mirrorResends[target] = new List<MirrorIntent>(toRepeat);
                mirrorResendTick[target] = loopTick;
                mirrorResendsLeft[target] = MirrorResendAttempts;
            }

            pendingMirrorTick.Remove(target);
            if (!pendingMirrors.TryGetValue(target, out List<MirrorIntent> queue))
            {
                return;
            }
            pendingMirrors.Remove(target);

            // Context "Default", NOT "Player": "Player" selects Traveller@Player,
            // the full local rig whose duplication caused every camera/identity
            // regression. "Default" selects the plain Traveller remote rig.
            List<Structs.Structs.InterestOverride> remoteComponents =
                RemoteSeed.Select(id => new Structs.Structs.InterestOverride(id, 1)).ToList();

            foreach (MirrorIntent intent in queue)
            {
                bool ok = intent.Op switch
                {
                    MirrorOp.AddEntity => SendOPHelper.SendAddEntityOP(target, intent.EntityId, "Traveller", "Default"),
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

            foreach (MirrorIntent intent in Mirror.OnComponentUpdate(PeerIdentity.IdOf(sender), componentId, payload))
            {
                ENetPeerHandle? target = PeerIdentity.Instance.Resolve(new IntPtr((long)intent.TargetPeer));
                if (target == null)
                {
                    continue;
                }

                if (SendOPHelper.SendRawComponentUpdateOp(target, intent.EntityId, intent.ComponentId, intent.Payload!))
                {
                    Console.WriteLine("[relay] component " + intent.ComponentId + " of entity " + intent.EntityId + " -> another player");
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
        private static readonly List<uint> authoritativeComponents = new List<uint>{ 8050, 8051, 6908, 1260, 1097, 1003, 1241, 1082, TransformStateComponentId, ClientAuthoritativePlayerStateComponentId, UtilitySlotActivatedStateComponentId, RopeControlPointsComponentId};
        private static List<long> playerEntityIDs = new List<long>();

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
        private const uint TransformStateComponentId = 190602;

        /// <summary>
        /// ClientAuthoritativePlayerState: carries the player's bone/animation
        /// bytes (and relative-position fields). Seeded on remote rigs so
        /// BoneAnimationReader binds and animates, and granted to the owner so its
        /// movement writer publishes. See docs/component-ids.md.
        /// </summary>
        private const uint ClientAuthoritativePlayerStateComponentId = 1073;

        /// <summary>
        /// UtilitySlotActivatedState: whether the head/body/feet utility slot is
        /// active. The glider is a body utility; deploying it flips this, and
        /// UtilitySlotActivatedVisualizer on the remote rig opens/closes the wings.
        /// Granted so the owner publishes it, seeded so the remote reader binds.
        /// 1109 PilotState is deliberately NOT used - it steals the PilotVisualizer
        /// singleton and pokes LocalPlayer. See docs/component-ids.md.
        /// </summary>
        private const uint UtilitySlotActivatedStateComponentId = 6910;

        /// <summary>
        /// RopeControlPoints: the grapple rope's control points. Granted so the
        /// owner's RopeObserver publishes the live rope, seeded so the remote rig
        /// carries the data; a mod component (RemoteGrappleLine) reads it by
        /// component id and draws the line. See docs/component-ids.md.
        /// </summary>
        private const uint RopeControlPointsComponentId = 1098;

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
        private static readonly uint[] RemoteSeed = { TransformStateComponentId, 1086, 1081, 1088, ClientAuthoritativePlayerStateComponentId, UtilitySlotActivatedStateComponentId, RopeControlPointsComponentId };

        /// <summary>Who owns which player entity. Internal: the component update
        /// handlers validate entity ownership against it.</summary>
        internal static readonly PlayerRegistry Players = new PlayerRegistry();

        /// <summary>Published appearance per player entity; read by the 1088
        /// serializer branch, written by PlayerPropertiesState_Handler.</summary>
        internal static readonly AppearanceStore Appearances = new AppearanceStore();

        /// <summary>Decides which ops go to which peers so players can see each other.</summary>
        private static readonly RemotePlayerMirror Mirror = new RemotePlayerMirror(Players);

        private static long nextEntityId = 0;
        /// <summary>
        /// The one island every client loads, under one shared entity id, so that
        /// cross-client Parent references (see the island AddEntityOp below)
        /// resolve on every client. Allocated from the id counter on first use.
        /// </summary>
        private static long? sharedIslandEntityId;
        private static long SharedIslandEntityId
        {
            get
            {
                if (sharedIslandEntityId == null)
                {
                    sharedIslandEntityId = NextEntityId;
                }
                return sharedIslandEntityId.Value;
            }
        }

        public static long NextEntityId
        {
            get
            {
                return nextEntityId++;
            }
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


            // define initial world state for first chunk
            GameState.Instance.WorldState[0] = new List<SyncStep>()
            {
                new SyncStep(GameState.NextStateRequirement.ASSET_LOADED_RESPONSE, new Action<object>((object o) =>
                {
                    Console.WriteLine("[info] requesting the game to load the player asset...");

                    if (SendOPHelper.SendAssetLoadRequestOP((ENetPeerHandle)o, "notNeeded?", "Traveller", "Player"))
                    {
                        Console.WriteLine("[info] successfully serialized and queued AssetLoadRequestOp.");
                    }
                    else
                    {
                        Console.WriteLine("[error] failed to serialize and queue AssetLoadRequestOp.");
                    }
                })),
                new SyncStep(GameState.NextStateRequirement.ASSET_LOADED_RESPONSE, new Action<object>((object o) =>
                {
                    Console.WriteLine("[info] requesting the game to load the island from its asset bundles...");

                    if (SendOPHelper.SendAssetLoadRequestOP((ENetPeerHandle)o, "notNeeded?", "949069116@Island", "notNeeded?"))
                    {
                        Console.WriteLine("[info] successfully serialized and queued AssetLoadRequestOp.");
                    }
                    else
                    {
                        Console.WriteLine("[error] failed to serialize and queue AssetLoadRequestOp.");
                    }
                })),
                new SyncStep(GameState.NextStateRequirement.ADDED_ENTITY_RESPONSE, new Action<object>((object o) =>
                {
                    Console.WriteLine("[success] island asset loaded. requesting loading of island...");

                    // Every client gets the SAME island entity id. A remote player's
                    // rig positions itself by PARENTING: the client publishes
                    // TransformState with Parent = its island's entity id, and the
                    // receiving client's RelativeParentTransformChildHierarchyBehaviour
                    // looks that entity up locally to attach the rig. With per-client
                    // island ids (0 for one client, 2 for the next) the lookup found
                    // nothing and every remote avatar stayed frozen at the default
                    // seed position, ~90km off-island.
                    if (SendOPHelper.SendAddEntityOP((ENetPeerHandle)o, SharedIslandEntityId, "949069116@Island", "notNeeded?"))
                    {
                        Console.WriteLine("[info] successfully serialized and queued AddEntityOp.");
                    }
                    else
                    {
                        Console.WriteLine("[error] failed to serialize and queue AddEntityOp.");
                    }
                })),
                new SyncStep(GameState.NextStateRequirement.ADDED_ENTITY_RESPONSE, new Action<object>((object o) =>
                {
                    ENetPeerHandle peer = (ENetPeerHandle)o;
                    Console.WriteLine("[info] client ack'ed island spawning instruction (info by sdk, does not mean it truly spawned). requesting to spawn player...");

                    // Capture this peer's own entity id. Reading playerEntityIDs.Last()
                    // was only safe while a single client could ever connect; with
                    // several spawning at once it can return someone else's entity.
                    long playerEntityId = NextEntityId;
                    playerEntityIDs.Add(playerEntityId);

                    if(SendOPHelper.SendAddEntityOP(peer, playerEntityId, "Traveller", "Player"))
                    {
                        Console.WriteLine("[info] successfully serialized and queued AddEntityOp for player entity " + playerEntityId + ".");
                        MirrorNewPlayer(peer, playerEntityId);
                    }
                    else
                    {
                        Console.WriteLine("[error] failed to serialize and queue AddEntityOp.");
                    }
                }))
            };

            while (keepRunning)
            {
                loopTick++;

                // Fallback flush for parked mirror ops. The ack-driven flush only
                // fires when the target sends an asset-load ack - which an ALREADY-
                // IN-WORLD, idle player never does (it finished loading), so its
                // mirror of a newly joined player never spawned. After a short
                // delay (the asset request has had time to load) flush anyway.
                FlushStaleMirrors();
                // ResendMirrors() DISABLED: resending AddComponents re-applied the
                // DEFAULT seeded TransformState (0,100,0) - telemetry caught the
                // local player teleporting to exactly that point and then falling.
                // Re-seeding components on an already-spawned entity is unsafe.

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
                        Console.WriteLine("[warning] packet from an unknown peer, dropping.");
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
                        FlushPendingMirrors(sender);
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
                                bool isSendersOwnEntity = Players.EntityOf(PeerIdentity.IdOf(sender)) == entityId;
                                if(isSendersOwnEntity && !PeerManager.Instance.clientSetupState.Contains(keyValuePair.Key))
                                {
                                    // a player entity requests components for the first time, we need to setup a few things to make him work properly
                                    // some of this might not be needd anymore in the future once we sorted out a few things.
                                    //
                                    // we can make use of the fact that the game requests components for players in two stages, where the second one will terminate the loading screen of the client.
                                    // the second stage needs a few components setup properly, for this we need to inject one component and call auth changed for a few others once.

                                    // some components are needed in the first stage and need to be injected.
                                    // we also need PilotState since schematics for glider where added, as the game nullrefs in PlayerExternalDataVisualizer.IsDriving() now (1109)
                                    List<Structs.Structs.InterestOverride> injectedEarly = new List<Structs.Structs.InterestOverride> { new Structs.Structs.InterestOverride(1109, 1) };

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
                                Console.WriteLine("[info] game requests " + updateCount + " ComponentUpdate's for entity id " + entityId);

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

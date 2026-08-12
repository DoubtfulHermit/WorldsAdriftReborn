using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Improbable.Worker;
using Improbable.Worker.Internal;
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

                // Live-session bookkeeping for the operator dashboard. Keyed by
                // the same peer id every later lookup uses, and stamped with wall
                // clock (not the monotonic ServerClock) because the connect time
                // is written to a file the login server reads against ITS clock.
                Stats.OnConnect(PeerIdentity.IdOf(ePeer), DateTimeOffset.UtcNow);

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

            // The relay emitter's slice: this peer's coalesced movement state,
            // its ingest baselines, and every synthetic timeline it appears in
            // as sender OR recipient. A leaked baseline would judge a
            // reconnected player's fresh timestamps against a dead session.
            Relay.Forget(peerId);

            // The aboard tracker's slice, keyed by the same peer id. A disconnect
            // is a disembark the 1073 stream will never send, so dropping the peer
            // here is also what tells the (future) abandonment timer that the ship
            // may now be empty; Forget reports whether they were aboard for exactly
            // that consumer. Left behind, a departed peer would count forever as
            // "still aboard ship X".
            Multiplayer.AboardTransition leftAboard = Aboard.Forget(peerId);
            if (leftAboard.Change == Multiplayer.AboardChange.Disembarked)
            {
                Console.WriteLine("[info] peer left while aboard ship " + leftAboard.PreviousShipRootEntityId
                    + "; cleared from aboard tracker.");
            }

            // The carry-echo tracker's slice, keyed by the same peer id. Dropping it
            // here means a reconnecting peer's first genuine board is not deduped away
            // against the value the dead session last echoed. Same everything-contract
            // as the aboard tracker above.
            CarryEcho.Forget(peerId);

            // Drop the departed player's stored appearance so the store does not
            // grow across reconnects (entity ids are handed out monotonically, so
            // a stale record is never re-read, only wasted memory).
            if (ownEntity.HasValue)
            {
                Appearances.Forget(ownEntity.Value);

                // Stop the harvest latch FIRST. It is the only one of these that
                // is still actively doing work: left behind it would keep felling
                // a tree every 0.75 s on behalf of somebody who has logged out,
                // forever - and it can still grant items, so it has to stop
                // before the inventory below is saved and dropped.
                if (Harvest.Forget(ownEntity.Value))
                {
                    Console.WriteLine("[info] dropped the tree-cutting latch of entity " + ownEntity.Value + ".");
                }

                // Save, then drop, the departed player's inventory. The save is
                // the last chance a session gets: every mutation already wrote
                // through the push seam, but a server-side grant that happened
                // between the last push and the disconnect would otherwise be
                // lost. It is a no-op for a player whose character uid never
                // arrived - those inventories are session-scoped by design and
                // deliberately unsaveable.
                Game.Inventory.InventoryService.Forget(ownEntity.Value);

                // Save, then drop, the departed player's knowledge, on the same
                // last-chance contract as the inventory above: every scan and
                // purchase already wrote through, but a mutation between the last
                // write and this disconnect would otherwise be lost. A no-op for a
                // player whose character uid never arrived.
                Game.Knowledge.ProgressionService.Forget(ownEntity.Value);

                // Drop this player's live ship-blueprint builds and cancel any running
                // build timer, so the completion path never fires against a gone peer.
                Multiplayer.Crafting.ShipBlueprintBuildStore.ForgetPlayer(ownEntity.Value);
                Game.Crafting.ShipBuildTimerService.ForgetPlayer(ownEntity.Value);

                // Free any in-flight timed STATION craft guard this player held, so a re-use
                // of the id is clean. The deferred completion (if still pending) fires
                // harmlessly on the main loop and spawns the part into the world regardless.
                Game.Crafting.StationCraftTracker.ForgetPlayer(ownEntity.Value);

                Teleports.Forget(ownEntity.Value);

                // The fall watch keyed by the same entity. Left behind, the record
                // would still be counting rescue attempts for somebody who has
                // logged out - harmless in itself, but ForgetPeer's contract is
                // that it drops EVERY piece of per-peer state, and the last time
                // that contract was broken it cost a debugging round.
                Falls.Forget(ownEntity.Value);
            }

            // Drop this peer's slice of the component map. ForgetPeer's own
            // docblock claims to clean every piece of per-peer state and this
            // one was never in it, so a departed peer's stored component
            // references stayed live for the lifetime of the process - and the
            // inventory push seam iterates exactly this map to decide who to
            // send to, so a stale entry is a send to a peer that is gone.
            //
            // Each stored refId is a live native ClientObjects reference; the map
            // entry disappearing does NOT free it. The peer is gone, so every
            // refId under it is dead and safe to destroy (see ComponentRefCleanup
            // for the "never serialized again" contract). Destroy each BEFORE the
            // Remove, or the only handle to them is lost and they leak natively for
            // the life of the process.
            if (GameState.Instance.ComponentMap.TryGetValue(peer, out var peerComponents))
            {
                foreach (ulong refId in ComponentRefCleanup.RefsForDepartedPeer(peerComponents))
                {
                    ClientObjects.Instance.DestroyReference(refId);
                }
            }
            GameState.Instance.ComponentMap.Remove(peer);

            PeerManager.Instance.playerState.Remove(peer);
            PeerManager.Instance.clientSetupState.Remove(peer);

            // The peer's served-component ledger. A reused handle would otherwise
            // inherit a stale "already delivered" set and wrongly skip seeding the
            // next joiner's entities.
            ServedComponents.ForgetPeer(peer);

            // The peer's spawn-pacing metronome. Left behind, a reused handle would
            // inherit a stale nextDue and mis-pace the next joiner on that slot.
            SpawnPacers.Remove(peer);

            // The peer's wire-metrics window. Left behind it would keep emitting
            // an all-zero [rates] line for a ghost every five seconds, forever.
            Rates.Forget(peerId);

            // Live-session bookkeeping: this peer is gone. Drops it from the
            // online count and adds to the cumulative disconnect total the
            // dashboard shows. (The peak is a high-water mark and does not fall.)
            Stats.OnDisconnect(peerId);

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
        /// <summary>
        /// One monotonic clock for everything in this process that measures a
        /// deadline. Monotonic so NTP stepping the host's wall clock cannot make a
        /// timer fire twice or never.
        ///
        /// DECLARED FIRST, and it has to be: static field initializers run in
        /// TEXTUAL order, so a clock declared below its consumers would be null
        /// when they are constructed.
        /// </summary>
        private static readonly IClock ServerClock = new MonotonicClock();

        private static readonly MirrorSchedule Schedule = new MirrorSchedule(ServerClock);

        /// <summary>
        /// Per-peer wire counters behind the 5 s [rates] log line. Internal
        /// because SendOPHelper records every outbound send into it - the send
        /// side of "sends vs receives" only exists at that choke point.
        /// Declared below <see cref="ServerClock"/> for the same textual-order
        /// reason as everything else that holds it.
        /// </summary>
        internal static readonly PeerRates Rates = new PeerRates(ServerClock);

        /// <summary>
        /// How many ENet events one loop iteration may drain. See
        /// PollDrainPolicy for why one-per-iteration was an unbounded-queue
        /// death spiral. Overridable via WAREBORN_DRAIN_BUDGET.
        /// </summary>
        private static readonly int DrainBudget =
            PollDrainPolicy.BudgetFrom(Environment.GetEnvironmentVariable("WAREBORN_DRAIN_BUDGET"));

        /// <summary>
        /// The floor on how OFTEN a new AfterPlayer world entity is allowed to
        /// START loading on a joining client. The spawn handshake is already
        /// ack-gated per step, but on a LAN the acks come back in a couple of
        /// milliseconds, so ~44 world entities drain back-to-back the instant the
        /// loading screen lifts and the client's SYNCHRONOUS asset loader turns
        /// that into one long first-load hitch. Spacing each AfterPlayer entity's
        /// RequestAsset by this interval fades the world in over a second or two
        /// instead. Overridable via WAREBORN_SPAWN_PACE_MS; 0 disables pacing (the
        /// old one-burst behaviour). See <see cref="SpawnPacePolicy"/>.
        /// </summary>
        private static readonly TimeSpan SpawnPaceInterval =
            SpawnPacePolicy.IntervalFrom(Environment.GetEnvironmentVariable("WAREBORN_SPAWN_PACE_MS"));

        /// <summary>
        /// One pacing metronome per joining peer, so two clients joining at once
        /// each stream at the full rate rather than sharing one budget. Created
        /// lazily on a peer's first paced step and dropped in ForgetPeer on
        /// disconnect. Only ever touched from the single-threaded main loop and
        /// the ENet callbacks it drives, so it needs no lock. See
        /// <see cref="SpawnPacePolicy"/> and <see cref="CadenceTimer"/>.
        /// </summary>
        private static readonly Dictionary<ENetPeerHandle, CadenceTimer> SpawnPacers = new();

        /// <summary>
        /// Which components have already been delivered to each peer for each
        /// entity, so a repeat interest request never re-ADDS one the client still
        /// holds. See <see cref="Multiplayer.ServedComponentLedger{TPeer}"/> for the
        /// reason this exists (the walk-on-then-fall-through deck: a second seed of
        /// 1518/190602 was cycling ShipDeckVisualizer and destroying its solid
        /// collider). Forgotten on disconnect alongside the other per-peer state.
        /// </summary>
        internal static readonly Multiplayer.ServedComponentLedger<ENetPeerHandle> ServedComponents = new();

        /// <summary>
        /// Rate-limiter for the per-packet crash-isolation catch below. A modified
        /// client that sends a packet which throws on every frame must not turn the
        /// log into a fault-per-packet firehose; the first faults print in full and
        /// the rest are sampled with a running total. See PacketFaultThrottle.
        /// </summary>
        private static readonly PacketFaultThrottle PacketFaults = new PacketFaultThrottle();

        /// <summary>
        /// Every harvestable tree's CURRENT sectionMask, and the timer that decides
        /// when a held beam takes the next chunk out of one.
        ///
        /// Internal because ComponentsSerializer's 1036 branch reads the live mask
        /// off it: a client that checks the tree out after somebody else has
        /// chopped half of it must be told what is actually standing, not what the
        /// prefab was authored with.
        /// </summary>
        internal static readonly TreeHarvest Harvest = new TreeHarvest(ServerClock);

        /// <summary>
        /// Applies every cut whose timer has elapsed and tells the clients.
        ///
        /// TWO RULES, both of which cost a debugging round elsewhere in this file
        /// if broken:
        ///
        /// 1. The update is pushed to each peer DIRECTLY, never through
        ///    <see cref="RelayToOtherPlayers"/>. That method exists to forward a
        ///    player's update about THEMSELVES and substitutes the sender's own
        ///    entity id for the address; routed through it, a tree's mask change
        ///    would arrive addressed to whoever happened to be chopping, and the
        ///    tree would never change on anyone's screen.
        /// 2. It sends ONLY SetSectionMask, never <c>Data.ToUpdate()</c>.
        ///    TreeFSimState's ToUpdate sets all seven properties, and one of them
        ///    is <c>dynamic</c> - whose setter on the client starts a falling-tree
        ///    audio loop on the true edge. Sending the whole component every 0.75 s
        ///    would also re-assert sectionCount and massPerSection at the client
        ///    for no reason. One field changed, one field sent.
        ///
        /// Peers that have not been served the tree's 1036 are skipped: an update
        /// for a component a client does not hold is at best ignored, and the
        /// ComponentMap lookup that establishes it is the same one the update path
        /// uses, so this cannot disagree with reality.
        /// </summary>
        private static void TickTreeHarvest()
        {
            foreach (TreeSectionMaskChange change in Harvest.Due())
            {
                Console.WriteLine("[info] " + change + ".");

                foreach (ENetPeerHandle peer in PeerManager.Instance.playerState.Keys.ToList())
                {
                    if (!GameState.Instance.ComponentMap.TryGetValue(peer, out Dictionary<long, Dictionary<uint, ulong>>? byEntity)
                        || !byEntity.TryGetValue(change.TreeEntityId, out Dictionary<uint, ulong>? byComponent)
                        || !byComponent.TryGetValue(TreeFSimStateComponentId, out ulong refId))
                    {
                        continue;
                    }

                    Bossa.Travellers.Materials.TreeFSimState.Update maskOnly =
                        new Bossa.Travellers.Materials.TreeFSimState.Update().SetSectionMask(change.SectionMask);

                    // Keep this peer's stored component in step with what it has
                    // just been told, so a later re-serve of 1036 from the stored
                    // object cannot resurrect a felled section.
                    if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(refId) is Bossa.Travellers.Materials.TreeFSimState.Data stored)
                    {
                        maskOnly.ApplyTo(stored);
                    }

                    SendOPHelper.SendComponentUpdateOp(peer, change.TreeEntityId,
                        new List<uint> { TreeFSimStateComponentId },
                        new List<object> { maskOnly });
                }

                // ------------------------------------------------------------------
                // INVENTORY GRANT SEAM (Phase 5.4). The empty comment that used to
                // live here listed everything a grant needs - an id allocator, a
                // placement search, stacking, a full-replacement 1081 push, the
                // 8060 toast. All of that now exists behind ONE call: HarvestReward
                // resolves the yield, grants it through the single InventoryPush
                // seam (so nothing here races 1081's owner), and fires the native
                // "Salvaged <material> xN" toast.
                //
                // Everything the award needs is already in `change`:
                //   change.CutterEntityId - the PLAYER entity that owns the beam.
                //   change.WoodType       - the source key ("birch"), pre-registered
                //                           in HarvestReward from Trees.WoodType.
                //   change.SectionsFelled - the unit count for this cut.
                //
                // A metal beam+node pair (the sibling agents) reaches the SAME
                // HarvestReward.Award from its own hit handler - see the seam note
                // on HarvestReward.
                Game.Gathering.HarvestReward.Award(
                    change.CutterEntityId,
                    change.WoodType,
                    change.SectionsFelled,
                    "tree " + change.TreeEntityId + " section " + change.SectionId);
            }
        }

        /// <summary>
        /// One salvage shot landed on <paramref name="nodeEntityId"/>, fired by
        /// <paramref name="harvesterEntityId"/> (the shooter's own player entity,
        /// which owns the 2106 the shot rode). This is the metal counterpart to the
        /// award seam in <see cref="TickTreeHarvest"/>, and the reason it lives here
        /// rather than in the handler is the same reason the tree's does: it is where
        /// the pure depletion policy (<see cref="MetalHarvest"/>) meets the node
        /// ledger (<see cref="Nodes"/>), the yield table and the wire.
        ///
        /// Most shots do nothing here: the beam rests on trees, hulls, players and
        /// already-emptied husks, and <see cref="MetalHarvest.Hit"/> reports the
        /// deplete transition on exactly ONE shot per node. Only that shot grants -
        /// so there is no double-payout even though a held beam keeps publishing
        /// ShotEvents until the node teleports out of the raycast's reach.
        /// </summary>
        internal static void OnSalvageShot(long harvesterEntityId, long nodeEntityId,
            Improbable.Math.Coordinates shotCoordinate)
        {
            if (!MetalHarvest.IsNode(nodeEntityId))
            {
                return;
            }

            Multiplayer.MetalNode? node = Nodes.NodeOf(nodeEntityId);

            // A DEPOSIT runs the real crust/core mining loop; a nugget keeps the
            // count-and-sink path below. The two share the ledger and the shot
            // counter but nothing else about depletion.
            if (node != null && node.IsDeposit)
            {
                OnDepositShot(harvesterEntityId, nodeEntityId, node, shotCoordinate);
                return;
            }

            MetalHitOutcome outcome = MetalHarvest.Hit(nodeEntityId);
            if (!outcome.Depleted)
            {
                return;
            }

            // Keep the ledger's destroyed flag in step - it STAYS in the registry
            // (rule 1) so a late joiner is told the truth, sunk rather than intact -
            // then grant, then make it visibly vanish.
            Nodes.MarkDestroyed(nodeEntityId);

            string metalType = node?.MetalType ?? "metal";

            Console.WriteLine("[info] metal node " + nodeEntityId + " depleted by entity "
                + harvesterEntityId + ": " + outcome.Units + " x " + metalType + ".");

            // ORDER as the tree's: award first (grant + the 8060 "Salvaged X xN"
            // toast, which fires only if the grant landed), then the visual. A metal
            // whose itemTypeId is not in itemData.json (cobalt, aurium today) grants
            // nothing and toasts nothing - HarvestReward/InventoryService log it -
            // but the node still depletes and sinks, so it is never an un-minable
            // rock, just a silent one.
            Game.Gathering.HarvestReward.Award(
                harvesterEntityId,
                metalType,
                outcome.Units,
                "metal node " + nodeEntityId);

            BroadcastNodeDepletion(nodeEntityId);
        }

        /// <summary>
        /// One salvage shot on an anchored metal DEPOSIT: fracture the crust where the
        /// beam hit, wear the core, and - on the shot that empties it - destroy the
        /// core, explode the crust and grant the metal. The deposit stays anchored
        /// throughout (no sink); depletion is entirely state-based, exactly as the
        /// shipped client's own deposit loop is.
        /// </summary>
        private static void OnDepositShot(long harvesterEntityId, long nodeEntityId,
            Multiplayer.MetalNode node, Improbable.Math.Coordinates shotCoordinate)
        {
            // Already emptied? the beam legitimately keeps resting on the destroyed
            // rock and publishing ShotEvents; nothing more to do.
            if (Nodes.IsDestroyed(nodeEntityId))
            {
                return;
            }

            // 1. CRUST. The shot's LOCAL-space offset from the entity ROOT
            //    (base.transform - the transform the server knows and the late-join
            //    replay path uses), PLAIN METRES, NO x4096: the client feeds both the
            //    shot coordinate and the entity's own position through the SAME
            //    RemapGlobalToUnityVector (each subtracts the identical world origin),
            //    so the origin cancels and the local offset is just
            //    shotCoordinate - entityMetres. A server-placed deposit has identity
            //    rotation, so world axes are its local axes. (12283 shotPoints is a
            //    Vector3f cloud, not fixed point - mixing that up is the single easiest
            //    way to get this wrong, findings-metal-deposits.md.)
            Multiplayer.ShotPoint local = new Multiplayer.ShotPoint(
                (float)(shotCoordinate.X - node.Position.MetresX),
                (float)(shotCoordinate.Y - node.Position.MetresY),
                (float)(shotCoordinate.Z - node.Position.MetresZ));
            Nodes.AddShotPoint(nodeEntityId, local);
            BroadcastCrustShot(nodeEntityId, local);

            // 2. CORE HEALTH. Count the shot (MetalHarvest, sized to ten for a deposit)
            //    and tell every viewer the decremented 1016 so the client's own
            //    HealthPct-driven core-crack models advance.
            MetalHitOutcome outcome = MetalHarvest.Hit(nodeEntityId);
            BroadcastDepositHealth(nodeEntityId);

            Console.WriteLine("[info] deposit " + nodeEntityId + " shot by entity " + harvesterEntityId
                + ": " + MetalHarvest.HitsOn(nodeEntityId) + "/" + Multiplayer.MetalDeposits.ShotsToDeplete
                + " shots, core health "
                + Multiplayer.MetalDeposits.HealthAfter(MetalHarvest.HitsOn(nodeEntityId)) + ".");

            if (!outcome.Depleted)
            {
                return;
            }

            // 3. DEPLETION. Mark the ledger destroyed (it STAYS in the registry, rule
            //    1, so a late joiner is seeded isDestroyed=true - whose one-shot
            //    suppression gives the SILENT destroyed state, not a replayed
            //    explosion), tell present clients the core is destroyed and the crust
            //    exploded, then award (grant + the 8060 toast, which fires only if the
            //    grant landed). ORDER matches the tree/nugget: award before the flag is
            //    the wire's concern, not the ledger's.
            Nodes.MarkDestroyed(nodeEntityId);
            BroadcastDepositDestroyed(nodeEntityId);

            Console.WriteLine("[info] metal DEPOSIT " + nodeEntityId + " depleted by entity "
                + harvesterEntityId + ": " + outcome.Units + " x " + node.MetalType + ".");

            Game.Gathering.HarvestReward.Award(
                harvesterEntityId,
                node.MetalType,
                outcome.Units,
                "metal deposit " + nodeEntityId);

            // 4. RELEASE THE SHARD. Destroying the core is exactly the retail seam that
            //    frees a lodged atlas shard into the world (findings-atlas-shards §2
            //    Phase B). ReleaseByHost flips each lodged shard to RELEASED once, so
            //    this fires on the SAME single deplete transition as the destroyed
            //    broadcast above - never on a held beam still resting on the dead core.
            ReleaseAtlasShardsFor(nodeEntityId);
        }

        /// <summary>
        /// Releases every atlas shard lodged in a destroyed deposit's core: the
        /// server's counterpart to the shipped client's "core Exploded -> shard
        /// rigidbody goes non-kinematic" chain. For each shard the state ledger
        /// transitions Lodged -> Released (once), and every viewer holding the shard is
        /// told its 2102 is now dislodged and its 1210 PickUp prompt is available.
        ///
        /// RATE + RELAY: this is EVENT-driven - one 2102 + one 1210 update per shard,
        /// on the single core-destruction transition, NOT a per-frame stream. Both are
        /// pushed to each peer DIRECTLY (never through RelayToOtherPlayers, which would
        /// re-address them to the shooter's own avatar). A shard is one-to-one with a
        /// deposit today, so this is at most one shard per destroyed deposit.
        /// </summary>
        private static void ReleaseAtlasShardsFor(long depositEntityId)
        {
            foreach (long shardId in AtlasShards.ReleaseByHost(depositEntityId))
            {
                Console.WriteLine("[info] atlas shard " + shardId + " released from destroyed deposit "
                    + depositEntityId + "; it can now be picked up.");
                BroadcastShardReleased(shardId);
            }
        }

        /// <summary>
        /// Tells every viewer of a released shard its 2102 is dislodged (isLodged=false
        /// + a transient Dislodged event) and its 1210 prompt is available. Pushed
        /// directly and reliably, only to peers that already hold each component; peers
        /// that have not checked the shard out are seeded the released state from the
        /// ledger when they do (the serializer reads the same AtlasShards state).
        /// </summary>
        private static void BroadcastShardReleased(long shardEntityId)
        {
            foreach (ENetPeerHandle peer in PeerManager.Instance.playerState.Keys.ToList())
            {
                if (TryGetStoredComponentRef(peer, shardEntityId, LodgeableStateComponentId, out ulong lodgeRef))
                {
                    Bossa.Travellers.Materials.LodgeableState.Update lodgeUpdate =
                        new Bossa.Travellers.Materials.LodgeableState.Update()
                            .SetIsLodged(false)
                            .AddOnDislodged(new Bossa.Travellers.Materials.Dislodged());
                    if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(lodgeRef) is Bossa.Travellers.Materials.LodgeableState.Data storedLodge)
                    {
                        lodgeUpdate.ApplyTo(storedLodge);
                    }
                    SendOPHelper.SendComponentUpdateOp(peer, shardEntityId,
                        new List<uint> { LodgeableStateComponentId },
                        new List<object> { lodgeUpdate });
                }

                if (TryGetStoredComponentRef(peer, shardEntityId, InteractiveStateComponentId, out ulong interactRef))
                {
                    Bossa.Travellers.Interact.InteractiveState.Update availUpdate =
                        new Bossa.Travellers.Interact.InteractiveState.Update().SetAvailable(true);
                    if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(interactRef) is Bossa.Travellers.Interact.InteractiveState.Data storedInteract)
                    {
                        availUpdate.ApplyTo(storedInteract);
                    }
                    SendOPHelper.SendComponentUpdateOp(peer, shardEntityId,
                        new List<uint> { InteractiveStateComponentId },
                        new List<object> { availUpdate });
                }
            }
        }

        /// <summary>
        /// The pickup TRANSACTION for an atlas shard: the authoritative side of a
        /// native 1211 <c>InteractWithObject(shard, PickUp)</c>. Called from
        /// InteractAgentState_Handler once per PickUp interaction the client issues.
        ///
        /// The DECISION is the pure <see cref="Multiplayer.AtlasPickupPolicy"/>; this
        /// method is only the thin transaction around it: gather the facts, decide,
        /// then RESERVE -> Grant -> Collect, rolling the reservation back if the grant
        /// fails so a full inventory (or the still-pending item id) does not consume the
        /// shard. Ownership and verb are passed in from the handler (which already knows
        /// them) so the policy is the single gate.
        /// </summary>
        /// <returns>The outcome, for logging by the caller.</returns>
        internal static Multiplayer.AtlasPickupOutcome TryCollectAtlasShard(
            long playerEntityId, long shardEntityId, bool peerOwnsPlayer, bool verbIsPickUp)
        {
            Multiplayer.AtlasPickupDecision decision = Multiplayer.AtlasPickupPolicy.Evaluate(
                peerOwnsPlayer: peerOwnsPlayer,
                verbIsPickUp: verbIsPickUp,
                targetIsShard: AtlasShards.IsShard(shardEntityId),
                released: AtlasShards.IsReleased(shardEntityId),
                collected: AtlasShards.IsCollected(shardEntityId),
                reservedByOther: AtlasShards.IsReservedByOther(shardEntityId, playerEntityId),
                // No server-authoritative player position is kept keyed by entity id, and
                // the client only issues the interaction after its OWN range check - the
                // same trust the salvage path already extends to the client raycast. The
                // pure policy fully supports a distance when a position source lands; the
                // retail tolerance itself is not recoverable (findings §5).
                distanceMetres: null,
                radiusMetres: Multiplayer.AtlasShardCatalogue.PickUpRadius);

            if (!decision.ShouldGrant)
            {
                return decision.Outcome;
            }

            // RESERVE first, so a second PickUp event in the same poll drain cannot also
            // reach the grant. A failed reserve means someone beat us to it this drain.
            if (!AtlasShards.Reserve(shardEntityId, playerEntityId))
            {
                return Multiplayer.AtlasPickupOutcome.Reserved;
            }

            // GRANT. Returns the item id on success, null when the type is unknown (the
            // pending placeholder id until refdata lands) or the grid is full.
            int? grantedItemId = Game.Inventory.InventoryService.Grant(
                playerEntityId, Multiplayer.AtlasShardCatalogue.ItemTypeId, 1);

            if (grantedItemId == null)
            {
                // Roll the reservation back so the shard stays pickable - a full grid
                // now might have room later, and the pending item id will resolve once
                // the refdata row is added. The shard is NOT consumed.
                AtlasShards.Rollback(shardEntityId, playerEntityId);
                Console.WriteLine("[warning] atlas shard " + shardEntityId + " pickup by entity "
                    + playerEntityId + " did not grant '" + Multiplayer.AtlasShardCatalogue.ItemTypeId + "'"
                    + (Multiplayer.AtlasShardCatalogue.IsItemIdPending
                        ? " - the retail itemTypeId is PENDING: add the row to itemData.json and set "
                          + "AtlasShardCatalogue.ItemTypeId (findings-atlas-shards.md §5)."
                        : " (unknown item type or full inventory grid).")
                    + " Reservation rolled back; the shard stays available.");
                return Multiplayer.AtlasPickupOutcome.GrantFailed;
            }

            // COMMIT: the item is in the bag (Grant already pushed the 1081 update). Mark
            // the shard collected and make the world entity vanish for everyone.
            AtlasShards.Collect(shardEntityId, playerEntityId);
            Console.WriteLine("[info] atlas shard " + shardEntityId + " collected by entity "
                + playerEntityId + " -> inventory item " + grantedItemId + " ('"
                + Multiplayer.AtlasShardCatalogue.ItemTypeId + "').");
            BroadcastShardCollected(shardEntityId);
            return Multiplayer.AtlasPickupOutcome.Grant;
        }

        /// <summary>
        /// Tells every viewer that a collected shard is gone: its 1210 prompt is no
        /// longer available and its 190602 is sunk under the terrain (WAReborn has no
        /// RemoveEntityOp, so the nugget's sink teleport is how a world pickup vanishes
        /// - findings-metal-deposits, "SURFACE NUGGETS"). The collecting client already
        /// cleared the model optimistically (MetalDepositAtlasVisualiser_client
        /// OnInteractionAttempted); this makes the removal authoritative for everyone.
        /// A late joiner is instead seeded the collected state (1210 unavailable +
        /// sunk transform) by the serializer, which reads the same AtlasShards ledger.
        /// </summary>
        private static void BroadcastShardCollected(long shardEntityId)
        {
            Multiplayer.FixedPointPosition intact =
                WorldEntities.TransformSeedFor(shardEntityId);
            Multiplayer.FixedPointPosition sunk = Multiplayer.MetalNodes.Sink(intact);

            foreach (ENetPeerHandle peer in PeerManager.Instance.playerState.Keys.ToList())
            {
                if (TryGetStoredComponentRef(peer, shardEntityId, InteractiveStateComponentId, out ulong interactRef))
                {
                    Bossa.Travellers.Interact.InteractiveState.Update availUpdate =
                        new Bossa.Travellers.Interact.InteractiveState.Update().SetAvailable(false);
                    if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(interactRef) is Bossa.Travellers.Interact.InteractiveState.Data storedInteract)
                    {
                        availUpdate.ApplyTo(storedInteract);
                    }
                    SendOPHelper.SendComponentUpdateOp(peer, shardEntityId,
                        new List<uint> { InteractiveStateComponentId },
                        new List<object> { availUpdate });
                }

                if (TryGetStoredComponentRef(peer, shardEntityId, TransformStateComponentId, out ulong transformRef))
                {
                    Improbable.Corelibrary.Transforms.TransformState.Update sink =
                        new Improbable.Corelibrary.Transforms.TransformState.Update()
                            .SetLocalPosition(new Improbable.Corelibrary.Math.FixedPointVector3(
                                new Improbable.Collections.List<long> { sunk.X, sunk.Y, sunk.Z }));
                    if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(transformRef) is Improbable.Corelibrary.Transforms.TransformState.Data storedTransform)
                    {
                        sink.ApplyTo(storedTransform);
                    }
                    SendOPHelper.SendComponentUpdateOp(peer, shardEntityId,
                        new List<uint> { TransformStateComponentId },
                        new List<object> { sink });
                }
            }
        }

        /// <summary>
        /// Tells every client that holds the node its depletion: the nugget has no
        /// damage feedback of its own, so "it's gone" is a 190602 teleport that sinks
        /// it under the terrain (findings-metal-deposits, "SURFACE NUGGETS").
        ///
        /// Mirrors <see cref="TickTreeHarvest"/>'s fan-out, and for the same two
        /// reasons: the update is pushed to each peer DIRECTLY (never through
        /// <see cref="RelayToOtherPlayers"/>, which would re-address it to the
        /// shooter's own avatar and teleport the PLAYER underground), and it carries
        /// ONE field (localPosition) so nothing else on the transform is re-asserted.
        /// Peers that have not checked the node out are skipped - they will be seeded
        /// the sunk position from the registry when they do (rule 1), which is why
        /// <see cref="Multiplayer.MetalNodes.Sink"/> is a pure function both paths call.
        /// </summary>
        private static void BroadcastNodeDepletion(long nodeEntityId)
        {
            Multiplayer.MetalNode? node = Nodes.NodeOf(nodeEntityId);
            if (node == null)
            {
                return;
            }

            Multiplayer.FixedPointPosition sunk = Multiplayer.MetalNodes.Sink(node.Position);

            foreach (ENetPeerHandle peer in PeerManager.Instance.playerState.Keys.ToList())
            {
                if (!GameState.Instance.ComponentMap.TryGetValue(peer, out Dictionary<long, Dictionary<uint, ulong>>? byEntity)
                    || !byEntity.TryGetValue(nodeEntityId, out Dictionary<uint, ulong>? byComponent)
                    || !byComponent.TryGetValue(TransformStateComponentId, out ulong refId))
                {
                    continue;
                }

                Improbable.Corelibrary.Transforms.TransformState.Update sink =
                    new Improbable.Corelibrary.Transforms.TransformState.Update()
                        .SetLocalPosition(new Improbable.Corelibrary.Math.FixedPointVector3(
                            new Improbable.Collections.List<long> { sunk.X, sunk.Y, sunk.Z }));

                // Keep this peer's stored 190602 in step with what it has just been
                // told, so a later re-serve from the stored object cannot resurrect
                // the node at its intact spot.
                if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(refId) is Improbable.Corelibrary.Transforms.TransformState.Data stored)
                {
                    sink.ApplyTo(stored);
                }

                SendOPHelper.SendComponentUpdateOp(peer, nodeEntityId,
                    new List<uint> { TransformStateComponentId },
                    new List<object> { sink });
            }
        }

        /// <summary>
        /// The stored native reference for one (peer, entity, component), or false if
        /// that peer has not been served the component. The precondition every deposit
        /// broadcast shares: a live update is only pushed to a peer that already holds
        /// the component (it checked the deposit out); peers that have not are skipped
        /// and pick up the accumulated state from the registry when they do check out.
        /// </summary>
        private static bool TryGetStoredComponentRef(ENetPeerHandle peer, long entityId, uint componentId, out ulong refId)
        {
            refId = 0;
            return GameState.Instance.ComponentMap.TryGetValue(peer, out Dictionary<long, Dictionary<uint, ulong>>? byEntity)
                && byEntity.TryGetValue(entityId, out Dictionary<uint, ulong>? byComponent)
                && byComponent.TryGetValue(componentId, out refId);
        }

        /// <summary>
        /// Tells every viewer of a deposit that its crust just fractured at one point.
        /// Carries BOTH channels the shipped crust visualiser reads: the full
        /// <c>shotPoints</c> STATE (so a re-serve and any future late joiner
        /// reconstruct the same hole via SimulatePastShot) and the single new point as
        /// a transient <c>ShotCrustEvent</c> (the LIVE break VFX via SimulateShot). The
        /// event is not part of Data, so it is never replayed - which is exactly what
        /// stops a late joiner seeing every past impact flash at once.
        ///
        /// RATE + RELAY: one update per salvage shot, and the client rate-limits itself
        /// to ~0.75 s per deploy (MinDeployInterval), so this is a low-rate, event-paced
        /// broadcast, NOT a per-frame stream. It is pushed to each peer DIRECTLY (never
        /// through RelayToOtherPlayers, which re-addresses to the shooter's own avatar);
        /// SendComponentUpdateOp is the same reliable component-update channel the tree
        /// and nugget already use.
        /// </summary>
        private static void BroadcastCrustShot(long nodeEntityId, Multiplayer.ShotPoint newPoint)
        {
            Improbable.Collections.List<Improbable.Math.Vector3f> points =
                new Improbable.Collections.List<Improbable.Math.Vector3f>();
            foreach (Multiplayer.ShotPoint sp in Nodes.ShotPointsOf(nodeEntityId))
            {
                points.Add(new Improbable.Math.Vector3f(sp.X, sp.Y, sp.Z));
            }
            Improbable.Math.Vector3f offset = new Improbable.Math.Vector3f(newPoint.X, newPoint.Y, newPoint.Z);

            foreach (ENetPeerHandle peer in PeerManager.Instance.playerState.Keys.ToList())
            {
                if (!TryGetStoredComponentRef(peer, nodeEntityId, MetalRockCrustStateComponentId, out ulong refId))
                {
                    continue;
                }

                Bossa.Travellers.Materials.MetalRockCrustState.Update crustUpdate =
                    new Bossa.Travellers.Materials.MetalRockCrustState.Update()
                        .SetShotPoints(points)
                        .AddShot(new Bossa.Travellers.Materials.ShotCrustEvent(offset));

                // Keep this peer's stored crust in step with the state half of what it
                // has just been told, so a later re-serve from the stored object carries
                // the same shotPoints. The event half is transient and not stored.
                if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(refId) is Bossa.Travellers.Materials.MetalRockCrustState.Data stored)
                {
                    crustUpdate.ApplyTo(stored);
                }

                SendOPHelper.SendComponentUpdateOp(peer, nodeEntityId,
                    new List<uint> { MetalRockCrustStateComponentId },
                    new List<object> { crustUpdate });
            }
        }

        /// <summary>
        /// Tells every viewer of a deposit its core's decremented 1016 health, so the
        /// client's HealthPct-driven core-crack damage models advance. One update per
        /// shot (same low, event-paced rate as the crust), pushed directly and
        /// reliably. Health is a pure function of the shot count (MetalDeposits.
        /// HealthAfter), so this live value and a late joiner's 1016 seed agree without
        /// storing a second number.
        /// </summary>
        private static void BroadcastDepositHealth(long nodeEntityId)
        {
            int health = Multiplayer.MetalDeposits.HealthAfter(MetalHarvest.HitsOn(nodeEntityId));

            foreach (ENetPeerHandle peer in PeerManager.Instance.playerState.Keys.ToList())
            {
                if (!TryGetStoredComponentRef(peer, nodeEntityId, ItemHealthStateComponentId, out ulong refId))
                {
                    continue;
                }

                Bossa.Travellers.Items.ItemHealthState.Update healthUpdate =
                    new Bossa.Travellers.Items.ItemHealthState.Update().SetHealth(health);

                if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(refId) is Bossa.Travellers.Items.ItemHealthState.Data stored)
                {
                    healthUpdate.ApplyTo(stored);
                }

                SendOPHelper.SendComponentUpdateOp(peer, nodeEntityId,
                    new List<uint> { ItemHealthStateComponentId },
                    new List<object> { healthUpdate });
            }
        }

        /// <summary>
        /// Tells every viewer of a deposit that its core is destroyed (2103
        /// isDestroyed) and its crust exploded (12283 exploded). Fired ONCE, on the
        /// deplete transition. Both are single-field sets pushed directly and reliably;
        /// the 1016 health was already driven to zero by the last
        /// <see cref="BroadcastDepositHealth"/>. A late joiner is instead seeded these
        /// as destroyed Data (ComponentsSerializer), whose one-shot suppression shows
        /// the silent destroyed rock rather than a replayed blast.
        /// </summary>
        private static void BroadcastDepositDestroyed(long nodeEntityId)
        {
            foreach (ENetPeerHandle peer in PeerManager.Instance.playerState.Keys.ToList())
            {
                if (TryGetStoredComponentRef(peer, nodeEntityId, MetalRockCoreStateComponentId, out ulong coreRef))
                {
                    Bossa.Travellers.Materials.MetalRockCoreState.Update coreUpdate =
                        new Bossa.Travellers.Materials.MetalRockCoreState.Update().SetIsDestroyed(true);
                    if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(coreRef) is Bossa.Travellers.Materials.MetalRockCoreState.Data storedCore)
                    {
                        coreUpdate.ApplyTo(storedCore);
                    }
                    SendOPHelper.SendComponentUpdateOp(peer, nodeEntityId,
                        new List<uint> { MetalRockCoreStateComponentId },
                        new List<object> { coreUpdate });
                }

                if (TryGetStoredComponentRef(peer, nodeEntityId, MetalRockCrustStateComponentId, out ulong crustRef))
                {
                    Bossa.Travellers.Materials.MetalRockCrustState.Update crustUpdate =
                        new Bossa.Travellers.Materials.MetalRockCrustState.Update().SetExploded(true);
                    if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(crustRef) is Bossa.Travellers.Materials.MetalRockCrustState.Data storedCrust)
                    {
                        crustUpdate.ApplyTo(storedCrust);
                    }
                    SendOPHelper.SendComponentUpdateOp(peer, nodeEntityId,
                        new List<uint> { MetalRockCrustStateComponentId },
                        new List<object> { crustUpdate });
                }
            }
        }

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

            // Not everything a client publishes about itself is worth putting on
            // everyone else's wire. The aim components (1231 SalvagerAimerState,
            // 1037 TreeCutterState) are filtered here rather than at the mirror,
            // because the reason is a property of THIS method: it re-addresses
            // every relayed update to the SENDER's own entity id. That is right
            // for a position and wrong for a payload whose meaning is a reference
            // to a third entity - the tree - read by behaviours that only exist on
            // a local rig. See MirrorSendPolicy.IsRelayedToOtherPlayers.
            //
            // BEFORE the copy: these arrive at raycast rate, so relaying them
            // would allocate a byte[] per packet per peer to be discarded.
            if (!MirrorSendPolicy.IsRelayedToOtherPlayers(componentId))
            {
                return;
            }

            // The two movement streams do not travel this path under relay v2:
            // their typed handlers already fed RelayEmitter (latest-state ingest),
            // and the emitter puts them on the wire at a fixed cadence with the
            // pairing timestamp rewritten. Also BEFORE the copy - under v2 the
            // per-packet byte[] for movement (the overwhelming majority of relay
            // traffic) is never allocated at all. With WAREBORN_RELAY_V2=0 this
            // gate is inert and the raw arrival-order path below is exactly what
            // it always was.
            if (Networking.RelayEmitter.CoalescesComponent(componentId))
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
                    // Guarded BEFORE building the line: ServerLog's own contract
                    // forbids call-site concatenation, because the two Describe()
                    // allocations and the concat ran per relayed packet whether
                    // or not Verbose was set - on the hot path, for nothing.
                    if (ServerLog.Verbose)
                    {
                        ServerLog.Trace("[relay] component " + intent.ComponentId + " of entity " + intent.EntityId
                            + ": " + Describe(senderId) + " -> " + Describe(intent.TargetPeer));
                    }
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
        /// <summary>
        /// Whether deployable placement is armed (WAREBORN_PLACEMENT=1). Declared
        /// here, ABOVE <see cref="authoritativeComponents"/>, because a static field
        /// initialiser reads it, and static initialisers run in textual order. Kept
        /// as its own bool rather than reading <c>Placement.Enabled</c> so it does not
        /// depend on the init order of that heavier field.
        /// </summary>
        private static readonly bool PlacementEnabled =
            Environment.GetEnvironmentVariable("WAREBORN_PLACEMENT") == "1";

        // The set itself lives in MirrorSendPolicy so it is testable. Placement's 1017
        // grant is appended ONLY when the feature is armed, so an un-flagged server
        // grants exactly what it always did (see MirrorSendPolicy.PlacementAuthoritativeComponents).
        private static readonly List<uint> authoritativeComponents = BuildAuthoritativeComponents();

        private static List<uint> BuildAuthoritativeComponents()
        {
            List<uint> list = new List<uint>(MirrorSendPolicy.AuthoritativeComponents);
            if (PlacementEnabled)
            {
                list.AddRange(MirrorSendPolicy.PlacementAuthoritativeComponents);
                // The placed-shipyard BUILD UI shares the placement flag (a build UI is
                // only reachable through a placed shipyard). 1208 + 1270 are the two
                // client writers the FRAME DESIGNS / SHIP BLUEPRINTS behaviours [Require].
                list.AddRange(MirrorSendPolicy.ShipBuildUiAuthoritativeComponents);
                // The PART-MOUNT toolchain shares the flag too (a part is only ever lifted
                // at a shipyard-docked ship). 1070 (the commit) + 1239 (carry notifications)
                // are the two client writers BuilderObserver / PlayerPlacementToolBehaviour
                // [Require]; 1071 stays a server-owned reader (injected, not granted).
                list.AddRange(MirrorSendPolicy.PartMountAuthoritativeComponents);
            }
            return list;
        }

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
        /// TreeFSimState: a tree's sectionMask, i.e. which of its twelve sections
        /// still exist. The ONLY thing a client needs to render a tree coming
        /// apart - TreeVisualizer activates and deactivates section GameObjects by
        /// bit - and therefore the only thing chopping puts on the wire.
        /// See docs/component-ids.md.
        /// </summary>
        private const uint TreeFSimStateComponentId = 1036;

        /// <summary>1016 ItemHealthState - a deposit core's live health, decremented per salvage shot.</summary>
        private const uint ItemHealthStateComponentId = 1016;

        /// <summary>1255 MetalDepositState - the deposit's variantId + coreId; static once seeded.</summary>
        private const uint MetalDepositStateComponentId = 1255;

        /// <summary>2103 MetalRockCoreState - the core; only isDestroyed is live (set once, at depletion).</summary>
        private const uint MetalRockCoreStateComponentId = 2103;

        /// <summary>
        /// 12283 MetalRockCrustState - the crust. shotPoints GROWS one point per shot
        /// (state, replayed to late joiners via SimulatePastShot) and each shot ALSO
        /// carries a transient ShotCrustEvent (the live break VFX, SimulateShot);
        /// exploded is set once, at depletion.
        /// </summary>
        private const uint MetalRockCrustStateComponentId = 12283;

        /// <summary>
        /// 2102 LodgeableState - an atlas shard's lodged/released state. isLodged is
        /// flipped false (+ a Dislodged event) when the host deposit's core is
        /// destroyed, which is the shard's "you can pick me up now" transition.
        /// </summary>
        private const uint LodgeableStateComponentId = 2102;

        /// <summary>
        /// 1210 InteractiveState - the interaction prompt. For an atlas shard the
        /// server flips available false->true on release (the PickUp prompt appears)
        /// and true->false on collection (it is gone).
        /// </summary>
        private const uint InteractiveStateComponentId = 1210;

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

        /// <summary>
        /// Live-session bookkeeping for the operator dashboard: connect/disconnect
        /// tallies, current and peak online, and when each peer connected. Pure
        /// and tested; the main loop feeds it two calls (connect, disconnect) and
        /// <see cref="StatsWriter"/> snapshots it to a file. Wall-clock boot time,
        /// because the snapshot crosses a process boundary to a reader with its
        /// own clock. Internal so the connect/disconnect hooks above can reach it.
        /// </summary>
        internal static readonly ServerStats Stats = new ServerStats(DateTimeOffset.UtcNow);

        /// <summary>
        /// Serialises <see cref="Stats"/> plus each live player's entity id and
        /// ENet health to /tmp/wareborn-stats.json every few seconds, atomically,
        /// so the login server can render the dashboard without any new network
        /// dependency. The builder below is invoked only when a write is actually
        /// due.
        /// </summary>
        private static readonly StatsFileWriter StatsWriter = new StatsFileWriter(BuildStatsSnapshot);

        /// <summary>
        /// Assembles the current snapshot: the accumulated counters, the relay's
        /// live mode/rate, and one row per player IN WORLD (from <see cref="Players"/>,
        /// so it lists spawned entities; a peer still on its loading screen is in
        /// the online COUNT but has no entity to show yet). Each player's wire
        /// health is read from ENet at snapshot time; an unreadable layout becomes
        /// a null health block, never fabricated zeros.
        /// </summary>
        private static StatsSnapshot BuildStatsSnapshot()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            long nowMs = now.ToUnixTimeMilliseconds();

            List<PlayerStat> players = new List<PlayerStat>();
            foreach ((ulong peerId, long entityId) in Players.All())
            {
                DateTimeOffset? connectedAt = Stats.ConnectedAt(peerId);
                long connectedAtMs = connectedAt?.ToUnixTimeMilliseconds() ?? nowMs;

                EnetPeerHealth? health =
                    EnetPeerProbe.TryRead(new IntPtr((long)peerId), out EnetPeerHealth read)
                        ? read
                        : (EnetPeerHealth?)null;

                players.Add(new PlayerStat(entityId, peerId, connectedAtMs, health));
            }

            string build = Environment.GetEnvironmentVariable("WAREBORN_BUILD");
            if (string.IsNullOrWhiteSpace(build))
            {
                build = "unknown";
            }

            return new StatsSnapshot(
                bootTimeUnixMs: Stats.BootTime.ToUnixTimeMilliseconds(),
                generatedAtUnixMs: nowMs,
                uptimeSeconds: (long)Math.Max(0, (now - Stats.BootTime).TotalSeconds),
                relayMode: Relay.ModeDescription,
                relayHz: Relay.Hz,
                build: build,
                totalConnects: Stats.TotalConnects,
                totalDisconnects: Stats.TotalDisconnects,
                currentOnline: Stats.CurrentOnline,
                peakOnline: Stats.PeakOnline,
                players: players);
        }

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

        /// <summary>
        /// The fall floor. Watches every 190602 a player publishes about itself
        /// and teleports anyone who has fallen out from under the world back to
        /// the spawn point. Internal because TransformState_Handler feeds it.
        ///
        /// Declared AFTER <see cref="Teleports"/> deliberately: static field
        /// initialisers run in textual order, so a field constructed above the
        /// service it is handed would be handed null. Same rule as
        /// <see cref="ServerClock"/>, and this is its second customer.
        /// </summary>
        internal static readonly FallRescueService Falls = new FallRescueService(ServerClock, Teleports);

        /// <summary>Decides which ops go to which peers so players can see each other.</summary>
        private static readonly RemotePlayerMirror Mirror = new RemotePlayerMirror(Players);

        /// <summary>
        /// The movement relay, second generation: 190602/1073 are no longer
        /// forwarded raw on arrival but ingested to latest-state (drops, jump
        /// filter, staleness metric) and emitted at a fixed cadence on a
        /// synthetic per-recipient timebase. WAREBORN_RELAY_V2=0 restores the
        /// raw path. Internal because the two movement handlers feed it and
        /// ComponentsSerializer's 1073 seed branch resets its timelines.
        ///
        /// Declared AFTER <see cref="ServerClock"/> and <see cref="Players"/>
        /// for the same textual-order rule as <see cref="Falls"/>.
        /// </summary>
        internal static readonly Networking.RelayEmitter Relay = new Networking.RelayEmitter(ServerClock, Players);

        /// <summary>
        /// STEP 3, THE CARRY GATE. Fires ONE 1130 control point that translates the
        /// spawned hull a few metres, on a human's write to /tmp/wareborn-ship, so
        /// whether a player standing on the deck is carried can be proven before
        /// the ferry is trusted. See Game.ShipMoveService.
        /// </summary>
        internal static readonly Game.ShipMoveService Ships = new Game.ShipMoveService();

        /// <summary>
        /// THE DEPLOYABLE-PLACEMENT MILESTONE. Off unless WAREBORN_PLACEMENT=1. When
        /// armed, a player who selects a crafted shipyard on the hotbar and presses
        /// use (or a write to the debug file) enters the native placement preview and,
        /// on confirm, deploys a shipyard both players see. All of it - the 1019
        /// start, the 1017 confirm handler, the runtime spawn broadcast - lives in
        /// Game.Placement.PlacementService. See Game.Placement and the two handlers
        /// (ItemPlacingState_Handler / InteractAgentState_Handler).
        /// </summary>
        internal static readonly Game.Placement.PlacementService Placement = new Game.Placement.PlacementService();

        /// <summary>
        /// DEBUG UNDOCK. Since flight does not exist yet, a built ship cannot be flown
        /// off its shipyard to free the dock, so a second build cannot be tested. A
        /// human write of a shipyard entity id (or an empty file for "all") to
        /// /tmp/wareborn-undock clears that yard's docked-ship association and pushes a
        /// live 1205 DockedShipId=invalid, re-opening the one-ship-per-yard CRAFT gate.
        /// See Game.Crafting.ShipUndockTrigger.
        /// </summary>
        internal static readonly Game.Crafting.ShipUndockTrigger ShipUndock = new Game.Crafting.ShipUndockTrigger();

        /// <summary>
        /// STEP 4, THE MILESTONE. Off unless WAREBORN_SHIP_FERRY=1. When armed it
        /// flies the hull along a straight path by publishing one 1130 control
        /// point every 0.24 s; the client's SSPDeadReckoningVisualizer -> PathFollower
        /// does the motion, no client patch. Takes ServerClock for the same
        /// textual-order reason as Falls/Relay. See Game.ShipFerryService.
        /// </summary>
        internal static readonly Game.ShipFerryService ShipFerry = new Game.ShipFerryService(ServerClock);

        /// <summary>
        /// Keeps the bolted parts (deck, helm, engine, sail) FOLLOWING the moving
        /// hull. A hull-relative seed is not enough: the parts' follow-visualizer
        /// sleeps after 1 s and only wakes on its own 190602 update, so this
        /// re-publishes each part's transform on a 0.5 s heartbeat (and on every hull
        /// move) to keep it awake. Takes ServerClock for the same textual-order reason
        /// as Falls/Relay/ShipFerry. See Game.ShipPartMotionService.
        /// </summary>
        internal static readonly Game.ShipPartMotionService ShipPartMotion = new Game.ShipPartMotionService(ServerClock);

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
            Multiplayer.WorldEntities.Default(EntityIds, SpawnProofIsland, SpawnTree, SpawnMetal, MetalOnlyProven,
                Environment.GetEnvironmentVariable("WAREBORN_TREE_COUNT"),
                Environment.GetEnvironmentVariable("WAREBORN_ORE_COUNT"),
                SpawnDeck, SpawnExtraShipParts, RecogniseShip,
                SpawnDeposit,
                Environment.GetEnvironmentVariable("WAREBORN_DEPOSIT_COUNT"),
                SpawnDatabank,
                Environment.GetEnvironmentVariable("WAREBORN_DATABANK_COUNT"),
                SpawnAtlasShard);

        /// <summary>
        /// The ledger of every placed resource node and the ONLY place a node's
        /// live harvest state lives (depletion, and the accumulated crust damage a
        /// late joiner is replayed). A node analogue of <see cref="Harvest"/>.
        ///
        /// Internal because ComponentsSerializer's 1099 branch reads a node's metal
        /// type off it, and because a node becomes an entry here the moment it is
        /// given an entity id (see AddWorldEntity). Populated only for entities whose
        /// asset is the nugget; every other id is, correctly, absent.
        /// </summary>
        internal static readonly NodeRegistry Nodes = new NodeRegistry();

        /// <summary>
        /// The depletion POLICY for metal nodes: how many salvage shots empty each
        /// node and what that is worth. The metal analogue of <see cref="Harvest"/>,
        /// and the counterpart to <see cref="Nodes"/> - <see cref="Nodes"/> is the
        /// persistent ledger a late joiner is replayed, this is the live "how do I
        /// mine it" state. They meet in <see cref="OnSalvageShot"/>: a shot that
        /// empties a node here is what marks it destroyed there.
        ///
        /// Internal because the 2106 handler drives it: MultitoolSalvagerState_Handler
        /// distils each inbound ShotEvent to (shooter, node) and calls
        /// <see cref="OnSalvageShot"/>.
        /// </summary>
        internal static readonly MetalHarvest MetalHarvest =
            new MetalHarvest(Multiplayer.MetalNodes.NuggetShotsToDeplete);

        /// <summary>
        /// The ledger of every ATLAS SHARD placed in the world and its acquisition
        /// state (lodged -> released -> collected, plus the pickup reservation). The
        /// atlas analogue of <see cref="Nodes"/>, kept separate because a shard is a
        /// SECOND entity from its host deposit with its own lifecycle: destroying a
        /// deposit's core RELEASES the shard (<see cref="OnDepositShot"/> calls
        /// <see cref="Multiplayer.AtlasShardRegistry.ReleaseByHost"/>), and the shard
        /// is then a free-standing pickup a player collects with a 1211 PickUp
        /// (InteractAgentState_Handler -> <see cref="TryCollectAtlasShard"/>). Internal
        /// because both the serializer (seeds 1305/2102/1210/2103 from it) and those
        /// two glue seams read it. Populated in <see cref="AddWorldEntity"/> the moment
        /// a shard entity has an id. See docs/research/findings-atlas-shards.md.
        /// </summary>
        internal static readonly Multiplayer.AtlasShardRegistry AtlasShards =
            new Multiplayer.AtlasShardRegistry();

        /// <summary>
        /// Which entity ids are ship SURFACES, and which ship each belongs to. The
        /// server fills this from its own spawn decisions (a hull registers itself
        /// as its own surface in <see cref="AddWorldEntity"/>), so aboard-detection
        /// never has to trust or read the client's 8066.
        ///
        /// Internal because the aboard glue in the 1073 handler consumes it via
        /// <see cref="Aboard"/>, and AddWorldEntity writes it.
        /// </summary>
        internal static readonly ShipMembership ShipMembership = new ShipMembership();

        /// <summary>
        /// Who is aboard which ship, fed from the 1073 stream. THE piece the flight
        /// publisher, the abandonment timer and the eventual pilot grant consume:
        /// "is anyone on ship X" / "which ship is this player on". A player on a deck
        /// is not parented - they attach via 1073 relativeTo - so this reads that,
        /// accumulates the deltas, and emits board/leave edges. See AboardTracker.
        ///
        /// Internal because ClientAuthoritativePlayerState_Handler feeds it every
        /// 1073 and ForgetPeer clears a departed peer from it.
        /// </summary>
        internal static readonly AboardTracker Aboard = new AboardTracker(ShipMembership);

        /// <summary>
        /// Whether the carry echo is armed. ON by default; set
        /// <c>WAREBORN_CARRY_ECHO=0</c> to switch it off if it misbehaves.
        ///
        /// The echo sends a player its OWN 1073 <c>relativeTo</c> back on a
        /// board/leave edge, which is the one thing that arms the client-side ship
        /// carry (<c>ClientAuthoritativePlayerMovement</c> only sets its PathFollower
        /// from a RECEIVED relativeTo, and this custom server otherwise never echoes
        /// a worker its own authoritative update). See <see cref="CarryEcho"/>.
        /// </summary>
        internal static readonly bool CarryEchoEnabled =
            Environment.GetEnvironmentVariable("WAREBORN_CARRY_ECHO") != "0";

        /// <summary>
        /// Per-peer carry-echo edge detector: decides when to echo a player's own
        /// 1073 relativeTo back so the ship carry arms, and dedupes so a stationary
        /// player is not echoed every frame (which would fight its own prediction).
        ///
        /// Internal because ClientAuthoritativePlayerState_Handler drives it every
        /// 1073 and ForgetPeer clears a departed peer from it.
        /// </summary>
        internal static readonly CarryEchoTracker CarryEcho = new CarryEchoTracker();

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
        /// Whether to spawn the choppable tree (see Multiplayer.WorldEntities.HavenTree).
        /// ON unless WAREBORN_SPAWN_TREE=0.
        ///
        /// Opt-OUT rather than opt-in, unlike the proof island, because the tree is
        /// the feature rather than a diagnostic - but with a kill switch, because
        /// no game was launched for this change either. Leaving it on is safe even
        /// if the tree misbehaves: it is AfterPlayer, so nothing about it can delay
        /// or break a player's own spawn.
        /// </summary>
        private static bool SpawnTree =>
            Environment.GetEnvironmentVariable("WAREBORN_SPAWN_TREE") != "0";

        /// <summary>
        /// Whether to place the MetalNugget resource nodes (see
        /// Multiplayer.MetalNodes). ON unless WAREBORN_SPAWN_METAL=0, same footing
        /// as the tree: the nodes are AfterPlayer, so a misbehaving node cannot
        /// delay or break a player's own spawn, and no game was launched for this.
        /// </summary>
        private static bool SpawnMetal =>
            Environment.GetEnvironmentVariable("WAREBORN_SPAWN_METAL") != "0";

        /// <summary>
        /// Whether to place ONLY the single proven node - the cautious first-live
        /// mode the standing caveat calls for. WAREBORN_SPAWN_METAL=proven: the
        /// extracted coordinate chain has never been validated against a running
        /// client, so one measured node can be spawned before the whole table is
        /// trusted.
        /// </summary>
        private static bool MetalOnlyProven =>
            Environment.GetEnvironmentVariable("WAREBORN_SPAWN_METAL") == "proven";

        /// <summary>
        /// Whether to place the anchored metal DEPOSIT(s) - the real ore mining loop
        /// (see Multiplayer.MetalDeposits). OFF by default, opt-in with
        /// WAREBORN_SPAWN_DEPOSIT=1, because the deposit is new: its measured coordinate
        /// AND its runtime-imported variant (1255) have never been in front of a running
        /// client, and unlike the nugget its geometry is imported rather than baked, so
        /// an invalid variantId is an invisible entity. It is AfterPlayer, so leaving it
        /// off or on cannot delay or break a player's own spawn either way.
        /// </summary>
        private static bool SpawnDeposit =>
            Environment.GetEnvironmentVariable("WAREBORN_SPAWN_DEPOSIT") == "1";

        /// <summary>
        /// Whether to lodge an ATLAS SHARD in the proven deposit - the real retail
        /// acquisition object. ON unless WAREBORN_SPAWN_ATLAS=0, and only takes effect
        /// when <see cref="SpawnDeposit"/> is on (a shard needs a live host core to
        /// render and be mined loose). AfterPlayer and inert until its core is
        /// destroyed, so it cannot delay or break a spawn; and its grant is a no-op
        /// until the pending retail itemTypeId is recovered (AtlasShardCatalogue.
        /// ItemTypeId), so it can never mis-grant. See findings-atlas-shards.md.
        /// </summary>
        private static bool SpawnAtlasShard =>
            Environment.GetEnvironmentVariable("WAREBORN_SPAWN_ATLAS") != "0";

        /// <summary>
        /// Whether to place the scannable DATABANK that feeds the KNOWLEDGE loop.
        /// Opt-in via WAREBORN_SPAWN_DATABANK=1, matching the deposit: AfterPlayer, so
        /// leaving it off or on cannot delay or break a player's own spawn, and its
        /// geometry is imported from the DataBank_001 prefab (whether it draws when
        /// spawned as its own entity rather than by the island spawner is the one
        /// thing only a live client can confirm - see Multiplayer.Databanks).
        /// </summary>
        private static bool SpawnDatabank =>
            Environment.GetEnvironmentVariable("WAREBORN_SPAWN_DATABANK") == "1";

        /// <summary>
        /// Whether to bolt the walkable Deck01 onto the hull (see
        /// Multiplayer.WorldEntities.Deck01). ON unless WAREBORN_SHIP_DECK=0. It is
        /// the whole point of the full-ship work, so opt-OUT like the tree, but with
        /// a kill switch because its solid-floor path has never been in front of a
        /// running client. Safe to leave on: AfterPlayer, so it cannot delay or break
        /// a player's own spawn.
        /// </summary>
        private static bool SpawnDeck =>
            Environment.GetEnvironmentVariable("WAREBORN_SHIP_DECK") != "0";

        /// <summary>
        /// Whether to add the cosmetic ModularEngine + Sail01 (see
        /// Multiplayer.WorldEntities.ModularEngine/Sail01). OFF unless
        /// WAREBORN_SHIP_PARTS=1: they rest on an unverified assumption that they
        /// render from baked geometry without their special visualizer, so they are
        /// opt-IN until a live client confirms it - the cautious default the standing
        /// caveat calls for. Safe to enable: best-effort interest leaves an
        /// unrenderable part inert, never the deck or the ship.
        /// </summary>
        private static bool SpawnExtraShipParts =>
            Environment.GetEnvironmentVariable("WAREBORN_SHIP_PARTS") == "1";

        /// <summary>
        /// Whether to append the ship-recognition components (8062/8071/4349) to the
        /// hull's proactive seed so the client's own ShipVisualizer ENABLES and the
        /// hull is recognised as a ship (see Multiplayer.ShipRecognition and
        /// Multiplayer.WorldEntities.ShipRecognitionSeedComponents). ON unless
        /// WAREBORN_SHIP_RECOGNISE=0. Opt-OUT with a kill switch, on the same footing
        /// as the deck: it has never been in front of a running client, and it widens
        /// the hull's all-or-nothing seed batch from four ids to seven, so a switch to
        /// fall back to the proven four without a rebuild is worth its one line. Safe
        /// to leave on: even off, the client still receives all three over interest,
        /// so recognition degrades to best-effort rather than disappearing.
        /// </summary>
        private static bool RecogniseShip =>
            Environment.GetEnvironmentVariable("WAREBORN_SHIP_RECOGNISE") != "0";

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
        /// The AfterPlayer pacing metronome for a peer, created on first use. One
        /// per peer (see <see cref="SpawnPacers"/>); dropped in ForgetPeer when the
        /// peer leaves. Only called when <see cref="SpawnPaceInterval"/> is
        /// positive, so the CadenceTimer's "interval &gt; 0" contract always holds.
        /// </summary>
        private static CadenceTimer SpawnPacerFor(ENetPeerHandle peer)
        {
            if (!SpawnPacers.TryGetValue(peer, out CadenceTimer? pacer))
            {
                pacer = new CadenceTimer(SpawnPaceInterval);
                SpawnPacers[peer] = pacer;
            }
            return pacer;
        }

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

                // A tree becomes harvestable the moment it has an entity id, which
                // is here and only here. Keyed on the ASSET so a second registration
                // of the same prefab is planted too, and idempotent so the second
                // player walking this same step does not stand the tree back up:
                // every client walks the identical plan, but there is one tree.
                if (entity.AssetName == Multiplayer.Trees.AssetName
                    && Harvest.Plant(entityId, Multiplayer.Trees.Topology(), Multiplayer.Trees.WoodType))
                {
                    Console.WriteLine("[info] planted '" + entity.Key + "' as entity " + entityId
                        + ": " + Multiplayer.Trees.SectionCount + " sections, mask "
                        + Convert.ToString(Multiplayer.Trees.FullSectionMask, 2)
                        + ", " + Multiplayer.Trees.WoodType + ".");
                }

                // A metal node becomes an entry in the harvest ledger the moment it
                // has an entity id - the same seam as the tree above. Keyed on the
                // registration key so its metal type and future depletion state ride
                // with it, and idempotent (Register returns false on re-registration)
                // so the second joiner walking this identical step does not stand a
                // depleted node back up: every client walks the same plan, but there
                // is one node.
                if (entity.AssetName == Multiplayer.MetalNodes.AssetName)
                {
                    Multiplayer.MetalNode? metalNode = Multiplayer.MetalNodes.ByKey(entity.Key);
                    if (metalNode != null && Nodes.Register(entityId, metalNode))
                    {
                        // Teach the yield table what this metal grants, exactly as
                        // wood is pre-registered from Trees.WoodType. The metal type
                        // string is the source key AND the itemTypeId (a real row in
                        // itemData.json - iron, aluminium, copper... - so the client
                        // can look it up); one item per unit, and MetalHarvest below
                        // decides the unit count. Register is idempotent, so the many
                        // "iron" nodes all resolving to the same rule is harmless.
                        Game.Gathering.HarvestReward.Register(
                            metalNode.MetalType,
                            new Multiplayer.Gathering.YieldRule(metalNode.MetalType, amountPerUnit: 1));

                        // And make it shootable: the same spawn seam as the ledger
                        // above, idempotent for the same reason (one node, every
                        // joiner walks this step).
                        MetalHarvest.Place(entityId, Multiplayer.MetalNodes.NuggetYieldUnits);

                        Console.WriteLine("[info] placed metal node '" + entity.Key + "' as entity "
                            + entityId + ": " + metalNode.MetalType + " q" + metalNode.Quality
                            + " at " + metalNode.Position + " (" + Multiplayer.MetalNodes.NuggetShotsToDeplete
                            + " shots -> " + Multiplayer.MetalNodes.NuggetYieldUnits + " units).");
                    }
                }

                // An anchored metal DEPOSIT becomes an entry in the SAME ledgers the
                // moment it has an entity id - the identical seam as the nugget above,
                // and it shares the NodeRegistry (which carries the crust shotPoints)
                // and the MetalHarvest shot counter. What differs is the DEPLETION
                // SIZING (ten shots, richer yield) and the fact that it is a DEPOSIT
                // (MetalNode.IsDeposit true), which OnSalvageShot and ComponentsSerializer
                // branch on to run the crust/core loop instead of the nugget sink.
                // Idempotent (Register/Place return false on re-registration) so the
                // second joiner walking this same step cannot refill a mined-out core.
                if (entity.AssetName == Multiplayer.MetalDeposits.AssetName)
                {
                    Multiplayer.MetalNode? deposit = Multiplayer.MetalDeposits.ByKey(entity.Key);
                    if (deposit != null && Nodes.Register(entityId, deposit))
                    {
                        Game.Gathering.HarvestReward.Register(
                            deposit.MetalType,
                            new Multiplayer.Gathering.YieldRule(deposit.MetalType, amountPerUnit: 1));

                        MetalHarvest.Place(entityId,
                            Multiplayer.MetalDeposits.YieldUnits,
                            shotsToDeplete: Multiplayer.MetalDeposits.ShotsToDeplete);

                        Console.WriteLine("[info] placed metal DEPOSIT '" + entity.Key + "' as entity "
                            + entityId + ": " + deposit.MetalType + " q" + deposit.Quality
                            + " variant '" + deposit.VariantId + "' at " + deposit.Position
                            + " (" + Multiplayer.MetalDeposits.ShotsToDeplete + " shots -> "
                            + Multiplayer.MetalDeposits.YieldUnits + " units).");
                    }
                }

                // An ATLAS SHARD becomes an entry in the AtlasShards ledger the moment
                // it has an entity id - the same spawn seam as the deposit above. It is
                // registered AFTER its host deposit (WorldEntities.Default registers the
                // deposit first), so the deposit's shared entity id is already allocated
                // and BoundEntityIdFor resolves it without allocating. The shard's 1305
                // rockCoreId and the deposit's 2103 attachedEntities are both wired from
                // this stored host id at serialize time. Idempotent (Register returns
                // false on re-registration) so a second joiner walking this identical
                // step cannot re-lodge a shard someone already mined loose or collected.
                if (entity.AssetName == Multiplayer.AtlasShardCatalogue.AssetName)
                {
                    int? shardIndex = Multiplayer.AtlasShardCatalogue.IndexOf(entity.Key);
                    long? hostDepositId = shardIndex == null
                        ? (long?)null
                        : WorldEntities.BoundEntityIdFor(
                            Multiplayer.AtlasShardCatalogue.HostDepositKeyFor(shardIndex.Value));

                    if (hostDepositId == null)
                    {
                        Console.WriteLine("[warning] atlas shard '" + entity.Key + "' (entity " + entityId
                            + ") has no bound host deposit; its 1305 rockCoreId would be invalid, so it is "
                            + "not registered. Register its deposit-" + shardIndex + " first.");
                    }
                    else if (AtlasShards.Register(entityId, hostDepositId.Value,
                                 Multiplayer.AtlasShardCatalogue.DefaultSlotId))
                    {
                        Console.WriteLine("[info] placed ATLAS SHARD '" + entity.Key + "' as entity "
                            + entityId + " lodged in deposit entity " + hostDepositId.Value
                            + " (slot " + Multiplayer.AtlasShardCatalogue.DefaultSlotId + ") at "
                            + entity.Position + "; grants '" + Multiplayer.AtlasShardCatalogue.ItemTypeId
                            + "'" + (Multiplayer.AtlasShardCatalogue.IsItemIdPending
                                ? " (PENDING refdata - see findings-atlas-shards.md §5)" : "") + ".");
                    }
                }

                // A scannable DATABANK becomes an entry in the DatabankLedger the moment
                // it has an entity id - the same spawn seam as the deposit above. The
                // 2107 scan handler consults the ledger to decide a ScanEntityEvent
                // target is worth knowledge and how much. Idempotent, so a second joiner
                // walking this same step cannot double-register it.
                if (entity.AssetName == Multiplayer.Databanks.AssetName)
                {
                    if (Multiplayer.DatabankLedger.Register(entityId, Multiplayer.Databanks.GrantAmount,
                            Multiplayer.Databanks.NoteTitle, Multiplayer.Databanks.NoteDescription))
                    {
                        Console.WriteLine("[info] placed scannable DATABANK '" + entity.Key + "' as entity "
                            + entityId + " at " + entity.Position + " (scan grants "
                            + Multiplayer.Databanks.GrantAmount + " knowledge).");
                    }
                }

                // The hull becomes a ship SURFACE the moment it has an entity id -
                // the same spawn seam as the tree and node ledgers above. A player
                // standing on this bare hull publishes 1073 relativeTo == this id
                // (the ground object IS the hull, since no Deck01 parts are bolted
                // on), so the hull registers itself as its own ship root. Idempotent
                // (Register returns false on the second joiner walking this identical
                // step): every client walks the same plan, but there is one ship.
                if (entity.Key == Multiplayer.WorldEntities.ShipFrameKey
                    && ShipMembership.Register(entityId, entityId))
                {
                    Console.WriteLine("[info] registered ship surface: hull entity " + entityId
                        + " is its own ship root (aboard-detection).");
                }

                // The DECK is a SECOND ship surface of the SAME ship: once it is
                // bolted on, a player stands on the deck's solid collider, not the
                // beams, so their 1073 relativeTo is the DECK's entity id. Map that id
                // to the hull's ship so aboard-detection still fires. Same spawn seam,
                // idempotent for the same reason; the hull is registered before the
                // deck, so its id is known here.
                if (entity.Key == Multiplayer.WorldEntities.DeckKey)
                {
                    long? hullId = WorldEntities.BoundEntityIdFor(Multiplayer.WorldEntities.ShipFrameKey);
                    if (hullId.HasValue && ShipMembership.Register(entityId, hullId.Value))
                    {
                        Console.WriteLine("[info] registered ship surface: deck entity " + entityId
                            + " belongs to hull " + hullId.Value + " (aboard-detection).");
                    }
                }

                if (entity.SeedComponents.Count == 0)
                {
                    return;
                }

                List<Structs.Structs.InterestOverride> seeds = entity.SeedComponents
                    .Select(id => new Structs.Structs.InterestOverride(id, 1))
                    .ToList();

                // Record what the seed push actually delivered, so this peer's later
                // interest re-checkout (the else branch above) does NOT re-ADD these
                // ids. Without this the ledger was empty for the entity and the
                // re-checkout re-seeded the whole set - "Component X added to entity N,
                // but it already exists" on the client, which is merely wasteful for
                // most entities but a CRASH for a placed shipyard (a second seed of
                // 1205/1210/1004/1005/1206 on the same entity while its visualizers are
                // already registered) and DESTRUCTIVE for the deck (cycles
                // ShipDeckVisualizer, Clear()ing its solid collider). The joining
                // client's spawn plan serves the restored shipyard HERE, so this is the
                // exact path that crashed the second player.
                List<uint> seedServed = new List<uint>();
                if (!SendOPHelper.SendAddComponentOp(peer, entityId, seeds, true, seedServed))
                {
                    Console.WriteLine("[error] '" + entity.Key + "' (" + entityId
                        + ") was created but its seeded components were dropped. It will render and do nothing.");
                }
                ServedComponents.MarkServed(peer, entityId, seedServed);
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

        /// <summary>
        /// Emits each connected peer's [rates] line when its 5 s window is due:
        /// receive and send counts (total plus top-5 per component id) from
        /// <see cref="Rates"/>, and - when the native struct passes its layout
        /// sanity check - ENet's own health counters for the peer: RTT, packets
        /// lost, and reliable bytes in flight. Those three are the stages of the
        /// suspected death chain (relay outruns ACKs -> window fills -> RTT blows
        /// out -> ENet times the peer out); this line is the visibility into it
        /// that the 73-second silent drop did not have.
        /// </summary>
        private static void ReportPeerRates()
        {
            foreach (PeerRateReport report in Rates.DueReports())
            {
                string health = EnetPeerProbe.TryRead(new IntPtr((long)report.PeerId), out EnetPeerHealth peerHealth)
                    ? "; " + EnetPeerHealthPolicy.Describe(peerHealth)
                    : "; enet health unreadable";

                Console.WriteLine("[rates] " + report.Describe() + health);
            }
        }

        /// <summary>
        /// Handles ONE received packet exactly as the old inline poll body did -
        /// this method is that body, moved verbatim so the drain loop around it
        /// stays readable. Takes ownership of the packet: every path through here
        /// ends in ENet_Destroy_Packet.
        /// </summary>
        private static unsafe void ProcessPacket(EnetLayer.ENetPacket_Wrapper* packet)
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
                return;
            }

            // Wire metrics, receive side. Component updates are counted per
            // component id inside their branch below; everything else is one op
            // packet, counted by channel.
            ulong senderId = PeerIdentity.IdOf(sender);
            if (packet->Channel != (int)EnetLayer.ENetChannel.COMPONENT_UPDATE_OP)
            {
                Rates.RecordReceive(senderId, PeerRates.ChannelKey(packet->Channel));
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
            // "stop processing this packet": they land on ENet_Destroy_Packet
            // below rather than skipping it and leaking the packet.
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
                            List<uint> setupServed = new List<uint>();
                            if (!SendOPHelper.SendAddComponentOp(keyValuePair.Key, entityId, interests, interestCount, true, setupServed))
                            {
                                continue;
                            }
                            // Remember the player's own first-stage components so the
                            // best-effort branch below does not re-ADD them when the
                            // client later re-declares its interest for its own entity
                            // (after setup, that path falls through to the else branch
                            // too). Same reset hazard as the deck, one entity up.
                            ServedComponents.MarkServed(keyValuePair.Key, entityId, setupServed);

                            // What the client did not ask for but needs, IN ORDER.
                            //
                            // 1080 SchematicsLearnerGSimState because the game does not
                            // reliably request it and InventoryVisualiser needs its reader;
                            // 1086 PlayerName because LocalPlayerInit [Require]s it and
                            // nothing multitool-shaped works before LocalPlayer exists;
                            // then everything the client is granted authority over.
                            //
                            // The order is load-bearing and lives in MirrorSendPolicy so a
                            // test can hold it, rather than in the shape of this expression.
                            // Placement's 1017 (writer, granted) + 1019 (reader,
                            // server-owned) ride in here ONLY when the feature is armed,
                            // so ItemPlacingBehaviour's [Require]d 1017 writer and 1019
                            // reader can bind. Appended, not baked into MirrorSendPolicy's
                            // always-on set, so an un-flagged server injects exactly what
                            // it always did.
                            List<uint> injectedIds = MirrorSendPolicy.InjectedComponents.ToList();
                            if (PlacementEnabled)
                            {
                                injectedIds.AddRange(MirrorSendPolicy.PlacementInjectedComponents);
                                // Ship-build UI: 1207+1208 (FRAME DESIGNS visualizer) and
                                // 1270+1274 (SHIP BLUEPRINTS behaviour) so every [Require]
                                // reader/writer checks out. 1208+1270 are also granted above.
                                injectedIds.AddRange(MirrorSendPolicy.ShipBuildUiInjectedComponents);
                                // Part-mount toolchain: 1070 (BuilderObserver writer) + 1071
                                // (BuilderVisualizer reader) + 1239 (PlayerPlacementToolBehaviour
                                // writer) so every [Require] resolves and 1070/1239 are in the
                                // component map for their handlers. 1070+1239 are also granted.
                                injectedIds.AddRange(MirrorSendPolicy.PartMountInjectedComponents);
                            }

                            List<Structs.Structs.InterestOverride> injected = injectedIds
                                .Select(p => new Structs.Structs.InterestOverride(p, 1))
                                .ToList();

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
                            // BEST EFFORT, DELIBERATELY - this is the only interest
                            // send that is not first-time setup, and it is the one a
                            // client makes when it asks about ANOTHER entity.
                            //
                            // With failOnComponentInitError:true one unrecognised id
                            // threw away the WHOLE batch. Measured live: a client asks
                            // about the other player's avatar with 21 ids, we have a
                            // branch for sixteen, and 2108 ScannerToolState kills all
                            // twenty-one - so the observer gets no name, no appearance,
                            // no gear and no tool visuals for the other player. The id
                            // that breaks it is chosen by the client's own prefab, which
                            // we neither control nor can enumerate ahead of time.
                            //
                            // Best effort is the SDK's own semantics, not a degradation:
                            // a visualizer activates only once ALL of its required
                            // readers are injected (EntityVisualizers.UpdateActivation),
                            // so delivering sixteen of twenty-one disables exactly the
                            // visualizers whose data is missing and leaves every other
                            // one working. Delivering zero disables all of them.
                            // All-or-nothing is strictly worse at every count but 21.
                            //
                            // Diagnosability is untouched, which was the reason the flag
                            // was set: the [interest] line prints the whole requested
                            // list before anything is attempted, and every miss still
                            // prints [error] failed to initialize component NNNN. We
                            // just stop throwing away the sixteen that worked.
                            //
                            // The first-time-setup sends above KEEP the flag: a player
                            // whose own batch is incomplete has no authority grants and
                            // no loading screen, which is worth failing loudly for.
                            //
                            // DEDUPE - and it is why the spawned deck stops being a
                            // trap. The client re-declares its whole interest set for
                            // an entity from time to time (its SpatialCommunicator
                            // clears the dict and resends) WITHOUT dropping what it
                            // already holds. Re-ADDing a component the client still has
                            // is at best the "already exists" error the client log
                            // shows and at worst DESTRUCTIVE: re-delivering a
                            // ShipDeckVisualizer [Require] (1518 / 1099) cycles the
                            // reader, whose OnDisable Clear()s away the SOLID deck
                            // collider, and the async rebuild can be dropped - the deck
                            // is solid on first render, then the player falls through it
                            // ever after. So serve only the ids this peer has not been
                            // given for this entity; component VALUE updates are
                            // unaffected because they travel on COMPONENT_UPDATE_OP, a
                            // different channel, not this AddComponent path.
                            List<uint> requestedIds = new List<uint>((int)interestCount);
                            for (int ii = 0; ii < interestCount; ii++)
                            {
                                requestedIds.Add(interests[ii].ComponentId);
                            }

                            IReadOnlyList<uint> toServe =
                                ServedComponents.UnservedOf(keyValuePair.Key, entityId, requestedIds);

                            int skipped = requestedIds.Count - toServe.Count;
                            if (skipped > 0)
                            {
                                Console.WriteLine("[interest] entity " + entityId + ": " + skipped + " of "
                                    + requestedIds.Count + " requested component(s) already delivered to this peer; "
                                    + "skipping the re-add that would reset live visualizers (e.g. the deck's solid collider)"
                                    + (toServe.Count == 0 ? " - nothing new to send." : "."));
                            }

                            if (toServe.Count > 0)
                            {
                                List<Structs.Structs.InterestOverride> overrides =
                                    new List<Structs.Structs.InterestOverride>(toServe.Count);
                                foreach (uint id in toServe)
                                {
                                    overrides.Add(new Structs.Structs.InterestOverride(id, 1));
                                }

                                List<uint> served = new List<uint>();
                                if (SendOPHelper.SendAddComponentOp(keyValuePair.Key, entityId, overrides, false, served))
                                {
                                    ServedComponents.MarkServed(keyValuePair.Key, entityId, served);
                                }
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
                            // Wire metrics: one count per component update, keyed
                            // by the id the client actually sent.
                            Rates.RecordReceive(senderId, update[i].ComponentId);

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
            Game.Knowledge.ProgressionService.ReportPersistenceState();

            // Said once so the operator knows where the dashboard's live data
            // comes from and can point the login server at the same file if the
            // default path is overridden.
            Console.WriteLine("[info] stats: writing a live snapshot to " + StatsWriter.Path
                + " every few seconds for the admin dashboard (WAREBORN_STATS_FILE overrides the path).");

            // Say it once, at startup, because a feature nobody can find is not a
            // feature. Destinations come from TeleportPolicy so this list cannot
            // go stale.
            Console.WriteLine("[info] teleport: write a destination to " + Teleports.TriggerFile
                + " to move players. Destinations: " + string.Join(", ", TeleportPolicy.Names)
                + ", or 'coord X Y Z' for an arbitrary world coordinate (metres) - e.g. a ferry's "
                + "arrival on another island. Add an entity id to move just one, e.g. `echo '"
                + TeleportPolicy.SafeDestination.Name + " 3' > " + Teleports.TriggerFile + "`.");

            // The ship carry gate (step 3), said once so a human can find it: write
            // to the ship file to translate the spawned hull one control point and
            // watch whether a player standing on it is carried along.
            Console.WriteLine("[info] ship: CARRY TEST - write to " + Ships.TriggerFile
                + " to move the spawned hull one 1130 control point (default 5 m north)."
                + " e.g. `echo 'nudge 8' > " + Ships.TriggerFile + "`. Stand on the beams first."
                + " The ferry (continuous flight) is "
                + (ShipFerryService.Enabled ? "ARMED (WAREBORN_SHIP_FERRY=1)." : "OFF (set WAREBORN_SHIP_FERRY=1)."));

            // Deployable placement (the milestone), said once so a human can find it.
            if (Placement.Enabled)
            {
                Console.WriteLine("[info] placement: ARMED (WAREBORN_PLACEMENT=1). Craft a shipyard, put it on the"
                    + " hotbar, SELECT it and press use (left hand) to start placing; position the ghost and hold use"
                    + " to deploy. If the native trigger does not fire, `echo > " + Placement.TriggerFile
                    + "` (optionally with a player entity id) to start placement for that player's shipyard.");
            }
            else
            {
                Console.WriteLine("[info] placement: OFF (set WAREBORN_PLACEMENT=1 to enable shipyard deployment).");
            }

            // Also said once, so that "why did I suddenly reappear at spawn?" (or
            // "why did I NOT?") has an answer in the same log as the event. The
            // mode line comes from AutoFallRescuePolicy so it cannot drift from the
            // floor the watch is actually using. Manual recovery is F10, client
            // side; grep 'fall-rescue' to see any automatic catch happen.
            Console.WriteLine("[info] fall floor: " + Falls.ModeDescription);


            // RESTORE PERSISTED WORLD STATE before the spawn plan is computed. Placed
            // deployables (shipyards) and built ships a player made in an earlier
            // session are re-registered as world entities HERE, so the plan below picks
            // them up and every joining client is served them exactly like the static
            // world entities - the whole point of persistence being that the shipyard is
            // still standing after a restart without anyone re-placing it. Must run
            // before SpawnPlan.For, and does: this is the last thing before it.
            Game.Persistence.WorldStatePersistence.RestoreOnBoot(Placement);

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

            // Which plan steps are PACED: the RequestAsset that BEGINS each
            // AfterPlayer world entity. Its AddEntity is not paced - it follows on
            // the client's ack, unchanged. The player's own avatar (Entity == null)
            // and every BeforePlayer entity (the ground) are never paced: they gate
            // the loading screen and must go out immediately. Parallel to the
            // WorldState list so the perform loop can index it by SyncStepPointer.
            bool[] pacedStep = plan
                .Select(s => s.Op == SpawnOp.RequestAsset
                             && s.Entity != null
                             && s.Entity.Order == SpawnOrder.AfterPlayer)
                .ToArray();

            int pacedCount = pacedStep.Count(p => p);
            if (SpawnPacePolicy.IsEnabled(SpawnPaceInterval))
            {
                Console.WriteLine("[info] spawn pacing: " + pacedCount
                    + " AfterPlayer entities released " + SpawnPaceInterval.TotalMilliseconds.ToString("0")
                    + " ms apart (~" + SpawnPacePolicy.StreamDurationFor(pacedCount, SpawnPaceInterval).TotalSeconds.ToString("0.0")
                    + " s to stream in); player + ground spawn immediately. WAREBORN_SPAWN_PACE_MS=0 disables.");
            }
            else
            {
                Console.WriteLine("[info] spawn pacing: OFF (WAREBORN_SPAWN_PACE_MS=0); "
                    + pacedCount + " AfterPlayer entities drain as fast as the client acks.");
            }

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
                // STEP 3 carry gate: same file-poll shape as teleport, self-
                // throttled to twice a second. A write to /tmp/wareborn-ship
                // translates the spawned hull one 1130 control point.
                Ships.PollTrigger();
                // DEPLOYABLE PLACEMENT debug trigger: same file-poll shape, self-
                // throttled. A write to /tmp/wareborn-place starts placement for a
                // player's hotbar shipyard. A no-op unless WAREBORN_PLACEMENT=1.
                Placement.PollTrigger();
                // DEBUG UNDOCK: same file-poll shape, self-throttled. A write of a
                // shipyard entity id (or an empty file for "all") to /tmp/wareborn-undock
                // frees that yard's dock so a second ship can be built and tested.
                ShipUndock.PollTrigger();
                // STEP 4 ferry: fixed-cadence 1130 control-point stream that flies
                // the hull. Off unless WAREBORN_SHIP_FERRY=1; cheap when off (an env
                // check) or idle (one Stopwatch compare). See Game.ShipFerryService.
                ShipFerry.Tick();
                // Keep the bolted parts awake and following the hull: a 0.5 s heartbeat
                // that re-publishes each part's 190602 (below the client's 1 s
                // follow-visualizer sleep). Cheap when idle (one Stopwatch compare) and
                // a no-op until a ship and a loaded client both exist. See
                // Game.ShipPartMotionService.
                ShipPartMotion.Tick();
                // The cadence chopping does not get from the wire. The 1037 cut
                // signal is a LATCH - one packet when the beam arrives on a
                // section, one when it leaves - so "hold the beam and the tree
                // comes apart" is this timer or it does not happen. Cheap when
                // nobody is chopping: an empty dictionary.
                TickTreeHarvest();
                // Fire any due one-shot "seed in-progress then flip" completions on the main
                // loop: the shipyard fold-out flip (1205 deployed=true), the crafted-part
                // materialize flip (1013 spawning=false), and timed station-craft completions.
                // Cheap when idle (one UtcNow compare over an empty list). See Game.DeferredActions.
                Game.DeferredActions.Tick();
                Relay.Tick(); // fixed-cadence movement emit + 5 s relay stats; cheap when idle (two Stopwatch compares). See Networking.RelayEmitter.

                // Report each connected peer's 5 s wire-rate line (with ENet peer
                // health appended when readable). Runs with the other timers so a
                // peer that has gone silent still gets its line.
                ReportPeerRates();

                // Snapshot the live session to /tmp/wareborn-stats.json for the
                // operator dashboard. Self-throttled to a few seconds and atomic;
                // never throws into this loop. See StatsFileWriter.
                StatsWriter.MaybeWrite();

                // BOUNDED DRAIN. The old loop polled exactly ONCE per iteration, and
                // the shim returns at most one event per call - so the server's
                // maximum intake was one packet per loop turn. The moment per-packet
                // cost exceeded packet inter-arrival time the inbound queue grew
                // without bound (observed live: rate pinned flat at the arrival rate
                // while latency climbed, ending in a 73 s ENet timeout). Draining up
                // to DrainBudget events per turn lets a backlog CLEAR; the budget
                // keeps the timers above from starving under flood. Only the first
                // poll may block (50 ms as before); the rest are zero-wait catch-up.
                // See PollDrainPolicy.
                for (int drained = 0; drained < DrainBudget; drained++)
                {
                    EnetLayer.ENetPacket_Wrapper* packet = EnetLayer.ENet_Poll(server, PollDrainPolicy.WaitMsFor(drained), Marshal.GetFunctionPointerForDelegate(callbackC), Marshal.GetFunctionPointerForDelegate(callbackD));
                    if (packet == null)
                    {
                        // Nothing queued - or a connect/disconnect event, which the
                        // shim also reports as NULL after invoking its callback.
                        // Either way stop draining; if events remain, the next
                        // iteration's first poll returns them without blocking.
                        break;
                    }

                    // CRASH ISOLATION. A bad PACKET must cost that packet, never the
                    // process. One unhandled exception in any component handler - e.g.
                    // a malformed inventory delta from a modified client - would
                    // otherwise unwind out of Main and drop EVERY player. We catch it
                    // here, per packet, and keep draining.
                    //
                    // Scope is deliberate: this wraps ONE ProcessPacket call, not the
                    // while-loop and not the ENet_Poll above it. A genuinely fatal
                    // condition - a dead ENet host - surfaces through ENet_Poll, not
                    // through here, so it is NOT swallowed and can still stop the loop.
                    //
                    // ProcessPacket owns the packet and frees it (ENet_Destroy_Packet)
                    // on every NORMAL path. A throw escapes BEFORE that free - it is
                    // the last statement in the method and its early-return branch
                    // frees-then-returns with nothing between - so on catch the packet
                    // is still alive and unfreed. We free it here: not doing so would
                    // make a throwing packet ALSO leak, on top of the fault. There is
                    // no double-free because the two paths are mutually exclusive.
                    try
                    {
                        ProcessPacket(packet);
                    }
                    catch (Exception ex)
                    {
                        if (PacketFaults.ShouldLog(out long total))
                        {
                            Console.WriteLine("[error] packet processing threw (fault #" + total
                                + ") on channel " + packet->Channel + " from " + Describe(packet->Peer)
                                + ", packet dropped: " + ex);
                        }

                        EnetLayer.ENet_Destroy_Packet(new IntPtr(packet));
                    }
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
                        // Pace the step that BEGINS each AfterPlayer entity: if this
                        // peer released one too recently, hold it for a later tick
                        // (leave Performed false so it retries) instead of adding to
                        // the first-load burst. The step is still ack-gated as
                        // before - this only spaces the ready ones out in time.
                        // Player + ground steps are never paced (pacedStep is false
                        // for them) so they are never held back.
                        if (SpawnPacePolicy.IsEnabled(SpawnPaceInterval)
                            && pStatus.SyncStepPointer < pacedStep.Length
                            && pacedStep[pStatus.SyncStepPointer]
                            && !SpawnPacerFor(keyValuePair.Key).Due(ServerClock.Elapsed))
                        {
                            continue;
                        }

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

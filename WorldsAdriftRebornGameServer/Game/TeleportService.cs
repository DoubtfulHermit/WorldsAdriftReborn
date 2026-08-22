using System.Diagnostics;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Components;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// Fires teleports and watches for them landing. All the ENet-shaped work;
    /// every decision it makes lives in <see cref="TeleportPolicy"/> and
    /// <see cref="TeleportRequestCounter"/>, which are tested natively.
    ///
    /// HOW A HUMAN FIRES ONE. There is no command channel - the SDK's
    /// SendCommandRequest is a `// TODO` stub - so a client cannot ask to be
    /// teleported and this is deliberately not trying to invent one. Instead the
    /// server watches a file:
    ///
    /// <code>
    ///   echo haven         &gt; /tmp/wareborn-teleport   # everyone, to spawn
    ///   echo 'mausoleum'   &gt; /tmp/wareborn-teleport   # everyone, 4.4 km away
    ///   echo 'haven 3'     &gt; /tmp/wareborn-teleport   # just player entity 3
    /// </code>
    ///
    /// The path is <c>WAREBORN_TELEPORT_FILE</c>, defaulting to
    /// <c>/tmp/wareborn-teleport</c>. The file is consumed (deleted) once read,
    /// so one write is one teleport.
    ///
    /// A FILE rather than a keypress on purpose: this server is normally run
    /// detached or over ssh, where <c>Console.ReadKey</c> either throws on
    /// redirected input or silently never fires. A file works identically in a
    /// terminal, under systemd, and from a second ssh session, needs no TTY, and
    /// costs one <c>File.Exists</c> per poll.
    /// </summary>
    internal sealed class TeleportService
    {
        /// <summary>
        /// How often the trigger file is looked at. The main loop turns once per
        /// ENet EVENT, not once per 50 ms - a busy server spins it far faster
        /// than the poll timeout suggests - so the check is clock-gated rather
        /// than run every iteration. Half a second is imperceptible to whoever
        /// just typed `echo`, and it is why this costs nothing when idle.
        /// </summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        private const string DefaultTriggerFile = "/tmp/wareborn-teleport";

        /// <summary>
        /// The label the operator trigger file's teleports carry in the log.
        /// </summary>
        private const string OperatorReason = "teleport";

        /// <summary>
        /// The label an automatic fall-floor rescue carries in the log, at BOTH
        /// ends: on the send, and again on the 1073 ack that proves it landed.
        /// Grep-able on purpose - "why did I end up back at spawn" and "did the
        /// rescue work" are the two questions this feature will ever be asked.
        /// </summary>
        internal const string FallRescueReason = "fall-rescue";
        internal const string LogoutRestoreReason = "logout-restore";

        private readonly TeleportRequestCounter _requests = new TeleportRequestCounter();
        private readonly TeleportArrivalTracker _arrivals = new TeleportArrivalTracker();

        /// <summary>
        /// Why the outstanding teleport for an entity was sent, so the ack can be
        /// logged in the same words as the send. Set on every send, read once on
        /// the ack; entries are dropped with the entity in <see cref="Forget"/>.
        /// </summary>
        private readonly Dictionary<long, string> _reasonByEntity = new Dictionary<long, string>();
        private readonly Dictionary<long, TeleportDestination> _destinationByEntity = new();
        private readonly Dictionary<long, (ulong PeerId, long HullEntityId)> _shipRestoreByEntity
            = new();

        private sealed class PendingTerrainTeleport
        {
            public ulong PeerId;
            public long EntityId;
            public TeleportDestination Destination;
            public TimeSpan Deadline;

            /// <summary>The label this teleport will carry in the log when it fires.</summary>
            public string Reason = OperatorReason;

            /// <summary>
            /// Whether this is a returning player's logout restore rather than an
            /// operator teleport. A restore may be holding a loading screen open,
            /// and it fails toward the spawn point rather than toward nothing, so
            /// both of its endings say something different from an operator's.
            /// </summary>
            public bool IsRestore;
            public long RequiredShipHullId;
        }

        private readonly Dictionary<long, PendingTerrainTeleport> _pendingTerrain = new();
        private readonly Stopwatch _terrainWaitClock = Stopwatch.StartNew();

        private readonly Stopwatch _sinceLastPoll = Stopwatch.StartNew();
        private readonly string _triggerFile;

        public TeleportService()
        {
            string? configured = Environment.GetEnvironmentVariable("WAREBORN_TELEPORT_FILE");
            _triggerFile = string.IsNullOrWhiteSpace(configured) ? DefaultTriggerFile : configured.Trim();
        }

        /// <summary>The trigger file path, for the startup banner.</summary>
        public string TriggerFile => _triggerFile;

        /// <summary>
        /// Puts 190607 TeleportRequestState on a player entity that has just
        /// finished first-time setup. Without it nothing here can move anybody:
        /// it is the third [Require] of TeleportTransformVisualizer, and the
        /// client does not reliably ask for it.
        ///
        /// It is NOT added to the authority grant. The server is the only writer;
        /// granting it would let any client teleport itself anywhere in the
        /// world. And the seed it carries has request 0, which by construction
        /// cannot fire a teleport - see TeleportComponent.Seed for why that is
        /// the difference between "teleport is available" and "everybody
        /// teleports the moment they load in".
        ///
        /// Deliberately non-fatal: a failure here costs teleport and nothing
        /// else.
        /// </summary>
        public void SeedOn(ENetPeerHandle peer, long entityId)
        {
            List<Structs.Structs.InterestOverride> teleport = new List<Structs.Structs.InterestOverride>
            {
                new Structs.Structs.InterestOverride(TeleportPolicy.TeleportRequestStateComponentId, 1),
            };

            List<uint> teleportServed = new List<uint>();
            if (SendOPHelper.SendAddComponentOp(peer, entityId, teleport, false, teleportServed))
            {
                // Ledger the seed so the client's re-declared interest for its own
                // entity does not re-ADD 190607 (same MarkServed-gap class as the
                // 190602 duplicate; a re-add cycles TeleportTransformVisualizer's
                // reader for no reason).
                WorldsAdriftRebornGameServer.ServedComponents.MarkServed(peer, entityId, teleportServed);
                Console.WriteLine("[info] teleport: seeded 190607 on entity " + entityId
                    + " (request " + TeleportPolicy.SeedRequest + ", parent absent). It can now be moved.");
            }
            else
            {
                Console.WriteLine("[warning] teleport: could not seed 190607 on entity " + entityId
                    + "; that player cannot be teleported this session.");
            }
        }

        /// <summary>
        /// Reads and consumes the trigger file if it is there and the poll
        /// interval has elapsed. Safe to call as often as you like.
        /// </summary>
        public void PollTrigger()
        {
            if (_sinceLastPoll.Elapsed < PollInterval)
            {
                return;
            }
            _sinceLastPoll.Restart();

            PollPendingTerrainTeleports();

            string line;
            try
            {
                if (!File.Exists(_triggerFile))
                {
                    return;
                }

                // Read then delete, so a teleport fires exactly once per write
                // even if it takes several polls to reach every peer. Delete
                // before acting: if anything below throws, the file is already
                // gone and the server cannot get stuck teleporting on a loop.
                line = File.ReadAllText(_triggerFile);
                File.Delete(_triggerFile);
            }
            catch (Exception e)
            {
                // A half-written file, a permissions change, a race with the
                // shell redirect: none of that is worth taking the server down.
                Console.WriteLine("[warning] teleport: could not read " + _triggerFile + ": " + e.Message);
                return;
            }

            foreach (string candidate in line.Split('\n'))
            {
                if (TeleportPolicy.TryParseCommand(candidate, out TeleportCommand command, out string error))
                {
                    Execute(command);
                }
                else if (error.Length > 0)
                {
                    Console.WriteLine("[warning] teleport: " + error + ".");
                }
            }
        }

        /// <summary>Carries out one parsed command.</summary>
        private void Execute(TeleportCommand command)
        {
            if (!TeleportPolicy.RequiredTerrainIsRegistered(
                    command.Destination,
                    key => WorldsAdriftRebornGameServer.WorldEntities.ByKey(key) != null))
            {
                Console.WriteLine("[warning] teleport: refusing '" + command.Destination.Name
                    + "' because required terrain '" + command.Destination.RequiredWorldEntityKey
                    + "' is not registered. Enable WAREBORN_SPAWN_SECOND_ISLAND=1 and restart first.");
                return;
            }

            if (!command.Destination.LandsOnLoadedGround)
            {
                // Not a refusal - going somewhere with no ground is the whole
                // point of testing this - but it must be said out loud, because
                // there is still no fall damage and no world-edge pushback here,
                // so the arrival is a fall rather than a landing.
                //
                // Whether the fall ends automatically now depends on the
                // fall-rescue MODE (see the startup banner / AutoFallRescuePolicy):
                // the deep world-fall net is always armed, but the ordinary island
                // floor only catches when the legacy auto-rescue is on. What is
                // always true is that the player can press F10 to return to Haven.
                Console.WriteLine("[warning] teleport: '" + command.Destination.Name
                    + "' has no entity spawned at it, so expect a fall. "
                    + (FallPolicy.IsBelowDeepFloor(command.Destination.Position)
                        ? "It is BELOW the deep world-fall net (" + FallPolicy.DeepFloorMetres.ToString("0.#")
                          + " m), so it is caught on arrival and sent back to "
                          + TeleportPolicy.SafeDestination.Name + " regardless of mode."
                        : "It is above the deep net, so whether the fall is auto-caught depends on the "
                          + "fall-rescue mode; the player can press F10 to return to "
                          + TeleportPolicy.SafeDestination.Name + " at any time."));
            }

            int sent = 0;
            foreach ((ulong peerId, long entityId) in WorldsAdriftRebornGameServer.Players.All())
            {
                if (command.EntityId.HasValue && command.EntityId.Value != entityId)
                {
                    continue;
                }

                if (DispatchWithTerrainGate(peerId, entityId, command.Destination, OperatorReason)) sent++;
            }

            if (sent == 0)
            {
                Console.WriteLine("[warning] teleport: nobody to move ("
                    + (command.EntityId.HasValue
                        ? "entity " + command.EntityId.Value + " is not a connected player"
                        : "no players connected")
                    + ").");
            }
        }

        /// <summary>
        /// Sends one teleport, or defers it until the destination's terrain is on
        /// that peer. Returns true when the teleport is under way - SENT or WAITING -
        /// and false when it could not start at all.
        ///
        /// This is THE arrival gate, and every path that puts a player somewhere
        /// they are not already standing goes through it: the operator trigger
        /// file, and the wilderness shrine. (The logout restore composes the same
        /// three decisions in <see cref="RestoreLoggedOutPosition"/>, which also
        /// has a loading screen to hold and so cannot simply call this.) It was
        /// extracted from the operator path rather than copied, because a second
        /// copy of "ask, then wait, then give up" is exactly how the restore path
        /// once ended up with no gate at all - see SpawnRestorePolicy's remarks on
        /// the bug this class of thing produces.
        ///
        /// A destination naming no island is sent immediately: there is no terrain
        /// to wait for, which is honest for Haven and for an ad-hoc coordinate, and
        /// is why the RequiredWorldEntityKey on a wilderness destination is the
        /// load-bearing field rather than a label.
        /// </summary>
        private bool DispatchWithTerrainGate(
            ulong peerId, long entityId, TeleportDestination destination, string reason)
        {
            ENetPeerHandle? peer = PeerIdentity.Instance.Resolve(new IntPtr((long)peerId));
            IslandDefinition? destinationIsland = destination.RequiredWorldEntityKey == null
                ? null
                : WorldsAdriftRebornGameServer.IslandTopology.ByWorldEntityKey(
                    destination.RequiredWorldEntityKey);
            IslandTerrainInterestService? terrain = WorldsAdriftRebornGameServer.TerrainInterest;
            if (peer == null || destinationIsland == null || terrain == null || !terrain.Enabled)
            {
                return Send(peerId, entityId, destination, reason);
            }

            // Asking is also DOING: RequestDestination pins the island as this
            // peer's forced destination, which is what makes the terrain interest
            // service check it out however far away it is. A wilderness island is
            // at least 9.9 km from Haven, well past any load radius, so without
            // this call it would never even be requested.
            TerrainDestinationStatus readiness = terrain.RequestDestination(peer, destinationIsland.Id);
            TerrainTeleportDecision decision = IslandTerrainTeleportPolicy.Decide(
                terrainManaged: true,
                destinationKnown: readiness != TerrainDestinationStatus.Unknown,
                terrainReady: readiness == TerrainDestinationStatus.Ready,
                waitExpired: false);
            if (decision == TerrainTeleportDecision.Send)
            {
                return Send(peerId, entityId, destination, reason);
            }

            if (decision == TerrainTeleportDecision.Wait)
            {
                _pendingTerrain[entityId] = new PendingTerrainTeleport
                {
                    PeerId = peerId,
                    EntityId = entityId,
                    Destination = destination,
                    Deadline = TerrainWaitDeadline(terrain),
                    Reason = reason,
                };
                Console.WriteLine("[" + reason + "] deferring entity " + entityId + " -> "
                    + destination.Name + " until terrain " + destinationIsland.Id
                    + " is checked out for that peer (up to "
                    + TerrainWaitBudget(terrain).TotalSeconds.ToString("0.#") + " s).");
                return true;
            }

            Console.WriteLine("[warning] " + reason + ": refusing '" + destination.Name
                + "' because its terrain is not managed by the local authority host.");
            return false;
        }

        /// <summary>
        /// GRADUATION: moves a player off Haven to their Wilderness island, by the
        /// same 190607 path and the same terrain gate as everything else here.
        ///
        /// Nothing about it is a special teleport, and that is the point. The
        /// decision - which island, and whether the crew has one already - is made
        /// by <see cref="Multiplayer.Wilderness.WildernessGraduationPolicy"/>, which
        /// is pure and tested; this method is only the wire, and it deliberately
        /// takes an already-built <see cref="TeleportDestination"/> so it cannot
        /// grow a second opinion about where anybody goes.
        /// </summary>
        public bool Graduate(long entityId, TeleportDestination destination) =>
            DispatchTo(entityId, destination, Multiplayer.Wilderness.WildernessShrine.TeleportReason);

        /// <summary>
        /// OPERATOR ENTRY POINT: moves one already-identified player to one
        /// already-built destination, through the same terrain gate as everything
        /// else in this file.
        ///
        /// It exists so the operator command surface does not have to reach for
        /// <see cref="Graduate"/> (which would mislabel every log line as a shrine
        /// use) and so it CANNOT reach for <see cref="Send"/> (which is the gateless
        /// door, and the one the logout restore's original bug went through). The
        /// reason string is the caller's, the gate is not negotiable.
        ///
        /// The target is a player ENTITY id because resolution - uid to entity,
        /// with its refusals for "nobody" and "more than one" - has already
        /// happened in <c>OperatorTargetPolicy</c>. Nothing here re-resolves
        /// anything.
        /// </summary>
        public bool DispatchTo(long entityId, TeleportDestination destination, string reason)
        {
            foreach ((ulong peerId, long candidate) in WorldsAdriftRebornGameServer.Players.All())
            {
                if (candidate == entityId)
                {
                    return DispatchWithTerrainGate(peerId, entityId, destination, reason);
                }
            }

            Console.WriteLine("[info] " + reason
                + ": entity " + entityId + " is no longer a connected player, nothing to move.");
            return false;
        }

        private void PollPendingTerrainTeleports()
        {
            foreach ((long entityId, PendingTerrainTeleport pending) in _pendingTerrain.ToArray())
            {
                ENetPeerHandle? peer = PeerIdentity.Instance.Resolve(new IntPtr((long)pending.PeerId));
                if (pending.RequiredShipHullId > 0)
                {
                    bool shipExpired = _terrainWaitClock.Elapsed >= pending.Deadline;
                    long resolvedHull = 0;
                    ShipDomainInterestService.RestoreCheckoutStatus shipStatus = peer == null
                        ? ShipDomainInterestService.RestoreCheckoutStatus.Unknown
                        : WorldsAdriftRebornGameServer.ShipInterest.RequestRestoreDestination(
                            peer, pending.Destination.Position, out resolvedHull);
                    bool sameHull = peer != null
                        && shipStatus != ShipDomainInterestService.RestoreCheckoutStatus.Unknown
                        && resolvedHull == pending.RequiredShipHullId;
                    if (sameHull && shipStatus == ShipDomainInterestService.RestoreCheckoutStatus.Ready)
                    {
                        _pendingTerrain.Remove(entityId);
                        bool sent = Send(pending.PeerId, entityId, pending.Destination, pending.Reason);
                        if (sent)
                            _shipRestoreByEntity[entityId] = (pending.PeerId,
                                pending.RequiredShipHullId);
                        else
                            WorldsAdriftRebornGameServer.ShipInterest.CompleteRestoreDestination(
                                peer!, pending.RequiredShipHullId);
                        ReleaseSpawnHoldFor(pending, sent
                            ? "destination ship root and decks materialized and the restore was sent"
                            : "destination ship was ready but the restore send failed");
                    }
                    else if (peer == null || shipExpired)
                    {
                        _pendingTerrain.Remove(entityId);
                        if (peer != null)
                            WorldsAdriftRebornGameServer.ShipInterest.CompleteRestoreDestination(
                                peer, pending.RequiredShipHullId);
                        Console.WriteLine("[warning] " + pending.Reason + ": safely refused entity "
                            + entityId + " -> open-sky ship " + pending.RequiredShipHullId
                            + "; its root/deck materialization did not complete in time.");
                        ReleaseSpawnHoldFor(pending,
                            "the destination ship never became ready");
                    }
                    continue;
                }
                IslandDefinition? island = pending.Destination.RequiredWorldEntityKey == null
                    ? null
                    : WorldsAdriftRebornGameServer.IslandTopology.ByWorldEntityKey(
                        pending.Destination.RequiredWorldEntityKey);
                IslandTerrainInterestService? terrain = WorldsAdriftRebornGameServer.TerrainInterest;
                if (peer == null || island == null || terrain == null || !terrain.Enabled)
                {
                    _pendingTerrain.Remove(entityId);
                    Console.WriteLine("[warning] " + pending.Reason
                        + ": cancelled deferred request for entity "
                        + entityId + "; peer or terrain authority vanished.");
                    ReleaseSpawnHoldFor(pending, "the destination terrain authority went away");
                    continue;
                }

                TerrainDestinationStatus status = terrain.RequestDestination(peer, island.Id);
                bool expired = IslandTerrainTeleportPolicy.WaitExpired(
                    _terrainWaitClock.Elapsed, pending.Deadline);
                TerrainTeleportDecision decision = IslandTerrainTeleportPolicy.Decide(
                    terrainManaged: true,
                    destinationKnown: status != TerrainDestinationStatus.Unknown,
                    terrainReady: status == TerrainDestinationStatus.Ready,
                    waitExpired: expired);
                if (decision == TerrainTeleportDecision.Send)
                {
                    _pendingTerrain.Remove(entityId);
                    Send(pending.PeerId, entityId, pending.Destination, pending.Reason);
                    // Only NOW is the loading screen allowed to lift: the client has
                    // the terrain AND has been told where to stand on it, in that
                    // order, which is the whole point of holding it.
                    ReleaseSpawnHoldFor(pending, "destination terrain is ready and the restore has been sent");
                    continue;
                }
                if (decision == TerrainTeleportDecision.Refuse)
                {
                    _pendingTerrain.Remove(entityId);
                    // The bounded ending. A restore that never becomes safe leaves
                    // the player standing at the spawn point they were seeded at -
                    // which is where they already are, so there is nothing to send.
                    SpawnRestoreOutcome refusal = SpawnRestorePolicy.Decide(
                        PositionRestoreVerdict.Restore,
                        new IslandLocation(IslandLocationKind.OnKnownTerrain, island, 0.0),
                        destinationTerrainRegistered: true,
                        terrainDecision: TerrainTeleportDecision.Refuse,
                        waitExpired: expired);
                    Console.WriteLine("[warning] " + pending.Reason + ": safely refused entity "
                        + entityId + " -> " + pending.Destination.Name + "; " + refusal.Reason + ".");
                    ReleaseSpawnHoldFor(pending, "the destination terrain never became ready");
                }
            }
        }

        /// <summary>
        /// Puts a player who has fallen through the bottom of the world back on
        /// solid ground, by the same 190607 path the operator trigger file uses.
        ///
        /// The DECISION to do this is not taken here and is not taken in the
        /// packet loop either - it is <see cref="FallWatch.Observe"/>, which is
        /// pure and tested. This method is only the wire.
        ///
        /// Nothing else about the rescue is special: the same request counter,
        /// the same parentless 190607 update, the same 1073 ack. That is the
        /// whole reason a fall floor was a small change - the machinery to put a
        /// player somewhere already existed and had already been proven against
        /// a real client.
        /// </summary>
        public bool RescueFromFall(long entityId, FixedPointPosition where, int attempt)
        {
            TeleportDestination home = TeleportPolicy.SafeDestination;

            // The trigger floor depends on the mode (island floor when the auto
            // rescue is on, the deep world-fall net when it is off - see
            // AutoFallRescuePolicy), so the actual y is logged rather than a
            // hardcoded floor that would be wrong in one of the two modes.
            Console.WriteLine("[warning] " + FallRescueReason + ": entity " + entityId + " is at y "
                + where.MetresY.ToString("0.#") + " m, below the active fall floor. "
                + "It fell out of the world; sending it to " + home.Name + " (attempt " + attempt
                + " of " + FallWatch.MaxAttemptsPerFall + ").");

            foreach ((ulong peerId, long candidate) in WorldsAdriftRebornGameServer.Players.All())
            {
                if (candidate == entityId)
                {
                    return Send(peerId, entityId, home, FallRescueReason);
                }
            }

            // The peer went away between publishing that transform and this call.
            // Nothing to do, and nothing wrong: the watch record is dropped by
            // ForgetPeer along with everything else the peer owned.
            Console.WriteLine("[info] " + FallRescueReason + ": entity " + entityId
                + " is no longer a connected player, nothing to rescue.");
            return false;
        }

        /// <summary>
        /// Puts a returning player back where they logged out, by the same 190607
        /// path as the operator trigger and the fall rescue - but NEVER before the
        /// ground they are being put on exists on their client.
        ///
        /// This is a teleport rather than a different spawn seed on purpose: see
        /// PlayerPositionService, and SpawnPolicy on why re-seeding 190602 on a
        /// live entity is the out-of-world bug this must not become.
        ///
        /// WHAT WENT WRONG BEFORE. The first version of this method called
        /// <see cref="Send"/> directly and built its destination with no
        /// <c>RequiredWorldEntityKey</c>. Its own comment claimed it inherited the
        /// terrain-readiness deferral; it did not - that deferral lives in
        /// <see cref="Execute"/>, which this path never entered. So a character
        /// whose logout position was on OPTIONAL terrain - checked out only by
        /// proximity, and Shattered Mausoleum is 4425 m from spawn, past a 4000 m
        /// load radius - was moved onto an island their client had never been sent,
        /// and fell through it. The naming gap had a second cost: with no island
        /// named, the 1073 landing could not pin ConfirmedGround, so even a lucky
        /// arrival could have its terrain unloaded out from under it later.
        ///
        /// It now asks <see cref="IslandLocationPolicy"/> which island the stored
        /// point belongs to, requests that terrain for this peer, and lets
        /// <see cref="SpawnRestorePolicy"/> decide between placing, holding, and
        /// staying at the spawn point. Every decision is pure and tested; this is
        /// only the wire.
        /// </summary>
        public bool RestoreLoggedOutPosition(
            long entityId,
            FixedPointPosition? stored,
            PositionRestoreVerdict verdict)
        {
            ulong? peerId = null;
            foreach ((ulong candidatePeer, long candidate) in WorldsAdriftRebornGameServer.Players.All())
            {
                if (candidate == entityId)
                {
                    peerId = candidatePeer;
                    break;
                }
            }

            if (!peerId.HasValue)
            {
                // They disconnected between publishing 1088 and this call. Nothing
                // to do and nothing wrong; ForgetPeer drops the rest.
                Console.WriteLine("[info] " + LogoutRestoreReason + ": entity " + entityId
                    + " is no longer a connected player, nothing to restore.");
                return false;
            }

            ENetPeerHandle? peer = PeerIdentity.Instance.Resolve(new IntPtr((long)peerId.Value));

            // Which island's terrain bundle is this point standing on? Answered
            // against the WHOLE known world, not this boot's topology, so a stored
            // position on an island we are not hosting is recognised and refused
            // rather than mistaken for open sky and restored into nothing.
            IslandLocation location = stored.HasValue && verdict == PositionRestoreVerdict.Restore
                ? IslandLocationPolicy.Locate(stored.Value, IslandLocationPolicy.KnownWorld())
                : IslandLocation.OpenSky;

            IslandDefinition? registered = location.Island == null
                ? null
                : WorldsAdriftRebornGameServer.IslandTopology.ById(location.Island.Id);
            bool terrainRegistered = registered != null
                && WorldsAdriftRebornGameServer.WorldEntities.ByKey(registered.WorldEntityKey) != null;

            IslandTerrainInterestService? terrain = WorldsAdriftRebornGameServer.TerrainInterest;
            bool managed = terrain != null && terrain.Enabled && terrainRegistered && peer != null;

            // Asking is also DOING: RequestDestination pins the island as this
            // peer's forced destination, which is what makes the terrain interest
            // service check it out regardless of the load radius. Without this call
            // an out-of-radius island is never even requested.
            TerrainDestinationStatus readiness = managed
                ? terrain!.RequestDestination(peer!, registered!.Id)
                : TerrainDestinationStatus.Ready;

            TerrainTeleportDecision terrainDecision = IslandTerrainTeleportPolicy.Decide(
                terrainManaged: managed,
                destinationKnown: readiness != TerrainDestinationStatus.Unknown,
                terrainReady: readiness == TerrainDestinationStatus.Ready,
                waitExpired: false);

            SpawnRestoreOutcome outcome = SpawnRestorePolicy.Decide(
                verdict, location, terrainRegistered, terrainDecision, waitExpired: false);

            if (outcome.Decision == SpawnRestoreDecision.UseSpawnPoint)
            {
                Console.WriteLine("[info] " + LogoutRestoreReason + ": entity " + entityId
                    + " stays at " + TeleportPolicy.SafeDestination.Name + "; " + outcome.Reason + ".");
                return false;
            }

            TeleportDestination home = new TeleportDestination(
                LogoutRestoreReason,
                stored!.Value,
                // Still false, and honestly so: the server has no terrain query, so
                // an arbitrary stored coordinate is never an EVIDENCED surface point
                // however confident we are about which island it is on. What
                // protects this arrival is the terrain gate below, not this flag.
                landsOnLoadedGround: false,
                description: "where this character logged out (" + location.Name + ")",
                // The island name is the load-bearing part: it is what lets the
                // 1073 landing call ConfirmTeleportLanding and pin this terrain as
                // the player's confirmed ground, so the streamer cannot unload it.
                requiredWorldEntityKey: registered?.WorldEntityKey);

            // A ship deck wins over a broad island envelope. Preload that
            // domain around the stored coordinate and wait for the client's own
            // component-interest requests for the root and every deck. Sending
            // first creates a circular dependency: resource interest moves only
            // after landing, while the collider is needed before landing.
            if (peer != null)
            {
                ShipDomainInterestService.RestoreCheckoutStatus shipStatus =
                    WorldsAdriftRebornGameServer.ShipInterest.RequestRestoreDestination(
                        peer, stored!.Value, out long restoreHull);
                if (shipStatus == ShipDomainInterestService.RestoreCheckoutStatus.Unknown)
                {
                    if (location.Kind == IslandLocationKind.OpenSky)
                    {
                        Console.WriteLine("[warning] " + LogoutRestoreReason + ": entity " + entityId
                            + " stays at " + TeleportPolicy.SafeDestination.Name
                            + "; its open-sky logout position is not inside a live hull envelope.");
                        return false;
                    }
                }
                else if (shipStatus == ShipDomainInterestService.RestoreCheckoutStatus.Ready)
                {
                    bool sent = Send(peerId.Value, entityId, home, LogoutRestoreReason);
                    if (sent)
                        _shipRestoreByEntity[entityId] = (peerId.Value, restoreHull);
                    else
                        WorldsAdriftRebornGameServer.ShipInterest.CompleteRestoreDestination(
                            peer, restoreHull);
                    return sent;
                }
                else
                {
                    _pendingTerrain[entityId] = new PendingTerrainTeleport
                    {
                        PeerId = peerId.Value,
                        EntityId = entityId,
                        Destination = home,
                        Deadline = _terrainWaitClock.Elapsed + TimeSpan.FromSeconds(35),
                        Reason = LogoutRestoreReason,
                        IsRestore = true,
                        RequiredShipHullId = restoreHull,
                    };
                    Console.WriteLine("[info] " + LogoutRestoreReason + ": entity " + entityId
                        + " held at " + TeleportPolicy.SafeDestination.Name + " while ship "
                        + restoreHull + " root/deck colliders materialize (up to 35 s).");
                    return true;
                }
            }

            if (outcome.Decision == SpawnRestoreDecision.Place)
            {
                Console.WriteLine("[info] " + LogoutRestoreReason + ": entity " + entityId
                    + " -> " + location.Name + "; " + outcome.Reason + ".");
                return Send(peerId.Value, entityId, home, LogoutRestoreReason);
            }

            _pendingTerrain[entityId] = new PendingTerrainTeleport
            {
                PeerId = peerId.Value,
                EntityId = entityId,
                Destination = home,
                Deadline = TerrainWaitDeadline(terrain),
                Reason = LogoutRestoreReason,
                IsRestore = true,
            };
            Console.WriteLine("[info] " + LogoutRestoreReason + ": entity " + entityId
                + " held at " + TeleportPolicy.SafeDestination.Name + "; " + outcome.Reason
                + " (up to " + TerrainWaitBudget(terrain).TotalSeconds.ToString("0.#")
                + " s, then it stays at " + TeleportPolicy.SafeDestination.Name + ").");
            return true;
        }

        /// <summary>
        /// Whether this entity's restore is still waiting for its destination
        /// terrain. The loading barrier asks: a client that signals ready while
        /// this is true is held on its loading screen rather than shown a world it
        /// is about to be moved out of - or worse, dropped into a hole.
        /// </summary>
        public bool HasPendingRestore(long entityId) =>
            _pendingTerrain.TryGetValue(entityId, out PendingTerrainTeleport? pending)
            && pending.IsRestore;

        /// <summary>
        /// How much of the bounded terrain wait this entity's pending restore has
        /// left, or null if it has none. Returned as a DURATION rather than a
        /// deadline because this service's wait clock and the server clock have
        /// different epochs; the caller adds it to its own.
        /// </summary>
        public TimeSpan? RestoreWaitRemaining(long entityId)
        {
            if (!_pendingTerrain.TryGetValue(entityId, out PendingTerrainTeleport? pending)
                || !pending.IsRestore)
            {
                return null;
            }

            TimeSpan remaining = pending.Deadline - _terrainWaitClock.Elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>The bounded budget one deferred teleport gets to wait for its terrain.</summary>
        private static TimeSpan TerrainWaitBudget(IslandTerrainInterestService? terrain) =>
            (terrain?.AssetAckTimeout ?? TimeSpan.FromSeconds(30)) + TimeSpan.FromSeconds(5);

        private TimeSpan TerrainWaitDeadline(IslandTerrainInterestService? terrain) =>
            _terrainWaitClock.Elapsed + TerrainWaitBudget(terrain);

        /// <summary>
        /// Lets go of any loading screen this restore was holding open. Called on
        /// EVERY ending of a pending restore - sent, refused, or abandoned - so
        /// there is no path on which a held client is forgotten. A no-op for an
        /// operator teleport and for a peer that was never held.
        /// </summary>
        private static void ReleaseSpawnHoldFor(PendingTerrainTeleport pending, string reason)
        {
            if (!pending.IsRestore) return;
            WorldsAdriftRebornGameServer.ReleaseSpawnHold(pending.PeerId, pending.EntityId, reason);
        }

        /// <summary>
        /// Sends one player one teleport: a 190607 update carrying the
        /// destination and a fresh request number.
        /// </summary>
        private bool Send(ulong peerId, long entityId, TeleportDestination destination, string reason = OperatorReason)
        {
            ENetPeerHandle? peer = PeerIdentity.Instance.Resolve(new IntPtr((long)peerId));
            if (peer == null)
            {
                Console.WriteLine("[warning] teleport: peer 0x" + peerId.ToString("x") + " vanished.");
                return false;
            }

            // A client that has not finished first-time setup has not been given
            // 190607 yet, so an update would land on a component it does not have.
            //
            // This is NOT a loading-screen gate, and an older comment here that
            // said it was is wrong: first-time setup ADDS the peer to
            // clientSetupState and only then ARMS the loading barrier, so every
            // teleport that passes this check may well reach a client still behind
            // its loading screen. That turns out to be exactly where a logout
            // restore wants to land - see RestoreLoggedOutPosition - but nothing
            // here may assume either way.
            if (!PeerManager.Instance.clientSetupState.Contains(peer))
            {
                Console.WriteLine("[info] teleport: entity " + entityId + " is still loading, skipping.");
                return false;
            }

            int request = _requests.Next(entityId);
            _reasonByEntity[entityId] = reason;

            bool ok = SendOPHelper.SendComponentUpdateOp(
                peer,
                entityId,
                new List<uint> { TeleportPolicy.TeleportRequestStateComponentId },
                new List<object> { TeleportComponent.Request(destination.Position, request) });

            if (!ok)
            {
                Console.WriteLine("[error] " + reason + ": failed to send request " + request
                    + " for entity " + entityId + ".");
                return false;
            }

            _destinationByEntity[entityId] = destination;
            _arrivals.Arm(entityId, request, destination.Position);

            Console.WriteLine("[info] " + reason + ": entity " + entityId + " -> " + destination.Name
                + " " + destination.Position + ", request " + request
                + ", awaiting 1073 ack or bounded transform confirmation.");
            return true;
        }

        /// <summary>
        /// Called with every 1073 <c>lastExecutedRequest</c> the client
        /// publishes. This is the preferred evidence that a teleport happened:
        /// <c>TeleportTransformVisualizer</c> writes the executed
        /// request number back into ClientAuthoritativePlayerState, a component
        /// we already grant the client authority over, which is precisely why
        /// this path needs no new grant.
        ///
        /// The client re-publishes 1073 every tick, so the field arrives
        /// constantly; the counter decides what is news so the log carries one
        /// line per landing rather than one per frame.
        /// </summary>
        public void OnAck(ENetPeerHandle peer, long entityId, int lastExecutedRequest)
        {
            int? outstandingBefore = _requests.Outstanding(entityId);

            if (!_requests.RecordAck(entityId, lastExecutedRequest))
            {
                return;
            }

            // Report the landing in the same words as the send, so a fall rescue
            // reads as one event with two halves rather than as an unexplained
            // teleport somebody has to correlate by request number.
            if (!_reasonByEntity.TryGetValue(entityId, out string? reason))
            {
                reason = OperatorReason;
            }

            if (outstandingBefore.HasValue && lastExecutedRequest >= outstandingBefore.Value)
            {
                _arrivals.Cancel(entityId);
                if (_destinationByEntity.Remove(entityId, out TeleportDestination landed))
                {
                    WorldsAdriftRebornGameServer.ResourceInterest.ObserveGlobalPosition(
                        peer, landed.Position, "teleport landing '" + landed.Name + "'");
                    CompleteShipRestoreInterest(entityId, peer);
                    ObserveTerrainLanding(peer, landed, landed.Position);
                }
                Console.WriteLine("[success] " + reason + ": entity " + entityId
                    + " executed request " + lastExecutedRequest + ". It landed"
                    + (reason == FallRescueReason ? " - it is back on solid ground." : "."));
            }
            else
            {
                // Either an ack for something we never sent (a re-seed, another
                // writer) or one that does not yet cover the outstanding
                // request. Worth a line: this is what a half-applied teleport
                // looks like from here.
                Console.WriteLine("[info] " + reason + ": entity " + entityId + " reports lastExecutedRequest "
                    + lastExecutedRequest + ", outstanding "
                    + (outstandingBefore.HasValue ? outstandingBefore.Value.ToString() : "none") + ".");
            }
        }

        /// <summary>
        /// Fallback landing proof for client builds that execute 190607 but do
        /// not publish 1073 lastExecutedRequest. The caller has already enforced
        /// player ownership. The pure tracker additionally requires two
        /// consecutive unparented world transforms within 12m of the exact
        /// outstanding server-issued destination; arbitrary client jumps cannot
        /// qualify.
        /// </summary>
        public void OnPlayerTransform(
            ENetPeerHandle peer,
            long entityId,
            FixedPointPosition position,
            bool? parentPresent)
        {
            int? provedRequest = _arrivals.Observe(entityId, position, parentPresent);
            if (!provedRequest.HasValue)
            {
                return;
            }

            int? completedRequest = _requests.ConfirmOutstanding(entityId);
            if (!completedRequest.HasValue || completedRequest.Value != provedRequest.Value)
            {
                // A newer request or a real ack won the race between samples.
                // Never advance interest for stale evidence.
                return;
            }

            if (!_reasonByEntity.TryGetValue(entityId, out string? reason))
            {
                reason = OperatorReason;
            }

            if (!_destinationByEntity.Remove(entityId, out TeleportDestination landed))
            {
                return;
            }

            WorldsAdriftRebornGameServer.ResourceInterest.ObserveGlobalPosition(
                peer, position, "teleport transform confirmation '" + landed.Name + "'");
            CompleteShipRestoreInterest(entityId, peer);
            ObserveTerrainLanding(peer, landed, position);
            Console.WriteLine("[success] " + reason + ": entity " + entityId
                + " transform-confirmed request " + completedRequest.Value + " near "
                + landed.Name + " at " + position
                + "; this client did not publish the 1073 ack, so world interest advanced from its authoritative 190602.");
        }

        /// <summary>
        /// A ship-relative 1073 naming the exact preloaded restore hull is stronger
        /// landing evidence than the ordinary unparented position fallback. Retail
        /// clients that omit lastExecutedRequest switch to this frame immediately
        /// on deck contact, so finish the request and release the speculative pin.
        /// </summary>
        public void OnShipBoarded(ENetPeerHandle peer, long entityId, long hullEntityId)
        {
            if (!_shipRestoreByEntity.TryGetValue(entityId, out var restore)
                || restore.HullEntityId != hullEntityId) return;

            int? request = _requests.ConfirmOutstanding(entityId);
            _arrivals.Cancel(entityId);
            _destinationByEntity.Remove(entityId);
            CompleteShipRestoreInterest(entityId, peer);
            Console.WriteLine("[success] " + LogoutRestoreReason + ": entity " + entityId
                + " boarded destination ship " + hullEntityId
                + (request.HasValue ? " for request " + request.Value : string.Empty)
                + "; ship-relative 1073 confirmed the landing and released its preload pin.");
        }

        private static void ObserveTerrainLanding(
            ENetPeerHandle peer,
            TeleportDestination landed,
            FixedPointPosition position)
        {
            IslandTerrainInterestService? terrain = WorldsAdriftRebornGameServer.TerrainInterest;
            IslandDefinition? island = landed.RequiredWorldEntityKey == null
                ? null
                : WorldsAdriftRebornGameServer.IslandTopology.ByWorldEntityKey(
                    landed.RequiredWorldEntityKey);
            if (terrain != null && island != null)
                terrain.ConfirmTeleportLanding(peer, island.Id, position);
            else
                terrain?.ObserveGlobalPosition(peer, position);
        }

        /// <summary>Drops an entity's counters when its peer disconnects.</summary>
        public void Forget(long entityId)
        {
            _requests.Forget(entityId);
            _arrivals.Cancel(entityId);
            _reasonByEntity.Remove(entityId);
            _destinationByEntity.Remove(entityId);
            _pendingTerrain.Remove(entityId);
            _shipRestoreByEntity.Remove(entityId);
        }

        private void CompleteShipRestoreInterest(long entityId, ENetPeerHandle peer)
        {
            if (!_shipRestoreByEntity.Remove(entityId, out var restore)) return;
            WorldsAdriftRebornGameServer.ShipInterest.CompleteRestoreDestination(
                peer, restore.HullEntityId);
        }
    }
}

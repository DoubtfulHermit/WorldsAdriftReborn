using Bossa.Travellers.Controls;
using Bossa.Travellers.Ship;
using Improbable;
using Improbable.Corelibrary.Math;
using Improbable.Math;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Walls;
using WorldsAdriftRebornGameServer.Networking.Wrapper;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Game.Persistence;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// PILOTED SHIP FLIGHT - the payoff feature: the player Mans the helm on the
    /// ship they built and FLIES it with the game's own controls.
    ///
    /// THE RETAIL WORKER THIS RECONSTRUCTS, in one sentence: on Man, set the
    /// pilot's 1109 PilotState.DrivingEntityId; the client's own
    /// ShipControlsBehaviour then reads WASD/throttle and writes 1111
    /// ShipControlInput every 0.05 s; this service consumes that input,
    /// integrates it into a pose (the pure <see cref="FlightIntegrator"/>), and
    /// publishes the hull's 1130 SSPPredictedMotionState control points - which
    /// the unmodified client replays via SSPDeadReckoningVisualizer ->
    /// PathFollower, the same LIVE-PROVEN path the ferry flies the static hull
    /// on. No client patch anywhere.
    ///
    /// OFF BY DEFAULT: WAREBORN_HELM_FLIGHT=1 arms it (the grant of the 1111/1112
    /// writers at player setup is gated on the same flag).
    ///
    /// WIRE SHAPE, per moving hull (the multiplayer-safety contract):
    /// <list type="bullet">
    /// <item>IN: 1111 from the ONE piloting player, at most 20 Hz (the client
    ///   diff-suppresses unchanged frames). Consumed here; NEVER relayed
    ///   (MirrorSendPolicy.IsRelayedToOtherPlayers).</item>
    /// <item>OUT: one 1130 control point per 0.24 s (the ferry's proven cadence),
    ///   plus the mounted parts' 190602 wakes and the hull's own 190602 timeline
    ///   advance riding the same tick - the exact per-point shape the ferry's
    ///   PublishWake already put in front of live clients. Idle/at-rest ships
    ///   drop to a slow keepalive (default 5 s).</item>
    /// </list>
    ///
    /// WHY THE HULL'S 190602 RIDES EVERY WAKE. Mounted parts are "~" followers:
    /// the client samples each child's local-transform interpolator at the
    /// PARENT hull's 190602 timestamp, and their follow-visualizer sleeps one
    /// second after the last transform change (ShipPartMotionPolicy). So a
    /// moving hull must (a) keep waking the children below the 1 s sleep and
    /// (b) advance its own 190602 stamp on the SAME timeline the mount commits
    /// used - which is why every stamp here draws from
    /// <see cref="PartMountService.NextTimelineSample"/> rather than a counter of
    /// its own (a lower stamp would be silently discarded and the parts would
    /// park mid-air while the hull flies off - the ferry's own "beams flew up,
    /// floor stayed" lesson). The built DECK panels are real Unity children and
    /// ride the hull's transform with no wake at all.
    /// </summary>
    internal sealed class ShipFlightService
    {
        /// <summary>Flies only when explicitly switched on. A bare OFF default.</summary>
        internal static readonly bool Enabled =
            Environment.GetEnvironmentVariable("WAREBORN_HELM_FLIGHT") == "1";

        /// <summary>
        /// Separates authoritative physics (50 Hz) from the stock 0.24 s 1130
        /// stream. Opt-in until live restart acceptance has been completed.
        /// </summary>
        internal static readonly bool FixedStepEnabled =
            Environment.GetEnvironmentVariable("WAREBORN_FLIGHT_FIXED_STEP") == "1";

        /// <summary>
        /// WAREBORN_FLIGHT_DRIVE_TARGET=helm points 1109 DrivingEntityId at the
        /// HELM entity instead of the hull. Default: the HULL - it is what
        /// ShipControlsBehaviour.UpdateVertical expects to find the
        /// ShipControlInputVisualizer on, and PilotVisualizer's SetInitialInput
        /// resolves through it. The helm option exists because the driven
        /// entity's GameObject is also where the client looks for the
        /// FullBodyIKTargets that pose the pilot at the wheel, and our mounted
        /// helm is NOT a Unity child of the hull ("~" follower) - so if the live
        /// client shows a working flight but a pilot who is not anchored to the
        /// wheel, this is the knob to try before touching code.
        /// </summary>
        private static readonly bool DriveTargetIsHelm =
            string.Equals(Environment.GetEnvironmentVariable("WAREBORN_FLIGHT_DRIVE_TARGET"), "helm",
                StringComparison.OrdinalIgnoreCase);

        /// <summary>How often the periodic [flight] stats line prints while any ship is live.</summary>
        private static readonly TimeSpan StatsInterval = TimeSpan.FromSeconds(5);

        private readonly IClock _clock;
        private readonly CadenceTimer _cadence;
        private readonly FlightTuning _tuning;
        private readonly WallFlightInfluence _wallFlightInfluence;
        private readonly RetailWorldBoundsPolicy _worldBounds;
        private readonly PilotSeats _seats = new PilotSeats();
        private readonly ShipDomainRegistry _domains;
        private readonly LocalDomainHost? _domainHost;
        private readonly HashSet<long> _activeHullIds = new();
        private readonly HashSet<long> _adminHeldHullIds = new();
        private readonly Dictionary<long, FixedFlightClock> _fixedClocks = new();
        private readonly Dictionary<long, FixedFlightStepBatch> _fixedClockTelemetry = new();
        private static readonly IReadOnlySet<ulong> NoDomainFrameSenders =
            new HashSet<ulong>();

        /// <summary>
        /// The flight tuning this service is ACTUALLY running with, for the
        /// operator snapshot. The console solves how far it may carry a hull
        /// between measurements from this acceleration, so it must read the live
        /// value rather than the compiled default: a deployment that raised
        /// WAREBORN_FLIGHT_ACCEL would otherwise be drawn with a window too
        /// generous for it.
        /// </summary>
        internal FlightTuning Tuning => _tuning;
        internal RetailWorldBoundsPolicy WorldBounds => _worldBounds;

        /// <summary>The authority token issued at the player's current helm handoff.</summary>
        private readonly Dictionary<long, ShipAuthorityToken> _authorityByPlayer = new();

        /// <summary>Latest merged 1111 input per PILOT entity (the update is a delta).</summary>
        private readonly Dictionary<long, FlightControlInput> _inputs = new Dictionary<long, FlightControlInput>();

        /// <summary>
        /// The helm each hull was last manned through, kept after dismount: the
        /// helm-feedback echo (wheel/levers) targets the helm entity too, and a
        /// dismounted helm still needs its one "wheel centres" echo.
        /// </summary>
        private readonly Dictionary<long, long> _helmByHull = new Dictionary<long, long>();

        /// <summary>
        /// The last input state ECHOED onto each hull/helm's ship-side 1111, so
        /// an unchanged input costs zero packets (the whole echo is
        /// event-on-change, capped by the tick cadence).
        /// </summary>
        private readonly Dictionary<long, FlightControlInput> _lastEchoed = new Dictionary<long, FlightControlInput>();

        /// <summary>
        /// When each resting hull's mounted parts were last woken. Moving domains
        /// publish every member with every root point; throttling those frames made
        /// sails and helms visibly remain behind and then snap onto the hull.
        /// </summary>
        private readonly Dictionary<long, TimeSpan> _lastWakeAt = new Dictionary<long, TimeSpan>();

        private static readonly TimeSpan PoseSaveInterval = TimeSpan.FromSeconds(2);
        private readonly Dictionary<long, TimeSpan> _nextPoseSaveAt = new Dictionary<long, TimeSpan>();
        private readonly Dictionary<long, long> _departingYardByHull = new Dictionary<long, long>();
        private readonly HashSet<long> _boundsInterveningHulls = new HashSet<long>();
        private readonly HashSet<long> _boundsHardClampedHulls = new HashSet<long>();
        private readonly HashSet<long> _boundsQuarantinedHulls = new HashSet<long>();

        /// <summary>
        /// Resting member heartbeat. A moving domain does not use this throttle:
        /// live acceptance on 2026-08-22 disproved the assumption that an awake
        /// "~" follower adds smoothness for free between root points. Mounted
        /// components visibly lagged one root frame and snapped on the next wake.
        /// </summary>
        private static readonly TimeSpan WakeInterval =
            TimeSpan.FromSeconds(ShipPartMotionPolicy.HeartbeatIntervalSeconds * 0.9);

        /// <summary>1111 packets consumed since the last stats line, for the rx-rate readout.</summary>
        private long _inputPacketsSinceStats;
        private TimeSpan _nextStatsAt;

        private sealed class FlightLatencyTrace
        {
            public long Sequence { get; init; }
            public long PlayerEntityId { get; init; }
            public long HullEntityId { get; init; }
            public float AxisYaw { get; init; }
            public TimeSpan ReceivedAt { get; init; }
            public DateTime ReceivedUtc { get; init; }
        }

        private long _nextLatencyTraceSequence;
        private readonly Dictionary<long, FlightLatencyTrace> _pendingLatencyByHull = new();

        public ShipFlightService(IClock clock, ShipDomainRegistry domains,
            LocalDomainHost? domainHost = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _domains = domains ?? throw new ArgumentNullException(nameof(domains));
            _domainHost = domainHost;
            _cadence = new CadenceTimer(TimeSpan.FromSeconds(ShipMotionPolicy.SendIntervalSeconds));
            _tuning = FlightTuning.FromEnvironment(Environment.GetEnvironmentVariable);
            _wallFlightInfluence = WallFlightInfluence.FromEnvironment(
                WallPolicy.EnabledFromEnvironment(), Environment.GetEnvironmentVariable);
            _worldBounds = RetailWorldBoundsPolicy.FromEnvironment(
                Environment.GetEnvironmentVariable);

            if (Enabled)
            {
                Console.WriteLine("[info] helm flight is ARMED (WAREBORN_HELM_FLIGHT=1): Man a mounted helm to fly"
                    + " its built ship. " + _tuning + "; drive target = "
                    + (DriveTargetIsHelm ? "HELM" : "HULL") + " (WAREBORN_FLIGHT_DRIVE_TARGET).");
                Console.WriteLine("[info] " + _wallFlightInfluence.Describe());
                Console.WriteLine("[info] retail flight world bounds are "
                    + (_worldBounds.Enabled ? "ON" : "OFF")
                    + " (WAREBORN_FLIGHT_WORLD_BOUNDS; edge "
                    + _worldBounds.EdgeLengthMetres.ToString("0.##",
                        System.Globalization.CultureInfo.InvariantCulture) + " m).");
                Console.WriteLine("[info] authoritative 50 Hz flight clock is "
                    + (FixedStepEnabled ? "ON" : "OFF")
                    + " (WAREBORN_FLIGHT_FIXED_STEP; 20 ms step, 25-step catch-up cap;"
                    + " 1130 remains 240 ms).");
            }
        }

        // ------------------------------------------------------------------
        // Man / dismount (called from InteractAgentState_Handler)
        // ------------------------------------------------------------------

        /// <summary>
        /// A 1211 InteractWithObject(target, Man) from <paramref name="playerEntityId"/>.
        /// Returns true when the target was a mounted helm this service owns (whatever
        /// the outcome), false when the target is not flight's business - so the
        /// caller can keep its own not-handled diagnostics precise.
        /// </summary>
        public bool OnManInteraction(ENetPeerHandle player, long playerEntityId, long targetEntityId, bool ownsPlayer)
        {
            if (!Enabled)
            {
                return false;
            }

            Crafting.MountedParts.Mount? mount = Crafting.MountedParts.MountFor(targetEntityId);
            if (!mount.HasValue)
            {
                // Not a mounted part. The static ship's bolted helm also carries Man,
                // but that hull belongs to the ferry/nudge probes - flying it here
                // would fight them, so flight deliberately does not answer for it.
                return false;
            }
            if (!string.Equals(mount.Value.ItemType, "helm", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[flight] Man on mounted part " + targetEntityId + " ('" + mount.Value.ItemType
                    + "') ignored: only a helm can be manned for flight.");
                return true;
            }
            if (!ownsPlayer)
            {
                // 1211 is the sender's own component; a Man for an entity the peer
                // does not own is a spoof or a bug, never a feature.
                Console.WriteLine("[warning] flight: Man on helm " + targetEntityId + " rejected: sender does not own"
                    + " entity " + playerEntityId + ".");
                return true;
            }

            if (!ShipInteractionEligibility.Allows(
                    player, targetEntityId, mount.Value, ownsPlayer,
                    Multiplayer.Helm.ManRadius, out double distanceMetres))
            {
                Console.WriteLine("[warning] flight: Man on helm " + targetEntityId
                    + " rejected for entity " + playerEntityId
                    + ": ownership/checkout/position was invalid, or distance "
                    + (double.IsFinite(distanceMetres)
                        ? distanceMetres.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " m"
                        : "was unavailable")
                    + " exceeded the recovered client completion envelope for hull "
                    + mount.Value.HullEntityId + ".");
                return true;
            }

            long hullEntityId = mount.Value.HullEntityId;
            ManOutcome outcome = _seats.TryMan(playerEntityId, targetEntityId, hullEntityId);
            switch (outcome)
            {
                case ManOutcome.StartPiloting:
                    StartPiloting(player, playerEntityId, targetEntityId, hullEntityId);
                    break;

                case ManOutcome.AlreadyPiloting:
                    // Live clients can publish duplicate Man deltas while the
                    // interaction transition is draining through a congested
                    // channel. Treating the second copy as a toggle produced four
                    // MANNED/DISMOUNTED cycles in one second on 2026-08-14 and made
                    // the helm appear impossible to enter. ReleaseInteraction is
                    // the unambiguous dismount operation.
                    Console.WriteLine("[flight] duplicate Man on helm " + targetEntityId + " by entity "
                        + playerEntityId + " ignored: already piloting hull " + hullEntityId + ".");
                    break;

                case ManOutcome.RejectedOccupied:
                    Console.WriteLine("[flight] Man on helm " + targetEntityId + " by entity " + playerEntityId
                        + " rejected: hull " + hullEntityId + " is already piloted by entity "
                        + _seats.PilotOf(hullEntityId)!.Value.PlayerEntityId + ".");
                    break;

                case ManOutcome.RejectedAlreadyPiloting:
                    Console.WriteLine("[flight] Man on helm " + targetEntityId + " by entity " + playerEntityId
                        + " rejected: they are already piloting hull "
                        + _seats.SeatOf(playerEntityId)!.Value.HullEntityId + ".");
                    break;
            }
            return true;
        }

        /// <summary>
        /// A 1211 ReleaseInteraction event - the client's own "let go of the
        /// exclusively-used object" signal (InteractAgentObserver
        /// .ReleaseInteractiveObject). Belt-and-braces beside the re-Man toggle:
        /// whichever the live client sends, the pilot gets off.
        /// </summary>
        public void OnReleaseInteraction(ENetPeerHandle player, long playerEntityId, long targetEntityId)
        {
            if (!Enabled)
            {
                return;
            }
            PilotSeats.Seat? seat = _seats.SeatOf(playerEntityId);
            // An INVALID target releases whatever seat the player holds: the measured
            // live exit is `verb Default on target -1` (a driving pilot is not aiming
            // at the helm, so the client has no target to name). A VALID target still
            // has to match the seat, so a release aimed at some other object cannot
            // eject a pilot.
            if (seat == null
                || (targetEntityId > 0
                    && targetEntityId != seat.Value.HelmEntityId
                    && targetEntityId != seat.Value.HullEntityId))
            {
                return;
            }
            _seats.Release(playerEntityId);
            StopPiloting(player, playerEntityId, seat.Value.HelmEntityId, seat.Value.HullEntityId, "released the interaction");
        }

        /// <summary>
        /// The pilot's peer is gone (ForgetPeer). No 1109 to push - there is nobody
        /// to push it to - but the seat frees and the ship settles to a stop instead
        /// of flying on with a ghost's throttle. This deliberately differs from a
        /// clean ReleaseInteraction, which leaves the physical throttle lever latched.
        /// </summary>
        public void OnPlayerGone(long playerEntityId)
        {
            _inputs.Remove(playerEntityId);
            PilotSeats.Seat? seat = _seats.Release(playerEntityId);
            if (seat != null && _domains.ByHull(seat.Value.HullEntityId) is ShipDomain domain)
            {
                if (_authorityByPlayer.Remove(playerEntityId, out ShipAuthorityToken authority))
                {
                    domain.ReleasePilot(authority, abandoned: true);
                }
                Console.WriteLine("[flight] pilot entity " + playerEntityId + " disconnected while piloting hull "
                    + seat.Value.HullEntityId + "; ship settles to rest at " + domain.Flight.State
                    + " (authority generation " + domain.Generation + ").");
            }
        }

        // ------------------------------------------------------------------
        // 1111 input (called from ShipControlInput_Handler)
        // ------------------------------------------------------------------

        /// <summary>
        /// One decoded 1111 delta from a player's own entity. Merged over the held
        /// input (an absent field means UNCHANGED, not zero - the client
        /// diff-suppresses) and applied to the session they pilot, if any. Cheap
        /// for non-pilots: one dictionary miss.
        /// </summary>
        public void OnControlInput(long playerEntityId, float? throttle, float? vertical,
            float? axisPitch, float? axisYaw, float? axisRoll)
        {
            if (!Enabled)
            {
                return;
            }

            _inputs.TryGetValue(playerEntityId, out FlightControlInput held);
            // A 1111 received after 1109 grants this player the helm is current
            // ship intent. Do not wait for a later neutral edge: the generated
            // client diff-suppresses unchanged zero, so that edge may not arrive
            // until unrelated mouse movement several seconds later. Pilot-seat
            // ownership and the ship-domain authority token below remain the
            // authorization boundary for applying the merged command.
            FlightControlInput merged = held.Merge(throttle, vertical, axisPitch, axisYaw, axisRoll);
            _inputs[playerEntityId] = merged;
            _inputPacketsSinceStats++;

            PilotSeats.Seat? seat = _seats.SeatOf(playerEntityId);
            if (seat != null && _domains.ByHull(seat.Value.HullEntityId) is ShipDomain domain
                && _authorityByPlayer.TryGetValue(playerEntityId, out ShipAuthorityToken authority))
            {
                bool yawEdge = System.Math.Abs(held.AxisYaw) <= 0.001f
                    && System.Math.Abs(merged.AxisYaw) > 0.001f;
                bool yawReversed = System.Math.Sign(held.AxisYaw) != System.Math.Sign(merged.AxisYaw)
                    && System.Math.Abs(held.AxisYaw) > 0.001f
                    && System.Math.Abs(merged.AxisYaw) > 0.001f;
                if (yawEdge || yawReversed)
                {
                    var trace = new FlightLatencyTrace
                    {
                        Sequence = ++_nextLatencyTraceSequence,
                        PlayerEntityId = playerEntityId,
                        HullEntityId = seat.Value.HullEntityId,
                        AxisYaw = merged.AxisYaw,
                        ReceivedAt = _clock.Elapsed,
                        ReceivedUtc = DateTime.UtcNow
                    };
                    _pendingLatencyByHull[trace.HullEntityId] = trace;
                    Console.WriteLine("[flight-latency] event=S" + trace.Sequence
                        + " phase=1111-receive utc=" + trace.ReceivedUtc.ToString("O")
                        + " elapsedMs=0 player=" + playerEntityId
                        + " hull=" + trace.HullEntityId
                        + " axisYaw=" + trace.AxisYaw.ToString("0.###",
                            System.Globalization.CultureInfo.InvariantCulture));
                }
                if (!domain.TrySetInput(authority, merged))
                {
                    Console.WriteLine("[flight] rejected stale control authority for entity " + playerEntityId
                        + " on hull " + seat.Value.HullEntityId + " (token generation "
                        + authority.Generation + ", current " + domain.Generation + ").");
                    return;
                }
                // A real control input begins departure. Zero/neutral packets while
                // taking the wheel do nothing, and the dock remains occupied until the
                // integrated hull actually clears the release volume.
                if (!merged.IsNeutral)
                {
                    long yard = Crafting.BuiltShips.ShipyardForHull(seat.Value.HullEntityId);
                    if (yard != 0)
                    {
                        // Motion begins the departure, but the yard stays occupied
                        // until the hull actually clears its wider release volume.
                        // That is the real "shipyard must be empty" build gate.
                        _departingYardByHull[seat.Value.HullEntityId] = yard;
                    }
                }
            }
        }

        /// <summary>
        /// The FLOWN pose of a hull, when a flight session owns it. The session is
        /// the single authority on where a flown hull is (WorldEntity.Position is
        /// immutable and still says "spawn"), so every OTHER producer of a hull
        /// 190602 - the part-mount commit's parent-timeline bump, the detach's
        /// world-pose reconstruction - must ask here first, or a part mounted on a
        /// flown ship stamps the hull visually back to its spawn point.
        /// </summary>
        public bool TryGetFlownPose(long hullEntityId, out FixedPointPosition position, out uint packedRotation)
        {
            if (Enabled && _activeHullIds.Contains(hullEntityId)
                && _domains.ByHull(hullEntityId) is ShipDomain domain)
            {
                FlightSession session = domain.Flight;
                position = FixedPointPosition.FromMetres(session.State.X, session.State.Y, session.State.Z);
                packedRotation = FlightIntegrator.PackedRotation(session.State);
                return true;
            }
            position = default;
            packedRotation = Multiplayer.Placement.Quaternion32Packing.Identity;
            return false;
        }

        internal bool IsPiloted(long hullEntityId) => _seats.PilotOf(hullEntityId).HasValue;

        internal bool IsActive(long hullEntityId) => _activeHullIds.Contains(hullEntityId);

        internal long? PilotEntityOf(long hullEntityId) =>
            _seats.PilotOf(hullEntityId)?.PlayerEntityId;

        /// <summary>
        /// The single hull-level combustion command consumed by fuel. It reads the
        /// flight session's physical lever, not the current pilot's packet mirror, so
        /// a clean dismount keeps burning while latched unmanned thrust continues.
        /// Disconnect/abandon already neutralises the session before this is read.
        /// </summary>
        internal HullPropulsionDemand PropulsionDemandFor(long hullEntityId)
        {
            ShipDomain? domain = _domains.ByHull(hullEntityId);
            return domain == null
                ? HullPropulsionDemand.None
                : new HullPropulsionDemand(domain.Flight.Input.Throttle, CountEngines(hullEntityId));
        }

        internal bool IsPilotOf(long playerEntityId, long hullEntityId)
        {
            PilotSeats.Seat? seat = _seats.SeatOf(playerEntityId);
            return seat.HasValue && seat.Value.HullEntityId == hullEntityId;
        }

        /// <summary>
        /// Safely recalls an uncrewed built hull to an operator-selected player's
        /// current world position. The whole domain moves as one frame and the new
        /// pose is persisted before the command reports success.
        /// </summary>
        internal bool TryAdminRecall(long hullEntityId, FixedPointPosition destination,
            out string error)
        {
            error = string.Empty;
            if (!Crafting.BuiltShips.IsBuiltHull(hullEntityId))
            {
                error = "Hull " + hullEntityId + " is not a live built ship.";
                return false;
            }
            if (IsPiloted(hullEntityId) || WorldsAdriftRebornGameServer.Aboard.AnyoneAboard(hullEntityId))
            {
                error = "Hull " + hullEntityId + " is piloted or occupied; recall refused.";
                return false;
            }

            ShipDomain? domain = _domains.ByHull(hullEntityId);
            if (domain == null)
            {
                error = "Hull " + hullEntityId + " has no simulation domain.";
                return false;
            }

            Crafting.BuiltShipSpawner.UndockDepartingHull(hullEntityId);
            RefreshDomainMembership(domain);
            double yaw = domain.Flight.State.YawRadians;
            domain.Flight.DockAt(destination.MetresX, destination.MetresY,
                destination.MetresZ, yaw);
            _adminHeldHullIds.Remove(hullEntityId);
            _activeHullIds.Add(hullEntityId);

            // Move the authoritative registry/persistence BEFORE asking checkout
            // to rebuild the domain. Its fresh 190602 and one-point 1130 seeds
            // must both describe the recalled pose.
            PersistPoseNow(hullEntityId, domain.Flight.State);
            int refreshingPeers = WorldsAdriftRebornGameServer.ShipInterest
                .RequestRecallRefresh(hullEntityId);

            FlightEmit point = domain.Flight.PrimePlayback(
                ShipHull.NowMillisecondsSinceEpoch(), ShipMotionPolicy.SendIntervalSeconds);
            uint rotation = point.PackedRotation;
            ShipPartWakeBundle wakes = ShipPartMotionService.BuildWakeBundle(
                hullEntityId, destination, rotation);
            ShipPublisher.BroadcastDomainMotion(
                hullEntityId, destination, domain.Generation.Value,
                new ShipDomainComponentUpdate(hullEntityId, ShipMotionPolicy.ComponentId,
                    ShipPublisher.BuildUpdate(point.Spec, rotation)),
                wakes.Root, wakes.Members);
            Console.WriteLine("[admin-world] hull " + hullEntityId
                + " recall scheduled a clean domain reconstruction for "
                + refreshingPeers + " peer(s).");
            return true;
        }

        /// <summary>
        /// Stages an unoccupied, settled ship while the server has no connected
        /// players. The owner's already-nearby durable logout position is carried
        /// by the same offset, and the hull is held outside the flight tick until
        /// an explicit release. This is deliberately narrower than an online
        /// teleport: checkout cannot make a hull move plus player teleport atomic.
        /// </summary>
        internal bool TryAdminStageOffline(long hullEntityId, FixedPointPosition destination,
            out string message)
        {
            if (WorldsAdriftRebornGameServer.Players.All().Any())
            {
                message = "Offline staging refused while any player is connected.";
                return false;
            }
            if (!Crafting.BuiltShips.IsBuiltHull(hullEntityId))
            {
                message = "Hull " + hullEntityId + " is not a live built ship.";
                return false;
            }
            if (IsPiloted(hullEntityId) || WorldsAdriftRebornGameServer.Aboard.AnyoneAboard(hullEntityId))
            {
                message = "Hull " + hullEntityId + " is piloted or occupied; staging refused.";
                return false;
            }
            ShipDomain? domain = _domains.ByHull(hullEntityId);
            if (domain == null || !domain.Flight.State.IsAtRest)
            {
                message = "Hull " + hullEntityId + " has no settled simulation domain.";
                return false;
            }
            if (!Guid.TryParse(Crafting.BuiltShips.OwnerFor(hullEntityId), out Guid ownerUid))
            {
                message = "Hull " + hullEntityId + " has no valid owner character uid.";
                return false;
            }
            FixedPointPosition? stored = PlayerPositionService.StoredFor(ownerUid);
            FixedPointPosition oldHull = FixedPointPosition.FromMetres(
                domain.Flight.State.X, domain.Flight.State.Y, domain.Flight.State.Z);
            if (!stored.HasValue || !AdminShipStagePolicy.TryCarryLogoutPosition(
                    oldHull, stored.Value, destination, out FixedPointPosition carriedPlayer))
            {
                message = "Owner logout position is missing or is more than "
                    + AdminShipStagePolicy.MaximumOwnerDistanceMetres.ToString("0")
                    + " m from hull " + hullEntityId + "; staging refused.";
                return false;
            }

            double yaw = domain.Flight.State.YawRadians;
            Crafting.BuiltShipSpawner.UndockDepartingHull(hullEntityId);
            domain.Flight.DockAt(destination.MetresX, destination.MetresY,
                destination.MetresZ, yaw);
            _adminHeldHullIds.Add(hullEntityId);
            _activeHullIds.Remove(hullEntityId);
            PersistPoseNow(hullEntityId, domain.Flight.State);
            if (!PlayerPositionService.Record(ownerUid, carriedPlayer))
            {
                domain.Flight.DockAt(oldHull.MetresX, oldHull.MetresY, oldHull.MetresZ, yaw);
                PersistPoseNow(hullEntityId, domain.Flight.State);
                _adminHeldHullIds.Remove(hullEntityId);
                PlayerPositionService.Record(ownerUid, stored.Value);
                message = "Could not persist the owner's carried logout position; hull rollback completed.";
                return false;
            }

            WorldsAdriftRebornGameServer.ShipInterest.RequestRecallRefresh(hullEntityId);
            message = "Staged hull " + hullEntityId + " at ("
                + destination.MetresX.ToString("0.###") + ", "
                + destination.MetresY.ToString("0.###") + ", "
                + destination.MetresZ.ToString("0.###")
                + ") m and carried its offline owner's deck-relative logout position; flight is held.";
            return true;
        }

        internal bool TryAdminReleaseStaged(long hullEntityId, out string message)
        {
            if (!_adminHeldHullIds.Contains(hullEntityId))
            {
                message = "Hull " + hullEntityId + " is not held by offline staging.";
                return false;
            }
            if (IsPiloted(hullEntityId))
            {
                message = "Hull " + hullEntityId + " has a pilot; staged release refused.";
                return false;
            }
            ShipDomain? domain = _domains.ByHull(hullEntityId);
            if (domain == null)
            {
                message = "Hull " + hullEntityId + " has no simulation domain.";
                return false;
            }

            RefreshDomainMembership(domain);
            _adminHeldHullIds.Remove(hullEntityId);
            _activeHullIds.Add(hullEntityId);
            FlightEmit point = domain.Flight.PrimePlayback(
                ShipHull.NowMillisecondsSinceEpoch(), ShipMotionPolicy.SendIntervalSeconds);
            FixedPointPosition position = FixedPointPosition.FromMetres(
                domain.Flight.State.X, domain.Flight.State.Y, domain.Flight.State.Z);
            ShipPartWakeBundle wakes = ShipPartMotionService.BuildWakeBundle(
                hullEntityId, position, point.PackedRotation);
            ShipPublisher.BroadcastDomainMotion(
                hullEntityId, position, domain.Generation.Value,
                new ShipDomainComponentUpdate(hullEntityId, ShipMotionPolicy.ComponentId,
                    ShipPublisher.BuildUpdate(point.Spec, point.PackedRotation)),
                wakes.Root, wakes.Members);
            message = "Released staged hull " + hullEntityId + " into authoritative flight.";
            return true;
        }

        /// <summary>
        /// Stops an unpiloted runaway at its exact authoritative pose. This is
        /// intentionally separate from recall: it neither moves the hull nor
        /// changes its checkout, and it is safe with passengers aboard.
        /// </summary>
        internal bool TryAdminStop(long hullEntityId, out string message)
        {
            if (!Crafting.BuiltShips.IsBuiltHull(hullEntityId))
            {
                message = "Hull " + hullEntityId + " is not a live built ship.";
                return false;
            }
            if (IsPiloted(hullEntityId))
            {
                message = "Hull " + hullEntityId
                    + " still has a pilot; release the helm first or ask the pilot to stop.";
                return false;
            }
            ShipDomain? domain = _domains.ByHull(hullEntityId);
            if (domain == null)
            {
                message = "Hull " + hullEntityId + " has no simulation domain.";
                return false;
            }

            RefreshDomainMembership(domain);
            domain.Flight.EmergencyStop();
            _adminHeldHullIds.Remove(hullEntityId);
            _activeHullIds.Add(hullEntityId);
            FlightEmit point = domain.Flight.PrimePlayback(
                ShipHull.NowMillisecondsSinceEpoch(), ShipMotionPolicy.SendIntervalSeconds);
            FixedPointPosition position = FixedPointPosition.FromMetres(
                domain.Flight.State.X, domain.Flight.State.Y, domain.Flight.State.Z);
            ShipPartWakeBundle wakes = ShipPartMotionService.BuildWakeBundle(
                hullEntityId, position, point.PackedRotation);
            ShipPublisher.BroadcastDomainMotion(
                hullEntityId, position, domain.Generation.Value,
                new ShipDomainComponentUpdate(hullEntityId, ShipMotionPolicy.ComponentId,
                    ShipPublisher.BuildUpdate(point.Spec, point.PackedRotation)),
                wakes.Root, wakes.Members);
            PersistPoseNow(hullEntityId, domain.Flight.State);
            message = "Stopped hull " + hullEntityId + " at its current authoritative pose.";
            return true;
        }

        /// <summary>
        /// Clears a stuck exclusive helm owner. Unlike a normal voluntary
        /// dismount this is an operator recovery, so stale throttle is abandoned
        /// and neutralized instead of remaining latched.
        /// </summary>
        internal bool TryAdminReleaseHelm(long hullEntityId, out string message)
        {
            if (!Crafting.BuiltShips.IsBuiltHull(hullEntityId))
            {
                message = "Hull " + hullEntityId + " is not a live built ship.";
                return false;
            }
            PilotSeats.Seat? seat = _seats.PilotOf(hullEntityId);
            if (!seat.HasValue)
            {
                message = "Hull " + hullEntityId + " already has no helm owner.";
                return true;
            }

            long playerEntityId = seat.Value.PlayerEntityId;
            foreach ((ulong peerId, long entityId) in WorldsAdriftRebornGameServer.Players.All())
            {
                if (entityId != playerEntityId) continue;
                ENetPeerHandle? peer = PeerIdentity.Instance.Resolve(new IntPtr((long)peerId));
                if (peer == null) break;
                _seats.Release(playerEntityId);
                StopPiloting(peer, playerEntityId, seat.Value.HelmEntityId,
                    hullEntityId, "operator released a stuck helm", abandoned: true);
                message = "Released player entity " + playerEntityId + " from hull "
                    + hullEntityId + " and neutralized its controls.";
                return true;
            }

            // A seat whose peer disappeared between stats and command execution
            // is still recoverable through the normal disconnect cleanup path.
            OnPlayerGone(playerEntityId);
            message = "Cleared disconnected pilot entity " + playerEntityId
                + " from hull " + hullEntityId + " and neutralized its controls.";
            return true;
        }

        internal bool TryGetDomainGeneration(long hullEntityId, out long generation)
        {
            ShipDomain? domain = _domains.ByHull(hullEntityId);
            generation = (domain?.Generation ?? AuthorityGeneration.Initial).Value;
            return domain != null;
        }

        internal long DomainGenerationFor(long hullEntityId) =>
            _domains.GenerationFor(hullEntityId).Value;

        internal bool IsFlightDomainActive(long hullEntityId)
        {
            ShipDomain? domain = _domains.ByHull(hullEntityId);
            return domain != null && _activeHullIds.Contains(hullEntityId);
        }

        internal void RegisterHull(long hullEntityId, int? persistentIndex,
            FixedPointPosition position, double yawRadians,
            Multiplayer.Persistence.DurableShipFlightSnapshot? durable = null)
        {
            ShipDomain domain = _domains.GetOrAdd(hullEntityId, () =>
            {
                if (FixedStepEnabled && durable != null
                    && durable.TryRead(out FlightState restoredState, out FlightControlInput _))
                {
                    if (durable.WasDocked)
                    {
                        restoredState = FlightState.AtRestAt(
                            position.MetresX, position.MetresY, position.MetresZ, yawRadians);
                    }
                    Console.WriteLine("[info] flight: restored durable v" + durable.Version
                        + " state for hull " + hullEntityId + " at " + restoredState
                        + "; invalidated pre-restart pilot/input and advanced authority generation "
                        + durable.AuthorityGeneration + " -> " + (durable.AuthorityGeneration + 1) + ".");
                    return ShipDomain.RestoreAfterProcessRestart(
                        hullEntityId, persistentIndex,
                        new AuthorityGeneration(durable.AuthorityGeneration),
                        new FlightSession(restoredState));
                }
                if (FixedStepEnabled && durable != null)
                {
                    Console.WriteLine("[warning] flight: ignored invalid/unsupported durable snapshot for hull "
                        + hullEntityId + "; using legacy pose.");
                }
                return new ShipDomain(hullEntityId, persistentIndex,
                    new FlightSession(FlightState.AtRestAt(
                        position.MetresX, position.MetresY, position.MetresZ, yawRadians)));
            });
            RefreshDomainMembership(domain);
            if (!domain.Flight.State.IsAtRest)
            {
                // A durable moving ship resumes coasting under server authority;
                // it does not wait for a new helm interaction to wake the service.
                _activeHullIds.Add(hullEntityId);
            }
            if (_domainHost != null && _domainHost.ById(domain.Id) == null)
                _domainHost.Register(domain);
            else if (_domainHost != null)
                _domainHost.Synchronize(domain);
        }

        /// <summary>
        /// A player deliberately unfurled canvas on this hull. Unlike a direct
        /// <see cref="FlightSession.WakeForCanvas"/> call, this service boundary also
        /// activates a boot-restored/never-manned domain so the heartbeat can consume
        /// the wake edge. If the hull is still dock-linked, sail motion is a real
        /// departure and must use the same release-volume lifecycle as helm input.
        /// </summary>
        internal bool WakeFromCanvasInteraction(long hullEntityId)
        {
            if (!Enabled || !Crafting.BuiltShips.IsBuiltHull(hullEntityId))
            {
                return false;
            }

            ShipDomain? domain = _domains.ByHull(hullEntityId);
            if (domain == null)
            {
                FixedPointPosition seed = WorldsAdriftRebornGameServer.WorldEntities
                    .TransformSeedFor(hullEntityId);
                double seedYaw = Multiplayer.Ship.ShipyardDockingPolicy.YawFromPacked(
                    WorldsAdriftRebornGameServer.WorldEntities.RotationSeedFor(hullEntityId));
                domain = _domains.GetOrAdd(hullEntityId, () =>
                    new ShipDomain(hullEntityId,
                        Crafting.BuiltShips.PersistentIndexFor(hullEntityId),
                        new FlightSession(FlightState.AtRestAt(
                            seed.MetresX, seed.MetresY, seed.MetresZ, seedYaw))));
            }

            RefreshDomainMembership(domain);
            _activeHullIds.Add(hullEntityId);
            domain.Flight.WakeForCanvas();

            long yardEntityId = Crafting.BuiltShips.ShipyardForHull(hullEntityId);
            if (yardEntityId != 0)
            {
                _departingYardByHull[hullEntityId] = yardEntityId;
            }
            return true;
        }

        /// <summary>Refreshes host ownership after a mount or detach outside the flight tick.</summary>
        internal void RefreshDomainOwnership(long hullEntityId)
        {
            if (_domains.ByHull(hullEntityId) is ShipDomain domain)
                RefreshDomainMembership(domain);
        }

        /// <summary>Forgets every session-side trace of a hull after authoritative salvage.</summary>
        internal void RetireHull(long hullEntityId)
        {
            PilotSeats.Seat? seat = _seats.PilotOf(hullEntityId);
            if (seat.HasValue)
            {
                _seats.Release(seat.Value.PlayerEntityId);
                _inputs.Remove(seat.Value.PlayerEntityId);
                _authorityByPlayer.Remove(seat.Value.PlayerEntityId);
            }
            if (_domainHost != null)
                _domainHost.RemoveDomain(SimulationDomainId.ForShip(hullEntityId));
            _domains.Remove(hullEntityId);
            _activeHullIds.Remove(hullEntityId);
            _adminHeldHullIds.Remove(hullEntityId);
            _helmByHull.Remove(hullEntityId);
            _lastEchoed.Remove(hullEntityId);
            _lastWakeAt.Remove(hullEntityId);
            _nextPoseSaveAt.Remove(hullEntityId);
            _departingYardByHull.Remove(hullEntityId);
            _boundsInterveningHulls.Remove(hullEntityId);
            _boundsHardClampedHulls.Remove(hullEntityId);
            _boundsQuarantinedHulls.Remove(hullEntityId);
            _fixedClocks.Remove(hullEntityId);
            _fixedClockTelemetry.Remove(hullEntityId);
            ShipPublisher.RetireDomain(hullEntityId);
        }

        // ------------------------------------------------------------------
        // The publisher heartbeat
        // ------------------------------------------------------------------

        /// <summary>
        /// One call per main-loop turn. The optional fixed clock consumes elapsed
        /// 20 ms physics steps on this cadence; the independent stock timer only
        /// decides when a 1130 control point may be published.
        /// </summary>
        public IReadOnlySet<ulong> Tick()
        {
            if (!Enabled || _activeHullIds.Count == 0)
            {
                return NoDomainFrameSenders;
            }
            bool publicationDue = _cadence.Due(_clock.Elapsed);
            if (!FixedStepEnabled && !publicationDue)
            {
                return NoDomainFrameSenders;
            }

            var domainFrameSenders = new HashSet<ulong>();
            long nowMs = ShipHull.NowMillisecondsSinceEpoch();
            foreach (long hullEntityId in _activeHullIds.ToArray())
            {
                if (_adminHeldHullIds.Contains(hullEntityId)) continue;
                ShipDomain? domain = _domains.ByHull(hullEntityId);
                if (domain == null) continue;
                FlightSession session = domain.Flight;

                FixedFlightStepBatch fixedBatch = default;
                if (FixedStepEnabled)
                {
                    if (!_fixedClocks.TryGetValue(hullEntityId, out FixedFlightClock? fixedClock))
                    {
                        fixedClock = new FixedFlightClock();
                        _fixedClocks[hullEntityId] = fixedClock;
                    }
                    fixedBatch = fixedClock.Advance(_clock.Elapsed);
                    _fixedClockTelemetry[hullEntityId] = fixedBatch;
                    if (fixedBatch.Steps == 0 && !publicationDue)
                    {
                        continue;
                    }
                }

                // Membership, docking capture and helm echo remain publication-
                // paced. They do not affect integration and need not turn a 50 Hz
                // clock into a 50 Hz world scan.
                if (publicationDue)
                {
                    RefreshDomainMembership(domain);
                    TryCaptureAtEmptyShipyard(hullEntityId, session);
                    EchoHelmFeedback(hullEntityId, session);
                }

                // HELM FEEDBACK first, motion second: the echo is a pure
                // input-changed compare (usually a no-op) and runs even on
                // no-emit ticks, so a wheel wiggle inside the deadzone still
                // animates the helm of a parked ship.
                int unfurledSails = WorldsAdriftRebornGameServer.Sails.UnfurledCountFor(hullEntityId);
                // HOW HEAVY THIS PARTICULAR SHIP IS. A hull built from cedar handles
                // like a skiff; one built from tungsten wallows. 1.0 - no change at
                // all from the pre-materials behaviour - for a ship of the reference
                // mass, which is what every legacy birch-and-iron hull lands near.
                double agility = AgilityScaleFor(hullEntityId);
                FlightEmit emit;
                if (FixedStepEnabled)
                {
                    double lastStepTime = Math.Floor(
                        _clock.Elapsed.TotalSeconds / FixedFlightClock.StepSeconds)
                        * FixedFlightClock.StepSeconds;
                    double firstStepTime = lastStepTime
                        - Math.Max(0, fixedBatch.Steps - 1) * FixedFlightClock.StepSeconds;
                    emit = session.AdvanceFixed(
                        nowMs, ShipMotionPolicy.SendIntervalSeconds,
                        fixedBatch.Steps, firstStepTime, _tuning, unfurledSails, agility,
                        PropulsionFor(hullEntityId, unfurledSails), _wallFlightInfluence.Segments,
                        _worldBounds, emitDue: publicationDue);
                    if (fixedBatch.UnderPressure)
                    {
                        Console.WriteLine("[warning] flight fixed-clock pressure: hull " + hullEntityId
                            + " ran " + fixedBatch.Steps + " catch-up step(s), dropped "
                            + fixedBatch.DroppedSteps + " step(s); total dropped "
                            + fixedBatch.TotalDroppedSteps + " across " + fixedBatch.PressureEvents
                            + " pressure event(s).");
                    }
                }
                else
                {
                    emit = session.Advance(
                        nowMs, ShipMotionPolicy.SendIntervalSeconds, _tuning, unfurledSails, agility,
                        PropulsionFor(hullEntityId, unfurledSails), _wallFlightInfluence.Segments,
                        _worldBounds);
                }
                ObserveWorldBounds(hullEntityId, session.State,
                    session.LastWorldBoundsTelemetry);
                CompleteDepartureIfOutside(hullEntityId, session.State);
                PersistPoseWhenDue(hullEntityId, domain);
                if (!emit.Emit)
                {
                    continue;
                }

                _pendingLatencyByHull.TryGetValue(hullEntityId, out FlightLatencyTrace? latencyTrace);
                if (latencyTrace != null)
                {
                    Console.WriteLine("[flight-latency] event=S" + latencyTrace.Sequence
                        + " phase=1130-emit utc=" + DateTime.UtcNow.ToString("O")
                        + " elapsedMs=" + (_clock.Elapsed - latencyTrace.ReceivedAt).TotalMilliseconds.ToString("0.0",
                            System.Globalization.CultureInfo.InvariantCulture)
                        + " player=" + latencyTrace.PlayerEntityId
                        + " hull=" + hullEntityId
                        + " inputAxisYaw=" + latencyTrace.AxisYaw.ToString("0.###",
                            System.Globalization.CultureInfo.InvariantCulture)
                        + " stateYawRad=" + session.State.YawRadians.ToString("0.######",
                            System.Globalization.CultureInfo.InvariantCulture));
                }

                FixedPointPosition hullPosition = FixedPointPosition.FromMetres(
                    emit.Spec.X, emit.Spec.Y, emit.Spec.Z);
                // A moving ship is one replication domain: root and mounted members
                // must publish in the SAME frame. The former every-other-point
                // throttle was visible in live acceptance as sails/helm remaining
                // behind, then snapping forward on their next 190602 wake. Only a
                // fully resting domain uses the cheaper heartbeat.
                bool wakeDue = !session.State.IsAtRest
                    || !_lastWakeAt.TryGetValue(hullEntityId, out TimeSpan lastWake)
                    || _clock.Elapsed - lastWake >= WakeInterval;
                ShipDomainComponentUpdate? hullWake = null;
                IReadOnlyList<ShipDomainComponentUpdate> memberWakes = Array.Empty<ShipDomainComponentUpdate>();
                if (wakeDue)
                {
                    _lastWakeAt[hullEntityId] = _clock.Elapsed;
                    ShipPartWakeBundle wakes = BuildHullAndPartWakes(hullEntityId, emit);
                    hullWake = wakes.Root;
                    memberWakes = wakes.Members;
                }

                // ONE domain frame per flight tick. On wake ticks this is ordered
                // hull 1130 -> hull 190602 -> member 190602; between wakes it is the
                // same root stream with an empty member set. Relevance is evaluated
                // once for the whole ship by ShipPublisher.
                ShipDomainDeliveryResult delivery = ShipPublisher.BroadcastDomainMotion(
                    hullEntityId, hullPosition, (long)domain.Generation.Value,
                    new ShipDomainComponentUpdate(
                        hullEntityId, ShipMotionPolicy.ComponentId,
                        ShipPublisher.BuildUpdate(emit.Spec, emit.PackedRotation)),
                    hullWake,
                    memberWakes);
                if (latencyTrace != null)
                {
                    Console.WriteLine("[flight-latency] event=S" + latencyTrace.Sequence
                        + " phase=1130-send utc=" + DateTime.UtcNow.ToString("O")
                        + " elapsedMs=" + (_clock.Elapsed - latencyTrace.ReceivedAt).TotalMilliseconds.ToString("0.0",
                            System.Globalization.CultureInfo.InvariantCulture)
                        + " player=" + latencyTrace.PlayerEntityId
                        + " hull=" + hullEntityId
                        + " recipients=" + delivery.RootDeliveredPeerIds.Count);
                    _pendingLatencyByHull.Remove(hullEntityId);
                }
                if (delivery.Stamp.HullEntityId == hullEntityId
                    && delivery.RootDeliveredPeerIds.Count > 0)
                {
                    // The normal avatar relay remains 20 Hz. This set only forces
                    // the latest aboard sample to follow the hull frame on THIS
                    // loop turn when the independent 20 Hz cadence is not due.
                    foreach (ulong aboardPeerId in domain.AboardPeerIds)
                    {
                        if (delivery.RootDeliveredPeerIds.Contains(aboardPeerId))
                            domainFrameSenders.Add(aboardPeerId);
                    }
                }
            }

            if (_clock.Elapsed >= _nextStatsAt)
            {
                _nextStatsAt = _clock.Elapsed + StatsInterval;
                foreach (long hullEntityId in _activeHullIds.ToArray())
                {
                    ShipDomain? domain = _domains.ByHull(hullEntityId);
                    if (domain == null) continue;
                    FlightSession session = domain.Flight;
                    if (session.IsManned || !session.State.IsAtRest)
                    {
                        Console.WriteLine("[flight] hull " + hullEntityId + ": " + session.State
                            + (session.IsManned
                                ? " piloted by entity " + _seats.PilotOf(hullEntityId)!.Value.PlayerEntityId
                                    + ", input " + session.Input
                                : (session.Input.Throttle != 0f
                                    ? " cruising unmanned on latched throttle " + session.Input.Throttle.ToString("0.##")
                                    : " settling"))
                            + ", unfurled sails "
                            + WorldsAdriftRebornGameServer.Sails.UnfurledCountFor(hullEntityId)
                            + " (propulsion x"
                            + _tuning.SailPropulsionScale(
                                WorldsAdriftRebornGameServer.Sails.UnfurledCountFor(hullEntityId)).ToString("0.##")
                            + "); 1111 rx " + _inputPacketsSinceStats + " in last "
                            + StatsInterval.TotalSeconds.ToString("0") + " s.");
                        _inputPacketsSinceStats = 0;
                    }
                }
            }
            return domainFrameSenders;
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        private void StartPiloting(ENetPeerHandle player, long playerEntityId, long helmEntityId, long hullEntityId)
        {
            ShipDomain domain = _domains.GetOrAdd(hullEntityId, () =>
            {
                // First manning this boot: the session starts from the hull's
                // registered seed pose. WorldEntity.Position is immutable, so from
                // here on the SESSION is the authority on where this hull is.
                FixedPointPosition seed = WorldsAdriftRebornGameServer.WorldEntities.TransformSeedFor(hullEntityId);
                uint seedRotation = WorldsAdriftRebornGameServer.WorldEntities.RotationSeedFor(hullEntityId);
                double seedYaw = Multiplayer.Ship.ShipyardDockingPolicy.YawFromPacked(seedRotation);
                return new ShipDomain(hullEntityId, Crafting.BuiltShips.PersistentIndexFor(hullEntityId),
                    new FlightSession(FlightState.AtRestAt(
                        seed.MetresX, seed.MetresY, seed.MetresZ, seedYaw)));
            });
            RefreshDomainMembership(domain);
            _activeHullIds.Add(hullEntityId);
            FlightSession session = domain.Flight;

            ShipAuthorityToken authority = domain.AcquirePilot(playerEntityId, helmEntityId);
            _authorityByPlayer[playerEntityId] = authority;
            // Seed the delta-merge ledger from the ship's actual lever state.
            // ShipControlInput updates omit unchanged fields; a re-manning client
            // initialized from the hull's echoed 1111 may therefore send no
            // throttle field at all. Starting this ledger at neutral would turn an
            // unrelated steering delta into an accidental throttle reset.
            _inputs[playerEntityId] = session.Input;
            _helmByHull[hullEntityId] = helmEntityId;

            // Wake the hull's halted PathFollower at the unchanged authoritative
            // pose BEFORE 1109 lets the client send steering. Otherwise a fast
            // first input can be the wake point, and its small yaw delta takes the
            // client's five-second slow spline-correction path.
            FlightEmit prime = session.PrimePlayback(
                ShipHull.NowMillisecondsSinceEpoch(), ShipMotionPolicy.SendIntervalSeconds);
            FixedPointPosition primePosition = FixedPointPosition.FromMetres(
                prime.Spec.X, prime.Spec.Y, prime.Spec.Z);
            ShipPublisher.BroadcastDomainMotion(
                hullEntityId, primePosition, domain.Generation.Value,
                new ShipDomainComponentUpdate(hullEntityId, ShipMotionPolicy.ComponentId,
                    ShipPublisher.BuildUpdate(prime.Spec, prime.PackedRotation)),
                rootAuxiliary: null,
                members: Array.Empty<ShipDomainComponentUpdate>());

            long driveTarget = DriveTargetIsHelm ? helmEntityId : hullEntityId;
            PilotState.Update update = new PilotState.Update()
                .SetDrivingEntityId(new EntityId(driveTarget))
                .SetControlEntityId(new EntityId(helmEntityId))
                .SetControlType(ControlVehicleType.Ship)
                .AddStartPiloting(default(StartPiloting));
            bool pushed = PushPilotState(player, playerEntityId, update);

            Console.WriteLine("[flight] entity " + playerEntityId + " MANNED helm " + helmEntityId + " of hull "
                + hullEntityId + " at " + session.State + "; 1109 driving=" + driveTarget
                + (DriveTargetIsHelm ? " (helm)" : " (hull)") + "; domain " + domain.Id
                + " generation " + domain.Generation + (pushed ? " pushed." : " PUSH FAILED."));

            LogHullShapeOnMan(hullEntityId);
        }

        /// <summary>
        /// Prints the hull's measured shape THE MOMENT A PILOT TAKES THE HELM, and
        /// escalates to a warning when the hull's beam exceeds its keel.
        ///
        /// WHY HERE AS WELL AS AT SPAWN. The spawn line is emitted once, at build or
        /// at boot restore, and by the time the player says "it goes sideways" it is
        /// thousands of lines back - or in a previous boot's log entirely, because a
        /// restored ship is spawned before anyone connects. Manning the helm is the
        /// exact instant the complaint is generated, so the explanation belongs on
        /// the same timestamp. Same sentence as the spawn line
        /// (<see cref="ShipHullMetrics.WideHullAdvice"/>), so two logs cannot tell
        /// two stories.
        ///
        /// Never throws and never blocks the man: a hull with no registered bytes
        /// (the static test hull) simply says nothing.
        /// </summary>
        private static void LogHullShapeOnMan(long hullEntityId)
        {
            try
            {
                byte[]? hullBytes = Crafting.BuiltShips.HullBytesFor(hullEntityId);
                if (hullBytes == null
                    || !Multiplayer.Ship.ShipPlanModel.TryDecode(hullBytes, out var model, out _)
                    || model == null)
                {
                    return;
                }

                var metrics = Multiplayer.Ship.ShipHullMetrics.Measure(model);
                string? advice = metrics.WideHullAdvice();
                if (advice == null)
                {
                    Console.WriteLine("[flight] hull " + hullEntityId + " geometry - " + metrics.Describe());
                    return;
                }

                Console.WriteLine("[warn] flight: hull " + hullEntityId + " geometry - " + metrics.Describe());
                Console.WriteLine("[warn] flight: " + advice);
            }
            catch (Exception e)
            {
                Console.WriteLine("[warn] flight: could not measure hull " + hullEntityId
                    + " geometry on man: " + e.Message);
            }
        }

        private void StopPiloting(ENetPeerHandle player, long playerEntityId, long helmEntityId,
            long hullEntityId, string why, bool abandoned = false)
        {
            FlightSession? session = null;
            if (_domains.ByHull(hullEntityId) is ShipDomain domain)
            {
                session = domain.Flight;
                if (_authorityByPlayer.Remove(playerEntityId, out ShipAuthorityToken authority))
                {
                    domain.ReleasePilot(authority, abandoned);
                }
                PersistPoseNow(hullEntityId, session.State);
                // Publish the released transient axes and retained physical lever
                // immediately. Waiting for the next 0.24 s flight tick leaves a
                // small window where a quick re-man reads the old full control
                // state (including steering) from the hull visualizer.
                EchoHelmFeedback(hullEntityId, session);
            }
            _inputs.Remove(playerEntityId);

            PilotState.Update update = new PilotState.Update()
                .SetDrivingEntityId(new EntityId(0))
                .SetControlEntityId(new EntityId(0))
                .SetControlType(ControlVehicleType.None)
                .AddStopPiloting(default(StopPiloting));
            bool pushed = PushPilotState(player, playerEntityId, update);

            Console.WriteLine("[flight] entity " + playerEntityId + " DISMOUNTED helm " + helmEntityId + " of hull "
                + hullEntityId + " (" + why + ")"
                + (session != null ? " at " + session.State : "")
                + "; 1109 cleared" + (pushed ? "." : " - PUSH FAILED."));
        }

        private void RefreshDomainMembership(ShipDomain domain)
        {
            domain.ReplaceMembers(
                Crafting.BuiltShips.DecksForHull(domain.HullEntityId),
                Crafting.MountedParts.OnHull(domain.HullEntityId).Select(x => x.Key));
            domain.ReplaceAboard(WorldsAdriftRebornGameServer.Aboard.AboardShip(domain.HullEntityId));
            if (_domainHost != null)
            {
                if (_domainHost.ById(domain.Id) == null) _domainHost.Register(domain);
                else _domainHost.Synchronize(domain);
            }
        }

        /// <summary>
        /// Sends a 1109 update to the PILOT's peer only. 1109 is deliberately
        /// never seeded on remote mirrors (it steals the singleton PilotVisualizer
        /// and pokes LocalPlayer - the standing rule in MirrorSendPolicy), so the
        /// owner is the one client that has the component to update.
        /// </summary>
        private static bool PushPilotState(ENetPeerHandle player, long playerEntityId, PilotState.Update update)
        {
            return SendOPHelper.SendComponentUpdateOp(
                player,
                playerEntityId,
                new List<uint> { MirrorSendPolicy.PilotStateComponentId },
                new List<object> { update });
        }

        /// <summary>
        /// ITEM 1 OF THE FEEL PASS - the missing 1111 echo. The research doc
        /// named it: "the missing original server/worker behaviour is the relay
        /// or copy from the pilot's 1111 to the ship's 1111". Two readers exist
        /// (VERIFIED in the decompile) and both are served their own 1111:
        /// <list type="bullet">
        /// <item>the HELM's HelmVisualizer lerps its wheel/lever displays toward
        ///   _input.Throttle/Vertical/ShipAxes every Update and drives the
        ///   lever/winch SOUNDS off the deltas (HelmVisualizer.cs:59-104) - this
        ///   is the dead-wheel fix, and remote players see/hear it too;</item>
        /// <item>the HULL's ShipControlInputVisualizer is what
        ///   ShipControlsBehaviour.SetInitialInput reads on man - echoing the
        ///   held state here means a RE-manned helm resumes at the ship's actual
        ///   latched throttle instead of snapping to zero.</item>
        /// </list>
        /// RATE, the safety argument: event-on-change ONLY (the exact-equality
        /// compare), evaluated at the 0.24 s tick - so the worst case is ~4.2
        /// updates/s per entity while the pilot is actively moving the stick,
        /// zero while held or parked, never the client's raw 20 Hz. Unmanned
        /// sessions keep echoing the latched throttle but zero the wheel, climb and
        /// attitude controls after dismount. A disconnect echoes full neutral.
        /// </summary>
        private void EchoHelmFeedback(long hullEntityId, FlightSession session)
        {
            FlightControlInput current = session.Input;
            if (_lastEchoed.TryGetValue(hullEntityId, out FlightControlInput previous) && previous == current)
            {
                return;
            }
            _lastEchoed[hullEntityId] = current;

            ShipControlInput.Update update = new ShipControlInput.Update()
                .SetThrottle(current.Throttle)
                .SetVertical(current.Vertical)
                .SetShipAxes(new Improbable.Math.Vector3f(current.AxisPitch, current.AxisYaw, current.AxisRoll));

            ShipPublisher.Broadcast(hullEntityId, MirrorSendPolicy.ShipControlInputComponentId, update);
            if (_helmByHull.TryGetValue(hullEntityId, out long helmEntityId))
            {
                ShipPublisher.Broadcast(helmEntityId, MirrorSendPolicy.ShipControlInputComponentId, update);
            }
        }

        /// <summary>
        /// The wake bundle, on every moving root point and the resting heartbeat
        /// (<see cref="WakeInterval"/>): the hull's own 190602 timeline advance (the
        /// moving pose + the next shared stamp) and one 190602 wake per mounted
        /// "~" part carrying its UNCHANGED hull-local offset/rotation at the same
        /// stamp. Real Unity children - the built ship's decks, and any mounted part
        /// <see cref="Multiplayer.Ship.MountedPartHierarchy.IsUnityChild"/> names (the
        /// bar pipes) - are never woken: they ride the hull through the scene graph,
        /// and waking them would re-fire ParentUpdated and churn an unparent/reparent
        /// plus a rigidbody destroy/re-add, the exact trap ShipPartMotionService
        /// documents for the static deck.
        /// </summary>
        private static ShipPartWakeBundle BuildHullAndPartWakes(
            long hullEntityId, FlightEmit emit)
        {
            long sample = PartMountService.NextTimelineSample();
            float stamp = ShipPartMotionPolicy.StampFor(sample, ShipPartMotionPolicy.HeartbeatIntervalSeconds);

            FixedPointPosition hullPos = FixedPointPosition.FromMetres(emit.Spec.X, emit.Spec.Y, emit.Spec.Z);
            var hullUpdate = ShipPartTransform.BuildParentlessWakeUpdate(
                hullPos,
                new Improbable.Corelibrary.Math.Quaternion32(emit.PackedRotation),
                ShipPartMotionPolicy.ParentStampFor(sample, ShipPartMotionPolicy.HeartbeatIntervalSeconds));
            var members = new List<ShipDomainComponentUpdate>();
            foreach ((long partEntityId, Crafting.MountedParts.Mount mount) in Crafting.MountedParts.OnHull(hullEntityId))
            {
                // A mounted part seeded as a REAL Unity CHILD of the hull (a bar pipe) is
                // dragged along by the hull's own transform and needs no wake. Sending one
                // is actively harmful: the wake carries the parent field, every
                // ParentUpdated runs TransformChildHierarchyBehaviour.OnParentUpdated,
                // and that begins by UN-parenting (CachedTransform.parent =
                // OriginalParentTransform, decompile :254-292) before re-parenting. At
                // this cadence that is an unparent/reparent several times a second - the
                // rigidbody destroy/re-add churn the roadmap names as risk 1, and what a
                // player would report as jitter. "~" followers still MUST be woken: their
                // follow-visualizer sleeps a second after its last transform change.
                if (Multiplayer.Ship.MountedPartHierarchy.IsUnityChild(mount.ItemType))
                {
                    continue;
                }

                var wake = ShipPartTransform.BuildWakeUpdate(
                    mount.LocalOffset, hullEntityId, BoltedPartTransform.RelativeSlotKey, stamp,
                    new Improbable.Corelibrary.Math.Quaternion32(mount.PackedRotation));
                members.Add(new ShipDomainComponentUpdate(
                    partEntityId, ShipPartMotionPolicy.TransformStateComponentId, wake));
            }

            return new ShipPartWakeBundle(
                new ShipDomainComponentUpdate(
                    hullEntityId, ShipPartMotionPolicy.TransformStateComponentId, hullUpdate),
                members);
        }

        /// <summary>
        /// The flight agility multiplier for one hull, from what it is made of and how
        /// big it is: heavier ships accelerate, turn and climb more lazily.
        ///
        /// Cached per hull for the life of the ship. A hull's materials and geometry
        /// are immutable once built, so recomputing them - which means decoding the
        /// hull blob - on every 0.24 s tick for every flying ship would be pure waste.
        ///
        /// Falls back to 1.0 (today's exact behaviour) for anything that is not a
        /// recognised built hull, so the static demo ship and the acceptance fixture
        /// fly unchanged.
        /// </summary>
        private double AgilityScaleFor(long hullEntityId)
        {
            if (_agilityByHull.TryGetValue(hullEntityId, out double cached))
            {
                return cached;
            }

            double agility = 1.0;
            byte[]? hullBytes = Crafting.BuiltShips.HullBytesFor(hullEntityId);
            if (hullBytes != null
                && Multiplayer.Ship.ShipPlanModel.TryDecode(hullBytes, out Multiplayer.Ship.ShipPlanModel? plan, out _)
                && plan != null)
            {
                Multiplayer.Ship.ShipHullMetrics metrics = Multiplayer.Ship.ShipHullMetrics.Measure(plan);
                double massKg = Multiplayer.Materials.HullMassCalculator.HullMassKg(
                    Crafting.BuiltShips.MaterialsFor(hullEntityId), metrics.CellCount, metrics.DeckCount);
                agility = Multiplayer.Materials.HullMassCalculator.AgilityScale(massKg);

                Console.WriteLine("[flight] hull " + hullEntityId + " mass "
                    + massKg.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                    + " kg (" + Crafting.BuiltShips.MaterialsFor(hullEntityId)
                    + ", " + metrics.CellCount + " cell(s), " + metrics.DeckCount + " deck(s)) -> agility x"
                    + agility.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            }

            _agilityByHull[hullEntityId] = agility;
            return agility;
        }

        /// <summary>Per-hull agility cache; see <see cref="AgilityScaleFor"/>.</summary>
        private readonly Dictionary<long, double> _agilityByHull = new Dictionary<long, double>();

        /// <summary>
        /// WAREBORN_FLIGHT_FORCES=1 replaces the kinematic speed-command model
        /// with the RECOVERED force model: engines push in newtons, sails push
        /// with the wind, quadratic drag resists, and top speed is wherever those
        /// balance rather than a constant.
        ///
        /// OFF by default, deliberately and not merely conventionally. Switching it
        /// on is a balance decision about ships players have already built, so it is
        /// the operator's to make and wants a flight test behind it, not a deploy
        /// that silently changes how everybody's ship handles.
        ///
        /// THE OLD JUSTIFICATION HERE WAS WRONG, and it was believed twice, so it
        /// is corrected rather than deleted. It read: *"the force model makes thrust
        /// depend on MOUNTED ENGINES, and hulls built before it existed have no
        /// thrust at all"*. That sentence ignores the other two thirds of the model.
        /// Propulsion has THREE independent sources, and engines are the last of
        /// them to arrive in a player's progression, not the first:
        ///
        ///   1. the HULL itself, which the wind pushes as long as the ship has a
        ///      working sky core - roughly 2 m/s, under 4 knots
        ///      (ShipForceModel.BaselineDriveSpeedMps, magnitude PROVED);
        ///   2. SAILS, which are an always-on wind force with no throttle and no
        ///      velocity term, and were retail's early-game propulsion - a player
        ///      sailed long before they could build an engine;
        ///   3. ENGINES.
        ///
        /// So no hull is stranded by this flag: a bare hull still moves, just
        /// slowly, which is what it did in retail. What the flag really changes is
        /// that speed stops being a constant and starts being 10*sqrt(thrust/mass).
        /// </summary>
        internal static readonly bool ForceModelEnabled =
            Environment.GetEnvironmentVariable("WAREBORN_FLIGHT_FORCES") == "1";

        /// <summary>
        /// This hull's physical make-up for the force model, or null when the
        /// force model is off - in which case the integrator keeps its existing
        /// kinematic behaviour exactly.
        ///
        /// Engines are counted LIVE rather than cached, because unlike hull
        /// materials they are not immutable: a player can mount or salvage an
        /// engine mid-flight and should feel the difference on the next control
        /// point. <c>MountedParts.OnHull</c> is a yielding scan over a per-ship
        /// handful of parts and is already called twice per flight tick for
        /// replication bookkeeping, so this adds no new traversal class.
        /// </summary>
        private ShipPropulsion? PropulsionFor(long hullEntityId, int unfurledSails)
        {
            if (!ForceModelEnabled)
            {
                return null;
            }

            int engines = CountEngines(hullEntityId);
            int mountedParts = 0;
            foreach (KeyValuePair<long, Crafting.MountedParts.Mount> entry
                in Crafting.MountedParts.OnHull(hullEntityId))
            {
                mountedParts++;
            }

            bool enginesPowered = WorldsAdriftRebornGameServer.ShipFuel.EnginesPowered(hullEntityId);
            return new ShipPropulsion(
                Multiplayer.Materials.ShipTotalMass.TotalFlightMassKg(
                    DerivedHullMassKgFor(hullEntityId), mountedParts,
                    Environment.GetEnvironmentVariable("WAREBORN_SHIP_MASS")),
                enginesPowered ? engines * _tuning.EngineThrustNewtons : 0.0,
                unfurledSails);
        }

        private static int CountEngines(long hullEntityId)
        {
            int engines = 0;
            foreach (KeyValuePair<long, Crafting.MountedParts.Mount> entry
                in Crafting.MountedParts.OnHull(hullEntityId))
            {
                Crafting.MountedParts.Mount mount = entry.Value;
                if (Multiplayer.Ship.ShipPartKinds.Classify(
                        mount.ItemType, mount.PrefabName, mount.AttachmentType)
                    == Multiplayer.Ship.ShipPartKinds.Engine)
                {
                    engines++;
                }
            }
            return engines;
        }

        /// <summary>
        /// Evaluates the same force inputs used by <see cref="FlightIntegrator"/>
        /// for the operator inspector. Read-only: no flight state, tuning or part
        /// state is changed. Returning an explicit unavailable value when the force
        /// model is off prevents legacy kinematic flight from being presented as a
        /// physical measurement.
        /// </summary>
        internal Multiplayer.ShipFlightStat FlightStatFor(long hullEntityId,
            Multiplayer.ShipHullStat hull)
        {
            ShipDomain? domain = _domains.ByHull(hullEntityId);
            if (domain == null || !ForceModelEnabled)
            {
                return Multiplayer.ShipFlightStat.Unavailable;
            }

            int sails = WorldsAdriftRebornGameServer.Sails.UnfurledCountFor(hullEntityId);
            int mountedSails = 0;
            foreach (KeyValuePair<long, Crafting.MountedParts.Mount> entry
                in Crafting.MountedParts.OnHull(hullEntityId))
            {
                Crafting.MountedParts.Mount mount = entry.Value;
                if (Multiplayer.Ship.ShipPartKinds.Classify(
                        mount.ItemType, mount.PrefabName, mount.AttachmentType)
                    == Multiplayer.Ship.ShipPartKinds.Sail)
                {
                    mountedSails++;
                }
            }
            ShipPropulsion? maybeShip = PropulsionFor(hullEntityId, sails);
            if (!maybeShip.HasValue)
            {
                return Multiplayer.ShipFlightStat.Unavailable;
            }

            ShipPropulsion ship = maybeShip.Value;
            ShipForceEvaluation evaluation = domain.Flight.LastForceEvaluation;
            if (!evaluation.Present)
            {
                FlightState state = domain.Flight.State;
                evaluation = ShipForceEvaluator.Evaluate(
                    state.X, state.Z, state.YawRadians, domain.Flight.Input,
                    ship, _tuning, _clock.Elapsed.TotalSeconds, _wallFlightInfluence.Segments);
            }

            Multiplayer.ShipFlightShadowStat shadow = ShadowStatFor(
                hullEntityId, domain, hull, evaluation, ship);
            return new Multiplayer.ShipFlightStat(
                evaluation.MassKg, mountedSails, evaluation.UnfurledSails,
                evaluation.SampledAtSeconds,
                evaluation.Wind.WindX, evaluation.Wind.WindZ,
                evaluation.Wind.WallIntensity, evaluation.WindAngleDegrees,
                evaluation.SailForceNewtons, evaluation.EngineForceNewtons,
                evaluation.PropulsionAccelerationMps2,
                evaluation.WindAlongHeadingMps,
                evaluation.PredictedSettledSpeedMps, shadow);
        }

        private Multiplayer.ShipFlightShadowStat ShadowStatFor(long hullEntityId,
            ShipDomain domain, Multiplayer.ShipHullStat hull,
            ShipForceEvaluation scalar, ShipPropulsion ship)
        {
            bool enabled = Environment.GetEnvironmentVariable("WAREBORN_FLIGHT_SHADOW_OBSERVE") == "1";
            if (!enabled)
                return new Multiplayer.ShipFlightShadowStat(false, false, "observer-off",
                    scalar.EngineForceNewtons + scalar.SailForceNewtons,
                    ShadowVector3.Zero, ShadowVector3.Zero, ShadowVector3.Zero,
                    0, 0, false, default, false);
            if (!hull.Present)
                return new Multiplayer.ShipFlightShadowStat(true, false, "hull-geometry-unavailable",
                    scalar.EngineForceNewtons + scalar.SailForceNewtons,
                    ShadowVector3.Zero, ShadowVector3.Zero, ShadowVector3.Zero,
                    0, 0, false, default, false);

            var parts = new List<ShadowPropulsor>();
            int propulsorCount = 0;
            foreach (KeyValuePair<long, Crafting.MountedParts.Mount> entry in
                Crafting.MountedParts.OnHull(hullEntityId).OrderBy(x => x.Key))
            {
                Crafting.MountedParts.Mount mount = entry.Value;
                var kind = Multiplayer.Ship.ShipPartKinds.Classify(
                    mount.ItemType, mount.PrefabName, mount.AttachmentType);
                if (kind != Multiplayer.Ship.ShipPartKinds.Engine
                    && kind != Multiplayer.Ship.ShipPartKinds.Sail) continue;
                (float w, float x, float y, float z) =
                    Multiplayer.Placement.Quaternion32Packing.Decode(mount.PackedRotation);
                if (!ShadowQuaternion.TryNormalized(w, x, y, z, out ShadowQuaternion rotation))
                    continue;
                bool isEngine = kind == Multiplayer.Ship.ShipPartKinds.Engine;
                bool sailUnfurled = !isEngine
                    && WorldsAdriftRebornGameServer.Sails.IsUnfurled(entry.Key);
                double power = isEngine
                    ? (WorldsAdriftRebornGameServer.ShipFuel.EnginesPowered(hullEntityId)
                        ? _tuning.EngineThrustNewtons : 0.0)
                    : (sailUnfurled
                        ? _tuning.SailPowerNewtons : 0.0);
                parts.Add(new ShadowPropulsor(isEngine ? ShadowPartKind.Engine : ShadowPartKind.Sail,
                    new ShadowVector3(mount.LocalOffset.MetresX, mount.LocalOffset.MetresY,
                        mount.LocalOffset.MetresZ), rotation, power, 50.0,
                    torqueless: false));
                propulsorCount++;
            }

            Multiplayer.Ship.ShipHullMetrics metrics = hull.Silhouette.Metrics;
            ShadowVector3 half = new(Math.Max(0.25, metrics.BeamMetres * 0.5),
                Math.Max(0.25, metrics.DeckPlaneMetres * 0.5),
                Math.Max(0.25, metrics.KeelMetres * 0.5));
            double hullAndNonPropulsorMass = Math.Max(1.0, ship.MassKg - propulsorCount * 50.0);
            double yaw = domain.Flight.State.YawRadians;
            double sin = Math.Sin(yaw), cos = Math.Cos(yaw);
            ShadowVector3 localWind = new(
                scalar.Wind.WindX * cos - scalar.Wind.WindZ * sin, 0.0,
                scalar.Wind.WindX * sin + scalar.Wind.WindZ * cos);
            double spin = Math.Clamp(domain.Flight.Input.Throttle, -1.0, 1.0);
            if (spin < 0.0) spin *= _tuning.ReverseFactor;
            if (!VectorRigidBodyShadow.TryEvaluate(hullAndNonPropulsorMass, half, parts,
                spin, localWind, out VectorRigidBodyShadowResult vector))
                return new Multiplayer.ShipFlightShadowStat(true, false, "vector-input-rejected",
                    scalar.EngineForceNewtons + scalar.SailForceNewtons,
                    ShadowVector3.Zero, ShadowVector3.Zero, ShadowVector3.Zero,
                    0, parts.Count, true, default, false);

            FlightState state = domain.Flight.State;
            CollisionProxy subject = new(domain.Id.ToString(), CollisionProxyKind.ShipHull,
                CollisionAabb.FromCentreHalfExtents(new ShadowVector3(state.X, state.Y, state.Z), half),
                new ShadowVector3(state.VxMps, state.VyMps, state.VzMps));
            CollisionShadowResult collision = CollisionShadowEvaluator.Evaluate(
                new[] { subject }, Array.Empty<CollisionProxy>(), FixedFlightClock.StepSeconds);
            return new Multiplayer.ShipFlightShadowStat(true, true,
                "vector-equilibrium-trim-shadow; dynamic-sail-yaw-unavailable; collision-hull-only; terrain-proxies-unwired",
                scalar.EngineForceNewtons + scalar.SailForceNewtons,
                vector.ForceNewtons, vector.RawTorqueNewtonMetres,
                vector.RetailTorqueNewtonMetres, vector.AcceptedParts,
                vector.RejectedParts, vector.Mass.IsApproximation,
                collision.Telemetry, false);
        }

        internal Multiplayer.ShipWorldBoundsStat WorldBoundsStatFor(long hullEntityId)
        {
            ShipDomain? domain = _domains.ByHull(hullEntityId);
            RetailWorldBoundsTelemetry telemetry = domain?.Flight.LastWorldBoundsTelemetry
                ?? RetailWorldBoundsTelemetry.Off;
            return new Multiplayer.ShipWorldBoundsStat(
                _worldBounds.Enabled,
                _worldBounds.EdgeLengthMetres,
                _worldBounds.HorizontalPushbackThresholdMetres,
                _worldBounds.HorizontalHardLimitMetres,
                RetailWorldBoundsPolicy.VerticalPushbackMetres,
                RetailWorldBoundsPolicy.VerticalHardLimitMetres,
                telemetry);
        }

        internal Multiplayer.FixedFlightClockStat FixedClockStatFor(long hullEntityId)
        {
            _fixedClockTelemetry.TryGetValue(hullEntityId, out FixedFlightStepBatch batch);
            return new Multiplayer.FixedFlightClockStat(
                FixedStepEnabled,
                (int)Math.Round(FixedFlightClock.StepSeconds * 1000.0),
                FixedFlightClock.DefaultMaxCatchUpSteps,
                batch.CompletedSteps, batch.TotalDroppedSteps,
                batch.PressureEvents, batch.RemainderSeconds);
        }

        /// <summary>
        /// Edge-triggered operator evidence for the disposable-hull acceptance
        /// run. Continuous pushback can last many cadences, so journal only
        /// transitions rather than turning a safety feature into a log flood.
        /// The admin snapshot remains the high-frequency numerical view.
        /// </summary>
        private void ObserveWorldBounds(long hullEntityId, FlightState state,
            RetailWorldBoundsTelemetry telemetry)
        {
            if (!telemetry.Enabled) return;

            bool intervening = telemetry.PushbackDeltaVxMps != 0.0
                || telemetry.PushbackDeltaVyMps != 0.0
                || telemetry.PushbackDeltaVzMps != 0.0;
            LogBoundaryTransition(_boundsInterveningHulls, hullEntityId, intervening,
                "pushback", state, telemetry);
            LogBoundaryTransition(_boundsHardClampedHulls, hullEntityId,
                telemetry.HardClamped, "hard-clamp", state, telemetry);
            LogBoundaryTransition(_boundsQuarantinedHulls, hullEntityId,
                telemetry.InvalidState, "invalid-state-quarantine", state, telemetry);
        }

        private static void LogBoundaryTransition(HashSet<long> active, long hullEntityId,
            bool nowActive, string kind, FlightState state,
            RetailWorldBoundsTelemetry telemetry)
        {
            bool wasActive = active.Contains(hullEntityId);
            if (nowActive == wasActive) return;
            if (nowActive) active.Add(hullEntityId);
            else active.Remove(hullEntityId);

            Console.WriteLine("[flight-bounds] hull=" + hullEntityId
                + " event=" + kind + (nowActive ? "-entered" : "-cleared")
                + " position=[" + state.X.ToString("0.###",
                    System.Globalization.CultureInfo.InvariantCulture)
                + "," + state.Y.ToString("0.###",
                    System.Globalization.CultureInfo.InvariantCulture)
                + "," + state.Z.ToString("0.###",
                    System.Globalization.CultureInfo.InvariantCulture) + "]"
                + " distance=" + telemetry.BoundaryDistanceMetres.ToString("0.###",
                    System.Globalization.CultureInfo.InvariantCulture)
                + " deltaV=[" + telemetry.PushbackDeltaVxMps.ToString("0.###",
                    System.Globalization.CultureInfo.InvariantCulture)
                + "," + telemetry.PushbackDeltaVyMps.ToString("0.###",
                    System.Globalization.CultureInfo.InvariantCulture)
                + "," + telemetry.PushbackDeltaVzMps.ToString("0.###",
                    System.Globalization.CultureInfo.InvariantCulture) + "]"
                + " substeps=" + telemetry.ReferenceSubsteps + ".");
        }

        /// <summary>
        /// This hull's mass in kilograms, from its real materials and real cell and
        /// deck counts. Cached on the same reasoning as the agility cache: a built
        /// hull's materials and geometry never change. A hull whose plan will not
        /// decode falls back to the reference mass rather than to zero, because the
        /// force model divides by this.
        /// </summary>
        private double DerivedHullMassKgFor(long hullEntityId)
        {
            if (_hullMassByHull.TryGetValue(hullEntityId, out double cached))
            {
                return cached;
            }

            double massKg = Multiplayer.Materials.HullMassCalculator.ReferenceHullMassKg;
            byte[]? hullBytes = Crafting.BuiltShips.HullBytesFor(hullEntityId);
            if (hullBytes != null
                && Multiplayer.Ship.ShipPlanModel.TryDecode(hullBytes, out Multiplayer.Ship.ShipPlanModel? plan, out _)
                && plan != null)
            {
                Multiplayer.Ship.ShipHullMetrics metrics = Multiplayer.Ship.ShipHullMetrics.Measure(plan);
                massKg = Multiplayer.Materials.HullMassCalculator.HullMassKg(
                    Crafting.BuiltShips.MaterialsFor(hullEntityId), metrics.CellCount, metrics.DeckCount);
            }

            _hullMassByHull[hullEntityId] = massKg;
            return massKg;
        }

        /// <summary>Per-hull mass cache; see <see cref="HullMassKgFor"/>.</summary>
        private readonly Dictionary<long, double> _hullMassByHull = new Dictionary<long, double>();

        private void PersistPoseWhenDue(long hullEntityId, ShipDomain domain)
        {
            if (_nextPoseSaveAt.TryGetValue(hullEntityId, out TimeSpan due) && _clock.Elapsed < due) return;
            _nextPoseSaveAt[hullEntityId] = _clock.Elapsed + PoseSaveInterval;
            PersistPoseNow(hullEntityId, domain.Flight.State);
        }

        private void PersistPoseNow(long hullEntityId, FlightState state)
        {
            FixedPointPosition position = FixedPointPosition.FromMetres(state.X, state.Y, state.Z);
            uint packedRotation = FlightIntegrator.PackedRotation(state);
            // WorldEntity.Position is also the seed used for a same-process late
            // join/rejoin. Keeping only the JSON record current made a newly
            // checked-out ship appear back at its build spot until a later motion
            // keepalive happened to correct it.
            WorldsAdriftRebornGameServer.WorldEntities.Relocate(
                hullEntityId, position, packedRotation);

            int? index = Crafting.BuiltShips.PersistentIndexFor(hullEntityId);
            if (!index.HasValue) return;
            ShipDomain? domain = _domains.ByHull(hullEntityId);
            if (domain == null)
            {
                WorldStatePersistence.UpdateBuiltShipPose(index.Value, position, state.YawRadians);
                return;
            }
            if (FixedStepEnabled)
            {
                var durable = Multiplayer.Persistence.DurableShipFlightSnapshot.Capture(
                    state, domain.Flight.Input, domain.Generation.Value, domain.Flight.IsManned,
                    domain.AboardPeerIds.Count, Crafting.BuiltShips.IsHullDocked(hullEntityId),
                    WorldsAdriftRebornGameServer.Sails.UnfurledCountFor(hullEntityId));
                WorldStatePersistence.UpdateBuiltShipFlight(index.Value,
                    position, state.YawRadians, durable);
            }
            else
            {
                WorldStatePersistence.UpdateBuiltShipPose(index.Value,
                    position, state.YawRadians);
            }
        }

        /// <summary>
        /// Captures a settled owned hull at an EMPTY shipyard. Departing docked hulls
        /// remain linked until they clear the wider release radius, so they cannot
        /// churn here. Restored legacy hulls physically sitting in a yard are eligible,
        /// which repairs the live overlap case.
        /// </summary>
        private void TryCaptureAtEmptyShipyard(long hullEntityId, FlightSession session)
        {
            if (Crafting.BuiltShips.IsHullDocked(hullEntityId)) return;

            FixedPointPosition hullPosition = FixedPointPosition.FromMetres(
                session.State.X, session.State.Y, session.State.Z);
            string hullOwner = Crafting.BuiltShips.OwnerFor(hullEntityId);
            foreach (long yardEntityId in Placement.PlacedShipyards.EntityIds.OrderBy(id => id))
            {
                FixedPointPosition yardPosition = WorldsAdriftRebornGameServer.WorldEntities
                    .TransformSeedFor(yardEntityId);
                Placement.PlacedShipyards.Seed yard = Placement.PlacedShipyards.SeedFor(yardEntityId);
                if (!Multiplayer.Ship.ShipyardDockingPolicy.CanDock(
                        captureArmed: true,
                        hullAtRest: session.State.IsAtRest,
                        inputNeutral: session.Input.IsNeutral,
                        yardOccupied: Crafting.BuiltShips.IsShipyardOccupied(yardEntityId),
                        hullOwner: hullOwner,
                        yardOwner: yard.OwnerCharacterUid,
                        hullPosition: hullPosition,
                        shipyardPosition: yardPosition))
                {
                    continue;
                }

                FixedPointPosition target = Multiplayer.Ship.ShipyardDockingPolicy.DockPose(yardPosition);
                double yaw = Multiplayer.Ship.ShipyardDockingPolicy.YawFromPacked(
                    WorldsAdriftRebornGameServer.WorldEntities.RotationSeedFor(yardEntityId));
                session.DockAt(target.MetresX, target.MetresY, target.MetresZ, yaw);
                Crafting.BuiltShips.SetDocked(yardEntityId, hullEntityId);

                int? persistentIndex = Crafting.BuiltShips.PersistentIndexFor(hullEntityId);
                if (persistentIndex.HasValue)
                {
                    WorldStatePersistence.DockBuiltShip(
                        persistentIndex.Value, target, yaw, yardPosition);
                }
                PersistPoseNow(hullEntityId, session.State);

                PilotSeats.Seat? pilot = _seats.PilotOf(hullEntityId);
                if (pilot.HasValue) _inputs[pilot.Value.PlayerEntityId] = FlightControlInput.Neutral;

                var hullUpdate = new DockableState.Update()
                    .SetDockEntityId(new EntityId(yardEntityId))
                    .SetDockLocation(new Coordinates(target.MetresX, target.MetresY, target.MetresZ))
                    .SetDocked(true)
                    .SetApproachingDock(false);
                foreach (ENetPeerHandle peer in PeerManager.Instance.playerState.Keys.ToList())
                {
                    Crafting.BuiltShipSpawner.PushDockedShipId(peer, yardEntityId, hullEntityId);
                    SendOPHelper.SendComponentUpdateOp(peer, hullEntityId,
                        new List<uint> { 1114 }, new List<object> { hullUpdate });
                }

                Console.WriteLine("[flight] hull " + hullEntityId + " captured by empty shipyard "
                    + yardEntityId + "; editing restored and dock link persisted.");
                return;
            }
        }

        private void CompleteDepartureIfOutside(long hullEntityId, FlightState state)
        {
            if (!_departingYardByHull.TryGetValue(hullEntityId, out long yardEntityId)) return;
            FixedPointPosition hullPosition = FixedPointPosition.FromMetres(state.X, state.Y, state.Z);
            FixedPointPosition yardPosition = WorldsAdriftRebornGameServer.WorldEntities
                .TransformSeedFor(yardEntityId);
            if (Multiplayer.Ship.ShipyardDockingPolicy.IsWithin(hullPosition,
                    yardPosition, Multiplayer.Ship.ShipyardDockingPolicy.RearmRadiusMetres)) return;

            _departingYardByHull.Remove(hullEntityId);
            Crafting.BuiltShipSpawner.UndockDepartingHull(hullEntityId);
        }
    }
}

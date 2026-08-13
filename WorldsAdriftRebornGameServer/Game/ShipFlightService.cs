using Bossa.Travellers.Controls;
using Bossa.Travellers.Ship;
using Improbable;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

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
        private readonly PilotSeats _seats = new PilotSeats();

        /// <summary>One session per hull that has ever been manned this boot; kept
        /// after dismount because the session is the only holder of the flown pose.</summary>
        private readonly Dictionary<long, FlightSession> _sessions = new Dictionary<long, FlightSession>();

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

        /// <summary>When each hull's mounted parts were last woken (the 0.5 s sub-cadence).</summary>
        private readonly Dictionary<long, TimeSpan> _lastWakeAt = new Dictionary<long, TimeSpan>();

        /// <summary>
        /// The wake sub-cadence, slightly under the policy's 0.5 s heartbeat so
        /// the 0.24 s tick grid lands a wake every OTHER tick (0.48 s spacing) -
        /// comfortably below the client's 1 s follow-visualizer sleep. v1 woke on
        /// EVERY point (4.2 Hz); halving it is free because a "~" follower
        /// composes against the hull's live interpolated transform every frame
        /// while awake - the wake only needs to keep it awake, not move it.
        /// </summary>
        private static readonly TimeSpan WakeInterval =
            TimeSpan.FromSeconds(ShipPartMotionPolicy.HeartbeatIntervalSeconds * 0.9);

        /// <summary>1111 packets consumed since the last stats line, for the rx-rate readout.</summary>
        private long _inputPacketsSinceStats;
        private TimeSpan _nextStatsAt;

        public ShipFlightService(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _cadence = new CadenceTimer(TimeSpan.FromSeconds(ShipMotionPolicy.SendIntervalSeconds));
            _tuning = FlightTuning.FromEnvironment(Environment.GetEnvironmentVariable);

            if (Enabled)
            {
                Console.WriteLine("[info] helm flight is ARMED (WAREBORN_HELM_FLIGHT=1): Man a mounted helm to fly"
                    + " its built ship. " + _tuning + "; drive target = "
                    + (DriveTargetIsHelm ? "HELM" : "HULL") + " (WAREBORN_FLIGHT_DRIVE_TARGET).");
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

            long hullEntityId = mount.Value.HullEntityId;
            ManOutcome outcome = _seats.TryMan(playerEntityId, targetEntityId, hullEntityId);
            switch (outcome)
            {
                case ManOutcome.StartPiloting:
                    StartPiloting(player, playerEntityId, targetEntityId, hullEntityId);
                    break;

                case ManOutcome.StopPiloting:
                    StopPiloting(player, playerEntityId, targetEntityId, hullEntityId, "re-manned the helm");
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
            if (seat != null && _sessions.TryGetValue(seat.Value.HullEntityId, out FlightSession? session))
            {
                session.Abandon();
                Console.WriteLine("[flight] pilot entity " + playerEntityId + " disconnected while piloting hull "
                    + seat.Value.HullEntityId + "; ship settles to rest at " + session.State + ".");
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
            FlightControlInput merged = held.Merge(throttle, vertical, axisPitch, axisYaw, axisRoll);
            _inputs[playerEntityId] = merged;
            _inputPacketsSinceStats++;

            PilotSeats.Seat? seat = _seats.SeatOf(playerEntityId);
            if (seat != null && _sessions.TryGetValue(seat.Value.HullEntityId, out FlightSession? session))
            {
                session.SetInput(merged);
                // A real control input is the first authoritative evidence that the
                // newly-built ship has LEFT its construction dock. Zero/neutral packets
                // while taking the wheel do not undock it; the first motion command does.
                if (!merged.IsNeutral)
                {
                    Crafting.BuiltShipSpawner.UndockDepartingHull(seat.Value.HullEntityId);
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
            if (Enabled && _sessions.TryGetValue(hullEntityId, out FlightSession? session))
            {
                position = FixedPointPosition.FromMetres(session.State.X, session.State.Y, session.State.Z);
                packedRotation = FlightIntegrator.PackedRotation(session.State);
                return true;
            }
            position = default;
            packedRotation = Multiplayer.Placement.Quaternion32Packing.Identity;
            return false;
        }

        // ------------------------------------------------------------------
        // The publisher heartbeat
        // ------------------------------------------------------------------

        /// <summary>
        /// One call per main-loop turn. Cheap when off or idle (an env check and a
        /// Stopwatch compare); when due, advances every session and publishes what
        /// they decided to emit.
        /// </summary>
        public void Tick()
        {
            if (!Enabled || _sessions.Count == 0)
            {
                return;
            }
            if (!_cadence.Due(_clock.Elapsed))
            {
                return;
            }

            long nowMs = ShipHull.NowMillisecondsSinceEpoch();
            foreach ((long hullEntityId, FlightSession session) in _sessions)
            {
                // HELM FEEDBACK first, motion second: the echo is a pure
                // input-changed compare (usually a no-op) and runs even on
                // no-emit ticks, so a wheel wiggle inside the deadzone still
                // animates the helm of a parked ship.
                EchoHelmFeedback(hullEntityId, session);

                int unfurledSails = WorldsAdriftRebornGameServer.Sails.UnfurledCountFor(hullEntityId);
                FlightEmit emit = session.Advance(
                    nowMs, ShipMotionPolicy.SendIntervalSeconds, _tuning, unfurledSails);
                if (!emit.Emit)
                {
                    continue;
                }

                ShipPublisher.Broadcast(hullEntityId, ShipPublisher.BuildUpdate(emit.Spec, emit.PackedRotation));

                // Mounted-part wakes ride a 0.5 s SUB-cadence, not every point:
                // an awake "~" follower composes against the hull's live
                // interpolated transform every frame, so more wakes add reliable
                // packets without adding smoothness (see WakeInterval).
                if (!_lastWakeAt.TryGetValue(hullEntityId, out TimeSpan lastWake)
                    || _clock.Elapsed - lastWake >= WakeInterval)
                {
                    _lastWakeAt[hullEntityId] = _clock.Elapsed;
                    PublishHullAndPartWakes(hullEntityId, emit);
                }
            }

            if (_clock.Elapsed >= _nextStatsAt)
            {
                _nextStatsAt = _clock.Elapsed + StatsInterval;
                foreach ((long hullEntityId, FlightSession session) in _sessions)
                {
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
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        private void StartPiloting(ENetPeerHandle player, long playerEntityId, long helmEntityId, long hullEntityId)
        {
            if (!_sessions.TryGetValue(hullEntityId, out FlightSession? session))
            {
                // First manning this boot: the session starts from the hull's
                // registered seed pose. WorldEntity.Position is immutable, so from
                // here on the SESSION is the authority on where this hull is.
                FixedPointPosition seed = WorldsAdriftRebornGameServer.WorldEntities.TransformSeedFor(hullEntityId);
                session = new FlightSession(FlightState.AtRestAt(seed.MetresX, seed.MetresY, seed.MetresZ));
                _sessions[hullEntityId] = session;
            }

            session.Man();
            // Seed the delta-merge ledger from the ship's actual lever state.
            // ShipControlInput updates omit unchanged fields; a re-manning client
            // initialized from the hull's echoed 1111 may therefore send no
            // throttle field at all. Starting this ledger at neutral would turn an
            // unrelated steering delta into an accidental throttle reset.
            _inputs[playerEntityId] = session.Input;
            _helmByHull[hullEntityId] = helmEntityId;

            long driveTarget = DriveTargetIsHelm ? helmEntityId : hullEntityId;
            PilotState.Update update = new PilotState.Update()
                .SetDrivingEntityId(new EntityId(driveTarget))
                .SetControlEntityId(new EntityId(helmEntityId))
                .SetControlType(ControlVehicleType.Ship)
                .AddStartPiloting(default(StartPiloting));
            bool pushed = PushPilotState(player, playerEntityId, update);

            Console.WriteLine("[flight] entity " + playerEntityId + " MANNED helm " + helmEntityId + " of hull "
                + hullEntityId + " at " + session.State + "; 1109 driving=" + driveTarget
                + (DriveTargetIsHelm ? " (helm)" : " (hull)") + (pushed ? " pushed." : " PUSH FAILED."));

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

        private void StopPiloting(ENetPeerHandle player, long playerEntityId, long helmEntityId, long hullEntityId, string why)
        {
            if (_sessions.TryGetValue(hullEntityId, out FlightSession? session))
            {
                session.Dismount();
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
        /// The wake bundle, on the 0.5 s sub-cadence (<see cref="WakeInterval"/>,
        /// v1 sent it per point): the hull's own 190602 timeline advance (the
        /// moving pose + the next shared stamp) and one 190602 wake per mounted
        /// "~" part carrying its UNCHANGED hull-local offset/rotation at the same
        /// stamp. Deck panels are real Unity children - never woken (waking them
        /// would re-fire ParentUpdated and churn a destroy/re-add, the exact trap
        /// ShipPartMotionService documents).
        /// </summary>
        private static void PublishHullAndPartWakes(long hullEntityId, FlightEmit emit)
        {
            long sample = PartMountService.NextTimelineSample();
            float stamp = ShipPartMotionPolicy.StampFor(sample, ShipPartMotionPolicy.HeartbeatIntervalSeconds);

            FixedPointPosition hullPos = FixedPointPosition.FromMetres(emit.Spec.X, emit.Spec.Y, emit.Spec.Z);
            var hullUpdate = ShipPartTransform.BuildParentlessWakeUpdate(
                hullPos,
                new Improbable.Corelibrary.Math.Quaternion32(emit.PackedRotation),
                ShipPartMotionPolicy.ParentStampFor(sample, ShipPartMotionPolicy.HeartbeatIntervalSeconds));
            ShipPublisher.Broadcast(hullEntityId, ShipPartMotionPolicy.TransformStateComponentId, hullUpdate);

            foreach ((long partEntityId, Crafting.MountedParts.Mount mount) in Crafting.MountedParts.OnHull(hullEntityId))
            {
                var wake = ShipPartTransform.BuildWakeUpdate(
                    mount.LocalOffset, hullEntityId, BoltedPartTransform.RelativeSlotKey, stamp,
                    new Improbable.Corelibrary.Math.Quaternion32(mount.PackedRotation));
                ShipPublisher.Broadcast(partEntityId, ShipPartMotionPolicy.TransformStateComponentId, wake);
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>What one cadence tick decided to put on the wire, if anything.</summary>
    public readonly struct FlightEmit
    {
        public FlightEmit(bool emit, ShipControlPointSpec spec, uint packedRotation)
        {
            Emit = emit;
            Spec = spec;
            PackedRotation = packedRotation;
        }

        /// <summary>False = publish nothing this tick.</summary>
        public bool Emit { get; }

        public ShipControlPointSpec Spec { get; }

        /// <summary>The heading, in the game's 32-bit wire form (1023 = identity).</summary>
        public uint PackedRotation { get; }

        public static FlightEmit Nothing => default;
    }

    /// <summary>
    /// One hull's flight, as a pure state machine the service ticks: manned
    /// (integrate pilot input), cruising on the helm's latched throttle after a
    /// voluntary release, settling after an explicit stop or abandoned connection,
    /// resting repeats after an unmanned stop (belt-and-braces re-sends of the
    /// final zero-velocity point), then a slow KEEPALIVE forever for peers that
    /// still have the ship checked out. A manned session always keeps the normal
    /// cadence, including at rest, so the client's playback buffer cannot halt. The
    /// server also advances the hull's registry seed as poses are persisted, so a
    /// later checkout starts at the latest authoritative pose rather than the old
    /// build location; the keepalive then maintains the client's motion timeline.
    ///
    /// TIMESTAMPS. Each emitted point is stamped
    /// <c>max(now, lastStamp + step)</c>: monotonic by construction, never
    /// closer than the client's 0.228 s reject floor (step is 0.24 s), and
    /// pinned to wall-clock whenever the cadence has real gaps (rest keepalives),
    /// so the client's server-latency estimate stays sane across a pause. The
    /// pure test asserts <see cref="ShipMotionPolicy.IsLegalSeparation"/> across
    /// every phase transition.
    ///
    /// A session survives dismount ON PURPOSE: the ship stays where it was flown
    /// (this object is the only holder of the flown pose - WorldEntity.Position
    /// is immutable), and re-manning resumes from that pose.
    /// </summary>
    public sealed class FlightSession
    {
        /// <summary>
        /// Extra zero-velocity points after settling, before dropping to the
        /// keepalive cadence. Same value and same reasoning as the ferry's
        /// RestRepeats: the point that stops the ship must not be the one that
        /// got lost.
        /// </summary>
        public const int RestRepeats = 4;

        private FlightState _state;
        private FlightControlInput _input;
        private bool _manned;
        private int _restEmitted;
        private long _lastStampMs;
        private bool _everEmitted;
        private bool _canvasWakeRequested;

        public FlightSession(FlightState initial)
        {
            _state = initial;
            _input = FlightControlInput.Neutral;
        }

        private FlightSession(FlightSessionSnapshot snapshot)
        {
            _state = snapshot.State;
            _input = snapshot.Input;
            _manned = snapshot.Manned;
            _restEmitted = snapshot.RestEmitted;
            _lastStampMs = snapshot.LastStampMs;
            _everEmitted = snapshot.EverEmitted;
        }

        /// <summary>The current simulated pose, for logs and for re-man resume.</summary>
        public FlightState State => _state;

        /// <summary>The exact wind/force sample consumed by the latest physical step.</summary>
        public ShipForceEvaluation LastForceEvaluation { get; private set; }

        /// <summary>The exact optional world-edge result consumed by the latest cadence step.</summary>
        public RetailWorldBoundsTelemetry LastWorldBoundsTelemetry { get; private set; }

        public bool IsManned => _manned;

        /// <summary>
        /// The pilot took the helm. The ship's latched throttle remains where the
        /// previous pilot left its physical lever; transient axes were already
        /// released by <see cref="Dismount"/>. The incoming client's initial 1111
        /// state can then replace individual fields without an artificial idle snap.
        /// </summary>
        public void Man()
        {
            _manned = true;
            _restEmitted = 0;
        }

        /// <summary>
        /// The pilot voluntarily left the helm. The forward/reverse throttle is a
        /// latched lever and stays where they left it; steering, pitch and vertical
        /// controls release to zero. Setting the lever to zero before dismount is
        /// therefore the explicit stop command and naturally settles the ship.
        /// </summary>
        public void Dismount()
        {
            _manned = false;
            _input = _input.LatchedThrottleOnly();
            _restEmitted = 0;
        }

        /// <summary>
        /// The pilot vanished without a clean release. Unlike a deliberate
        /// dismount, a disconnect must not leave an unattended ship powered by a
        /// possibly stale client command. Neutralize everything and settle safely.
        /// </summary>
        public void Abandon()
        {
            _manned = false;
            _input = FlightControlInput.Neutral;
            _restEmitted = 0;
        }

        /// <summary>Latest 1111-derived input. Ignored (kept unchanged) when unmanned.</summary>
        public void SetInput(FlightControlInput input)
        {
            if (_manned)
            {
                _input = input;
            }
        }

        /// <summary>
        /// Wakes a quiet at-rest session because a player has just unfurled one of
        /// its sails. This is an interaction edge, not persistent state: restored
        /// moored ships whose canvas was already up remain parked until somebody
        /// deliberately changes the rigging.
        /// </summary>
        public void WakeForCanvas()
        {
            _canvasWakeRequested = true;
            _restEmitted = 0;
        }

        /// <summary>The current held input, for the periodic stats line.</summary>
        public FlightControlInput Input => _input;

        /// <summary>
        /// Emits the current absolute pose without integrating. Used when a pilot
        /// takes the helm to wake a halted client PathFollower before 1109 enables
        /// input, preventing the first steering point from entering a 5 s spline.
        /// </summary>
        public FlightEmit PrimePlayback(long nowMs, double stepSeconds) =>
            EmitAt(nowMs, stepSeconds);

        public FlightSessionSnapshot Capture() => new FlightSessionSnapshot(
            _state, _input, _manned, _restEmitted, _lastStampMs, _everEmitted);

        public static FlightSession Restore(FlightSessionSnapshot snapshot) =>
            new FlightSession(snapshot ?? throw new System.ArgumentNullException(nameof(snapshot)));

        /// <summary>Snaps a settled ship into a yard's authored dock pose.</summary>
        public void DockAt(double x, double y, double z, double yawRadians)
        {
            _state = FlightState.AtRestAt(x, y, z, yawRadians);
            _input = FlightControlInput.Neutral;
            LastForceEvaluation = ShipForceEvaluation.Unavailable;
            LastWorldBoundsTelemetry = RetailWorldBoundsTelemetry.Off;
            _restEmitted = 0;
        }

        /// <summary>
        /// Operator recovery for an unpiloted runaway hull. Keep the exact
        /// authoritative pose and heading, but clear every velocity, attitude
        /// and held control immediately. The service refuses this while a pilot
        /// owns the helm, so this cannot fight live input.
        /// </summary>
        public void EmergencyStop()
        {
            _state = FlightState.AtRestAt(_state.X, _state.Y, _state.Z, _state.YawRadians);
            _input = FlightControlInput.Neutral;
            LastForceEvaluation = ShipForceEvaluation.Unavailable;
            LastWorldBoundsTelemetry = RetailWorldBoundsTelemetry.Off;
            _manned = false;
            _restEmitted = 0;
        }

        /// <summary>
        /// One cadence tick: integrate if moving or manned, and decide whether a
        /// control point goes out. Call at the control-point cadence
        /// (<paramref name="stepSeconds"/> = ShipMotionPolicy.SendIntervalSeconds).
        /// </summary>
        public FlightEmit Advance(long nowMs, double stepSeconds, FlightTuning tuning,
            int unfurledSails = 0, double agilityScale = 1.0,
            ShipPropulsion? propulsion = null,
            IReadOnlyList<WeatherWallSegment>? walls = null,
            RetailWorldBoundsPolicy? worldBounds = null) =>
            AdvanceFixed(nowMs, stepSeconds, 1, nowMs / 1000.0, tuning,
                unfurledSails, agilityScale, propulsion, walls, worldBounds,
                fixedStepSeconds: stepSeconds);

        /// <summary>
        /// Advances authoritative physics through zero or more deterministic
        /// fixed steps, then makes exactly one network-emission decision. Physics
        /// cadence and the stock 1130 cadence are intentionally independent.
        /// </summary>
        public FlightEmit AdvanceFixed(long nowMs, double emitStepSeconds,
            int fixedStepCount, double firstFixedStepTimeSeconds, FlightTuning tuning,
            int unfurledSails = 0, double agilityScale = 1.0,
            ShipPropulsion? propulsion = null,
            IReadOnlyList<WeatherWallSegment>? walls = null,
            RetailWorldBoundsPolicy? worldBounds = null,
            double fixedStepSeconds = FixedFlightClock.StepSeconds)
        {
            if (fixedStepCount < 0 || fixedStepCount > FixedFlightClock.DefaultMaxCatchUpSteps)
                throw new ArgumentOutOfRangeException(nameof(fixedStepCount));
            // A latched non-zero throttle is live even if the pilot released the
            // helm before the first integration tick, while the hull is technically
            // still at rest. Without this term that perfectly valid command would
            // be parked forever merely because release won a scheduling race.
            // Canvas is an independent force under the force model. A fresh
            // unfurl interaction explicitly wakes a quiet session; merely
            // restoring a moored ship that was persisted with canvas up does not.
            bool live = _manned || !_state.IsAtRest || _input.Throttle != 0f
                || _canvasWakeRequested
                // Retail evaluated WorldEdgePushback on every non-kinematic
                // rigidbody FixedUpdate. A restored/parked hull outside a push
                // threshold must therefore wake and recover inward too.
                || worldBounds?.RequiresEvaluation(_state) == true;

            if (live)
            {
                // A newly-created fixed clock legitimately yields zero steps on
                // its first observation. Preserve the one-shot sail wake until a
                // physics step actually consumes it, or sail-only departure would
                // be lost at activation.
                if (fixedStepCount > 0) _canvasWakeRequested = false;
                double boundsDvx = 0.0, boundsDvy = 0.0, boundsDvz = 0.0;
                double boundsDistance = worldBounds?.Enabled == true
                    ? worldBounds.DistanceToBoundary(_state) : 0.0;
                int boundsSubsteps = 0;
                bool boundsHardClamped = false, boundsInvalid = false;
                for (int fixedStep = 0; fixedStep < fixedStepCount; fixedStep++)
                {
                    IntegrateForDuration(fixedStepSeconds,
                        firstFixedStepTimeSeconds + (fixedStep * fixedStepSeconds),
                        tuning, unfurledSails, agilityScale, propulsion, walls, worldBounds);
                    if (worldBounds?.Enabled == true)
                    {
                        RetailWorldBoundsTelemetry sample = LastWorldBoundsTelemetry;
                        boundsDvx += sample.PushbackDeltaVxMps;
                        boundsDvy += sample.PushbackDeltaVyMps;
                        boundsDvz += sample.PushbackDeltaVzMps;
                        boundsDistance = sample.BoundaryDistanceMetres;
                        boundsSubsteps += sample.ReferenceSubsteps;
                        boundsHardClamped |= sample.HardClamped;
                        boundsInvalid |= sample.InvalidState;
                        // Track 1 quarantines the entire cadence candidate. Do not
                        // resume from the finite rest anchor inside the same outer
                        // fixed-step batch.
                        if (sample.InvalidState) break;
                    }
                }
                if (worldBounds?.Enabled == true)
                {
                    LastWorldBoundsTelemetry = new RetailWorldBoundsTelemetry(
                        true, boundsDistance, boundsDvx, boundsDvy, boundsDvz,
                        boundsHardClamped, boundsInvalid, boundsSubsteps);
                }

                if (_state.IsAtRest && !_manned)
                {
                    // Settled this tick; fall through into the rest-repeat phase
                    // and emit this zero-velocity point as repeat #1.
                    _restEmitted++;
                }
                else if (_state.IsAtRest && _manned)
                {
                    // Manned but idle. With the OPTIONAL idle bob armed, the ship
                    // breathes: a slow sine on Y, emitted every tick - the point
                    // stream stays at the flying cadence for the whole manned-idle
                    // time, which is exactly why the knob defaults OFF. The bob is
                    // an OUTPUT offset only: the state's base Y never moves, so
                    // dismounting settles back to the true altitude (a <=amplitude
                    // step, invisible at the default 0).
                    if (tuning.IdleBobMetres > 0.0)
                    {
                        return EmitBobbedAt(nowMs, emitStepSeconds, tuning);
                    }

                    // Keep the 1130 playback buffer continuously populated while
                    // somebody holds the helm. If this stream goes quiet, the
                    // client's PathFollower enters its halting branch; the first
                    // small yaw correction after that is deliberately blended over
                    // ShipConfiguration.SlowSplineCorrectionTime (5 seconds).
                    // That was the measured "helm moves now, hull turns 5 s later"
                    // defect. A live pilot costs the normal 4.2 Hz root stream so
                    // steering begins on the next 240 ms point instead.
                    _restEmitted = 0;
                }
                else
                {
                    _restEmitted = 0;
                }

                return EmitAt(nowMs, emitStepSeconds);
            }

            // At rest, unmanned.
            if (_restEmitted <= RestRepeats)
            {
                _restEmitted++;
                return EmitAt(nowMs, emitStepSeconds);
            }

            if (KeepaliveDue(nowMs, tuning))
            {
                return EmitAt(nowMs, emitStepSeconds);
            }

            return FlightEmit.Nothing;
        }

        private void IntegrateForDuration(double durationSeconds, double endTimeSeconds,
            FlightTuning tuning, int unfurledSails, double agilityScale,
            ShipPropulsion? propulsion, IReadOnlyList<WeatherWallSegment>? walls,
            RetailWorldBoundsPolicy? worldBounds)
        {
            // The wind field's time is the deterministic simulation-step time,
            // never the incidental instant at which the poll loop happened to run.
            if (worldBounds?.Enabled != true)
            {
                _state = FlightIntegrator.StepEvaluated(
                    _state, _input, durationSeconds, tuning, out ShipForceEvaluation evaluation,
                    unfurledSails, agilityScale, propulsion, endTimeSeconds, walls);
                LastForceEvaluation = evaluation;
                LastWorldBoundsTelemetry = RetailWorldBoundsTelemetry.Off;
                return;
            }

            // Track 2 owns the outer fixed-step accumulator. This validation is
            // Track 1's defensive cadence ceiling for the legacy 240 ms adapter
            // and direct callers; it must never become a second catch-up clock.
            if (!RetailWorldBoundsPolicy.IsValidCadenceInterval(durationSeconds))
            {
                if (!RetailWorldBoundsPolicy.IsFinite(_state))
                {
                    RetailWorldBoundsStep quarantined = worldBounds.Apply(_state, _state);
                    _state = quarantined.State;
                    LastWorldBoundsTelemetry = quarantined.Telemetry;
                }
                else
                {
                    LastWorldBoundsTelemetry = new RetailWorldBoundsTelemetry(
                        true, worldBounds.DistanceToBoundary(_state),
                        0, 0, 0, false, false, 0);
                }
                LastForceEvaluation = ShipForceEvaluation.Unavailable;
                return;
            }

            double remaining = durationSeconds;
            double elapsed = 0.0;
            int substeps = 0;
            double dvx = 0.0, dvy = 0.0, dvz = 0.0;
            bool hardClamped = false, invalidState = false;
            double distance = worldBounds.DistanceToBoundary(_state);
            ShipForceEvaluation finalEvaluation = ShipForceEvaluation.Unavailable;
            while (remaining > 1e-12)
            {
                double dt = Math.Min(RetailWorldBoundsPolicy.ReferenceStepSeconds, remaining);
                FlightState previous = _state;
                FlightState candidate = FlightIntegrator.StepEvaluated(
                    previous, _input, dt, tuning, out finalEvaluation,
                    unfurledSails, agilityScale, propulsion,
                    endTimeSeconds - durationSeconds + elapsed + dt, walls);
                RetailWorldBoundsStep bounded = worldBounds.Apply(previous, candidate);
                _state = bounded.State;
                RetailWorldBoundsTelemetry sample = bounded.Telemetry;
                dvx += sample.PushbackDeltaVxMps;
                dvy += sample.PushbackDeltaVyMps;
                dvz += sample.PushbackDeltaVzMps;
                hardClamped |= sample.HardClamped;
                invalidState |= sample.InvalidState;
                distance = sample.BoundaryDistanceMetres;
                substeps++;
                elapsed += dt;
                remaining -= dt;
                if (sample.InvalidState) break;
            }
            LastForceEvaluation = finalEvaluation;
            LastWorldBoundsTelemetry = new RetailWorldBoundsTelemetry(
                true, distance, dvx, dvy, dvz,
                hardClamped, invalidState, substeps);
        }

        private bool KeepaliveDue(long nowMs, FlightTuning tuning)
        {
            return _everEmitted && nowMs - _lastStampMs >= (long)(tuning.RestKeepaliveSeconds * 1000.0);
        }

        private FlightEmit EmitAt(long nowMs, double stepSeconds)
        {
            long stamp = NextStamp(nowMs, stepSeconds);
            return new FlightEmit(
                true,
                FlightIntegrator.ToControlPoint(_state, stamp),
                FlightIntegrator.PackedRotation(_state));
        }

        /// <summary>
        /// The idle-bob point: the resting pose with a wall-clock sine on Y and
        /// its true derivative as the vertical velocity, so the client's hermite
        /// tangents follow the bob instead of fighting it. Not Arrived - an
        /// arrived point claims zero velocity, and this one is honestly moving.
        /// </summary>
        private FlightEmit EmitBobbedAt(long nowMs, double stepSeconds, FlightTuning tuning)
        {
            long stamp = NextStamp(nowMs, stepSeconds);
            double omega = 2.0 * System.Math.PI / FlightTuning.IdleBobPeriodSeconds;
            double phase = (nowMs / 1000.0) * omega;
            double bobY = tuning.IdleBobMetres * System.Math.Sin(phase);
            double bobVy = tuning.IdleBobMetres * omega * System.Math.Cos(phase);

            return new FlightEmit(
                true,
                new ShipControlPointSpec(
                    stamp, _state.X, _state.Y + bobY, _state.Z,
                    0.0, bobVy, 0.0, arrived: false),
                FlightIntegrator.PackedRotation(_state));
        }

        private long NextStamp(long nowMs, double stepSeconds)
        {
            long stepMs = (long)System.Math.Round(stepSeconds * 1000.0);
            long stamp = _everEmitted && nowMs < _lastStampMs + stepMs ? _lastStampMs + stepMs : nowMs;
            _lastStampMs = stamp;
            _everEmitted = true;
            return stamp;
        }
    }
}

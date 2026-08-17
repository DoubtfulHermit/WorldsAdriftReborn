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
            _manned = false;
            _restEmitted = 0;
        }

        /// <summary>
        /// One cadence tick: integrate if moving or manned, and decide whether a
        /// control point goes out. Call at the control-point cadence
        /// (<paramref name="stepSeconds"/> = ShipMotionPolicy.SendIntervalSeconds).
        /// </summary>
        public FlightEmit Advance(long nowMs, double stepSeconds, FlightTuning tuning,
            int unfurledSails = 0)
        {
            // A latched non-zero throttle is live even if the pilot released the
            // helm before the first integration tick, while the hull is technically
            // still at rest. Without this term that perfectly valid command would
            // be parked forever merely because release won a scheduling race.
            bool live = _manned || !_state.IsAtRest || _input.Throttle != 0f;

            if (live)
            {
                _state = FlightIntegrator.Step(
                    _state, _input, stepSeconds, tuning, unfurledSails);

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
                        return EmitBobbedAt(nowMs, stepSeconds, tuning);
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

                return EmitAt(nowMs, stepSeconds);
            }

            // At rest, unmanned.
            if (_restEmitted <= RestRepeats)
            {
                _restEmitted++;
                return EmitAt(nowMs, stepSeconds);
            }

            if (KeepaliveDue(nowMs, tuning))
            {
                return EmitAt(nowMs, stepSeconds);
            }

            return FlightEmit.Nothing;
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

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
    /// (integrate pilot input), settling (pilot left, integrate to a stop),
    /// resting repeats (belt-and-braces re-sends of the final zero-velocity
    /// point, the ferry's own trick against a dropped last packet), then a slow
    /// KEEPALIVE forever - because a client that joins after the flight seeds
    /// this hull at its SPAWN position and only a live control point corrects it.
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

        /// <summary>The current simulated pose, for logs and for re-man resume.</summary>
        public FlightState State => _state;

        public bool IsManned => _manned;

        /// <summary>The pilot took the helm: fresh input, live cadence.</summary>
        public void Man()
        {
            _manned = true;
            _input = FlightControlInput.Neutral;
            _restEmitted = 0;
        }

        /// <summary>
        /// The pilot left the helm. Input goes neutral and the session SETTLES:
        /// it keeps integrating until the ship stands still rather than freezing
        /// mid-air with stale velocity on the wire.
        /// </summary>
        public void Dismount()
        {
            _manned = false;
            _input = FlightControlInput.Neutral;
            _restEmitted = 0;
        }

        /// <summary>Latest 1111-derived input. Ignored (kept neutral) when unmanned.</summary>
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
        /// One cadence tick: integrate if moving or manned, and decide whether a
        /// control point goes out. Call at the control-point cadence
        /// (<paramref name="stepSeconds"/> = ShipMotionPolicy.SendIntervalSeconds).
        /// </summary>
        public FlightEmit Advance(long nowMs, double stepSeconds, FlightTuning tuning)
        {
            bool live = _manned || !_state.IsAtRest;

            if (live)
            {
                _state = FlightIntegrator.Step(_state, _input, stepSeconds, tuning);

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

                    // Otherwise: emit through the rest repeats, then hold the
                    // keepalive cadence. A parked pilot does not need 4 Hz.
                    _restEmitted++;
                    if (_restEmitted > RestRepeats && !KeepaliveDue(nowMs, tuning))
                    {
                        return FlightEmit.Nothing;
                    }
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

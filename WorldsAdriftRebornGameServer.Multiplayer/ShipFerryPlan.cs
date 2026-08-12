namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// THE FERRY, as pure arithmetic: a straight flight from one global-metre
    /// point to another at a constant speed, sampled once per emit interval into
    /// the <see cref="ShipControlPointSpec"/> stream that
    /// <c>SSPDeadReckoningVisualizer</c> replays through <c>PathFollower</c>.
    ///
    /// It is a TOTAL FUNCTION of the sample index: <see cref="Spec"/> is defined
    /// for every <c>index &gt;= 0</c>, and once the ship has reached the end it
    /// keeps returning the resting point (zero velocity, position = destination)
    /// with a timestamp that still advances by one step. That is deliberate - the
    /// service that drives it decides WHEN to stop emitting, and a zero-velocity
    /// point is safe to repeat at any timestamp - so nothing here has to track
    /// "are we finished".
    ///
    /// WHY INDEX-BASED and not elapsed-time-based: the emit cadence
    /// (<see cref="CadenceTimer"/>) already decides when a sample is due, once
    /// per <see cref="ShipMotionPolicy.SendIntervalSeconds"/>, and refuses to
    /// burst-catch-up after a stall. Sampling on the index it hands out - rather
    /// than on wall-clock elapsed - is what keeps the emitted timestamps on the
    /// ideal grid that <see cref="ShipMotionPolicy.TimestampMsFor"/> builds, so
    /// consecutive points are always exactly one step apart and never trip the
    /// client's 0.228 s reject floor. This mirrors <see cref="SyntheticTimeline"/>,
    /// which advances the 1073 stamp per emitted SAMPLE, not per second.
    ///
    /// Pure: no ENet, no Improbable types, no game install.
    /// </summary>
    public sealed class ShipFerryPlan
    {
        private readonly double _startX, _startY, _startZ;
        private readonly double _unitX, _unitY, _unitZ;
        private readonly double _length;
        private readonly double _speed;
        private readonly double _stepSeconds;
        private readonly long _anchorMs;

        /// <param name="start">Where the ship is now, in global metres (its 190602/1130 seed).</param>
        /// <param name="end">The waypoint, in global metres.</param>
        /// <param name="speedMetresPerSecond">Constant cruise speed; see <see cref="ShipMotionPolicy.SpeedFrom"/>.</param>
        /// <param name="stepSeconds">The emit interval; <see cref="ShipMotionPolicy.SendIntervalSeconds"/>.</param>
        /// <param name="anchorMs">
        /// The wall-clock timestamp (ms since the 2018 epoch) of sample 0, i.e.
        /// when the flight starts. Real time, so the client's server-latency
        /// estimate - <c>UpdateNow - (timestamp - ExtrapolationTime)</c> - is sane
        /// and its playback buffer stays populated.
        /// </param>
        public ShipFerryPlan(FixedPointPosition start, FixedPointPosition end, double speedMetresPerSecond, double stepSeconds, long anchorMs)
        {
            if (stepSeconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(stepSeconds));
            }
            if (speedMetresPerSecond <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(speedMetresPerSecond));
            }

            _startX = start.MetresX;
            _startY = start.MetresY;
            _startZ = start.MetresZ;

            double dx = end.MetresX - _startX;
            double dy = end.MetresY - _startY;
            double dz = end.MetresZ - _startZ;
            _length = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            if (_length > 1e-9)
            {
                _unitX = dx / _length;
                _unitY = dy / _length;
                _unitZ = dz / _length;
            }
            // else: start == end, unit stays (0,0,0); every Spec is the resting
            // point. A zero-length ferry is a caller mistake, not a crash.

            _speed = speedMetresPerSecond;
            _stepSeconds = stepSeconds;
            _anchorMs = anchorMs;
        }

        /// <summary>The straight-line distance the flight covers, in metres.</summary>
        public double LengthMetres => _length;

        /// <summary>
        /// The sample index at which the ship first reaches the destination, i.e.
        /// the index of the FIRST resting point. Emitting up to and including this
        /// index flies the whole path and comes to rest; anything past it repeats
        /// the resting point. Never negative; 0 for a zero-length plan.
        /// </summary>
        public long ArrivalIndex
        {
            get
            {
                if (_length <= 1e-9 || _speed <= 0.0 || _stepSeconds <= 0.0)
                {
                    return 0;
                }
                double samples = _length / (_speed * _stepSeconds);
                return (long)Math.Ceiling(samples);
            }
        }

        /// <summary>
        /// The control point for one sample. See the type remarks for why this is
        /// total and why "arrived" points keep advancing their timestamp.
        /// </summary>
        public ShipControlPointSpec Spec(long index)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            long timestampMs = ShipMotionPolicy.TimestampMsFor(_anchorMs, index, _stepSeconds);

            double travelled = index * _stepSeconds * _speed;
            bool arrived = travelled >= _length;
            double distance = arrived ? _length : travelled;

            double x = _startX + _unitX * distance;
            double y = _startY + _unitY * distance;
            double z = _startZ + _unitZ * distance;

            // Zero velocity at rest - the point the client can extrapolate from
            // without drifting. While cruising, the path derivative, so the
            // client's own extrapolation between our points (and its
            // rigidbody.velocity) matches the motion instead of guessing it.
            double vx = arrived ? 0.0 : _unitX * _speed;
            double vy = arrived ? 0.0 : _unitY * _speed;
            double vz = arrived ? 0.0 : _unitZ * _speed;

            return new ShipControlPointSpec(timestampMs, x, y, z, vx, vy, vz, arrived);
        }
    }
}

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// The pilot's control state, as the server holds it between 1111
    /// ShipControlInput updates: throttle, vertical, and the three ship axes.
    ///
    /// WHY THIS PERSISTS RATHER THAN DECAYS. The client's generated updater
    /// diff-suppresses unchanged sends (ShipControlInput
    /// FinishAndSend_ResolveDiff, VERIFIED in the decompiled gencode): while the
    /// pilot HOLDS full throttle the client sends NOTHING, because nothing
    /// changed. So "no packet" means "input unchanged", never "input released" -
    /// the server must keep the last value until a new one arrives. A staleness
    /// timeout here would stop a ship mid-flight while
    /// its pilot is happily holding W.
    ///
    /// FIELD MEANING, off ShipControlsBehaviour.SendData
    /// (acs/ShipControlsBehaviour.cs:174-188): Throttle and Vertical are the
    /// deadzone-applied -1..1 axes; ShipAxes is (pitch, yaw, roll), each -1..1.
    /// All five are integrated since v3: yaw steers, mouse-pitch dives/climbs
    /// (blended with Vertical), mouse-roll is the banked turn (see
    /// <see cref="FlightIntegrator"/>).
    /// </summary>
    public readonly struct FlightControlInput : System.IEquatable<FlightControlInput>
    {
        // The retail lever occasionally reports a tiny signed residual when it
        // visually reaches centre (live case logged as throttle=-0). Latching
        // that noise after dismount commanded an endless ~1 cm/s cruise and kept
        // the whole domain publishing forever. This is far below a meaningful
        // propulsion command and applies only at the latch boundary.
        // Live 2026-08-21: the authored helm looked exactly centred but its last
        // quantized wire value was -0.09.  The old 0.01 boundary consequently
        // latched reverse thrust after dismount.  One tenth is the first wire
        // step outside that observed centre detent; commands beyond it remain
        // deliberate forward/reverse lever positions.
        private const float LatchedThrottleDeadzone = 0.1f;

        // The client normally applies its own control deadzone, but the same live
        // trace delivered (pitch, roll)=(0.08, -0.03) at a visually neutral helm
        // and then diff-suppressed the return to zero.  Normalize only the three
        // transient steering axes here.  Throttle remains a physical latched
        // lever and uses the separate dismount boundary above.
        private const float TransientAxisDeadzone = 0.1f;
        public FlightControlInput(float throttle, float vertical, float axisPitch, float axisYaw, float axisRoll)
        {
            Throttle = Sanitize(throttle);
            Vertical = Sanitize(vertical);
            AxisPitch = SanitizeTransientAxis(axisPitch);
            AxisYaw = SanitizeTransientAxis(axisYaw);
            AxisRoll = SanitizeTransientAxis(axisRoll);
        }

        /// <summary>Forward drive, -1..1. Negative is reverse.</summary>
        public float Throttle { get; }

        /// <summary>Climb/descend, -1..1.</summary>
        public float Vertical { get; }

        /// <summary>
        /// ShipAxes.x - pitch, -1..1, accumulated from MOUSE Y
        /// (MouseInputProvider: ShipPitch = "Mouse Y"). Drives climb/dive,
        /// blended with <see cref="Vertical"/>; retail sign: positive = nose
        /// DOWN (the FSIM torque map's +X).
        /// </summary>
        public float AxisPitch { get; }

        /// <summary>ShipAxes.y - yaw, -1..1, from A/D. Steers the heading.</summary>
        public float AxisYaw { get; }

        /// <summary>
        /// ShipAxes.z - roll, -1..1, accumulated from MOUSE X
        /// (MouseInputProvider: ShipRoll = "Mouse X"). The banked turn: adds to
        /// the yaw rate; retail sign: positive = bank RIGHT (the torque map's
        /// forward*(-z)).
        /// </summary>
        public float AxisRoll { get; }

        /// <summary>All axes at zero: the input of an empty helm.</summary>
        public static FlightControlInput Neutral => default;

        public bool IsNeutral =>
            Throttle == 0f && Vertical == 0f && AxisPitch == 0f && AxisYaw == 0f && AxisRoll == 0f;

        /// <summary>
        /// A copy with individual fields replaced - the merge shape for a DELTA
        /// 1111 update, whose generated Update carries only the fields that
        /// changed (each an Option). Null keeps the current value.
        /// </summary>
        public FlightControlInput Merge(float? throttle, float? vertical, float? axisPitch, float? axisYaw, float? axisRoll)
        {
            return new FlightControlInput(
                throttle ?? Throttle,
                vertical ?? Vertical,
                axisPitch ?? AxisPitch,
                axisYaw ?? AxisYaw,
                axisRoll ?? AxisRoll);
        }

        /// <summary>
        /// The helm's latched propulsion lever after nobody is holding the wheel.
        /// Throttle is a persistent lever (including explicit stop and reverse),
        /// while climb and the three steering axes are active piloting controls and
        /// must be released to zero so an empty helm cannot keep turning or climbing.
        /// </summary>
        public FlightControlInput LatchedThrottleOnly() =>
            new FlightControlInput(System.MathF.Abs(Throttle) <= LatchedThrottleDeadzone
                ? 0f
                : Throttle, 0f, 0f, 0f, 0f);

        private static float SanitizeTransientAxis(float value)
        {
            float sanitized = Sanitize(value);
            return System.MathF.Abs(sanitized) <= TransientAxisDeadzone ? 0f : sanitized;
        }

        /// <summary>
        /// A client-supplied axis, made safe: NaN/Infinity become 0 (a broken
        /// packet must not steer the ship to NaN - the client REJECTS a NaN
        /// control point and the whole flight would go silent), anything else is
        /// clamped to the -1..1 the client's own deadzone math promises.
        /// </summary>
        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }
            return value < -1f ? -1f : (value > 1f ? 1f : value);
        }

        /// <summary>
        /// Field equality - what the helm-feedback echo dedupes on: an unchanged
        /// input must cost zero packets, so "changed" has to be exact.
        /// </summary>
        public bool Equals(FlightControlInput other) =>
            Throttle == other.Throttle && Vertical == other.Vertical
            && AxisPitch == other.AxisPitch && AxisYaw == other.AxisYaw && AxisRoll == other.AxisRoll;

        public override bool Equals(object? obj) => obj is FlightControlInput other && Equals(other);

        public override int GetHashCode() =>
            System.HashCode.Combine(Throttle, Vertical, AxisPitch, AxisYaw, AxisRoll);

        public static bool operator ==(FlightControlInput a, FlightControlInput b) => a.Equals(b);
        public static bool operator !=(FlightControlInput a, FlightControlInput b) => !a.Equals(b);

        public override string ToString() =>
            "throttle=" + Throttle.ToString("0.##") + " vertical=" + Vertical.ToString("0.##")
            + " axes=(" + AxisPitch.ToString("0.##") + ", " + AxisYaw.ToString("0.##")
            + ", " + AxisRoll.ToString("0.##") + ")";
    }
}

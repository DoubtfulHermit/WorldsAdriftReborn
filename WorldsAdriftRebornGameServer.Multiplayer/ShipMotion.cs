namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// ONE control point a server-driven ship publishes, as plain numbers.
    ///
    /// It is the pure-assembly analogue of the game's <c>ShipControlPoint</c>
    /// (Bossa.Travellers.Motion.Prediction): the same five things - a timestamp,
    /// a position, a velocity, and (implicitly, added at the wire edge) a
    /// rotation and an fsimIdHash - but expressed as doubles and longs so the
    /// PATH and the TIMELINE that produce them can be built and asserted on
    /// without the game's assemblies, exactly as <see cref="FixedPointPosition"/>
    /// and <see cref="RelayTimestampPolicy"/> already are.
    ///
    /// The rotation is not here on purpose: a hull flying a straight ferry does
    /// not bank or yaw, and the client rejects a NaN rotation
    /// (<c>ControlPoint.ValidateControlPoint</c>), so the wire edge stamps the
    /// identity sentinel <c>Quaternion32(1023)</c> and this struct never has to
    /// carry an orientation it would only get wrong.
    ///
    /// POSITION IS GLOBAL METRES, not fixed point and not Unity-space. VERIFIED:
    /// <c>ControlPoint(ShipControlPoint)</c> copies <c>controlPoint.position</c>
    /// straight into a <c>Vector3d</c> and only <c>Remap()</c> later subtracts the
    /// client's origin (<c>Position.RemapGlobalToUnityVector()</c>). The 1130
    /// seed in ComponentsSerializer feeds it <c>at.MetresX/Y/Z</c> the same way.
    /// </summary>
    public readonly struct ShipControlPointSpec : IEquatable<ShipControlPointSpec>
    {
        public ShipControlPointSpec(long timestampMs, double x, double y, double z, double vx, double vy, double vz, bool arrived)
        {
            TimestampMs = timestampMs;
            X = x;
            Y = y;
            Z = z;
            Vx = vx;
            Vy = vy;
            Vz = vz;
            Arrived = arrived;
        }

        /// <summary>
        /// Milliseconds since the client's own <c>SynchronisedTime.EpochTime</c>
        /// (2018-03-01T00:00:00Z). The client converts it back with
        /// <c>FromMillisecondsSinceEpoch(t) = t / 1000</c> and treats it as an
        /// NTP wall-clock instant, so this is a real time, not an uptime.
        /// </summary>
        public long TimestampMs { get; }

        /// <summary>Global-metre position.</summary>
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        /// <summary>Velocity in metres per second, in global axes.</summary>
        public double Vx { get; }
        public double Vy { get; }
        public double Vz { get; }

        /// <summary>
        /// Whether the ship has reached the end of its path at this point, i.e.
        /// this is the (zero-velocity) resting point. A zero-velocity control
        /// point is the one the client can extrapolate from forever without
        /// drifting, so it is the safe thing to repeat once the ferry is done.
        /// </summary>
        public bool Arrived { get; }

        public bool Equals(ShipControlPointSpec other) =>
            TimestampMs == other.TimestampMs && X == other.X && Y == other.Y && Z == other.Z
            && Vx == other.Vx && Vy == other.Vy && Vz == other.Vz && Arrived == other.Arrived;

        public override bool Equals(object? obj) => obj is ShipControlPointSpec other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(TimestampMs, X, Y, Z, Vx, Vy, Vz, Arrived);

        public override string ToString() =>
            "t=" + TimestampMs + "ms (" + X.ToString("0.##") + ", " + Y.ToString("0.##") + ", "
            + Z.ToString("0.##") + ") m, v=(" + Vx.ToString("0.##") + ", " + Vy.ToString("0.##")
            + ", " + Vz.ToString("0.##") + ") m/s" + (Arrived ? " [arrived]" : "");
    }

    /// <summary>
    /// The three numbers the shipped client uses to accept or reject a 1130
    /// control point, and nothing else. Pure, so they are pinned by a test
    /// rather than by re-reading a decompile: every one of them was measured off
    /// the client and is silent when violated.
    ///
    /// VERIFIED against ~/Games/WAReborn-decompiled:
    ///
    /// * <b>Cadence.</b> <c>ControlPoint.ValidateControlPoints</c>
    ///   (Bossa.DeadReckoning/ControlPoint.cs:113-126) drops a point whose
    ///   timestamp is LESS THAN its predecessor's (a regression) and one closer
    ///   than <c>desiredInterval * 0.95</c>, where <c>desiredInterval</c> is
    ///   <c>ShipConfiguration.SendInterval = 0.24</c> (ShipConfiguration.cs:24).
    ///   So the floor is <c>0.24 * 0.95 = 0.228 s</c> and points must be
    ///   monotonic. We emit at exactly <see cref="SendIntervalSeconds"/>, which
    ///   is 12 ms of headroom over the floor - enough that ordinary loop jitter
    ///   in WHEN a tick fires does not matter, because the TIMESTAMP is on an
    ///   ideal grid (see <see cref="ShipFerryPlan"/>) and never the wall-clock of
    ///   the emit.
    /// * <b>fsimIdHash.</b> <c>SSPDeadReckoningVisualizer.AddControlPoint</c>
    ///   (:102-115) drops a point whose hash equals
    ///   <c>SpatialOS.Configuration.WorkerId.GetHashCode()</c> (a client's own
    ///   echo) and, if the hash CHANGES between consecutive points, calls
    ///   <c>IgnoreControlPointsUntil(t + ServerBoundaryRejectionTime)</c> and
    ///   ignores <see cref="ServerBoundaryRejectionSeconds"/> of motion. Hence
    ///   one fixed value for the whole flight - <see cref="ShipHull.FsimIdHash"/>.
    /// </summary>
    public static class ShipMotionPolicy
    {
        /// <summary>
        /// 1130 SSPPredictedMotionState - the component a control point rides on.
        /// Named here, in the pure layer, so the two services and the seed branch
        /// cannot drift apart on the id and a test can pin it.
        /// </summary>
        public const uint ComponentId = 1130;

        /// <summary>
        /// The interval a ship control point is published at, in seconds.
        /// VERIFIED equal to the client's own <c>ShipConfiguration.SendInterval</c>
        /// - not a coincidence, it is the rate the whole dead-reckoning path was
        /// tuned around, and matching it is what keeps every consecutive pair
        /// legal by construction.
        /// </summary>
        public const double SendIntervalSeconds = 0.24;

        /// <summary>
        /// The 0.95 factor from <c>ValidateControlPoints</c>, applied. A pair
        /// closer than this in time is dropped, so this is the hard floor the
        /// timeline must never violate: <c>0.24 * 0.95 = 0.228 s</c>.
        /// </summary>
        public const double MinSeparationSeconds = SendIntervalSeconds * 0.95;

        /// <summary>
        /// <c>ShipConfiguration.ServerBoundaryRejectionTime = 0.5</c>: how long the
        /// client ignores control points after the fsimIdHash changes. We never
        /// change the hash, so this is only here to explain why we do not.
        /// </summary>
        public const double ServerBoundaryRejectionSeconds = 0.5;

        /// <summary>Default ferry speed, m/s: brisk enough to read as flight, slow enough to watch.</summary>
        public const double DefaultSpeedMetresPerSecond = 15.0;

        /// <summary>
        /// Speed clamp. The floor keeps a mistyped 0 from making a ferry that
        /// never arrives; the ceiling keeps a mistyped 5000 from teleporting the
        /// hull a kilometre between two 0.24 s points (which the client's own
        /// spline correction would then fight).
        /// </summary>
        public const double MinSpeedMetresPerSecond = 1.0;
        public const double MaxSpeedMetresPerSecond = 60.0;

        /// <summary>
        /// The timestamp for sample <paramref name="index"/> of a flight that
        /// started at <paramref name="anchorMs"/>, on an IDEAL grid: anchor plus
        /// exactly index x step, rounded to whole milliseconds the same way the
        /// client's <c>ToMillisecondsSinceEpoch(t) = round(t * 1000)</c> does.
        ///
        /// Ideal-grid, not wall-clock-of-emit, on purpose: it guarantees every
        /// consecutive pair is exactly <see cref="SendIntervalSeconds"/> apart
        /// however much the server's main loop jittered, so no emit can ever
        /// accidentally land inside the <see cref="MinSeparationSeconds"/> reject
        /// window. And because the grid advances by the step per EMIT while real
        /// time advances by AT LEAST the step per emit (the cadence never bursts),
        /// the stamps stay at or behind real wall-clock - which only ever grows
        /// the client's measured server latency, never inverts it.
        /// </summary>
        public static long TimestampMsFor(long anchorMs, long index, double stepSeconds)
        {
            return anchorMs + (long)Math.Round(index * stepSeconds * 1000.0);
        }

        /// <summary>The seconds between two control-point timestamps.</summary>
        public static double SeparationSeconds(long previousMs, long ms) => (ms - previousMs) / 1000.0;

        /// <summary>
        /// Whether the client would ACCEPT this pair on cadence grounds: not a
        /// regression, and not closer than the 0.228 s floor. The exact negation
        /// of the two <c>ValidateControlPoints</c> drop branches. A tiny epsilon
        /// absorbs the millisecond rounding so a legitimately 240 ms pair is not
        /// failed by a 0.2288-vs-0.228 float comparison.
        /// </summary>
        public static bool IsLegalSeparation(long previousMs, long ms)
        {
            double separation = SeparationSeconds(previousMs, ms);
            return separation >= 0.0 && separation >= MinSeparationSeconds - 1e-6;
        }

        /// <summary>
        /// The ferry speed from an environment-variable string: invariant-culture,
        /// clamped to [<see cref="MinSpeedMetresPerSecond"/>,
        /// <see cref="MaxSpeedMetresPerSecond"/>], and
        /// <see cref="DefaultSpeedMetresPerSecond"/> for anything unset,
        /// unparsable, NaN or non-positive. Never throws - a bad env var must not
        /// take the server down.
        /// </summary>
        public static double SpeedFrom(string? env)
        {
            if (string.IsNullOrWhiteSpace(env)
                || !double.TryParse(env, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double speed)
                || double.IsNaN(speed)
                || speed <= 0.0)
            {
                return DefaultSpeedMetresPerSecond;
            }

            return Math.Clamp(speed, MinSpeedMetresPerSecond, MaxSpeedMetresPerSecond);
        }
    }
}

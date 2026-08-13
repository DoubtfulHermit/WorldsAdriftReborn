using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// Every number that decides how a piloted ship FEELS, in one place, each an
    /// env knob with a clamped default.
    ///
    /// HONESTY NOTE, load-bearing: these are RECONSTRUCTED values, not retail
    /// physics. The original flight was a UnityWorker rigidbody force stack
    /// (engines, wings, sails, lift - findings-flight-windwalls-storms.md,
    /// "What produces forces in the original FSIM") whose tuning constants lived
    /// in prefab data this project does not have. This phase-1 integrator is a
    /// deliberate, documented approximation: throttle drives forward speed along
    /// the heading, yaw input turns the heading, vertical input drives altitude.
    /// Every default below is a guess made tunable so the live game can adjust
    /// it without a rebuild.
    ///
    /// The speed ceiling is shared with <see cref="ShipMotionPolicy"/>: at the
    /// 0.24 s control-point cadence a speed above
    /// <see cref="ShipMotionPolicy.MaxSpeedMetresPerSecond"/> starts to read as
    /// teleporting between points, which the client's spline correction fights.
    /// </summary>
    public sealed class FlightTuning
    {
        /// <summary>WAREBORN_FLIGHT_MAX_SPEED - top forward speed, m/s.</summary>
        public const double DefaultMaxSpeedMps = 12.0;

        /// <summary>WAREBORN_FLIGHT_ACCEL - how fast speed approaches the throttle target, m/s^2.</summary>
        public const double DefaultAccelMps2 = 4.0;

        /// <summary>WAREBORN_FLIGHT_YAW_RATE - full-stick turn rate, degrees/s.</summary>
        public const double DefaultYawRateDegPerSec = 20.0;

        /// <summary>WAREBORN_FLIGHT_CLIMB_RATE - full-stick climb/descend rate, m/s.</summary>
        public const double DefaultClimbRateMps = 6.0;

        /// <summary>WAREBORN_FLIGHT_REVERSE_FACTOR - reverse speed as a fraction of forward.</summary>
        public const double DefaultReverseFactor = 0.4;

        /// <summary>
        /// WAREBORN_FLIGHT_REST_KEEPALIVE - seconds between at-rest control-point
        /// repeats once a flown ship has settled. NOT zero: a client that joins
        /// AFTER a flight seeds the hull's 1130/190602 at the SPAWN position
        /// (WorldEntity.Position is immutable and persistence stores the spawn),
        /// and only a live control point moves it to where the ship really is.
        /// One reliable ~60-byte packet per interval per hull is the whole cost.
        /// </summary>
        public const double DefaultRestKeepaliveSeconds = 5.0;

        // ------------------------------------------------------------------
        // The v2 FEEL knobs. All reconstructions; the live verdict on v1 was
        // "feels like faking the flying" - constant yaw rate, instant stops,
        // dead-level turns. These add inertia and attitude. Zero disables the
        // attitude ones.
        // ------------------------------------------------------------------

        /// <summary>
        /// WAREBORN_FLIGHT_YAW_ACCEL - how fast the TURN RATE itself ramps,
        /// deg/s^2. The ease-in/ease-out of a turn: at 25 with the default
        /// 20 deg/s rate, full lock takes ~0.8 s to reach and ~0.8 s to unwind,
        /// so turns start and end soft instead of stepping.
        /// </summary>
        public const double DefaultYawAccelDegPerSec2 = 25.0;

        /// <summary>
        /// WAREBORN_FLIGHT_BANK_ANGLE - maximum ROLL into a full-rate turn,
        /// degrees. The control point carries full rotation and the client
        /// SlerpUnclamps it between points, so banked points render as smooth
        /// visible banking. 0 = flat turns (the v1 look).
        /// </summary>
        public const double DefaultBankAngleDeg = 8.0;

        /// <summary>
        /// WAREBORN_FLIGHT_PITCH_ANGLE - maximum nose PITCH at full climb or
        /// descent rate, degrees. 0 = level climbs (the v1 look).
        /// </summary>
        public const double DefaultPitchAngleDeg = 5.0;

        /// <summary>
        /// WAREBORN_FLIGHT_ATTITUDE_SMOOTHING - the time constant, seconds, roll
        /// and pitch ease toward their targets with (and back to level).
        /// </summary>
        public const double DefaultAttitudeSmoothingSeconds = 0.5;

        /// <summary>
        /// WAREBORN_FLIGHT_VELOCITY_SMOOTHING - the time constant, seconds, the
        /// VELOCITY VECTOR chases the commanded heading*speed with. This is what
        /// makes a turn CARVE - the ship keeps some of its old momentum and
        /// drifts through the turn - instead of pivoting on the spot with its
        /// velocity snapped to the new heading every step. 0 = the v1 pivot.
        /// </summary>
        public const double DefaultVelocitySmoothingSeconds = 0.6;

        /// <summary>
        /// WAREBORN_FLIGHT_IDLE_BOB - amplitude, metres, of a slow vertical bob
        /// while a pilot is at the helm of a resting ship. DEFAULT OFF (0): it
        /// keeps the 4 Hz point stream alive for the whole manned-idle time
        /// (which the idle emitter otherwise drops to the keepalive), and a
        /// moving "resting" ship is a taste call the operator should opt into.
        /// The period is fixed at <see cref="IdleBobPeriodSeconds"/>.
        /// </summary>
        public const double DefaultIdleBobMetres = 0.0;

        /// <summary>The idle bob's fixed period, seconds.</summary>
        public const double IdleBobPeriodSeconds = 6.0;

        public double MaxSpeedMps { get; }
        public double AccelMps2 { get; }
        public double YawRateRadPerSec { get; }
        public double ClimbRateMps { get; }
        public double ReverseFactor { get; }
        public double RestKeepaliveSeconds { get; }
        public double YawAccelRadPerSec2 { get; }
        public double BankMaxRadians { get; }
        public double PitchMaxRadians { get; }
        public double AttitudeSmoothingSeconds { get; }
        public double VelocitySmoothingSeconds { get; }
        public double IdleBobMetres { get; }

        /// <summary>
        /// WAREBORN_FLIGHT_INVERT_YAW=1 flips the yaw direction. Insurance: the
        /// client's ShipYaw axis sign convention has never been observed live,
        /// and "A turns the ship right" should be a config flip, not a rebuild.
        /// </summary>
        public bool InvertYaw { get; }

        public FlightTuning(
            double maxSpeedMps = DefaultMaxSpeedMps,
            double accelMps2 = DefaultAccelMps2,
            double yawRateDegPerSec = DefaultYawRateDegPerSec,
            double climbRateMps = DefaultClimbRateMps,
            double reverseFactor = DefaultReverseFactor,
            double restKeepaliveSeconds = DefaultRestKeepaliveSeconds,
            bool invertYaw = false,
            double yawAccelDegPerSec2 = DefaultYawAccelDegPerSec2,
            double bankAngleDeg = DefaultBankAngleDeg,
            double pitchAngleDeg = DefaultPitchAngleDeg,
            double attitudeSmoothingSeconds = DefaultAttitudeSmoothingSeconds,
            double velocitySmoothingSeconds = DefaultVelocitySmoothingSeconds,
            double idleBobMetres = DefaultIdleBobMetres)
        {
            MaxSpeedMps = Clamp(maxSpeedMps, 1.0, ShipMotionPolicy.MaxSpeedMetresPerSecond, DefaultMaxSpeedMps);
            AccelMps2 = Clamp(accelMps2, 0.5, 30.0, DefaultAccelMps2);
            YawRateRadPerSec = Clamp(yawRateDegPerSec, 2.0, 90.0, DefaultYawRateDegPerSec) * System.Math.PI / 180.0;
            ClimbRateMps = Clamp(climbRateMps, 0.5, 30.0, DefaultClimbRateMps);
            ReverseFactor = Clamp(reverseFactor, 0.0, 1.0, DefaultReverseFactor);
            RestKeepaliveSeconds = Clamp(restKeepaliveSeconds, 1.0, 60.0, DefaultRestKeepaliveSeconds);
            InvertYaw = invertYaw;
            YawAccelRadPerSec2 = Clamp(yawAccelDegPerSec2, 5.0, 360.0, DefaultYawAccelDegPerSec2) * System.Math.PI / 180.0;
            // 0 legitimately DISABLES banking/pitch, so the floor is 0, not "a bit".
            BankMaxRadians = Clamp(bankAngleDeg, 0.0, 30.0, DefaultBankAngleDeg) * System.Math.PI / 180.0;
            PitchMaxRadians = Clamp(pitchAngleDeg, 0.0, 30.0, DefaultPitchAngleDeg) * System.Math.PI / 180.0;
            AttitudeSmoothingSeconds = Clamp(attitudeSmoothingSeconds, 0.05, 5.0, DefaultAttitudeSmoothingSeconds);
            // 0 disables velocity smoothing (the v1 pivot behaviour, kept reachable).
            VelocitySmoothingSeconds = Clamp(velocitySmoothingSeconds, 0.0, 5.0, DefaultVelocitySmoothingSeconds);
            IdleBobMetres = Clamp(idleBobMetres, 0.0, 2.0, DefaultIdleBobMetres);
        }

        /// <summary>
        /// The tuning from the process environment, via an injected getter so a
        /// test never has to mutate real env vars. Unset/garbage falls back to
        /// the default; out-of-range clamps. Never throws - a bad env var must
        /// not take the server down (same contract as ShipMotionPolicy.SpeedFrom).
        /// </summary>
        public static FlightTuning FromEnvironment(System.Func<string, string?> getenv)
        {
            return new FlightTuning(
                Parse(getenv("WAREBORN_FLIGHT_MAX_SPEED"), DefaultMaxSpeedMps),
                Parse(getenv("WAREBORN_FLIGHT_ACCEL"), DefaultAccelMps2),
                Parse(getenv("WAREBORN_FLIGHT_YAW_RATE"), DefaultYawRateDegPerSec),
                Parse(getenv("WAREBORN_FLIGHT_CLIMB_RATE"), DefaultClimbRateMps),
                Parse(getenv("WAREBORN_FLIGHT_REVERSE_FACTOR"), DefaultReverseFactor),
                Parse(getenv("WAREBORN_FLIGHT_REST_KEEPALIVE"), DefaultRestKeepaliveSeconds),
                getenv("WAREBORN_FLIGHT_INVERT_YAW") == "1",
                Parse(getenv("WAREBORN_FLIGHT_YAW_ACCEL"), DefaultYawAccelDegPerSec2),
                Parse(getenv("WAREBORN_FLIGHT_BANK_ANGLE"), DefaultBankAngleDeg),
                Parse(getenv("WAREBORN_FLIGHT_PITCH_ANGLE"), DefaultPitchAngleDeg),
                Parse(getenv("WAREBORN_FLIGHT_ATTITUDE_SMOOTHING"), DefaultAttitudeSmoothingSeconds),
                Parse(getenv("WAREBORN_FLIGHT_VELOCITY_SMOOTHING"), DefaultVelocitySmoothingSeconds),
                Parse(getenv("WAREBORN_FLIGHT_IDLE_BOB"), DefaultIdleBobMetres));
        }

        private static double Parse(string? env, double fallback)
        {
            if (string.IsNullOrWhiteSpace(env)
                || !double.TryParse(env, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.IsNaN(value) || double.IsInfinity(value))
            {
                return fallback;
            }
            return value;
        }

        private static double Clamp(double value, double min, double max, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return fallback;
            }
            return value < min ? min : (value > max ? max : value);
        }

        public override string ToString() =>
            "maxSpeed=" + MaxSpeedMps.ToString("0.#", CultureInfo.InvariantCulture)
            + " m/s accel=" + AccelMps2.ToString("0.#", CultureInfo.InvariantCulture)
            + " m/s^2 yawRate=" + (YawRateRadPerSec * 180.0 / System.Math.PI).ToString("0.#", CultureInfo.InvariantCulture)
            + " deg/s climb=" + ClimbRateMps.ToString("0.#", CultureInfo.InvariantCulture)
            + " m/s reverse=" + ReverseFactor.ToString("0.##", CultureInfo.InvariantCulture)
            + " keepalive=" + RestKeepaliveSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s"
            + (InvertYaw ? " (yaw inverted)" : "");
    }
}

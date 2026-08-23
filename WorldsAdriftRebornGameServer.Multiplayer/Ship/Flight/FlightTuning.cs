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

        // ------------------------------------------------------------------
        // The v3 MOUSE-STEERING knobs. The client's helm sends THREE axes in
        // 1111 ShipAxes: yaw from A/D, and pitch/roll accumulated from the
        // MOUSE (MouseInputProvider: ShipRoll = "Mouse X", ShipPitch =
        // "Mouse Y"; ShipControlsBehaviour.UpdateAxes). v1/v2 consumed only
        // yaw, so the mouse moved the (echoed) helm and not the ship. These
        // map the other two axes to motion, with retail's own SIGNS - the FSIM
        // torque map was right*x + up*y + forward*(-z) (ShipControlVisualizer
        /// .UpdateTorques), i.e. +pitch = nose DOWN, +roll = bank RIGHT.
        // ------------------------------------------------------------------

        /// <summary>
        /// WAREBORN_FLIGHT_PITCH_RATE - vertical speed, m/s, at full mouse-pitch
        /// deflection. Retail pitched the hull and let the wings translate
        /// attitude into climb; the reconstruction drives the climb directly and
        /// lets the existing attitude display nose the ship to match. BLENDS
        /// with (never replaces) the LShift/LCtrl Vertical axis. 0 disables
        /// mouse pitch.
        /// </summary>
        public const double DefaultPitchRateMps = 4.0;

        /// <summary>
        /// WAREBORN_FLIGHT_ROLL_TURN_FACTOR - how much of the full yaw rate a
        /// full mouse-roll deflection contributes: the BANKED TURN (a rolled
        /// ship turns). Sums with A/D and the total is clamped to the yaw-rate
        /// cap, so mouse+keys together never out-turn the tuning. The existing
        /// bank attitude follows the TOTAL turn rate, so a mouse-rolled ship
        /// visibly banks into its turn. 0 disables mouse roll.
        /// </summary>
        public const double DefaultRollTurnFactor = 0.7;

        /// <summary>
        /// WAREBORN_FLIGHT_SAIL_BONUS - forward propulsion added by EACH
        /// unfurled sail, as a fraction of the configured base maximum speed
        /// and acceleration. Retail applied one wind force per unfurled sail,
        /// linear in SailState.power; the reconstructed kinematic flight model
        /// has no rigidbody force accumulator, so the equivalent hook is a
        /// linear propulsion-capacity contribution. Four sails count at most,
        /// preventing a sail carpet from exceeding the safe point-stream speed.
        /// </summary>
        public const double DefaultSailBonusPerUnfurled = 0.25;

        public const int MaxContributingSails = 4;

        /// <summary>
        /// WAREBORN_FLIGHT_ENGINE_THRUST - newtons per mounted engine under the
        /// FORCE model (WAREBORN_FLIGHT_FORCES=1). Ignored entirely by the legacy
        /// kinematic path.
        ///
        /// Exposed as a knob for the same reason every other number in this file
        /// is: the live verdict on flight has twice been a feel judgement made at
        /// the helm, and "our speeds are wrong" should be a restart, not a
        /// rebuild. The default is calibrated rather than picked - see
        /// <see cref="ShipForceModel.DefaultEngineThrustNewtons"/> - and because
        /// the shipped drag exponent is 2.5, this knob moves speed only as the
        /// 0.4 power: doubling thrust buys about 1.32x speed.
        /// </summary>
        public double EngineThrustNewtons { get; }

        /// <summary>
        /// WAREBORN_FLIGHT_SAIL_POWER - one unfurled sail's power under the force
        /// model, the linear coefficient in retail's efficiency * |wind| * Power.
        /// The companion knob to the one above, and the one to reach for if canvas
        /// reads as decorative or as overpowered relative to engines.
        /// </summary>
        public double SailPowerNewtons { get; }

        /// <summary>
        /// WAREBORN_FLIGHT_WIND_SPEED - how windy this world is, m/s. The default
        /// is 2.236, retail's own <c>(1, 0, -2)</c> fallback magnitude.
        ///
        /// WHY THIS DESERVES A KNOB rather than staying the constant it was. That
        /// 2.236 is what <c>GlobalWeather</c> returns for a position with NO weather
        /// cell covering it - it is retail's BECALMED case, not a typical retail
        /// wind, and we serve no weather cells at all, so every position in our
        /// world gets it. Standing in for an entire absent weather system with the
        /// value that system used to mean "there is no weather here" is a defensible
        /// starting point and an odd permanent choice.
        ///
        /// It is also the physical wind lever on the bare-hull tier.
        /// WAREBORN_FLIGHT_ENGINE_THRUST moves engines and
        /// WAREBORN_FLIGHT_SAIL_POWER moves canvas, but a hull with neither is
        /// driven purely by the wind, so without this a live "the bare hull is too
        /// slow" verdict would need a rebuild. It moves sails and the baseline
        /// TOGETHER, which is correct rather than convenient: both are the same wind
        /// in retail's equations, and a world where the air moves faster should
        /// carry a bare hull faster AND fill a sail harder. A separate explicit
        /// WAReborn-only bare-hull balance multiplier exists below for deployments
        /// that must tune drift without lying about the shared physical wind.
        ///
        /// Speed scales LINEARLY in this for the baseline and as its SQUARE ROOT for
        /// sails, so doubling it doubles a bare hull's drift but only multiplies a
        /// sailed ship's speed by about 1.4.
        /// </summary>
        public double WindSpeedMps { get; }

        /// <summary>
        /// WAREBORN_FLIGHT_BARE_HULL_MULTIPLIER - WAReborn balance tuning applied
        /// only to the throttle-requested, no-canvas baseline wind carry. It does
        /// not alter the wind field, sail force, engine force, wall influence or
        /// drag. Retail requires propulsion from canvas or engines, so this
        /// speculative compatibility seam defaults OFF; any experiment must opt in.
        /// </summary>
        public double BareHullDriveMultiplier { get; }

        /// <summary>
        /// WAREBORN_FLIGHT_WIND_FIELD - 0 (the default, and production today) to 1.
        /// Turns the single global constant into a wind FIELD that varies by place
        /// and by time, and turns the bare-hull baseline back around to blow
        /// DOWNWIND the way retail's did.
        ///
        /// WHY THOSE TWO THINGS SHARE ONE KNOB. They are one design, not two
        /// features. <see cref="ShipForceModel.BaselineDriveSpeedMps(double)"/>
        /// explains that we aim the hull wind along the HEADING purely because our
        /// wind is a single constant, so a faithful downwind aim would condemn a
        /// bare hull to one compass direction for ever. Remove the constant and
        /// that objection goes with it - but only then. Splitting these would let
        /// an operator enable exactly the combination that comment warns against.
        ///
        /// At 0 this is off in the strongest sense available: <see cref="WindField"/>
        /// returns the same vector the code returned before it existed, computed
        /// the same way, and the baseline keeps its heading aim. There is a test
        /// that asserts equality rather than approximate equality for that.
        ///
        /// THE PRICE, stated because it is why the default is 0 and the veer
        /// ceiling is 40 degrees rather than free rotation: the client is already
        /// drawing wind streaks, bending grass and flying flags along
        /// <c>(1,0,-2)</c>, and it will keep doing that whatever this server
        /// believes, because that field is fed by 1139 weather cells and those are
        /// forbidden. Every degree here is a degree by which the wind a player can
        /// SEE disagrees with the wind they FEEL. See <see cref="WindField"/>.
        /// </summary>
        public WindFieldVariation WindVariation { get; }

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
        public double PitchRateMps { get; }
        public double RollTurnFactor { get; }
        public double SailBonusPerUnfurled { get; }

        /// <summary>
        /// WAREBORN_FLIGHT_INVERT_PITCH=1 flips mouse pitch (mouse up = climb
        /// instead of retail's nose-down). Same insurance class as InvertYaw:
        /// the live mouse sign is a config flip, not a rebuild.
        /// </summary>
        public bool InvertPitch { get; }

        /// <summary>WAREBORN_FLIGHT_INVERT_ROLL=1 flips mouse roll.</summary>
        public bool InvertRoll { get; }

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
            double idleBobMetres = DefaultIdleBobMetres,
            double pitchRateMps = DefaultPitchRateMps,
            double rollTurnFactor = DefaultRollTurnFactor,
            bool invertPitch = false,
            bool invertRoll = false,
            double sailBonusPerUnfurled = DefaultSailBonusPerUnfurled,
            double engineThrustNewtons = ShipForceModel.DefaultEngineThrustNewtons,
            double sailPowerNewtons = ShipForceModel.DefaultSailPowerNewtonsPerWind,
            double windSpeedMps = -1.0,
            double windFieldVariation = 0.0,
            double bareHullDriveMultiplier = 1.0)
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
            // 0 legitimately DISABLES each mouse axis, so both floors are 0.
            PitchRateMps = Clamp(pitchRateMps, 0.0, 30.0, DefaultPitchRateMps);
            RollTurnFactor = Clamp(rollTurnFactor, 0.0, 2.0, DefaultRollTurnFactor);
            InvertPitch = invertPitch;
            InvertRoll = invertRoll;
            SailBonusPerUnfurled = Clamp(
                sailBonusPerUnfurled, 0.0, 1.0, DefaultSailBonusPerUnfurled);
            // Floors of 0 are meaningful: 0 thrust is an engineless ship and 0 sail
            // power is bare poles, both of which a live operator may legitimately
            // want to see. The ceilings are absurdity guards, not balance.
            EngineThrustNewtons = Clamp(
                engineThrustNewtons, 0.0, 100_000.0, ShipForceModel.DefaultEngineThrustNewtons);
            SailPowerNewtons = Clamp(
                sailPowerNewtons, 0.0, 10_000.0, ShipForceModel.DefaultSailPowerNewtonsPerWind);
            // A negative sentinel means "unset": the default is a computed property
            // rather than a const, so it cannot be a parameter default.
            WindSpeedMps = windSpeedMps < 0.0
                ? ShipForceModel.DefaultWindSpeedMps
                : Clamp(windSpeedMps, 0.0, 100.0, ShipForceModel.DefaultWindSpeedMps);
            // Clamps to [0,1] inside the struct rather than here, because the same
            // clamp has to hold for a caller that constructs one directly.
            WindVariation = new WindFieldVariation(windFieldVariation);
            BareHullDriveMultiplier = Clamp(bareHullDriveMultiplier, 0.0, 4.0, 0.0);
        }

        /// <summary>
        /// Linear retail-shaped contribution of the currently rigged canvas.
        /// Zero/negative means no sail contribution; a bounded count makes this
        /// safe even if a malformed save mounts hundreds of sails.
        /// </summary>
        public double SailPropulsionScale(int unfurledSails)
        {
            int contributing = unfurledSails < 0 ? 0
                : (unfurledSails > MaxContributingSails ? MaxContributingSails : unfurledSails);
            return 1.0 + (contributing * SailBonusPerUnfurled);
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
                Parse(getenv("WAREBORN_FLIGHT_IDLE_BOB"), DefaultIdleBobMetres),
                Parse(getenv("WAREBORN_FLIGHT_PITCH_RATE"), DefaultPitchRateMps),
                Parse(getenv("WAREBORN_FLIGHT_ROLL_TURN_FACTOR"), DefaultRollTurnFactor),
                getenv("WAREBORN_FLIGHT_INVERT_PITCH") == "1",
                getenv("WAREBORN_FLIGHT_INVERT_ROLL") == "1",
                Parse(getenv("WAREBORN_FLIGHT_SAIL_BONUS"), DefaultSailBonusPerUnfurled),
                Parse(getenv("WAREBORN_FLIGHT_ENGINE_THRUST"),
                    ShipForceModel.DefaultEngineThrustNewtons),
                Parse(getenv("WAREBORN_FLIGHT_SAIL_POWER"),
                    ShipForceModel.DefaultSailPowerNewtonsPerWind),
                // The 100 m/s ceiling is retail's own: GlobalWeather returns a zero
                // field above it rather than a stronger one.
                Parse(getenv("WAREBORN_FLIGHT_WIND_SPEED"),
                    ShipForceModel.DefaultWindSpeedMps),
                // Default 0 = OFF. The wind is a constant until an operator says
                // otherwise, because turning it into a field makes what a player
                // FEELS diverge from what the client DRAWS - see WindVariation.
                Parse(getenv("WAREBORN_FLIGHT_WIND_FIELD"), 0.0),
                Parse(getenv("WAREBORN_FLIGHT_BARE_HULL_MULTIPLIER"), 0.0));
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
            + " m/s pitchRate=" + PitchRateMps.ToString("0.#", CultureInfo.InvariantCulture)
            + " m/s rollTurn=" + RollTurnFactor.ToString("0.##", CultureInfo.InvariantCulture)
            + " sailBonus=" + SailBonusPerUnfurled.ToString("0.##", CultureInfo.InvariantCulture) + "/sail"
            + " bareHull=" + BareHullDriveMultiplier.ToString("0.##", CultureInfo.InvariantCulture) + "x"
            + " reverse=" + ReverseFactor.ToString("0.##", CultureInfo.InvariantCulture)
            + " keepalive=" + RestKeepaliveSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s"
            + (InvertYaw ? " (yaw inverted)" : "")
            + (InvertPitch ? " (pitch inverted)" : "")
            + (InvertRoll ? " (roll inverted)" : "");
    }
}

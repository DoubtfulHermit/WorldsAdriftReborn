using System;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// The ship's pose and motion as the server simulates it - v2, the FEEL
    /// state. Beyond v1's position/yaw/speed it carries the TURN RATE (so turns
    /// ease in and out instead of stepping), a ROLL and PITCH attitude (so the
    /// ship banks into turns and noses into climbs - the control point carries
    /// full rotation and the client SlerpUnclamps it between points), and the
    /// actual VELOCITY VECTOR (so a turn carves - old momentum drifts through -
    /// instead of pivoting with velocity snapped to the new heading).
    ///
    /// AXIS CONVENTIONS (Unity, left-handed; VERIFIED against the client's own
    /// composition order - Quaternion.Euler applies Z then X then Y, i.e.
    /// q = qY * qX * qZ):
    /// <list type="bullet">
    /// <item>Yaw about +Y; 0 faces +Z, positive turns the nose toward +X.</item>
    /// <item>Pitch about +X; POSITIVE X-rotation noses DOWN (+Z toward -Y), so a
    ///   CLIMB carries a NEGATIVE pitch angle (nose up).</item>
    /// <item>Roll about +Z; POSITIVE Z-rotation lifts the right side (+X toward
    ///   +Y) = banks LEFT, so a RIGHT turn (positive yaw rate) carries a
    ///   NEGATIVE roll (right side dips into the turn).</item>
    /// </list>
    /// </summary>
    public readonly struct FlightState
    {
        public FlightState(
            double x, double y, double z,
            double yawRadians, double yawRateRadPerSec,
            double rollRadians, double pitchRadians,
            double speedCmdMps,
            double vxMps, double vyMps, double vzMps)
        {
            X = x;
            Y = y;
            Z = z;
            YawRadians = yawRadians;
            YawRateRadPerSec = yawRateRadPerSec;
            RollRadians = rollRadians;
            PitchRadians = pitchRadians;
            SpeedCmdMps = speedCmdMps;
            VxMps = vxMps;
            VyMps = vyMps;
            VzMps = vzMps;
        }

        /// <summary>Global-metre position (the space 1130 control points carry).</summary>
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        /// <summary>Heading about world +Y; 0 faces +Z, positive toward +X.</summary>
        public double YawRadians { get; }

        /// <summary>The CURRENT turn rate, eased toward the stick's target.</summary>
        public double YawRateRadPerSec { get; }

        /// <summary>Bank attitude; negative = banked right. Cosmetic-only: does not steer.</summary>
        public double RollRadians { get; }

        /// <summary>Nose attitude; negative = nose up. Cosmetic-only: does not steer.</summary>
        public double PitchRadians { get; }

        /// <summary>The COMMANDED forward speed (throttle target chased under the accel limit).</summary>
        public double SpeedCmdMps { get; }

        /// <summary>
        /// The actual velocity, m/s, global axes - the exact derivative of the
        /// position steps, which is what the control point must report so the
        /// client's hermite tangents match the path.
        /// </summary>
        public double VxMps { get; }
        public double VyMps { get; }
        public double VzMps { get; }

        /// <summary>
        /// Standing still AND settled: no velocity, no turn rate, wings level.
        /// The publisher's "safe to go quiet" condition, so every component must
        /// reach EXACT zero (the integrator snaps them).
        /// </summary>
        public bool IsAtRest =>
            VxMps == 0.0 && VyMps == 0.0 && VzMps == 0.0
            && YawRateRadPerSec == 0.0 && RollRadians == 0.0 && PitchRadians == 0.0;

        public static FlightState AtRestAt(double x, double y, double z, double yawRadians = 0.0)
        {
            return new FlightState(x, y, z, yawRadians, 0, 0, 0, 0, 0, 0, 0);
        }

        /// <summary>Horizontal ground speed, for the stats line.</summary>
        public double GroundSpeedMps => Math.Sqrt((VxMps * VxMps) + (VzMps * VzMps));

        public override string ToString() =>
            "(" + X.ToString("0.#") + ", " + Y.ToString("0.#") + ", " + Z.ToString("0.#")
            + ") m yaw=" + (YawRadians * 180.0 / Math.PI).ToString("0.#")
            + " deg v=" + GroundSpeedMps.ToString("0.##") + " m/s vy=" + VyMps.ToString("0.##")
            + " m/s bank=" + (RollRadians * 180.0 / Math.PI).ToString("0.#") + " deg";
    }

    /// <summary>
    /// The pure flight math: pilot input + current state + dt -> next state, and
    /// state -> the numbers a 1130 control point carries. No clock, no wire, no
    /// game types - every rule is asserted in unit tests.
    ///
    /// THE v2 MODEL (a documented reconstruction - retail constants are lost,
    /// every number is a knob in <see cref="FlightTuning"/>):
    /// <list type="number">
    /// <item>The stick sets a TARGET turn rate; the actual rate chases it under
    ///   the yaw-accel limit (ease-in/ease-out) and the heading integrates the
    ///   rate. Snap-to-target inside one step, so a released stick ends at
    ///   exactly zero rate.</item>
    /// <item>Roll chases -bankMax * (rate/rateMax) and pitch chases
    ///   -pitchMax * (vy/climbRate), both with the attitude time constant, both
    ///   snapping to exact level. They are attitude ONLY - they never steer -
    ///   which is honest (no reconstructed lift coupling to get wrong) and
    ///   reads correctly at these small angles.</item>
    /// <item>Throttle sets the commanded speed (accel-limited, exact-zero snap);
    ///   the VELOCITY VECTOR chases heading*cmd + up*vertical with the velocity
    ///   time constant, so turns carve and stops glide. Position advances by
    ///   the smoothed vector, so the reported velocity IS the path derivative.</item>
    /// </list>
    /// </summary>
    public static class FlightIntegrator
    {
        /// <summary>
        /// Below this the velocity snaps to its target: the boundary between
        /// "gliding out the last centimetres per second" and "the publisher can
        /// never sleep because 1e-9 m/s is not zero". 2 cm/s is invisible at the
        /// client's interpolation scale.
        /// </summary>
        private const double SnapEpsilon = 0.02;

        /// <summary>Attitude snap, radians (~0.06 deg) - same reasoning as <see cref="SnapEpsilon"/>.</summary>
        private const double AttitudeSnapEpsilon = 0.001;

        /// <summary>One fixed step at the control-point cadence.</summary>
        public static FlightState Step(FlightState state, FlightControlInput input, double dtSeconds,
            FlightTuning tuning, int unfurledSails = 0)
        {
            if (dtSeconds <= 0.0 || double.IsNaN(dtSeconds) || double.IsInfinity(dtSeconds))
            {
                return state;
            }

            // 1. Turn rate eases toward the combined stick target, heading
            // integrates it. TWO inputs turn the ship: A/D (AxisYaw) and the
            // MOUSE's roll axis (AxisRoll) - the banked turn. Retail's FSIM
            // torque map is right*x + up*y + forward*(-z)
            // (ShipControlVisualizer.UpdateTorques): a POSITIVE roll input is a
            // NEGATIVE Z torque = right side dips = the ship banks RIGHT and a
            // banked-right ship turns RIGHT - hence roll ADDS to the turn with
            // the same sign as yaw. The sum is clamped to +-1 before scaling so
            // keys + mouse together can never exceed the tuned rate cap, and the
            // bank attitude below follows the TOTAL rate, so a mouse-rolled ship
            // visibly banks into its turn.
            double yawSign = tuning.InvertYaw ? -1.0 : 1.0;
            double rollSign = tuning.InvertRoll ? -1.0 : 1.0;
            double turnInput = Math.Clamp(
                (yawSign * input.AxisYaw) + (rollSign * input.AxisRoll * tuning.RollTurnFactor),
                -1.0, 1.0);
            double yawRateTarget = turnInput * tuning.YawRateRadPerSec;
            double yawRate = ApproachWithSnap(
                state.YawRateRadPerSec, yawRateTarget, tuning.YawAccelRadPerSec2 * dtSeconds, 0.0005);
            double yaw = WrapAngle(state.YawRadians + yawRate * dtSeconds);

            // 2. Commanded speed under the accel limit (exact-zero snap).
            // Retail's SailBehaviour added one wind force per UNFURLED sail,
            // linear in SailState.power. This server's reconstructed flight is
            // kinematic (no rigidbody force accumulator or weather worker), so
            // mounted canvas contributes linearly to FORWARD propulsion while
            // the helm asks for forward drive. Reverse is engine/control power,
            // not something a sail should amplify. The result is capped at the
            // shared legal control-point speed.
            double sailScale = tuning.SailPropulsionScale(unfurledSails);
            double forwardMax = Math.Min(
                ShipMotionPolicy.MaxSpeedMetresPerSecond,
                tuning.MaxSpeedMps * sailScale);
            double speedTarget = input.Throttle >= 0f
                ? input.Throttle * forwardMax
                : input.Throttle * tuning.MaxSpeedMps * tuning.ReverseFactor;
            // Sail force increases the acceleration toward a higher FORWARD
            // target. Furling/removing canvas or pulling the lever back uses the
            // ordinary deceleration, rather than sails somehow braking harder.
            double acceleration = tuning.AccelMps2;
            if (speedTarget > state.SpeedCmdMps && speedTarget > 0.0)
            {
                acceleration *= sailScale;
            }
            double speedCmd = ApproachWithSnap(
                state.SpeedCmdMps, speedTarget, acceleration * dtSeconds, SnapEpsilon);

            // 3. Velocity vector chases the commanded velocity. Time-constant
            // form: a fraction dt/tau of the gap per step (capped at 1 = the
            // v1 instant behaviour when smoothing is 0).
            double targetVx = Math.Sin(yaw) * speedCmd;
            double targetVz = Math.Cos(yaw) * speedCmd;
            // Vertical BLENDS two inputs: the LShift/LCtrl Vertical axis (the
            // v1 behaviour, unchanged when the mouse is centred) and the
            // MOUSE's pitch axis. Retail sign: a POSITIVE pitch input is a
            // POSITIVE X torque = nose DOWN = dive, hence the minus. Both are
            // <=1, so the sum is naturally bounded by climbRate + pitchRate.
            double pitchSign = tuning.InvertPitch ? -1.0 : 1.0;
            double targetVy = (input.Vertical * tuning.ClimbRateMps)
                - (pitchSign * input.AxisPitch * tuning.PitchRateMps);
            double blend = tuning.VelocitySmoothingSeconds <= 0.0
                ? 1.0
                : Math.Min(1.0, dtSeconds / tuning.VelocitySmoothingSeconds);
            double vx = ChaseWithSnap(state.VxMps, targetVx, blend, SnapEpsilon);
            double vy = ChaseWithSnap(state.VyMps, targetVy, blend, SnapEpsilon);
            double vz = ChaseWithSnap(state.VzMps, targetVz, blend, SnapEpsilon);

            // 4. Attitude chases the motion. Negative signs per the axis
            // conventions in the FlightState remarks: right turn = right side
            // dips, climb = nose up.
            double rollTarget = tuning.YawRateRadPerSec <= 0.0 ? 0.0
                : -tuning.BankMaxRadians * (yawRate / tuning.YawRateRadPerSec);
            double pitchTarget = tuning.ClimbRateMps <= 0.0 ? 0.0
                : -tuning.PitchMaxRadians * Math.Clamp(vy / tuning.ClimbRateMps, -1.0, 1.0);
            double attitudeBlend = Math.Min(1.0, dtSeconds / tuning.AttitudeSmoothingSeconds);
            double roll = ChaseWithSnap(state.RollRadians, rollTarget, attitudeBlend, AttitudeSnapEpsilon);
            double pitch = ChaseWithSnap(state.PitchRadians, pitchTarget, attitudeBlend, AttitudeSnapEpsilon);

            // 5. Position advances by the SMOOTHED velocity - so the velocity
            // this state reports is exactly the derivative of the path the
            // client will interpolate.
            double x = state.X + vx * dtSeconds;
            double y = state.Y + vy * dtSeconds;
            double z = state.Z + vz * dtSeconds;

            return new FlightState(x, y, z, yaw, yawRate, roll, pitch, speedCmd, vx, vy, vz);
        }

        /// <summary>
        /// The control-point numbers for a state: position and the true path
        /// derivative. Arrived mirrors IsAtRest.
        /// </summary>
        public static ShipControlPointSpec ToControlPoint(FlightState state, long timestampMs)
        {
            return new ShipControlPointSpec(
                timestampMs, state.X, state.Y, state.Z,
                state.VxMps, state.VyMps, state.VzMps, state.IsAtRest);
        }

        /// <summary>
        /// The full attitude as the game's packed 32-bit wire quaternion:
        /// q = qY(yaw) * qX(pitch) * qZ(roll), the same composition order as
        /// Unity's Quaternion.Euler, through the same smallest-three encoder
        /// every placed structure uses. A level, north-facing state encodes to
        /// the identity SENTINEL 1023, so unflown ships stay byte-identical.
        /// </summary>
        public static uint PackedRotation(FlightState state)
        {
            (double w, double x, double y, double z) = AttitudeQuaternion(state);
            return Quaternion32Packing.Encode((float)w, (float)x, (float)y, (float)z);
        }

        /// <summary>
        /// The unpacked attitude quaternion, exposed for the tests (packing
        /// quantizes to 10 bits per component; the composition math is asserted
        /// here at full precision).
        /// </summary>
        public static (double W, double X, double Y, double Z) AttitudeQuaternion(FlightState state)
        {
            double hy = state.YawRadians * 0.5;
            double hp = state.PitchRadians * 0.5;
            double hr = state.RollRadians * 0.5;

            // qY = (cy, 0, sy, 0); qX = (cp, sp, 0, 0); qZ = (cr, 0, 0, sr)
            double cy = Math.Cos(hy), sy = Math.Sin(hy);
            double cp = Math.Cos(hp), sp = Math.Sin(hp);
            double cr = Math.Cos(hr), sr = Math.Sin(hr);

            // qY * qX
            double w1 = cy * cp;
            double x1 = cy * sp;
            double y1 = sy * cp;
            double z1 = -sy * sp;

            // (qY * qX) * qZ
            return (
                (w1 * cr) - (z1 * sr),
                (x1 * cr) + (y1 * sr),
                (y1 * cr) - (x1 * sr),
                (w1 * sr) + (z1 * cr));
        }

        /// <summary>
        /// Toward the target by at most maxDelta, landing exactly on it inside
        /// one step or within <paramref name="snapEpsilon"/> of it.
        /// </summary>
        private static double ApproachWithSnap(double current, double target, double maxDelta, double snapEpsilon)
        {
            double diff = target - current;
            if (Math.Abs(diff) <= Math.Max(maxDelta, snapEpsilon))
            {
                return target;
            }
            return current + Math.Sign(diff) * maxDelta;
        }

        /// <summary>
        /// Exponential-style chase: a fraction of the gap per step, snapping to
        /// the target when the gap is inside the epsilon (an exponential decay
        /// never REACHES zero on its own, and IsAtRest needs exact zeros).
        /// </summary>
        private static double ChaseWithSnap(double current, double target, double blend, double snapEpsilon)
        {
            double next = current + ((target - current) * blend);
            if (Math.Abs(next - target) <= snapEpsilon)
            {
                return target;
            }
            return next;
        }

        /// <summary>Wraps to (-pi, pi].</summary>
        private static double WrapAngle(double radians)
        {
            double wrapped = Math.IEEERemainder(radians, 2.0 * Math.PI);
            return wrapped <= -Math.PI ? wrapped + 2.0 * Math.PI : wrapped;
        }
    }
}

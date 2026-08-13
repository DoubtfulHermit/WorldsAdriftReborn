using System;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// The ship's pose and motion as the server simulates it: global-metre
    /// position, a yaw heading, a signed forward speed and a vertical rate.
    ///
    /// YAW ONLY, on purpose (phase 1): a heading about the world +Y axis is the
    /// whole orientation. Pitch and roll inputs are received and ignored - a
    /// ship that turns flat is honest and controllable; a reconstructed
    /// pitch/roll with wrong coupling reads as broken. Yaw 0 faces +Z (the
    /// hull's spawn facing - built hulls seed the identity rotation), positive
    /// yaw turns toward +X, matching Unity's left-handed Y rotation, so
    /// forward = (sin yaw, 0, cos yaw).
    /// </summary>
    public readonly struct FlightState
    {
        public FlightState(double x, double y, double z, double yawRadians, double speedMps, double verticalMps)
        {
            X = x;
            Y = y;
            Z = z;
            YawRadians = yawRadians;
            SpeedMps = speedMps;
            VerticalMps = verticalMps;
        }

        /// <summary>Global-metre position (the space 1130 control points carry).</summary>
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        /// <summary>Heading about world +Y; 0 faces +Z, positive turns toward +X.</summary>
        public double YawRadians { get; }

        /// <summary>Signed forward speed, m/s. Negative is reverse.</summary>
        public double SpeedMps { get; }

        /// <summary>Vertical rate, m/s. Direct-driven by the Vertical input.</summary>
        public double VerticalMps { get; }

        /// <summary>Standing still: every extrapolation from this pose lands on it.</summary>
        public bool IsAtRest => SpeedMps == 0.0 && VerticalMps == 0.0;

        public static FlightState AtRestAt(double x, double y, double z, double yawRadians = 0.0)
        {
            return new FlightState(x, y, z, yawRadians, 0.0, 0.0);
        }

        public override string ToString() =>
            "(" + X.ToString("0.#") + ", " + Y.ToString("0.#") + ", " + Z.ToString("0.#")
            + ") m yaw=" + (YawRadians * 180.0 / Math.PI).ToString("0.#") + " deg v="
            + SpeedMps.ToString("0.##") + " m/s vy=" + VerticalMps.ToString("0.##") + " m/s";
    }

    /// <summary>
    /// The pure flight math: pilot input + current state + dt -> next state, and
    /// state -> the numbers a 1130 control point carries. No clock, no wire, no
    /// game types - every rule here is asserted in unit tests.
    ///
    /// THE MODEL (a documented reconstruction, see <see cref="FlightTuning"/>):
    /// <list type="bullet">
    /// <item>Throttle sets a TARGET speed (max speed forward, a fraction of it in
    ///   reverse); actual speed approaches it at the accel limit and SNAPS to the
    ///   target when within one step, so "throttle released" ends at exactly 0
    ///   and the at-rest state is reachable (a 1e-9 residual speed would keep the
    ///   publisher emitting forever).</item>
    /// <item>Yaw input turns the heading at the yaw rate; the heading wraps to
    ///   (-pi, pi] so a long flight cannot walk the angle toward float trouble.</item>
    /// <item>Vertical input direct-drives the vertical rate. No inertia: the
    ///   retail feel had lift-force ramps, but direct drive is predictable and
    ///   cannot oscillate.</item>
    /// <item>Position advances along the heading by speed*dt plus the vertical
    ///   step. Velocity reported to the client is exactly that derivative, so
    ///   PathFollower's extrapolation between points matches the path.</item>
    /// </list>
    /// </summary>
    public static class FlightIntegrator
    {
        /// <summary>
        /// One fixed step. <paramref name="dtSeconds"/> is the control-point
        /// cadence (0.24 s); the caller owns the clock.
        /// </summary>
        public static FlightState Step(FlightState state, FlightControlInput input, double dtSeconds, FlightTuning tuning)
        {
            if (dtSeconds <= 0.0 || double.IsNaN(dtSeconds) || double.IsInfinity(dtSeconds))
            {
                return state;
            }

            // Heading first, so this step's travel uses the new heading - at
            // 0.24 s steps the difference is invisible, but it makes a turn
            // start on the point the input arrived with.
            double yawSign = tuning.InvertYaw ? -1.0 : 1.0;
            double yaw = WrapAngle(state.YawRadians + yawSign * input.AxisYaw * tuning.YawRateRadPerSec * dtSeconds);

            // Speed approaches the throttle target under the accel limit.
            double target = input.Throttle >= 0f
                ? input.Throttle * tuning.MaxSpeedMps
                : input.Throttle * tuning.MaxSpeedMps * tuning.ReverseFactor;
            double speed = ApproachWithSnap(state.SpeedMps, target, tuning.AccelMps2 * dtSeconds);

            // Vertical is direct-driven; exact zero when the stick is centred.
            double vertical = input.Vertical * tuning.ClimbRateMps;

            double x = state.X + Math.Sin(yaw) * speed * dtSeconds;
            double z = state.Z + Math.Cos(yaw) * speed * dtSeconds;
            double y = state.Y + vertical * dtSeconds;

            return new FlightState(x, y, z, yaw, speed, vertical);
        }

        /// <summary>
        /// The control-point numbers for a state: position as-is (global metres),
        /// velocity as the heading-aligned derivative the last Step produced.
        /// Arrived mirrors IsAtRest, which is what lets the publisher treat the
        /// settled point exactly like the ferry's arrival point (zero-velocity,
        /// safe to extrapolate from forever).
        /// </summary>
        public static ShipControlPointSpec ToControlPoint(FlightState state, long timestampMs)
        {
            double vx = Math.Sin(state.YawRadians) * state.SpeedMps;
            double vz = Math.Cos(state.YawRadians) * state.SpeedMps;
            return new ShipControlPointSpec(
                timestampMs, state.X, state.Y, state.Z, vx, state.VerticalMps, vz, state.IsAtRest);
        }

        /// <summary>
        /// The heading as the game's packed 32-bit wire quaternion: a rotation of
        /// yaw about +Y is (w, x, y, z) = (cos(yaw/2), 0, sin(yaw/2), 0), fed
        /// through the same smallest-three encoder every placed structure uses.
        /// Yaw 0 encodes to the identity SENTINEL 1023 (the encoder special-cases
        /// |w| == 1), which is exactly the value every at-rest hull already
        /// carries - so an unflown ship's points are byte-identical to before.
        /// </summary>
        public static uint PackedRotation(FlightState state)
        {
            double half = state.YawRadians * 0.5;
            return Quaternion32Packing.Encode((float)Math.Cos(half), 0f, (float)Math.Sin(half), 0f);
        }

        /// <summary>
        /// Toward the target by at most maxDelta, landing EXACTLY on it inside
        /// one step. The snap is load-bearing - see the type remarks.
        /// </summary>
        private static double ApproachWithSnap(double current, double target, double maxDelta)
        {
            double diff = target - current;
            if (Math.Abs(diff) <= maxDelta)
            {
                return target;
            }
            return current + Math.Sign(diff) * maxDelta;
        }

        /// <summary>Wraps to (-pi, pi].</summary>
        private static double WrapAngle(double radians)
        {
            double wrapped = Math.IEEERemainder(radians, 2.0 * Math.PI);
            // IEEERemainder returns [-pi, pi]; map the -pi edge to +pi so the
            // representation is unique.
            return wrapped <= -Math.PI ? wrapped + 2.0 * Math.PI : wrapped;
        }
    }
}

using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// One recovered wing acting as a steering surface. The up-vector comes from
    /// the mount's packed rotation - the same seam as sail yaw: live retail wings
    /// articulate on joints whose state the server does not own. Power is WAREBORN
    /// tuning because the retail WingState.Power values are lost.
    /// </summary>
    public readonly record struct VectorWingSurface(ShadowVector3 LocalUp, double PowerNewtonMetres)
    {
        public bool IsValid => LocalUp.IsFinite
            && Math.Abs(LocalUp.Magnitude - 1.0) <= 1e-6
            && double.IsFinite(PowerNewtonMetres) && PowerNewtonMetres >= 0.0
            && PowerNewtonMetres <= VectorRigidBodyShadowPolicy.MaxForceNewtons;
    }

    /// <summary>
    /// The complete 6-DOF vector state of one hull, plus the lift command
    /// smoothing state. The smoothing pair is INVISIBLE per-life state and is
    /// deliberately bundled here so every reset, capture and restore of the
    /// vector state carries it - a restart must not open with a one-frame fall
    /// because the command lift force silently restarted at zero.
    /// </summary>
    public readonly record struct VectorFlightState(
        ShadowVector3 Position,
        ShadowQuaternion Orientation,
        ShadowVector3 VelocityMps,
        ShadowVector3 AngularVelocityRadPerSec,
        double CommandLiftForceNewtons,
        double CommandLiftSmoothingVelocity)
    {
        public bool IsFinite => Position.IsFinite && Orientation.IsValid
            && VelocityMps.IsFinite && AngularVelocityRadPerSec.IsFinite
            && double.IsFinite(CommandLiftForceNewtons)
            && double.IsFinite(CommandLiftSmoothingVelocity);
    }

    /// <summary>Everything one accepted 20 ms vector step consumes. Pure data.</summary>
    public readonly struct VectorFlightStepInput
    {
        public VectorFlightStepInput(string stableHullKey, double deltaSeconds,
            ShadowMassProperties mass, ShadowVector3 halfExtentsMetres,
            IReadOnlyList<ShadowPropulsor> propulsors, IReadOnlyList<VectorWingSurface> wings,
            double engineSpin, ShadowVector3 worldWindMps, FlightControlInput input,
            LiftRuntimeStepPolicy lift)
        {
            StableHullKey = stableHullKey;
            DeltaSeconds = deltaSeconds;
            Mass = mass;
            HalfExtentsMetres = halfExtentsMetres;
            Propulsors = propulsors ?? Array.Empty<ShadowPropulsor>();
            Wings = wings ?? Array.Empty<VectorWingSurface>();
            EngineSpin = engineSpin;
            WorldWindMps = worldWindMps;
            Input = input;
            Lift = lift;
        }

        public string StableHullKey { get; }
        public double DeltaSeconds { get; }

        /// <summary>
        /// Total mass, COM and diagonal inertia FROM THE ONE ShipMassSnapshot.
        /// The runtime never re-estimates mass properties (rule 3 of the mass
        /// contract); a hull whose geometry would not decode arrives with zero
        /// COM/inertia and simply gets no angular response.
        /// </summary>
        public ShadowMassProperties Mass { get; }

        public ShadowVector3 HalfExtentsMetres { get; }

        /// <summary>Engines and sails in stable ascending entity-id order.</summary>
        public IReadOnlyList<ShadowPropulsor> Propulsors { get; }

        public IReadOnlyList<VectorWingSurface> Wings { get; }
        public double EngineSpin { get; }
        public ShadowVector3 WorldWindMps { get; }
        public FlightControlInput Input { get; }
        public LiftRuntimeStepPolicy Lift { get; }

        public bool IsValid => !string.IsNullOrWhiteSpace(StableHullKey)
            && StableHullKey.Length <= CollisionShadowLimits.MaxIdLength
            && double.IsFinite(DeltaSeconds) && DeltaSeconds > 0.0
            && Mass.TotalMassKg > 0.0 && double.IsFinite(Mass.TotalMassKg)
            && Mass.CentreOfMass.IsFinite && Mass.DiagonalInertiaKgM2.IsFinite
            && HalfExtentsMetres.IsFinite && HalfExtentsMetres.X > 0.0
            && HalfExtentsMetres.Y > 0.0 && HalfExtentsMetres.Z > 0.0
            && Propulsors.Count <= VectorRigidBodyShadowPolicy.MaxParts
            && Wings.Count <= VectorRigidBodyShadowPolicy.MaxParts
            && double.IsFinite(EngineSpin) && EngineSpin >= -1.0 && EngineSpin <= 1.0
            && WorldWindMps.IsFinite
            && Lift.IsValid;
    }

    /// <summary>What one step decided and measured - published, never re-evaluated.</summary>
    public readonly struct VectorFlightStepResult
    {
        public VectorFlightStepResult(bool integrated, string disposition,
            LiftGravityEvaluation lift, ShadowVector3 worldForceNewtons,
            ShadowVector3 bodyTorqueNewtonMetres, CollisionShadowTelemetry collision,
            int acceptedParts, int rejectedParts, bool snappedToRest)
        {
            Integrated = integrated;
            Disposition = disposition;
            Lift = lift;
            WorldForceNewtons = worldForceNewtons;
            BodyTorqueNewtonMetres = bodyTorqueNewtonMetres;
            Collision = collision;
            AcceptedParts = acceptedParts;
            RejectedParts = rejectedParts;
            SnappedToRest = snappedToRest;
        }

        /// <summary>False = the step failed closed and the hull merely coasted.</summary>
        public bool Integrated { get; }
        public string Disposition { get; }
        public LiftGravityEvaluation Lift { get; }
        public ShadowVector3 WorldForceNewtons { get; }
        public ShadowVector3 BodyTorqueNewtonMetres { get; }
        public CollisionShadowTelemetry Collision { get; }
        public int AcceptedParts { get; }
        public int RejectedParts { get; }
        public bool SnappedToRest { get; }
    }

    /// <summary>
    /// The per-hull 6-DOF vector integrator over the existing pure primitives:
    /// <see cref="ShadowForceAccumulator"/> for forces at mount positions with
    /// torque from recovered geometry, <see cref="RetailLiftGravityShadow"/> +
    /// <see cref="LiftGravityRuntime"/> for the vertical axis THROUGH the
    /// reviewed <see cref="IntegratedFlightShadow"/> seam (gravity exactly once),
    /// and the recovered <see cref="ShipForceModel"/> drag law.
    ///
    /// It owns NO clock: the caller feeds it whole 20 ms steps consumed from the
    /// hull's existing <see cref="FixedFlightClock"/> batch. It never reads
    /// DateTime or Stopwatch and keeps no time remainder of its own.
    ///
    /// PROVENANCE, stated once: propulsor geometry and force equations are
    /// RECOVERED with WAREBORN power tuning; lift/gravity is the RECOVERED retail
    /// vertical policy; horizontal drag is the RECOVERED serialized drag law;
    /// angular damping is an APPROXIMATION (the retail rigidbody's angularDrag
    /// is lost); the wing steering torque is the RECOVERED WingVisualizer shape
    /// with WAREBORN power tuning; sail and wing mount orientation comes from
    /// the packed mount rotation, not live joint state (the visible seam).
    /// </summary>
    public sealed class VectorFlightRuntime
    {
        /// <summary>See the sail-yaw open item: live joints are not server state.</summary>
        public const string SailYawSeam =
            "sail/wing orientation from packed mount rotation; live joint state unavailable";

        /// <summary>
        /// WAREBORN stabilization, not retail: Unity slept the rigidbody; the
        /// server snaps to an exact rest so the publisher's IsAtRest contract
        /// (exact zeros) holds for vector hulls too.
        /// </summary>
        public const double RestLinearSpeedThresholdMps = 0.01;
        public const double RestAngularSpeedThresholdRadPerSec = 0.005;
        public const double RestVerticalAccelerationThresholdMps2 = 0.001;

        /// <summary>APPROXIMATION: the retail rigidbody angularDrag value is lost.</summary>
        public const double AngularDampingPerSecond = 1.0;

        /// <summary>RECOVERED: WingVisualizer.UpdateMotion's MaxWingPowerSpeed.</summary>
        public const double MaxWingPowerSpeedMps = 10.0;

        /// <summary>RECOVERED: the Lerp(0.2, 1.0, alignment) floor.</summary>
        public const double WingMinAlignmentFactor = 0.2;

        /// <summary>
        /// WAREBORN tuning: retail WingState.Power is lost. Calibrated so two
        /// wings on the reference hull roughly reproduce the scalar model's
        /// 25 deg/s2 yaw authority; the parity telemetry is the honest check.
        /// </summary>
        public const double DefaultWingTorquePowerNewtonMetres = 12000.0;

        /// <summary>Below this inertia the angular axes are refused, not divided by.</summary>
        public const double MinimumUsableInertiaKgM2 = 1.0;

        private VectorFlightState _state;

        public VectorFlightRuntime(VectorFlightState initial)
        {
            if (!initial.IsFinite) throw new ArgumentException(
                "vector flight state must be finite", nameof(initial));
            _state = initial;
        }

        public VectorFlightState State => _state;

        /// <summary>
        /// Seeds the vector state from a committed scalar pose - promotion,
        /// shadow re-anchoring, and every external pose reset (dock snap,
        /// emergency stop) come through here so the two paths can never hold
        /// two divergent poses for one hull.
        /// </summary>
        public static VectorFlightState FromFlightState(FlightState scalar)
        {
            (double w, double x, double y, double z) = FlightIntegrator.AttitudeQuaternion(scalar);
            if (!ShadowQuaternion.TryNormalized(w, x, y, z, out ShadowQuaternion orientation))
            {
                orientation = ShadowQuaternion.Identity;
            }
            return new VectorFlightState(
                new ShadowVector3(scalar.X, scalar.Y, scalar.Z),
                orientation,
                new ShadowVector3(scalar.VxMps, scalar.VyMps, scalar.VzMps),
                new ShadowVector3(0.0, scalar.YawRateRadPerSec, 0.0),
                CommandLiftForceNewtons: 0.0,
                CommandLiftSmoothingVelocity: 0.0);
        }

        /// <summary>Replaces the state wholesale (restore / external pose reset).</summary>
        public void Reset(VectorFlightState state)
        {
            if (!state.IsFinite) throw new ArgumentException(
                "vector flight state must be finite", nameof(state));
            _state = state;
        }

        /// <summary>
        /// The scalar-shaped projection of a vector state: the SAME pose, in the
        /// representation every existing consumer (session, persistence, 1130
        /// wire packing) speaks. Euler angles are extracted with the exact
        /// inverse of <see cref="FlightIntegrator.AttitudeQuaternion"/>'s
        /// qY*qX*qZ composition, so re-composing them reproduces the vector
        /// orientation - one attitude, two spellings, no second pose stream.
        /// SpeedCmdMps is zero: the vector model has no commanded-speed state.
        /// </summary>
        public static FlightState Project(VectorFlightState state)
        {
            (double yaw, double pitch, double roll) = ExtractYawPitchRoll(state.Orientation);
            return new FlightState(
                state.Position.X, state.Position.Y, state.Position.Z,
                yaw, state.AngularVelocityRadPerSec.Y, roll, pitch,
                speedCmdMps: 0.0,
                state.VelocityMps.X, state.VelocityMps.Y, state.VelocityMps.Z);
        }

        /// <summary>
        /// Inverse of the client's qY(yaw)*qX(pitch)*qZ(roll) Euler composition.
        /// At the gimbal singularity (|pitch| = 90 deg) roll is folded into yaw;
        /// flight attitudes never approach it.
        /// </summary>
        public static (double YawRadians, double PitchRadians, double RollRadians)
            ExtractYawPitchRoll(ShadowQuaternion q)
        {
            double m12 = 2.0 * (q.Y * q.Z - q.W * q.X);
            double m02 = 2.0 * (q.X * q.Z + q.W * q.Y);
            double m22 = 1.0 - 2.0 * (q.X * q.X + q.Y * q.Y);
            double m10 = 2.0 * (q.X * q.Y + q.W * q.Z);
            double m11 = 1.0 - 2.0 * (q.X * q.X + q.Z * q.Z);

            double sinPitch = Math.Clamp(-m12, -1.0, 1.0);
            if (Math.Abs(sinPitch) >= 1.0 - 1e-9)
            {
                double m20 = 2.0 * (q.X * q.Z - q.W * q.Y);
                double m00 = 1.0 - 2.0 * (q.Y * q.Y + q.Z * q.Z);
                return (Math.Atan2(-m20, m00), Math.Asin(sinPitch), 0.0);
            }
            return (Math.Atan2(m02, m22), Math.Asin(sinPitch), Math.Atan2(m10, m11));
        }

        /// <summary>
        /// RECOVERED SHAPE from WingVisualizer.UpdateMotion: steering torque
        /// scales from zero at rest to full at 10 m/s, per axis, with the wing's
        /// up-vector alignment lerped over [0.2, 1]. A flat wing (up = +Y)
        /// pitches and rolls well; a vertical wing (up = +/-X) yaws well. Applied
        /// OUTSIDE the retail 2500 N*m torque dead zone, exactly as retail's
        /// AddTorque bypassed it. The airbrake term is NOT implemented: it needs
        /// the lost per-wing AirBrake value, and inventing one silently would
        /// hide the gap.
        /// </summary>
        public static ShadowVector3 WingSteeringTorque(
            IReadOnlyList<VectorWingSurface> wings, FlightControlInput input, double speedMps)
        {
            if (wings == null || wings.Count == 0 || !double.IsFinite(speedMps))
            {
                return ShadowVector3.Zero;
            }
            double speedFactor = Math.Clamp(speedMps / MaxWingPowerSpeedMps, 0.0, 1.0);
            if (speedFactor <= 0.0) return ShadowVector3.Zero;

            double torqueX = 0.0, torqueY = 0.0, torqueZ = 0.0;
            for (int i = 0; i < wings.Count; i++)
            {
                VectorWingSurface wing = wings[i];
                if (!wing.IsValid) continue;
                double p = speedFactor * wing.PowerNewtonMetres;
                double flatAlignment = AlignmentFactor(Math.Abs(
                    ShadowVector3.Dot(wing.LocalUp, ShadowVector3.Up)));
                double verticalAlignment = AlignmentFactor(Math.Abs(
                    ShadowVector3.Dot(wing.LocalUp, ShadowVector3.Right)));
                torqueX += input.AxisPitch * p * flatAlignment;
                torqueY += input.AxisYaw * p * verticalAlignment;
                torqueZ += -input.AxisRoll * p * flatAlignment;
            }
            return new ShadowVector3(torqueX, torqueY, torqueZ);
        }

        private static double AlignmentFactor(double alignment) =>
            WingMinAlignmentFactor + (1.0 - WingMinAlignmentFactor)
                * Math.Clamp(alignment, 0.0, 1.0);

        /// <summary>
        /// One deterministic 20 ms step. Order, pinned: propulsor forces at
        /// mount positions (local frame, snapshot COM) -> retail-filtered torque
        /// + recovered wing steering torque -> angular integration with labelled
        /// damping -> world-frame force through the IntegratedFlightShadow seam
        /// (lift + gravity exactly once, linear integration) -> recovered
        /// horizontal drag on the committed velocity -> orientation integration
        /// -> rest snap. A step that fails validation COASTS (position advances
        /// on the held velocity) rather than freezing or inventing forces.
        /// </summary>
        public VectorFlightStepResult Step(VectorFlightStepInput input)
        {
            if (!_state.IsFinite)
            {
                // Quarantine: a corrupted state must not integrate further.
                return Rejected("state-not-finite");
            }
            if (!input.IsValid)
            {
                return Coast(input.DeltaSeconds, "input-invalid");
            }

            // 1. Propulsor forces in the hull-local frame, torque about the
            //    snapshot COM. Sail trim consumes the hull-local wind.
            ShadowVector3 localWind = _state.Orientation.InverseRotate(input.WorldWindMps);
            var accumulator = new ShadowForceAccumulator();
            int accepted = 0, rejected = 0;
            for (int i = 0; i < input.Propulsors.Count; i++)
            {
                ShadowPropulsor part = input.Propulsors[i];
                ShadowVector3 force = part.Kind == ShadowPartKind.Engine
                    ? VectorRigidBodyShadow.EngineForce(part, input.EngineSpin)
                    : VectorRigidBodyShadow.TrimmedSailForce(part, localWind);
                if (accumulator.TryAdd(force, part.LocalPosition, input.Mass.CentreOfMass,
                        part.Torqueless))
                {
                    accepted++;
                }
                else
                {
                    rejected++;
                }
            }

            // 2. Torque: the retail dead-zone filter applies to propulsor
            //    geometry torque; wing steering torque bypasses it (recovered).
            ShadowVector3 retailTorque = accumulator.RetailFilteredTorque();
            ShadowVector3 wingTorque = WingSteeringTorque(
                input.Wings, input.Input, _state.VelocityMps.Magnitude);
            ShadowVector3 bodyTorque = retailTorque + wingTorque;

            // 3. Angular integration, body frame, diagonal-inertia approximation
            //    with labelled damping. Unusable inertia refuses angular response
            //    instead of dividing by it.
            ShadowVector3 angular = new ShadowVector3(
                AngularAxisStep(_state.AngularVelocityRadPerSec.X, bodyTorque.X,
                    input.Mass.DiagonalInertiaKgM2.X, input.DeltaSeconds),
                AngularAxisStep(_state.AngularVelocityRadPerSec.Y, bodyTorque.Y,
                    input.Mass.DiagonalInertiaKgM2.Y, input.DeltaSeconds),
                AngularAxisStep(_state.AngularVelocityRadPerSec.Z, bodyTorque.Z,
                    input.Mass.DiagonalInertiaKgM2.Z, input.DeltaSeconds));

            // 4. Linear + vertical through the reviewed cross-track seam. The
            //    world-frame propulsion force is the ONLY external vertical force
            //    and it enters through the seam itself; LiftGravityRuntime passes
            //    ExternalVerticalForceNewtons = 0 so the seam's double-gravity /
            //    double-force checks stay armed.
            ShadowVector3 worldForce = _state.Orientation.Rotate(accumulator.ForceNewtons);
            var forces = new VectorRigidBodyShadowResult(input.Mass, worldForce,
                accumulator.RawTorqueNewtonMetres, retailTorque, accepted, rejected);
            var motion = new ShadowMotionState(_state.Position, _state.VelocityMps,
                input.HalfExtentsMetres);
            if (!LiftGravityRuntime.TryIntegrateLinear(input.StableHullKey, motion, forces,
                    input.Lift, input.Input.Vertical, _state.CommandLiftForceNewtons,
                    _state.CommandLiftSmoothingVelocity, input.DeltaSeconds,
                    out IntegratedFlightShadowResult integrated))
            {
                return Coast(input.DeltaSeconds, "lift-seam-rejected");
            }

            // 5. Recovered horizontal drag on the committed velocity: it opposes
            //    motion and never reverses it. (Vertical speed is governed by the
            //    recovered lift caps, not by drag.)
            ShadowVector3 velocity = ApplyRecoveredHorizontalDrag(
                integrated.NextVelocityMetresPerSecond, input.DeltaSeconds);

            // 6. Orientation integration from the body angular velocity.
            ShadowQuaternion orientation = IntegrateOrientation(
                _state.Orientation, angular, input.DeltaSeconds);

            var next = new VectorFlightState(integrated.NextPositionMetres, orientation,
                velocity, angular,
                integrated.Lift.CommandLiftForceNewtons,
                integrated.Lift.CommandLiftSmoothingVelocity);

            // 7. WAREBORN rest snap so the publisher's exact-zero rest contract
            //    holds. Never fires while lift/gravity still accelerates the hull
            //    (an overloaded ship at its apex keeps falling) or while any
            //    propulsor is powered.
            bool snapped = false;
            if (input.Input.IsNeutral
                && TotalPropulsorPower(input.Propulsors) <= 0.0
                && next.VelocityMps.Magnitude < RestLinearSpeedThresholdMps
                && next.AngularVelocityRadPerSec.Magnitude < RestAngularSpeedThresholdRadPerSec
                && Math.Abs(integrated.Lift.VerticalAccelerationMps2)
                    < RestVerticalAccelerationThresholdMps2)
            {
                (double yaw, _, _) = ExtractYawPitchRoll(orientation);
                next = new VectorFlightState(next.Position,
                    ShadowQuaternion.FromAxisAngle(ShadowVector3.Up, yaw),
                    ShadowVector3.Zero, ShadowVector3.Zero,
                    CommandLiftForceNewtons: 0.0, CommandLiftSmoothingVelocity: 0.0);
                snapped = true;
            }

            if (!next.IsFinite)
            {
                return Rejected("integration-not-finite");
            }
            _state = next;
            return new VectorFlightStepResult(true, "integrated", integrated.Lift,
                worldForce, bodyTorque, integrated.Collision.Telemetry,
                accepted, rejected, snapped);
        }

        private static double TotalPropulsorPower(IReadOnlyList<ShadowPropulsor> propulsors)
        {
            double total = 0.0;
            for (int i = 0; i < propulsors.Count; i++) total += propulsors[i].Power;
            return total;
        }

        private static double AngularAxisStep(double omega, double torque, double inertia,
            double dt)
        {
            double next = inertia >= MinimumUsableInertiaKgM2 && double.IsFinite(inertia)
                ? omega + (torque / inertia) * dt
                : omega;
            double damping = Math.Max(0.0, 1.0 - (AngularDampingPerSecond * dt));
            return next * damping;
        }

        private static ShadowVector3 ApplyRecoveredHorizontalDrag(ShadowVector3 velocity,
            double dt)
        {
            double horizontal = Math.Sqrt(
                (velocity.X * velocity.X) + (velocity.Z * velocity.Z));
            if (horizontal <= VectorRigidBodyShadowPolicy.VectorEpsilon)
            {
                return velocity;
            }
            double slowed = Math.Max(0.0,
                horizontal - (ShipForceModel.DragDecelerationMps2(horizontal) * dt));
            double scale = slowed / horizontal;
            return new ShadowVector3(velocity.X * scale, velocity.Y, velocity.Z * scale);
        }

        private static ShadowQuaternion IntegrateOrientation(ShadowQuaternion orientation,
            ShadowVector3 bodyAngularRadPerSec, double dt)
        {
            double speed = bodyAngularRadPerSec.Magnitude;
            if (speed <= VectorRigidBodyShadowPolicy.VectorEpsilon)
            {
                return orientation;
            }
            ShadowQuaternion delta = ShadowQuaternion.FromAxisAngle(
                bodyAngularRadPerSec / speed, speed * dt);
            return ShadowQuaternion.TryMultiply(orientation, delta, out ShadowQuaternion next)
                ? next
                : orientation;
        }

        private VectorFlightStepResult Coast(double deltaSeconds, string reason)
        {
            if (double.IsFinite(deltaSeconds) && deltaSeconds > 0.0)
            {
                _state = _state with
                {
                    Position = _state.Position + (_state.VelocityMps * deltaSeconds),
                };
            }
            return new VectorFlightStepResult(false, reason, default,
                ShadowVector3.Zero, ShadowVector3.Zero, default, 0, 0, false);
        }

        private static VectorFlightStepResult Rejected(string reason) =>
            new VectorFlightStepResult(false, reason, default,
                ShadowVector3.Zero, ShadowVector3.Zero, default, 0, 0, false);
    }
}

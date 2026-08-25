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
            double engineSpin, WindSample wind, FlightControlInput input,
            LiftRuntimeStepPolicy lift, FlightTuning tuning)
        {
            StableHullKey = stableHullKey;
            DeltaSeconds = deltaSeconds;
            Mass = mass;
            HalfExtentsMetres = halfExtentsMetres;
            Propulsors = propulsors ?? Array.Empty<ShadowPropulsor>();
            Wings = wings ?? Array.Empty<VectorWingSurface>();
            EngineSpin = engineSpin;
            Wind = wind;
            Input = input;
            Lift = lift;
            Tuning = tuning;
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

        /// <summary>
        /// The ONE wind answer for this hull at this step, sampled by the caller
        /// from the production <see cref="WindField"/> (walls included). Sail trim
        /// reads its components; the carried-wind tier decision reads the whole
        /// sample - one wind truth, never two.
        /// </summary>
        public WindSample Wind { get; }

        public FlightControlInput Input { get; }
        public LiftRuntimeStepPolicy Lift { get; }

        /// <summary>
        /// The live flight tuning, consumed ONLY by the shared
        /// <see cref="ShipForceEvaluator.CarriedWindAlongHeadingMps"/> decision
        /// and the canvas-is-driving test - the same knobs the scalar path reads.
        /// </summary>
        public FlightTuning Tuning { get; }

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
            && Tuning != null
            && Lift.IsValid;
    }

    /// <summary>What one step decided and measured - published, never re-evaluated.</summary>
    public readonly struct VectorFlightStepResult
    {
        public VectorFlightStepResult(bool integrated, string disposition,
            LiftGravityEvaluation lift, ShadowVector3 worldForceNewtons,
            ShadowVector3 bodyTorqueNewtonMetres, CollisionShadowTelemetry collision,
            int acceptedParts, int rejectedParts, bool snappedToRest,
            double carriedWindAlongHeadingMps)
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
            CarriedWindAlongHeadingMps = carriedWindAlongHeadingMps;
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

        /// <summary>
        /// The carried-wind tier the step actually applied, straight from the
        /// SHARED <see cref="ShipForceEvaluator.CarriedWindAlongHeadingMps"/>
        /// decision - the commanded sky-core baseline, canvas floor, or
        /// mass-attenuated wall air. Telemetry; parity with the scalar
        /// evaluation is asserted on this exact value.
        /// </summary>
        public double CarriedWindAlongHeadingMps { get; }
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
    /// vertical policy; the horizontal air interaction is the RECOVERED
    /// relative-wind law (<see cref="ShipForceModel.RelativeWindApproachDeltaMps"/>)
    /// aimed at the SHARED carried-wind tier decision
    /// (<see cref="ShipForceEvaluator.CarriedWindAlongHeadingMps"/> - the same
    /// code the scalar path runs, covering the sky-core commanded baseline,
    /// canvas floor and mass-attenuated wall air); angular damping is an
    /// APPROXIMATION (the retail rigidbody's angularDrag is lost); the wing
    /// steering torque is the RECOVERED WingVisualizer shape with WAREBORN power
    /// tuning; sail and wing mount orientation comes from the packed mount
    /// rotation, not live joint state (the visible seam).
    ///
    /// REMAINING BEHAVIOR GAPS, each named so none can hide (world bounds are
    /// already labelled at the service startup warning):
    /// <list type="bullet">
    /// <item>WORLD BOUNDS - the retail edge pushback is not routed through the
    ///   vector path; promoted hulls fly unbounded (service startup warning).</item>
    /// <item>NO SELF-RIGHTING IN FLIGHT - retail's self-leveling behavior is
    ///   NOT recovered and none is invented: a hull rolled or pitched by torque
    ///   keeps that attitude while flying. Only the labelled WAREBORN rest
    ///   stabilization (bounded settle, then snap) levels it, and only once
    ///   every rest condition holds.</item>
    /// <item>WING AIRBRAKE - the recovered WingVisualizer airbrake term needs
    ///   the lost per-wing AirBrake value; it is absent, not approximated.</item>
    /// <item>SAIL/WING JOINT STATE - orientation is the packed mount rotation,
    ///   not live joint state (<see cref="SailYawSeam"/>).</item>
    /// <item>ABANDONED SINK - the retail 24 h CoreDampenTime accumulator is not
    ///   tracked; the tested IsAbandoned path is fed false by the service.</item>
    /// <item>VERTICAL WIND - <see cref="WindSample"/> is horizontal-only; a
    ///   Wind Rift's downward component has nowhere to enter yet.</item>
    /// <item>STORMS - the wall/storm vector shadow
    ///   (<c>VectorWallStormShadow</c>) stays dormant; only the wall WIND
    ///   (carry tier above) acts here, not storm forces.</item>
    /// </list>
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
        /// (lift + gravity exactly once, linear integration) -> the SHARED
        /// carried-wind decision + the recovered relative-wind law on the
        /// committed horizontal velocity -> orientation integration -> rest
        /// snap. A step that fails validation COASTS (position advances on the
        /// held velocity) rather than freezing or inventing forces.
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
            //    snapshot COM. Sail trim consumes the hull-local wind - the SAME
            //    WindSample the carried-wind tier decision below reads.
            ShadowVector3 worldWind = new ShadowVector3(
                input.Wind.WindX, 0.0, input.Wind.WindZ);
            ShadowVector3 localWind = _state.Orientation.InverseRotate(worldWind);
            var accumulator = new ShadowForceAccumulator();
            int accepted = 0, rejected = 0;
            int poweredSails = 0;
            for (int i = 0; i < input.Propulsors.Count; i++)
            {
                ShadowPropulsor part = input.Propulsors[i];
                ShadowVector3 force = part.Kind == ShadowPartKind.Engine
                    ? VectorRigidBodyShadow.EngineForce(part, input.EngineSpin)
                    : VectorRigidBodyShadow.TrimmedSailForce(part, localWind);
                if (part.Kind == ShadowPartKind.Sail && part.Power > 0.0)
                {
                    poweredSails++;
                }
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

            // 5. The horizontal air interaction, in two SHARED pieces so the
            //    scalar and vector paths cannot diverge (kill-list: no parallel
            //    calculations):
            //    a. WHAT wind carries this hull - the exact tier decision the
            //       scalar evaluator runs (sky-core commanded baseline, canvas
            //       floor, mass-attenuated wall air), via
            //       ShipForceEvaluator.CarriedWindAlongHeadingMps. Canvas-is-
            //       driving is decided by the same production
            //       ShipForceModel.SailForwardNewtons the scalar path uses.
            //    b. HOW the air closes the gap - the recovered relative-wind law
            //       (ShipForceModel.RelativeWindApproachDeltaMps: 2.5-power
            //       primary + always-on 0.03 m/s2 residual settle), applied to
            //       the velocity RELATIVE to the carried wind along the heading.
            //       With no carried wind it is plain drag opposing motion; with
            //       one it is what actually gets a bare hull under way. Never
            //       overshoots, never reverses. (Vertical speed is governed by
            //       the recovered lift caps, not by drag.)
            (double headingYaw, _, _) = ExtractYawPitchRoll(_state.Orientation);
            double throttle = Math.Clamp(input.Input.Throttle, -1.0, 1.0);
            double scalarSailForce = ShipForceModel.SailForwardNewtons(
                poweredSails, headingYaw, input.Tuning.SailPowerNewtons,
                input.Wind.WindX, input.Wind.WindZ);
            double carriedWindMps = ShipForceEvaluator.CarriedWindAlongHeadingMps(
                input.Wind, headingYaw, throttle, scalarSailForce,
                input.Mass.TotalMassKg, input.Tuning);
            ShadowVector3 velocity = ApplyRecoveredRelativeWindLaw(
                integrated.NextVelocityMetresPerSecond, headingYaw, carriedWindMps,
                input.DeltaSeconds);

            // 6. Orientation integration from the body angular velocity.
            ShadowQuaternion orientation = IntegrateOrientation(
                _state.Orientation, angular, input.DeltaSeconds);

            var next = new VectorFlightState(integrated.NextPositionMetres, orientation,
                velocity, angular,
                integrated.Lift.CommandLiftForceNewtons,
                integrated.Lift.CommandLiftSmoothingVelocity);

            // 7. WAREBORN rest snap so the publisher's exact-zero rest contract
            //    holds. Never fires while lift/gravity still accelerates the hull
            //    (an overloaded ship at its apex keeps falling), while any
            //    propulsor is powered, or while a carried wind still pushes it -
            //    with the lever centred that can only be wall air, which is
            //    spatial resistance and keeps shoving a parked hull exactly as
            //    the scalar integrator keeps integrating it.
            bool snapped = false;
            if (input.Input.IsNeutral
                && TotalPropulsorPower(input.Propulsors) <= 0.0
                && Math.Abs(carriedWindMps) < RestLinearSpeedThresholdMps
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
                accepted, rejected, snapped, carriedWindMps);
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

        /// <summary>
        /// The recovered relative-wind law in the horizontal plane: the shared
        /// <see cref="ShipForceModel.RelativeWindApproachDeltaMps"/> step closes
        /// the gap between the velocity and the carried wind aimed along the
        /// heading. The delta is bounded by the gap itself, so the air can bring
        /// a hull TO its carried speed but never past it, and with no carried
        /// wind this is exactly drag opposing motion - it never reverses travel.
        /// </summary>
        private static ShadowVector3 ApplyRecoveredRelativeWindLaw(ShadowVector3 velocity,
            double headingRadians, double carriedWindAlongHeadingMps, double dt)
        {
            double carryX = Math.Sin(headingRadians) * carriedWindAlongHeadingMps;
            double carryZ = Math.Cos(headingRadians) * carriedWindAlongHeadingMps;
            double relX = carryX - velocity.X;
            double relZ = carryZ - velocity.Z;
            double relMagnitude = Math.Sqrt((relX * relX) + (relZ * relZ));
            if (relMagnitude <= VectorRigidBodyShadowPolicy.VectorEpsilon)
            {
                return velocity;
            }
            double delta = ShipForceModel.RelativeWindApproachDeltaMps(relMagnitude, dt);
            double scale = delta / relMagnitude;
            return new ShadowVector3(
                velocity.X + (relX * scale), velocity.Y, velocity.Z + (relZ * scale));
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
                ShadowVector3.Zero, ShadowVector3.Zero, default, 0, 0, false, 0.0);
        }

        private static VectorFlightStepResult Rejected(string reason) =>
            new VectorFlightStepResult(false, reason, default,
                ShadowVector3.Zero, ShadowVector3.Zero, default, 0, 0, false, 0.0);
    }
}

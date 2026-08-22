using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// Cross-track PURE/SHADOW orchestration. The order is deliberate: compose
    /// propulsion and lift (gravity exactly once), integrate the next velocity,
    /// then sweep collision from the pre-step pose using that integrated velocity.
    /// This type has no live session, packet, persistence, or wall-clock dependency.
    /// </summary>
    public static class IntegratedFlightShadow
    {
        public const string ForceProvenance =
            "RECOVERED geometry + WAREBORN propulsor tuning; gravity supplied once by lift policy";

        public static bool TryStep(IntegratedFlightShadowInput input,
            out IntegratedFlightShadowResult result)
        {
            result = default;
            if (!input.IsValid
                || Math.Abs(input.LiftInput.MassKg - input.Forces.Mass.TotalMassKg) > 1e-6
                // The vector force is the only accepted external vertical force at
                // this seam. Requiring zero closes the accidental double-force path.
                || input.LiftInput.ExternalVerticalForceNewtons != 0.0)
            {
                return false;
            }

            LiftGravityInput liftInput = new(
                input.Forces.Mass.TotalMassKg,
                input.LiftInput.LiftCapacityKg,
                input.LiftInput.GravityYMetresPerSecondSquared,
                input.Motion.VelocityMetresPerSecond.Y,
                input.LiftInput.VerticalCommand,
                input.LiftInput.DeltaSeconds,
                externalVerticalForceNewtons: input.Forces.ForceNewtons.Y,
                compensationForceNewtons: input.LiftInput.CompensationForceNewtons,
                currentCommandLiftForceNewtons: input.LiftInput.CurrentCommandLiftForceNewtons,
                commandLiftSmoothingVelocity: input.LiftInput.CommandLiftSmoothingVelocity,
                isAbandoned: input.LiftInput.IsAbandoned);
            LiftGravityEvaluation lift = RetailLiftGravityShadow.Step(liftInput);
            if (!lift.Valid) return false;

            double dt = input.LiftInput.DeltaSeconds;
            double inverseMass = 1.0 / input.Forces.Mass.TotalMassKg;
            ShadowVector3 nextVelocity = new(
                input.Motion.VelocityMetresPerSecond.X
                    + input.Forces.ForceNewtons.X * inverseMass * dt,
                lift.NextVerticalVelocityMps,
                input.Motion.VelocityMetresPerSecond.Z
                    + input.Forces.ForceNewtons.Z * inverseMass * dt);

            CollisionProxy subject = new(input.StableHullKey, CollisionProxyKind.ShipHull,
                CollisionAabb.FromCentreHalfExtents(input.Motion.PositionMetres,
                    input.Motion.HalfExtentsMetres), nextVelocity);
            CollisionProxy[] dynamics = input.OtherHulls
                .Where(proxy => !string.Equals(proxy.Id, input.StableHullKey,
                    StringComparison.Ordinal))
                .Append(subject)
                .ToArray();
            CollisionShadowResult collision = CollisionShadowEvaluator.Evaluate(
                dynamics, input.Terrain, dt);
            ShadowVector3 nextPosition = input.Motion.PositionMetres + nextVelocity * dt;
            result = new IntegratedFlightShadowResult(nextPosition, nextVelocity,
                lift, collision, ForceProvenance);
            return !collision.Telemetry.HardInputRejected;
        }
    }

    public readonly record struct ShadowMotionState(
        ShadowVector3 PositionMetres,
        ShadowVector3 VelocityMetresPerSecond,
        ShadowVector3 HalfExtentsMetres)
    {
        public bool IsValid => PositionMetres.IsFinite && VelocityMetresPerSecond.IsFinite
            && HalfExtentsMetres.IsFinite
            && HalfExtentsMetres.X > 0.0 && HalfExtentsMetres.Y > 0.0
            && HalfExtentsMetres.Z > 0.0;
    }

    public readonly struct IntegratedFlightShadowInput
    {
        public IntegratedFlightShadowInput(string stableHullKey, ShadowMotionState motion,
            VectorRigidBodyShadowResult forces, LiftGravityInput liftInput,
            IReadOnlyList<CollisionProxy>? otherHulls = null,
            IReadOnlyList<CollisionProxy>? terrain = null)
        {
            StableHullKey = stableHullKey;
            Motion = motion;
            Forces = forces;
            LiftInput = liftInput;
            OtherHulls = otherHulls ?? Array.Empty<CollisionProxy>();
            Terrain = terrain ?? Array.Empty<CollisionProxy>();
        }

        public string StableHullKey { get; }
        public ShadowMotionState Motion { get; }
        public VectorRigidBodyShadowResult Forces { get; }
        public LiftGravityInput LiftInput { get; }
        public IReadOnlyList<CollisionProxy> OtherHulls { get; }
        public IReadOnlyList<CollisionProxy> Terrain { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(StableHullKey)
            && StableHullKey.Length <= CollisionShadowLimits.MaxIdLength
            && Motion.IsValid && LiftInput.IsValid
            && OtherHulls.Count < CollisionShadowLimits.HardInputCount
            && Terrain.Count <= CollisionShadowLimits.HardInputCount;
    }

    public readonly record struct IntegratedFlightShadowResult(
        ShadowVector3 NextPositionMetres,
        ShadowVector3 NextVelocityMetresPerSecond,
        LiftGravityEvaluation Lift,
        CollisionShadowResult Collision,
        string Provenance);
}

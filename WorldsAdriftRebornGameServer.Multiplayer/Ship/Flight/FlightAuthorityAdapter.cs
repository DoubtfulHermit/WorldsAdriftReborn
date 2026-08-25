using System;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>Which integrator owns this hull's motion.</summary>
    public enum FlightAuthorityMode
    {
        Scalar = 0,
        VectorAuthority = 1,
    }

    /// <summary>
    /// One hull's authority adapter: the SINGLE minter of
    /// <see cref="FlightAuthorityStamp"/>s and the single owner of the committed
    /// <see cref="AuthoritativeFlightPose"/>. Collision, docking, publication,
    /// persistence and telemetry consume the pose (or its stamp) - none of them
    /// may construct a stamp with new values or integrate a pose of their own.
    ///
    /// It is also the ONLY site that decides scalar-vs-vector per hull
    /// (<see cref="For"/>): everyone else asks the adapter, nobody re-reads the
    /// flags. Stamp acceptance is fail-closed: a stamp must be valid and either
    /// open a NEWER authority generation or strictly supersede the last accepted
    /// step within the current one - replays, duplicates and stale-generation
    /// evidence are rejected, never upgraded.
    /// </summary>
    public sealed class FlightAuthorityAdapter
    {
        private FlightAuthorityAdapter(FlightAuthorityMode mode, VectorFlightRuntime? vector)
        {
            if (mode == FlightAuthorityMode.VectorAuthority && vector == null)
            {
                throw new ArgumentNullException(nameof(vector),
                    "vector authority requires a vector runtime");
            }
            if (mode == FlightAuthorityMode.Scalar && vector != null)
            {
                throw new ArgumentException(
                    "a scalar-mode adapter must not carry a vector runtime", nameof(vector));
            }
            Mode = mode;
            Vector = vector;
        }

        public FlightAuthorityMode Mode { get; }

        /// <summary>Non-null exactly when <see cref="Mode"/> is vector authority.</summary>
        public VectorFlightRuntime? Vector { get; }

        /// <summary>The last accepted stamp; default (invalid) before the first commit.</summary>
        public FlightAuthorityStamp LastStamp { get; private set; }

        /// <summary>The last committed pose; invalid before the first commit.</summary>
        public AuthoritativeFlightPose CurrentPose { get; private set; }

        /// <summary>The last committed vector step telemetry (vector mode only).</summary>
        public VectorFlightStepResult LastVectorStep { get; private set; }

        /// <summary>The last committed lift-capacity plan, for telemetry.</summary>
        public LiftCapacityPlan LastCapacityPlan { get; private set; }

        /// <summary>
        /// THE per-hull scalar-vs-vector decision. A hull is promoted if and only
        /// if the flags promote its PERSISTENT index; the restored durable vector
        /// state resumes when present and finite, otherwise the vector state seeds
        /// from the committed scalar pose. A rolled-back hull (index removed)
        /// simply comes back Scalar - the restart that applied the flag change
        /// already advanced the authority generation, so nothing minted under the
        /// vector epoch survives.
        /// </summary>
        public static FlightAuthorityAdapter For(FlightRuntimeFlags flags, int? persistentIndex,
            FlightState scalarSeed, VectorFlightState? restoredVector = null)
        {
            if (flags == null) throw new ArgumentNullException(nameof(flags));
            if (!flags.IsPromoted(persistentIndex))
            {
                return new FlightAuthorityAdapter(FlightAuthorityMode.Scalar, null);
            }
            VectorFlightState seed = restoredVector.HasValue && restoredVector.Value.IsFinite
                ? restoredVector.Value
                : VectorFlightRuntime.FromFlightState(scalarSeed);
            return new FlightAuthorityAdapter(FlightAuthorityMode.VectorAuthority,
                new VectorFlightRuntime(seed));
        }

        /// <summary>
        /// Mints and commits the stamp+pose for one accepted scalar step. False =
        /// the stamp is stale/invalid and NOTHING changed - the caller publishes
        /// nothing for this frame.
        /// </summary>
        public bool TryCommitScalar(long fixedStep, long authorityGeneration, FlightState state)
        {
            if (Mode != FlightAuthorityMode.Scalar) return false;
            if (!TryMint(fixedStep, authorityGeneration, out FlightAuthorityStamp stamp))
            {
                return false;
            }
            AuthoritativeFlightPose pose = ScalarPose(stamp, state);
            if (!pose.IsValid) return false;
            LastStamp = stamp;
            CurrentPose = pose;
            return true;
        }

        /// <summary>
        /// Mints and commits the stamp+pose for one accepted vector step, from
        /// the runtime's CURRENT state (the caller has just stepped it).
        /// </summary>
        public bool TryCommitVector(long fixedStep, long authorityGeneration,
            VectorFlightStepResult stepResult, LiftCapacityPlan capacityPlan)
        {
            if (Mode != FlightAuthorityMode.VectorAuthority || Vector == null) return false;
            if (!TryMint(fixedStep, authorityGeneration, out FlightAuthorityStamp stamp))
            {
                return false;
            }
            AuthoritativeFlightPose pose = VectorPose(stamp, Vector.State);
            if (!pose.IsValid) return false;
            LastStamp = stamp;
            CurrentPose = pose;
            LastVectorStep = stepResult;
            LastCapacityPlan = capacityPlan;
            return true;
        }

        /// <summary>
        /// The scalar projection: position/velocity direct from the committed
        /// FlightState, orientation through the ONE existing Euler-to-quaternion
        /// conversion (<see cref="FlightIntegrator.AttitudeQuaternion"/> - the
        /// same attitude the 1130 wire packing uses), angular velocity the yaw
        /// rate about world +Y.
        /// </summary>
        public static AuthoritativeFlightPose ScalarPose(FlightAuthorityStamp stamp,
            FlightState state)
        {
            (double w, double x, double y, double z) = FlightIntegrator.AttitudeQuaternion(state);
            return new AuthoritativeFlightPose(stamp,
                state.X, state.Y, state.Z,
                w, x, y, z,
                state.VxMps, state.VyMps, state.VzMps,
                0.0, state.YawRateRadPerSec, 0.0);
        }

        /// <summary>
        /// The vector projection: the vector state IS the source and converts
        /// exactly once - the pose carries its quaternion directly and its body
        /// angular velocity rotated into the world frame.
        /// </summary>
        public static AuthoritativeFlightPose VectorPose(FlightAuthorityStamp stamp,
            VectorFlightState state)
        {
            ShadowVector3 worldAngular = state.Orientation.Rotate(state.AngularVelocityRadPerSec);
            return new AuthoritativeFlightPose(stamp,
                state.Position.X, state.Position.Y, state.Position.Z,
                state.Orientation.W, state.Orientation.X, state.Orientation.Y, state.Orientation.Z,
                state.VelocityMps.X, state.VelocityMps.Y, state.VelocityMps.Z,
                worldAngular.X, worldAngular.Y, worldAngular.Z);
        }

        /// <summary>The durable vector/lift-smoothing extension, or null for scalar hulls.</summary>
        public DurableVectorFlightState? CaptureVector()
        {
            if (Vector == null) return null;
            return DurableVectorFlightState.Capture(Vector.State);
        }

        private bool TryMint(long fixedStep, long authorityGeneration,
            out FlightAuthorityStamp stamp)
        {
            stamp = new FlightAuthorityStamp(fixedStep, authorityGeneration);
            if (!stamp.IsValid) return false;
            if (!LastStamp.IsValid)
            {
                return true;
            }
            if (stamp.AuthorityGeneration > LastStamp.AuthorityGeneration)
            {
                // A helm handoff / restart opened a new epoch; the step counter
                // may legitimately restart.
                return true;
            }
            return stamp.SupersedesWithinGeneration(LastStamp);
        }
    }
}

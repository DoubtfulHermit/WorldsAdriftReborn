using System;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class FlightAuthorityAdapterTests
    {
        private static FlightRuntimeFlags Promoting(int index) => FlightRuntimeFlags.Parse(
            "1", index.ToString(), null, fixedStepEnabled: true, forceModelEnabled: true);

        private static FlightState MovingState() => new FlightState(
            100.0, 250.0, -40.0, 0.3, 0.05, -0.02, 0.01, 6.0, 1.5, 0.2, 5.5);

        [Fact]
        public void Scalar_adapter_is_chosen_for_an_unpromoted_hull()
        {
            FlightAuthorityAdapter adapter = FlightAuthorityAdapter.For(
                FlightRuntimeFlags.Disabled, 3, MovingState());

            Assert.Equal(FlightAuthorityMode.Scalar, adapter.Mode);
            Assert.Null(adapter.Vector);
        }

        [Fact]
        public void Vector_adapter_is_chosen_only_for_a_promoted_persistent_index()
        {
            Assert.Equal(FlightAuthorityMode.VectorAuthority,
                FlightAuthorityAdapter.For(Promoting(3), 3, MovingState()).Mode);
            Assert.Equal(FlightAuthorityMode.Scalar,
                FlightAuthorityAdapter.For(Promoting(3), 4, MovingState()).Mode);
            Assert.Equal(FlightAuthorityMode.Scalar,
                FlightAuthorityAdapter.For(Promoting(3), null, MovingState()).Mode);
        }

        [Fact]
        public void Rollback_flags_produce_a_scalar_adapter_even_with_restored_vector_state()
        {
            VectorFlightState restored = VectorFlightRuntime.FromFlightState(MovingState());

            FlightAuthorityAdapter adapter = FlightAuthorityAdapter.For(
                Promoting(17), 3, MovingState(), restored);

            Assert.Equal(FlightAuthorityMode.Scalar, adapter.Mode);
            Assert.Null(adapter.CaptureVector());
        }

        [Fact]
        public void First_commit_mints_a_valid_stamp_and_pose()
        {
            FlightAuthorityAdapter adapter = FlightAuthorityAdapter.For(
                FlightRuntimeFlags.Disabled, null, MovingState());

            Assert.True(adapter.TryCommitScalar(1, 2, MovingState()));
            Assert.Equal(new FlightAuthorityStamp(1, 2), adapter.LastStamp);
            Assert.True(adapter.CurrentPose.IsValid);
        }

        [Fact]
        public void Replayed_or_regressed_fixed_steps_are_rejected_without_touching_the_pose()
        {
            FlightAuthorityAdapter adapter = FlightAuthorityAdapter.For(
                FlightRuntimeFlags.Disabled, null, MovingState());
            Assert.True(adapter.TryCommitScalar(5, 2, MovingState()));
            AuthoritativeFlightPose committed = adapter.CurrentPose;

            Assert.False(adapter.TryCommitScalar(5, 2, FlightState.AtRestAt(0, 0, 0)));
            Assert.False(adapter.TryCommitScalar(4, 2, FlightState.AtRestAt(0, 0, 0)));
            Assert.Equal(committed, adapter.CurrentPose);
        }

        [Fact]
        public void Stale_generation_evidence_fails_closed()
        {
            FlightAuthorityAdapter adapter = FlightAuthorityAdapter.For(
                FlightRuntimeFlags.Disabled, null, MovingState());
            Assert.True(adapter.TryCommitScalar(5, 3, MovingState()));

            Assert.False(adapter.TryCommitScalar(6, 2, MovingState()));
        }

        [Fact]
        public void A_newer_generation_may_restart_its_step_counter()
        {
            FlightAuthorityAdapter adapter = FlightAuthorityAdapter.For(
                FlightRuntimeFlags.Disabled, null, MovingState());
            Assert.True(adapter.TryCommitScalar(500, 2, MovingState()));

            Assert.True(adapter.TryCommitScalar(1, 3, MovingState()));
            Assert.Equal(new FlightAuthorityStamp(1, 3), adapter.LastStamp);
        }

        [Fact]
        public void Invalid_stamps_are_never_minted()
        {
            FlightAuthorityAdapter adapter = FlightAuthorityAdapter.For(
                FlightRuntimeFlags.Disabled, null, MovingState());

            Assert.False(adapter.TryCommitScalar(-1, 2, MovingState()));
            Assert.False(adapter.TryCommitScalar(1, 0, MovingState()));
            Assert.False(adapter.CurrentPose.IsValid);
        }

        [Fact]
        public void Scalar_pose_projects_through_the_one_attitude_conversion()
        {
            FlightState state = MovingState();
            AuthoritativeFlightPose pose = FlightAuthorityAdapter.ScalarPose(
                new FlightAuthorityStamp(1, 1), state);

            (double w, double x, double y, double z) = FlightIntegrator.AttitudeQuaternion(state);
            Assert.Equal(state.X, pose.X);
            Assert.Equal(state.Y, pose.Y);
            Assert.Equal(state.Z, pose.Z);
            Assert.Equal(w, pose.QW);
            Assert.Equal(x, pose.QX);
            Assert.Equal(y, pose.QY);
            Assert.Equal(z, pose.QZ);
            Assert.Equal(state.VxMps, pose.VxMps);
            Assert.Equal(state.VyMps, pose.VyMps);
            Assert.Equal(state.VzMps, pose.VzMps);
            Assert.Equal(state.YawRateRadPerSec, pose.AngVyRadPerSec);
            Assert.Equal(0.0, pose.AngVxRadPerSec);
            Assert.Equal(0.0, pose.AngVzRadPerSec);
        }

        [Fact]
        public void Vector_pose_carries_the_vector_quaternion_directly()
        {
            VectorFlightState state = VectorFlightRuntime.FromFlightState(MovingState());
            AuthoritativeFlightPose pose = FlightAuthorityAdapter.VectorPose(
                new FlightAuthorityStamp(1, 1), state);

            Assert.Equal(state.Orientation.W, pose.QW);
            Assert.Equal(state.Orientation.X, pose.QX);
            Assert.Equal(state.Orientation.Y, pose.QY);
            Assert.Equal(state.Orientation.Z, pose.QZ);
            Assert.Equal(state.Position.X, pose.X);
            Assert.Equal(state.VelocityMps.Z, pose.VzMps);
            Assert.True(pose.IsValid);
        }

        [Fact]
        public void A_scalar_commit_on_a_vector_adapter_is_refused_and_vice_versa()
        {
            FlightAuthorityAdapter vector = FlightAuthorityAdapter.For(
                Promoting(3), 3, MovingState());
            FlightAuthorityAdapter scalar = FlightAuthorityAdapter.For(
                FlightRuntimeFlags.Disabled, 3, MovingState());

            Assert.False(vector.TryCommitScalar(1, 2, MovingState()));
            Assert.False(scalar.TryCommitVector(1, 2, default, default));
        }

        [Fact]
        public void Publication_reads_the_committed_pose_not_a_second_stream()
        {
            // The 1130 spec fields and packed rotation must be derivable from the
            // adapter's committed pose alone: same position, same velocity, and a
            // packed attitude that decodes back to the pose quaternion.
            FlightState state = MovingState();
            FlightAuthorityAdapter adapter = FlightAuthorityAdapter.For(
                FlightRuntimeFlags.Disabled, null, state);
            Assert.True(adapter.TryCommitScalar(12, 2, state));
            AuthoritativeFlightPose pose = adapter.CurrentPose;

            ShipControlPointSpec spec = FlightIntegrator.ToControlPoint(state, 1000);
            Assert.Equal(pose.X, spec.X);
            Assert.Equal(pose.Y, spec.Y);
            Assert.Equal(pose.Z, spec.Z);
            Assert.Equal(pose.VxMps, spec.Vx);
            Assert.Equal(pose.VyMps, spec.Vy);
            Assert.Equal(pose.VzMps, spec.Vz);

            (float w, float x, float y, float z) = Quaternion32Packing.Decode(
                FlightIntegrator.PackedRotation(state));
            double dot = Math.Abs((w * pose.QW) + (x * pose.QX) + (y * pose.QY) + (z * pose.QZ));
            Assert.True(dot > 0.9999, "packed wire rotation diverged from the committed pose");
        }

        [Fact]
        public void Vector_capture_restores_through_the_durable_extension()
        {
            FlightState scalar = MovingState();
            FlightAuthorityAdapter adapter = FlightAuthorityAdapter.For(
                Promoting(3), 3, scalar);
            Multiplayer.Persistence.DurableVectorFlightState? durable = adapter.CaptureVector();

            Assert.NotNull(durable);
            Assert.True(durable!.TryRead(scalar, out VectorFlightState restored));
            VectorFlightState original = adapter.Vector!.State;
            Assert.Equal(original.Position, restored.Position);
            Assert.Equal(original.VelocityMps, restored.VelocityMps);
            Assert.Equal(original.AngularVelocityRadPerSec, restored.AngularVelocityRadPerSec);
            Assert.Equal(original.CommandLiftForceNewtons, restored.CommandLiftForceNewtons);
            // TryRead renormalises the quaternion; same attitude within epsilon.
            double dot = (original.Orientation.W * restored.Orientation.W)
                + (original.Orientation.X * restored.Orientation.X)
                + (original.Orientation.Y * restored.Orientation.Y)
                + (original.Orientation.Z * restored.Orientation.Z);
            Assert.True(Math.Abs(dot) > 1.0 - 1e-12);
        }
    }
}

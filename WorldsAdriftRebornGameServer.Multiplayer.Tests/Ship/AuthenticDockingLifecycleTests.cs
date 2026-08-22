using System;
using System.Text.Json;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public sealed class AuthenticDockingLifecycleTests
    {
        private static readonly DockingPose Target = new DockingPose(100, 20, -50, Math.PI - 0.1);

        [Theory]
        [InlineData(-3.0)]
        [InlineData(-1.5)]
        [InlineData(0.0)]
        [InlineData(1.5)]
        [InlineData(3.0)]
        public void Neutral_owned_ship_may_approach_from_any_heading(double yaw)
        {
            var claims = new ShipDockRegistry();
            var machine = new AuthenticDockingLifecycle(200);

            bool accepted = machine.TryBeginApproach(Request(
                new DockingPose(110, 20, -50, yaw)), claims, out DockingRejectReason reason);

            Assert.True(accepted);
            Assert.Equal(DockingRejectReason.None, reason);
            Assert.Equal(DockingPhase.Approaching, machine.Phase);
            Assert.Equal(200, claims.DockedShipFor(100));
        }

        [Fact]
        public void Permission_accepts_owner_crew_or_abandoned_but_rejects_foreign_private_yard()
        {
            Assert.True(DockingPermissionPolicy.MayApproach("owner", "owner", false, false));
            Assert.True(DockingPermissionPolicy.MayApproach("member", "owner", true, false));
            Assert.True(DockingPermissionPolicy.MayApproach("stranger", "owner", false, true));
            Assert.False(DockingPermissionPolicy.MayApproach("stranger", "owner", false, false));
            Assert.False(DockingPermissionPolicy.MayApproach(null, "owner", false, false));
        }

        [Fact]
        public void Active_propulsion_fast_approach_and_blocked_clearance_fail_closed()
        {
            AssertRejected(Request(Target, propulsionNeutral: false), DockingRejectReason.PropulsionActive);
            AssertRejected(Request(Target, motion: new DockingMotion(2.01, 0, 0, 0)),
                DockingRejectReason.ApproachTooFast);
            AssertRejected(Request(Target, motion: new DockingMotion(0, 0, 0, 0.251)),
                DockingRejectReason.ApproachTooFast);
            AssertRejected(Request(Target, collisionClear: false), DockingRejectReason.CollisionBlocked);
            AssertRejected(Request(new DockingPose(136, 20, -50, 0)),
                DockingRejectReason.OutsideApproachRadius);
        }

        [Fact]
        public void Capture_freezes_linear_and_angular_velocity_then_interpolates_without_teleporting()
        {
            var claims = new ShipDockRegistry();
            var machine = Begin(claims, new DockingPose(104, 20, -50, -Math.PI + 0.1),
                new DockingMotion(1, 0, 0, 0.1));

            DockingStepResult capture = machine.Step(Frame(0.02, machine.Pose,
                new DockingMotion(1, 0, 0, 0.1)), claims);
            Assert.Equal(DockingPhase.Captured, capture.Phase);
            Assert.True(capture.FreezeVelocity);
            Assert.Equal(0, capture.Motion.LinearSpeed);
            Assert.Equal(0, capture.Motion.AngularSpeedRadiansPerSecond);

            DockingStepResult interpolation = machine.Step(Frame(0.02, machine.Pose,
                new DockingMotion(999, 999, 999, 999)), claims);
            Assert.Equal(DockingPhase.Captured, interpolation.Phase);
            Assert.True(interpolation.Pose.X < 104);
            Assert.True(interpolation.Pose.X > Target.X);
            Assert.Equal(0, interpolation.Motion.LinearSpeed);
            // Shortest yaw arc crosses +/-pi instead of rotating almost a full turn.
            Assert.True(Math.Abs(interpolation.Pose.YawRadians) > 3.0);
        }

        [Fact]
        public void Fixed_twenty_millisecond_interpolation_is_replay_deterministic()
        {
            DockingSnapshotV1 first = RunCaptureReplay();
            DockingSnapshotV1 second = RunCaptureReplay();

            Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
            Assert.Equal((int)DockingPhase.Docked, first.Phase);
            Assert.Equal(Target.X, first.X);
            Assert.Equal(Target.YawRadians, first.YawRadians, 12);
        }

        [Fact]
        public void Occupied_yard_and_already_linked_hull_reject_without_overwriting_truth()
        {
            var claims = new ShipDockRegistry();
            Assert.Equal(ShipDockClaimResult.Claimed, claims.TryClaim(100, 200));
            Assert.Equal(ShipDockClaimResult.RejectedYardOccupied, claims.TryClaim(100, 201));
            Assert.Equal(ShipDockClaimResult.RejectedHullLinked, claims.TryClaim(101, 200));
            Assert.Equal(200, claims.DockedShipFor(100));
            Assert.Equal(100, claims.ShipyardForHull(200));
            Assert.Equal(0, claims.DockedShipFor(101));
        }

        [Fact]
        public void Concurrent_claims_have_one_winner_and_same_pair_retry_is_idempotent()
        {
            var claims = new ShipDockRegistry();
            var first = new AuthenticDockingLifecycle(200);
            var second = new AuthenticDockingLifecycle(201);

            Assert.True(first.TryBeginApproach(Request(Target), claims, out _));
            Assert.False(second.TryBeginApproach(Request(Target, hullId: 201), claims,
                out DockingRejectReason rejected));
            Assert.Equal(DockingRejectReason.YardOccupied, rejected);
            Assert.Equal(ShipDockClaimResult.AlreadyClaimed, claims.TryClaim(100, 200));
            Assert.Equal(200, claims.DockedShipFor(100));
        }

        [Fact]
        public void Exact_pair_release_cannot_clear_a_newer_or_unrelated_claim()
        {
            var claims = new ShipDockRegistry();
            claims.TryClaim(100, 200);

            Assert.False(claims.Release(100, 201));
            Assert.False(claims.Release(101, 200));
            Assert.Equal(200, claims.DockedShipFor(100));
            Assert.True(claims.Release(100, 200));
            Assert.False(claims.IsShipyardOccupied(100));
        }

        [Fact]
        public void Json_snapshot_round_trips_captured_state_and_reacquires_occupancy()
        {
            var sourceClaims = new ShipDockRegistry();
            AuthenticDockingLifecycle source = Begin(sourceClaims, new DockingPose(104, 20, -50, 0),
                new DockingMotion(1, 0, 0, 0.1));
            source.Step(Frame(0.02, source.Pose, source.Motion), sourceClaims);
            DockingSnapshotV1 snapshot = source.CaptureSnapshot();
            string json = JsonSerializer.Serialize(snapshot);
            DockingSnapshotV1 restoredDto = JsonSerializer.Deserialize<DockingSnapshotV1>(json)!;
            var restoredClaims = new ShipDockRegistry();

            Assert.True(AuthenticDockingLifecycle.TryRestore(restoredDto, 200, 100, restoredClaims,
                out AuthenticDockingLifecycle? restored, out DockingRejectReason reason));
            Assert.Equal(DockingRejectReason.None, reason);
            Assert.NotNull(restored);
            Assert.Equal(DockingPhase.Captured, restored!.Phase);
            Assert.Equal(0, restored.Motion.LinearSpeed);
            Assert.Equal(200, restoredClaims.DockedShipFor(100));
        }

        [Fact]
        public void Restore_refuses_unknown_nonfinite_and_conflicting_snapshots()
        {
            DockingSnapshotV1 valid = RunCaptureReplay();
            valid.Version = 2;
            Assert.False(AuthenticDockingLifecycle.TryRestore(valid, 200, 100, new ShipDockRegistry(),
                out _, out DockingRejectReason versionReason));
            Assert.Equal(DockingRejectReason.InvalidSnapshot, versionReason);

            valid = RunCaptureReplay();
            valid.X = double.NaN;
            Assert.False(AuthenticDockingLifecycle.TryRestore(valid, 200, 100, new ShipDockRegistry(),
                out _, out _));

            valid = RunCaptureReplay();
            var occupied = new ShipDockRegistry();
            occupied.TryClaim(100, 999);
            Assert.False(AuthenticDockingLifecycle.TryRestore(valid, 200, 100, occupied,
                out _, out DockingRejectReason occupiedReason));
            Assert.Equal(DockingRejectReason.YardOccupied, occupiedReason);
            Assert.Equal(999, occupied.DockedShipFor(100));
        }

        [Theory]
        [InlineData(DockingPropulsion.Sail)]
        [InlineData(DockingPropulsion.Engine)]
        [InlineData(DockingPropulsion.SailAndEngine)]
        public void Sail_or_engine_departure_keeps_yard_occupied_until_release_clearance(
            DockingPropulsion propulsion)
        {
            var claims = new ShipDockRegistry();
            AuthenticDockingLifecycle machine = Docked(claims);

            DockingStepResult inside = machine.Step(Frame(0.02, Target,
                new DockingMotion(1, 0, 0, 0), outsideRelease: false,
                propulsion: propulsion), claims);
            Assert.Equal(DockingPhase.Departing, inside.Phase);
            Assert.Equal(propulsion, machine.DeparturePropulsion);
            Assert.Equal(200, claims.DockedShipFor(100));

            DockingStepResult outside = machine.Step(Frame(0.02,
                new DockingPose(120, 20, -50, 0), new DockingMotion(1, 0, 0, 0),
                outsideRelease: true, propulsion: propulsion), claims);
            Assert.Equal(DockingPhase.Undocked, outside.Phase);
            Assert.True(outside.LinkReleased);
            Assert.False(claims.IsShipyardOccupied(100));
        }

        [Fact]
        public void Destroyed_yard_or_deleted_hull_clears_both_directions_idempotently()
        {
            var claims = new ShipDockRegistry();
            AuthenticDockingLifecycle machine = Docked(claims);

            DockingStepResult destroyed = machine.Step(Frame(0.02, Target, default,
                yardExists: false), claims);
            Assert.Equal(DockingPhase.Undocked, destroyed.Phase);
            Assert.True(destroyed.LinkReleased);
            Assert.Equal(0, claims.DockedShipFor(100));
            Assert.False(machine.DeleteHull(claims).LinkReleased);
        }

        [Fact]
        public void Permission_revocation_during_approach_releases_the_reservation()
        {
            var claims = new ShipDockRegistry();
            AuthenticDockingLifecycle machine = Begin(claims,
                new DockingPose(105, 20, -50, 0), default);

            DockingStepResult revoked = machine.Step(new DockingFrame(0.02,
                yardExists: true, permissionValid: false, propulsion: DockingPropulsion.None,
                collisionClear: true, outsideReleaseEnvelope: false,
                observedPose: machine.Pose, observedMotion: default), claims);

            Assert.Equal(DockingPhase.Undocked, revoked.Phase);
            Assert.Equal(DockingRejectReason.Unauthorized, revoked.Reason);
            Assert.True(revoked.LinkReleased);
            Assert.False(claims.IsShipyardOccupied(100));
        }

        [Fact]
        public void Lost_claim_fails_closed_without_clearing_the_current_occupant()
        {
            var claims = new ShipDockRegistry();
            AuthenticDockingLifecycle machine = Begin(claims, Target, default);
            Assert.True(claims.Release(100, 200));
            Assert.Equal(ShipDockClaimResult.Claimed, claims.TryClaim(100, 201));

            DockingStepResult stale = machine.Step(Frame(0.02, Target, default), claims);
            Assert.Equal(DockingPhase.Undocked, stale.Phase);
            Assert.Equal(DockingRejectReason.StaleClaim, stale.Reason);
            Assert.False(stale.LinkReleased);
            Assert.Equal(201, claims.DockedShipFor(100));
        }

        [Fact]
        public void Disconnect_is_not_a_dock_transition_and_neutral_approach_can_continue()
        {
            var claims = new ShipDockRegistry();
            AuthenticDockingLifecycle machine = Begin(claims,
                new DockingPose(105, 20, -50, 0), new DockingMotion(0.5, 0, 0, 0));

            // There is deliberately no peer/pilot field in the machine or DTO. A
            // disconnected pilot cannot release ownership or resurrect authority.
            DockingStepResult result = machine.Step(Frame(0.02,
                new DockingPose(104, 20, -50, 0), new DockingMotion(0.5, 0, 0, 0)), claims);
            Assert.Equal(DockingPhase.Captured, result.Phase);
            Assert.Equal(200, claims.DockedShipFor(100));
        }

        private static AuthenticDockingLifecycle Docked(ShipDockRegistry claims)
        {
            AuthenticDockingLifecycle machine = Begin(claims, Target, default);
            machine.Step(Frame(0.02, Target, default), claims);
            for (int i = 0; i < 200 && machine.Phase != DockingPhase.Docked; i++)
                machine.Step(Frame(0.02, machine.Pose, default), claims);
            Assert.Equal(DockingPhase.Docked, machine.Phase);
            return machine;
        }

        private static DockingSnapshotV1 RunCaptureReplay()
        {
            var claims = new ShipDockRegistry();
            AuthenticDockingLifecycle machine = Begin(claims,
                new DockingPose(104, 20, -50, -Math.PI + 0.1), default);
            machine.Step(Frame(0.02, machine.Pose, default), claims);
            for (int i = 0; i < 200 && machine.Phase != DockingPhase.Docked; i++)
                machine.Step(Frame(0.02, machine.Pose, default), claims);
            return machine.CaptureSnapshot();
        }

        private static AuthenticDockingLifecycle Begin(ShipDockRegistry claims,
            DockingPose pose, DockingMotion motion)
        {
            var machine = new AuthenticDockingLifecycle(200);
            Assert.True(machine.TryBeginApproach(Request(pose, motion: motion), claims, out _));
            return machine;
        }

        private static DockingApproachRequest Request(DockingPose pose,
            DockingMotion motion = default, bool propulsionNeutral = true,
            bool collisionClear = true, long hullId = 200) => new DockingApproachRequest(
                hullId, 100, "owner", "owner", false, false, true,
                propulsionNeutral, collisionClear, pose, Target, motion);

        private static DockingFrame Frame(double delta, DockingPose pose, DockingMotion motion,
            bool yardExists = true, bool collisionClear = true, bool outsideRelease = false,
            DockingPropulsion propulsion = DockingPropulsion.None) => new DockingFrame(delta,
                yardExists, true, propulsion, collisionClear, outsideRelease, pose, motion);

        private static void AssertRejected(DockingApproachRequest request,
            DockingRejectReason expected)
        {
            var machine = new AuthenticDockingLifecycle(request.HullEntityId);
            Assert.False(machine.TryBeginApproach(request, new ShipDockRegistry(),
                out DockingRejectReason actual));
            Assert.Equal(expected, actual);
        }
    }
}

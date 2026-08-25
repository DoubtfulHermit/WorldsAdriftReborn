using System;
using System.Text.Json;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public sealed class AuthenticDockingLifecycleTests
    {
        private static readonly DockingPose Target = new DockingPose(100, 20, -50, Math.PI - 0.1);

        /// <summary>
        /// The yard itself, <see cref="BuiltShipPlacement.HoverHeightMetres"/> below
        /// its dock pose - the bubble is centred on the YARD, not on where the hull
        /// ends up parked.
        /// </summary>
        private static readonly ShadowVector3 Yard = new ShadowVector3(100,
            20 - BuiltShipPlacement.HoverHeightMetres, -50);

        private static readonly DockingTuning Tuning = new DockingTuning();
        private static readonly ShipyardBubble Bubble = Tuning.BubbleAt(Yard);

        /// <summary>Beyond the bubble AND its exit margin: departure is complete here.</summary>
        private static readonly DockingPose FarOutsideBubble =
            new DockingPose(100 + 45, 20, -50, 0);

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
                FarOutsideBubble, new DockingMotion(1, 0, 0, 0),
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
                collisionClearance: Clearance(true, 1), bubble: Bubble,
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

        /// <summary>
        /// THE PLAYER'S RULE 1, first half: a ship the pilot is still flying never
        /// snaps, however deep inside the bubble they park it. Manning is not a
        /// docking decision - leaving the helm is.
        /// </summary>
        [Fact]
        public void A_manned_ship_parked_inside_the_bubble_only_ever_approaches()
        {
            var claims = new ShipDockRegistry();
            var machine = new AuthenticDockingLifecycle(200);
            var parked = new DockingPose(102, 20, -50, 0);
            Assert.True(machine.TryBeginApproach(Request(parked, helmManned: true),
                claims, out _));

            for (int i = 0; i < 50; i++)
            {
                DockingStepResult held = machine.Step(
                    Frame(0.02, parked, default, helmManned: true), claims);
                Assert.Equal(DockingPhase.Approaching, held.Phase);
                Assert.False(held.FreezeVelocity);
            }
            // The reservation is held the whole time - the yard is theirs to park in.
            Assert.Equal(200, claims.DockedShipFor(100));
        }

        /// <summary>
        /// THE PLAYER'S RULE 1, second half: they let go of the wheel inside the
        /// bubble and above the yard, and the ship snaps into the dock pose with its
        /// velocity frozen. The snap happens from anywhere in the dome - the bubble
        /// IS the capture volume, not a tighter invisible one inside it.
        /// </summary>
        [Theory]
        [InlineData(2.0)]
        [InlineData(15.0)]
        [InlineData(30.0)]
        public void Leaving_the_helm_inside_the_bubble_snaps_the_ship_into_the_dock(
            double metresFromTheYard)
        {
            var claims = new ShipDockRegistry();
            var machine = new AuthenticDockingLifecycle(200);
            var parked = new DockingPose(100 + metresFromTheYard, 20, -50, 0);
            Assert.True(Bubble.ContainsDock(parked.Position));
            Assert.True(machine.TryBeginApproach(Request(parked, helmManned: true),
                claims, out _));
            Assert.Equal(DockingPhase.Approaching,
                machine.Step(Frame(0.02, parked, default, helmManned: true), claims).Phase);

            DockingStepResult released = machine.Step(
                Frame(0.02, parked, new DockingMotion(0.4, 0, 0, 0)), claims);

            Assert.Equal(DockingPhase.Captured, released.Phase);
            Assert.True(released.FreezeVelocity);
            Assert.Equal(0, released.Motion.LinearSpeed);
            for (int i = 0; i < 200 && machine.Phase != DockingPhase.Docked; i++)
                machine.Step(Frame(0.02, machine.Pose, default), claims);
            Assert.Equal(DockingPhase.Docked, machine.Phase);
            Assert.Equal(Target, machine.Pose);
        }

        /// <summary>
        /// THE PLAYER'S RULE 2: getting back on the helm of a docked ship leaves it
        /// docked and in position. Only propulsion is a departure.
        /// </summary>
        [Fact]
        public void Taking_the_helm_back_while_docked_is_not_a_departure()
        {
            var claims = new ShipDockRegistry();
            AuthenticDockingLifecycle machine = Docked(claims);

            for (int i = 0; i < 50; i++)
            {
                DockingStepResult manned = machine.Step(
                    Frame(0.02, Target, default, helmManned: true), claims);
                Assert.Equal(DockingPhase.Docked, manned.Phase);
                Assert.True(manned.FreezeVelocity);
                Assert.False(manned.LinkReleased);
                Assert.Equal(Target, manned.Pose);
            }
            Assert.Equal(200, claims.DockedShipFor(100));
        }

        /// <summary>
        /// "Inside it and ABOVE the shipyard": the recovered ImpactRadius is a
        /// sphere, but a hull flying UNDER an island-mounted yard is not parked on
        /// it. The vertical band is the WAReborn half of the rule.
        /// </summary>
        [Fact]
        public void A_hull_under_the_yard_is_inside_the_sphere_but_not_inside_the_dome()
        {
            var below = new DockingPose(100, 20 - BuiltShipPlacement.HoverHeightMetres - 1,
                -50, 0);
            Assert.True(Bubble.IsWithinRange(below.Position));
            Assert.False(Bubble.ContainsDock(below.Position));
            AssertRejected(Request(below), DockingRejectReason.BelowShipyard);

            // And a hull that sinks below the yard mid-approach cannot capture there.
            var claims = new ShipDockRegistry();
            AuthenticDockingLifecycle machine = Begin(claims, Target, default);
            Assert.Equal(DockingPhase.Approaching,
                machine.Step(Frame(0.02, below, default), claims).Phase);
        }

        /// <summary>
        /// THE PLAYER'S RULE 3: propulsion begins the departure, but the link (and
        /// with it the bubble) only drops once the hull is FULLY outside the bubble.
        /// The 18 m release radius this path used to carry was invisible to the
        /// player; the dome is not.
        /// </summary>
        [Fact]
        public void Departure_completes_only_once_the_hull_is_fully_outside_the_bubble()
        {
            var claims = new ShipDockRegistry();
            AuthenticDockingLifecycle machine = Docked(claims);
            var moving = new DockingMotion(4, 0, 0, 0);

            // Well outside the OLD 18 m release radius, still inside the bubble.
            DockingStepResult leaving = machine.Step(Frame(0.02,
                new DockingPose(120, 14, -50, 0), moving,
                propulsion: DockingPropulsion.Sail), claims);
            Assert.Equal(DockingPhase.Departing, leaving.Phase);
            Assert.False(leaving.LinkReleased);
            Assert.Equal(200, claims.DockedShipFor(100));

            // On the visible edge, and inside the hysteresis band past it.
            foreach (double distance in new[] { 34.9, 35.0, 36.9 })
            {
                DockingStepResult edge = machine.Step(Frame(0.02,
                    new DockingPose(100 + distance, 14, -50, 0), moving,
                    propulsion: DockingPropulsion.Sail), claims);
                Assert.Equal(DockingPhase.Departing, edge.Phase);
                Assert.Equal(200, claims.DockedShipFor(100));
            }

            DockingStepResult cleared = machine.Step(Frame(0.02,
                new DockingPose(137.1, 14, -50, 0), moving,
                propulsion: DockingPropulsion.Sail), claims);
            Assert.Equal(DockingPhase.Undocked, cleared.Phase);
            Assert.True(cleared.LinkReleased);
            Assert.False(claims.IsShipyardOccupied(100));
        }

        /// <summary>
        /// "FULLY out" is literal: the hull's own extent counts, so a big hull whose
        /// centre is past the margin but whose flank still overlaps the dome has not
        /// cleared it.
        /// </summary>
        [Fact]
        public void Fully_outside_the_bubble_counts_the_hulls_own_extent()
        {
            var claims = new ShipDockRegistry();
            AuthenticDockingLifecycle machine = Docked(claims);
            var centreJustOutside = new DockingPose(137.5, 14, -50, 0);
            Assert.True(Bubble.HasFullyCleared(centreJustOutside.Position));

            DockingStepResult wide = machine.Step(new DockingFrame(0.02, true, true,
                DockingPropulsion.Engine, Clearance(true, 1), Bubble,
                centreJustOutside, new DockingMotion(4, 0, 0, 0),
                helmManned: false, hullClearanceRadiusMetres: 6.0), claims);

            Assert.Equal(DockingPhase.Departing, wide.Phase);
            Assert.False(wide.LinkReleased);
            Assert.Equal(200, claims.DockedShipFor(100));
        }

        /// <summary>
        /// A reservation is not a lease: a hull that leaves the bubble while merely
        /// approaching hands the yard back instead of holding it forever.
        /// </summary>
        [Fact]
        public void An_approach_that_leaves_the_bubble_gives_the_yard_back()
        {
            var claims = new ShipDockRegistry();
            AuthenticDockingLifecycle machine = Begin(claims,
                new DockingPose(110, 20, -50, 0), default);

            DockingStepResult gone = machine.Step(
                Frame(0.02, FarOutsideBubble, default, helmManned: true), claims);

            Assert.Equal(DockingPhase.Undocked, gone.Phase);
            Assert.Equal(DockingRejectReason.OutsideApproachRadius, gone.Reason);
            Assert.True(gone.LinkReleased);
            Assert.False(claims.IsShipyardOccupied(100));
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
            bool collisionClear = true, long hullId = 200,
            bool helmManned = false) => new DockingApproachRequest(
                hullId, 100, "ship:stable", "yard:stable", "owner", "owner", false, false, true,
                propulsionNeutral, Clearance(collisionClear, 0), pose, Target, motion,
                Bubble, helmManned);

        /// <summary>
        /// <paramref name="outsideRelease"/> substitutes a pose genuinely beyond the
        /// bubble: the lifecycle derives departure clearance from the geometry now,
        /// so a test cannot assert it by handing over a bare flag.
        /// </summary>
        private static DockingFrame Frame(double delta, DockingPose pose, DockingMotion motion,
            bool yardExists = true, bool collisionClear = true, bool outsideRelease = false,
            DockingPropulsion propulsion = DockingPropulsion.None,
            bool helmManned = false) => new DockingFrame(delta,
                yardExists, true, propulsion, Clearance(collisionClear, 1), Bubble,
                outsideRelease ? FarOutsideBubble : pose, motion, helmManned);

        private static CollisionClearanceRecord Clearance(bool clear, long fixedStep) =>
            new CollisionClearanceRecord("ship:stable", "yard:stable", fixedStep,
                clear ? 0 : 1, EvaluationComplete: true);

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

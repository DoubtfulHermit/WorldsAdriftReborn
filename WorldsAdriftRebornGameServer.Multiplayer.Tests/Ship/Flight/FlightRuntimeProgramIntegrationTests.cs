using System;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// Step-6 cross-track gates: the four merged tracks (mass, vector/lift,
    /// collision, docking) driven together through the production types, the way
    /// the service glue composes them. No policy is reimplemented in a helper.
    /// </summary>
    public sealed class FlightRuntimeProgramIntegrationTests
    {
        private const long HullEntityId = 200;
        private const long YardEntityId = 100;
        private const string HullKey = "ship:stable";
        private const string YardKey = "yard:stable";
        private static readonly FlightTuning Tuning = new FlightTuning();
        private static readonly DockingTuning Docking = new DockingTuning();
        private static readonly DockingPose Target = new(0, 100, 0, 0);

        private static ShadowMassProperties Mass() => new ShadowMassProperties(
            1000.0, ShadowVector3.Zero, new ShadowVector3(1e5, 1e5, 1e5), true);

        private static VectorFlightStepInput StepInput(double propulsorPower,
            FlightControlInput input) => new VectorFlightStepInput(
            HullKey, FixedFlightClock.StepSeconds, Mass(),
            new ShadowVector3(2.0, 1.5, 6.0),
            new[]
            {
                new ShadowPropulsor(ShadowPartKind.Engine, ShadowVector3.Zero,
                    ShadowQuaternion.Identity, propulsorPower, 58.5),
            },
            Array.Empty<VectorWingSurface>(),
            Math.Clamp(input.Throttle, -1.0, 1.0), WindSample.Calm, input,
            new LiftRuntimeStepPolicy(ShipLiftPolicy.SeededTotalLiftKg,
                GravityParameter.UnityDefaultApproximation, false), Tuning);

        private static StampedCollisionClearance Clear(long step, long generation = 1) =>
            new(new CollisionClearanceRecord(HullKey, YardKey, step, 0, true),
                new FlightAuthorityStamp(step, generation));

        private static DockingMotion MotionOf(VectorFlightState state) => new(
            state.VelocityMps.X, state.VelocityMps.Y, state.VelocityMps.Z,
            state.AngularVelocityRadPerSec.Magnitude);

        private static DockingPose PoseOf(VectorFlightState state)
        {
            FlightState projected = VectorFlightRuntime.Project(state);
            return new DockingPose(projected.X, projected.Y, projected.Z,
                projected.YawRadians);
        }

        /// <summary>
        /// THE REST-SNAP x CAPTURE INTERACTION (integration brief item 6): the
        /// vector rest snap acts below 0.01 m/s, docking capture negotiates at or
        /// below 2 m/s - the same low-speed regime. A hull coasting into a yard
        /// under vector authority must dock cleanly: the snap never robs the
        /// capture window of its real motion evidence, never fights the capture
        /// freeze, and never pins a hull whose departure propulsion is live.
        /// </summary>
        [Fact]
        public void Vector_rest_snap_never_fights_docking_capture_or_departure()
        {
            // ---- approach band: coasting in at ~1 m/s, neutral helm, no power.
            var runtime = new VectorFlightRuntime(VectorFlightRuntime.FromFlightState(
                new FlightState(0, 100, -6, 0, 0, 0, 0, 0, 0, 0, 1.0)));
            for (int i = 0; i < 25; i++)
            {
                runtime.Step(StepInput(0.0, FlightControlInput.Neutral));
                double speed = runtime.State.VelocityMps.Magnitude;
                // Inside the capture negotiation band (0.01 .. 2.0] the snap must
                // leave the real motion alone - a snapped-to-zero approach would
                // hand the capture gate a fake at-rest hull.
                Assert.InRange(speed, 0.5, 2.0);
            }

            // ---- capture: the transactional runtime sees the REAL vector motion.
            var claims = new ShipDockRegistry();
            var port = new RecordingPort();
            var docking = new DockingRuntime(HullEntityId, claims, port,
                new DockingRuntimeOptions { Enabled = true }, Docking);
            DockingPose observed = PoseOf(runtime.State);
            DockingMotion motion = MotionOf(runtime.State);
            Assert.True(observed.DistanceTo(Target) <= Docking.CaptureRadiusMetres);
            Assert.True(motion.LinearSpeed <= Docking.MaximumCaptureSpeedMetresPerSecond);
            DockingRuntimeResult approach = docking.TryBeginApproach(
                new DockingApproachRequest(HullEntityId, YardEntityId, HullKey, YardKey,
                    "owner", "owner", false, false, true, true,
                    Clear(26).Clearance, observed, Target, motion),
                Clear(26));
            Assert.Equal(DockingRuntimeDisposition.Committed, approach.Disposition);
            DockingRuntimeResult captured = docking.Step(
                new DockingFrame(FixedFlightClock.StepSeconds, true, true,
                    DockingPropulsion.None, Clear(27).Clearance, false, observed, motion),
                Clear(27));
            Assert.Equal(DockingRuntimeDisposition.Committed, captured.Disposition);
            Assert.Equal(DockingPhase.Captured, docking.Lifecycle.Phase);
            Assert.True(captured.FreezeVelocity);

            // ---- freeze + reseed: the glue resets the session pose (DockAt) and
            // requests a vector reseed; the runtime re-seeds at rest at the frozen
            // pose. The snap must HOLD that rest, not fight the freeze.
            DockingPose frozen = docking.Lifecycle.Pose;
            runtime.Reset(VectorFlightRuntime.FromFlightState(FlightState.AtRestAt(
                frozen.X, frozen.Y, frozen.Z, frozen.YawRadians)));
            for (int i = 0; i < 25; i++)
            {
                runtime.Step(StepInput(0.0, FlightControlInput.Neutral));
            }
            Assert.Equal(0.0, runtime.State.VelocityMps.Magnitude);
            DockingPose held = PoseOf(runtime.State);
            Assert.True(held.DistanceTo(frozen) < 0.1,
                "the reseeded hull drifted " + held.DistanceTo(frozen)
                + " m off its freeze pose while docked");
            // A steady docked frame from the held pose stays committed/frozen.
            DockingRuntimeResult steady = docking.Step(
                new DockingFrame(FixedFlightClock.StepSeconds, true, true,
                    DockingPropulsion.None, Clear(28).Clearance, false, held,
                    MotionOf(runtime.State)),
                Clear(28));
            Assert.Equal(DockingRuntimeDisposition.Committed, steady.Disposition);
            Assert.True(steady.FreezeVelocity);

            // ---- departure: live propulsion. The snap must never pin a powered
            // hull at rest, and the lifecycle must hold occupancy until the hull
            // clears the release envelope.
            for (int i = 0; i < 25; i++)
            {
                runtime.Step(StepInput(1400.0, new FlightControlInput(1f, 0, 0, 0, 0)));
            }
            Assert.True(runtime.State.VelocityMps.Magnitude > 0.0,
                "the rest snap pinned a hull whose departure propulsion is live");
            DockingRuntimeResult departing = docking.Step(
                new DockingFrame(FixedFlightClock.StepSeconds, true, true,
                    DockingPropulsion.Engine, Clear(29).Clearance, false,
                    PoseOf(runtime.State), MotionOf(runtime.State)),
                Clear(29));
            Assert.Equal(DockingPhase.Departing, docking.Lifecycle.Phase);
            Assert.False(departing.LinkReleased);
            Assert.Equal(HullEntityId, claims.DockedShipFor(YardEntityId));
            DockingRuntimeResult released = docking.Step(
                new DockingFrame(FixedFlightClock.StepSeconds, true, true,
                    DockingPropulsion.Engine, Clear(30).Clearance, true,
                    PoseOf(runtime.State), MotionOf(runtime.State)),
                Clear(30));
            Assert.True(released.LinkReleased);
            Assert.Equal(DockingPhase.Undocked, docking.Lifecycle.Phase);
            Assert.False(claims.IsShipyardOccupied(YardEntityId));
        }

        private sealed class RecordingPort : IDockingRuntimeTransaction
        {
            public DockingCommitResult TryCommit(DockingRuntimeCommit commit) =>
                DockingCommitResult.Committed;
        }
    }
}

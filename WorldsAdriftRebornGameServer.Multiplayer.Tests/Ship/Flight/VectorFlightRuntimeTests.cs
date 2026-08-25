using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class VectorFlightRuntimeTests
    {
        private const double Dt = FixedFlightClock.StepSeconds;
        private const double MassKg = 1000.0;
        private static readonly FlightTuning Tuning = new FlightTuning();

        private static ShadowMassProperties Mass(double inertia = 100000.0) =>
            new ShadowMassProperties(MassKg, ShadowVector3.Zero,
                new ShadowVector3(inertia, inertia, inertia), isApproximation: true);

        private static LiftRuntimeStepPolicy GenerousLift(bool abandoned = false) =>
            new LiftRuntimeStepPolicy(ShipLiftPolicy.SeededTotalLiftKg,
                GravityParameter.UnityDefaultApproximation, abandoned);

        private static ShadowPropulsor Engine(double x, double z, double power = 1400.0) =>
            new ShadowPropulsor(ShadowPartKind.Engine, new ShadowVector3(x, 0.0, z),
                ShadowQuaternion.Identity, power, 58.5);

        private static VectorFlightStepInput Input(
            IReadOnlyList<ShadowPropulsor>? propulsors = null,
            IReadOnlyList<VectorWingSurface>? wings = null,
            double engineSpin = 0.0,
            FlightControlInput input = default,
            LiftRuntimeStepPolicy? lift = null,
            ShadowMassProperties? mass = null,
            WindSample? wind = null,
            FlightTuning? tuning = null) =>
            new VectorFlightStepInput("hull:test", Dt,
                mass ?? Mass(), new ShadowVector3(2.0, 1.5, 6.0),
                propulsors ?? Array.Empty<ShadowPropulsor>(),
                wings ?? Array.Empty<VectorWingSurface>(),
                engineSpin, wind ?? WindSample.Calm, input,
                lift ?? GenerousLift(), tuning ?? Tuning);

        private static VectorFlightRuntime AtRestRuntime() => new VectorFlightRuntime(
            VectorFlightRuntime.FromFlightState(FlightState.AtRestAt(0.0, 300.0, 0.0)));

        [Fact]
        public void Identical_inputs_replay_to_identical_states()
        {
            VectorFlightStepInput step = Input(
                new[] { Engine(1.5, -3.0) }, engineSpin: 1.0,
                input: new FlightControlInput(1f, 0.2f, 0.1f, 0.3f, 0f),
                wind: WindSample.FromComponents(1.0, -2.0, 0.0));
            VectorFlightRuntime a = AtRestRuntime();
            VectorFlightRuntime b = AtRestRuntime();

            for (int i = 0; i < 250; i++)
            {
                a.Step(step);
                b.Step(step);
            }

            Assert.Equal(a.State, b.State);
        }

        [Fact]
        public void Euler_extraction_inverts_the_one_attitude_composition()
        {
            var state = new FlightState(0, 0, 0,
                yawRadians: 0.7, yawRateRadPerSec: 0.0,
                rollRadians: -0.12, pitchRadians: 0.08,
                speedCmdMps: 0, vxMps: 0, vyMps: 0, vzMps: 0);
            (double w, double x, double y, double z) = FlightIntegrator.AttitudeQuaternion(state);
            Assert.True(ShadowQuaternion.TryNormalized(w, x, y, z, out ShadowQuaternion q));

            (double yaw, double pitch, double roll) = VectorFlightRuntime.ExtractYawPitchRoll(q);

            Assert.Equal(0.7, yaw, 9);
            Assert.Equal(0.08, pitch, 9);
            Assert.Equal(-0.12, roll, 9);
        }

        [Fact]
        public void Projection_reports_the_same_pose_as_the_vector_state()
        {
            var scalar = new FlightState(10, 300, -5, 0.4, 0.02, -0.03, 0.01, 0, 1.0, -0.2, 3.0);
            VectorFlightState state = VectorFlightRuntime.FromFlightState(scalar);

            FlightState projected = VectorFlightRuntime.Project(state);

            Assert.Equal(scalar.X, projected.X, 12);
            Assert.Equal(scalar.Y, projected.Y, 12);
            Assert.Equal(scalar.Z, projected.Z, 12);
            Assert.Equal(scalar.YawRadians, projected.YawRadians, 9);
            Assert.Equal(scalar.PitchRadians, projected.PitchRadians, 9);
            Assert.Equal(scalar.RollRadians, projected.RollRadians, 9);
            Assert.Equal(scalar.VxMps, projected.VxMps, 12);
            Assert.Equal(scalar.YawRateRadPerSec, projected.YawRateRadPerSec, 12);
        }

        [Fact]
        public void An_engine_at_the_centre_of_mass_accelerates_without_turning()
        {
            VectorFlightRuntime runtime = AtRestRuntime();

            VectorFlightStepResult result = runtime.Step(Input(
                new[] { Engine(0.0, 0.0) }, engineSpin: 1.0));

            Assert.True(result.Integrated);
            Assert.True(runtime.State.VelocityMps.Z > 0.027);
            Assert.True(runtime.State.VelocityMps.Z <= 0.028 + 1e-12);
            Assert.Equal(ShadowVector3.Zero, runtime.State.AngularVelocityRadPerSec);
        }

        [Fact]
        public void Gravity_is_applied_exactly_once_so_a_weight_cancelled_hull_hovers()
        {
            VectorFlightRuntime runtime = AtRestRuntime();

            VectorFlightStepResult result = runtime.Step(Input(
                new[] { Engine(0.0, 0.0) }, engineSpin: 1.0));

            // Weight cancellation minus gravity leaves EXACTLY zero net vertical
            // force: no fall (gravity twice) and no climb (gravity missing).
            Assert.Equal(0.0, result.Lift.NetVerticalForceNewtons);
            Assert.Equal(0.0, runtime.State.VelocityMps.Y);
        }

        [Fact]
        public void An_offset_engine_produces_yaw_torque_through_the_retail_filter()
        {
            VectorFlightRuntime runtime = AtRestRuntime();

            runtime.Step(Input(new[] { Engine(5.0, 0.0, power: 1400.0) }, engineSpin: 1.0));

            // Lever (5,0,0) x force (0,0,1400) = (0,-7000,0); dead zone 2500 and
            // the halving leave -2250 N*m, damped one step on 1e5 kg*m2 inertia.
            double expected = -2250.0 / 100000.0 * Dt
                * (1.0 - (VectorFlightRuntime.AngularDampingPerSecond * Dt));
            Assert.Equal(expected, runtime.State.AngularVelocityRadPerSec.Y, 12);
        }

        [Fact]
        public void Torque_inside_the_retail_dead_zone_is_suppressed()
        {
            VectorFlightRuntime runtime = AtRestRuntime();

            // Lever 1 m x 1400 N = 1400 N*m, below the 2500 N*m dead zone.
            runtime.Step(Input(new[] { Engine(1.0, 0.0) }, engineSpin: 1.0));

            Assert.Equal(ShadowVector3.Zero, runtime.State.AngularVelocityRadPerSec);
        }

        [Fact]
        public void Wing_steering_torque_follows_the_recovered_shape()
        {
            var flat = new VectorWingSurface(ShadowVector3.Up, 1000.0);
            var vertical = new VectorWingSurface(ShadowVector3.Right, 1000.0);
            var stick = new FlightControlInput(0f, 0f, 1f, 1f, 1f);

            ShadowVector3 flatTorque = VectorFlightRuntime.WingSteeringTorque(
                new[] { flat }, stick, speedMps: 10.0);
            ShadowVector3 verticalTorque = VectorFlightRuntime.WingSteeringTorque(
                new[] { vertical }, stick, speedMps: 10.0);
            ShadowVector3 atRest = VectorFlightRuntime.WingSteeringTorque(
                new[] { flat }, stick, speedMps: 0.0);
            ShadowVector3 halfSpeed = VectorFlightRuntime.WingSteeringTorque(
                new[] { flat }, stick, speedMps: 5.0);

            // A flat wing pitches/rolls at full alignment and yaws at the floor;
            // a vertical wing yaws at full alignment. Zero speed gives no
            // authority and 5 m/s gives exactly half of the 10 m/s ramp.
            Assert.Equal(1000.0, flatTorque.X, 9);
            Assert.Equal(200.0, flatTorque.Y, 9);
            Assert.Equal(-1000.0, flatTorque.Z, 9);
            Assert.Equal(1000.0, verticalTorque.Y, 9);
            Assert.Equal(200.0, verticalTorque.X, 9);
            Assert.Equal(ShadowVector3.Zero, atRest);
            Assert.Equal(0.5 * flatTorque.X, halfSpeed.X, 9);
        }

        [Fact]
        public void Recovered_drag_opposes_horizontal_motion_and_never_reverses_it()
        {
            VectorFlightRuntime runtime = new VectorFlightRuntime(
                VectorFlightRuntime.FromFlightState(new FlightState(
                    0, 300, 0, 0, 0, 0, 0, 0, 0.0, 0.0, 12.0)));

            runtime.Step(Input());

            // Neutral input, calm air: the carried wind is zero, so the SHARED
            // relative-wind law reduces to the recovered 2.5-power drag plus the
            // always-on 0.03 m/s2 residual settle - the same pair the scalar
            // StepSpeed applies - opposing travel.
            double expected = 12.0
                - (ShipForceModel.RelativeWindApproachDeltaMps(12.0, Dt));
            Assert.Equal(12.0 - ((ShipForceModel.DragDecelerationMps2(12.0)
                + ShipForceModel.LowSpeedSettleAccelMps2) * Dt), expected, 12);
            Assert.Equal(expected, runtime.State.VelocityMps.Z, 9);
            Assert.True(runtime.State.VelocityMps.Z > 0.0);
        }

        [Fact]
        public void Hull_3639_full_throttle_empty_generator_furled_sails_carries_the_commanded_baseline()
        {
            // THE DEPLOYED "SLOW SHIP" REGRESSION, on the vector path: promote
            // hull 3639, man the helm, full throttle, generator empty (engines
            // mounted but unpowered), sails mounted but furled. The scalar
            // additive-tier fix carries this hull at the commanded sky-core
            // baseline; the vector path must produce the SAME carry through the
            // SAME shared decision and the SAME terminal-speed model, or the
            // player-visible defect the last deployment fixed returns.
            ShipMassSnapshot snapshot = Materials.ShipMassEvaluatorTests.Hull3639();
            // The deployed additive-tier configuration:
            // WAREBORN_FLIGHT_BARE_HULL_MULTIPLIER=4.
            var tuning = new FlightTuning(bareHullDriveMultiplier: 4.0);
            var mass = new ShadowMassProperties(snapshot.TotalFlightMassKg,
                snapshot.CentreOfMassApprox, snapshot.DiagonalInertiaApproxKgM2,
                isApproximation: true);
            ShadowPropulsor[] unpowered =
            {
                new ShadowPropulsor(ShadowPartKind.Sail, new ShadowVector3(-1.0, 0.0, 1.0),
                    ShadowQuaternion.Identity, 0.0, 50.0),
                new ShadowPropulsor(ShadowPartKind.Sail, new ShadowVector3(1.0, 0.0, 1.0),
                    ShadowQuaternion.Identity, 0.0, 50.0),
                new ShadowPropulsor(ShadowPartKind.Engine, new ShadowVector3(0.0, 0.0, -4.0),
                    ShadowQuaternion.Identity, 0.0, 58.5),
            };
            WindSample wind = WindSample.FromComponents(
                ShipForceModel.DefaultWindX, ShipForceModel.DefaultWindZ, 0.0);
            var helm = new FlightControlInput(1f, 0f, 0f, 0f, 0f);

            // The scalar reference is the production evaluation itself: no
            // powered engines, no unfurled canvas, full throttle.
            ShipForceEvaluation scalar = ShipForceEvaluator.Evaluate(
                0.0, 0.0, 0.0, helm, new ShipPropulsion(snapshot.TotalFlightMassKg, 0.0, 0),
                tuning, 0.0);
            Assert.True(scalar.WindAlongHeadingMps > 2.0,
                "the scalar commanded baseline itself vanished: " + scalar.WindAlongHeadingMps);

            VectorFlightRuntime runtime = AtRestRuntime();
            VectorFlightStepInput step = Input(unpowered, input: helm, mass: mass,
                wind: wind, tuning: tuning);
            double scalarSpeed = 0.0;
            VectorFlightStepResult last = default;
            for (int i = 0; i < 6000; i++)
            {
                last = runtime.Step(step);
                scalarSpeed = ShipForceModel.StepSpeed(
                    scalarSpeed, 0.0, Dt, scalar.WindAlongHeadingMps);
            }

            Assert.True(last.Integrated);
            // Same tier decision - the shared code, fed the same inputs.
            Assert.Equal(scalar.WindAlongHeadingMps, last.CarriedWindAlongHeadingMps, 12);
            // Same terminal-speed model - the vector velocity tracks the scalar
            // integration step for step over two minutes of flight.
            Assert.Equal(scalarSpeed, runtime.State.VelocityMps.Z, 9);
            // And the hull is genuinely CARRIED, not parked: it has reached the
            // commanded baseline, the exact speed the scalar path settles at.
            Assert.True(runtime.State.VelocityMps.Z > 0.99 * scalar.WindAlongHeadingMps,
                "vector carry " + runtime.State.VelocityMps.Z + " never reached the scalar "
                + scalar.WindAlongHeadingMps);
        }

        [Fact]
        public void Wall_air_mass_attenuation_applies_identically_in_scalar_and_vector()
        {
            // A Wind Rift 100 m ahead, lever centred: wall air is spatial
            // resistance and must act in BOTH paths, attenuated by the SAME
            // recovered mass law - a heavy hull feels less of the wall than a
            // light one, identically on either integrator.
            var walls = new List<WeatherWallSegment>
            {
                new WeatherWallSegment(-1000.0, 100.0, 1000.0, 100.0,
                    WeatherWallType.WindRift, windMultiplier: 3.0),
            };
            var tuning = new FlightTuning();
            var helm = new FlightControlInput(0f, 0f, 0f, 0f, 0f);
            WindSample sample = WindField.SampleAt(0.0, 0.0, 5.0,
                tuning.WindSpeedMps, tuning.WindVariation, walls);
            Assert.Equal(1.0, sample.WallIntensity);

            double CarryFor(double massKg)
            {
                var mass = new ShadowMassProperties(massKg, ShadowVector3.Zero,
                    new ShadowVector3(1e5, 1e5, 1e5), true);
                VectorFlightRuntime runtime = AtRestRuntime();
                VectorFlightStepResult result = runtime.Step(Input(
                    input: helm, mass: mass, wind: sample, tuning: tuning));
                Assert.True(result.Integrated);
                // The wall pushes the hull; it is felt, not merely reported.
                Assert.True(runtime.State.VelocityMps.Z < 0.0);

                ShipForceEvaluation scalar = ShipForceEvaluator.Evaluate(
                    0.0, 0.0, 0.0, helm, new ShipPropulsion(massKg, 0.0, 0),
                    tuning, 5.0, walls);
                // Identical evidence, identical answer, in both paths.
                Assert.Equal(scalar.WindAlongHeadingMps, result.CarriedWindAlongHeadingMps, 12);
                Assert.Equal(WindField.SignedAlongHeading(in sample, 0.0)
                    * ShipForceModel.WindMultiplier(massKg),
                    result.CarriedWindAlongHeadingMps, 12);
                return result.CarriedWindAlongHeadingMps;
            }

            double light = CarryFor(500.0);
            double heavy = CarryFor(4000.0);
            // The recovered attenuation: a 4000 kg barge feels 25% of the wall a
            // 500 kg skiff feels at 90.625% - the ratio is the mass law's, exactly.
            Assert.Equal(ShipForceModel.WindMultiplier(4000.0)
                / ShipForceModel.WindMultiplier(500.0), heavy / light, 9);
            // Headwind sign preserved: crossing a wall is a force contest, not a
            // free speed bonus.
            Assert.True(light < 0.0 && heavy < 0.0);
        }

        [Fact]
        public void A_neutral_settled_hull_snaps_to_exact_rest()
        {
            VectorFlightRuntime runtime = new VectorFlightRuntime(
                VectorFlightRuntime.FromFlightState(new FlightState(
                    0, 300, 0, 0.5, 0.001, 0, 0, 0, 0.0, 0.0, 0.005)));

            VectorFlightStepResult result = runtime.Step(Input());

            Assert.True(result.SnappedToRest);
            FlightState projected = VectorFlightRuntime.Project(runtime.State);
            Assert.True(projected.IsAtRest);
            // The residual 0.001 rad/s turn integrated one final 20 ms step
            // before the snap froze the heading, so the yaw keeps that step.
            Assert.Equal(0.5, projected.YawRadians, 3);
            Assert.Equal(0.0, runtime.State.CommandLiftForceNewtons);
        }

        [Fact]
        public void A_tilted_hull_settles_gradually_and_never_claims_rest_while_tilted()
        {
            // Left at a 10-degree roll with everything else quiet, the hull must
            // neither fly tilted forever nor pop flat in one 20 ms step: the
            // labelled WAREBORN rest stabilization walks it level at a bounded
            // per-step rate, and rest is not claimed until it is inside the
            // half-degree snap threshold.
            double initialRoll = 10.0 * Math.PI / 180.0;
            VectorFlightRuntime runtime = new VectorFlightRuntime(
                VectorFlightRuntime.FromFlightState(new FlightState(
                    0, 300, 0, 0.5, 0, initialRoll, 0, 0, 0, 0, 0)));
            VectorFlightStepInput step = Input();
            double maxPerStep = (VectorFlightRuntime.RestAttitudeSettleRateRadPerSec * Dt)
                + 1e-9;

            double previousRoll = initialRoll;
            int stepsToRest = 0;
            for (int i = 0; i < 200 && stepsToRest == 0; i++)
            {
                VectorFlightStepResult result = runtime.Step(step);
                (_, _, double roll) = VectorFlightRuntime.ExtractYawPitchRoll(
                    runtime.State.Orientation);
                if (result.SnappedToRest)
                {
                    // The one sanctioned discontinuity: the final level-off is
                    // bounded by the labelled threshold, under wire quantisation.
                    Assert.True(Math.Abs(previousRoll)
                        <= VectorFlightRuntime.RestAttitudeSnapThresholdRadians + 1e-9);
                    stepsToRest = i + 1;
                    break;
                }
                Assert.True(Math.Abs(roll - previousRoll) <= maxPerStep,
                    "attitude popped " + Math.Abs(roll - previousRoll)
                    + " rad in one step at step " + i);
                // Never at rest while tilted beyond the threshold.
                Assert.False(VectorFlightRuntime.Project(runtime.State).IsAtRest);
                previousRoll = roll;
            }

            // ~0.2 deg/step: a 10-degree roll is a ~1 s settle, not a pop.
            Assert.InRange(stepsToRest, 40, 60);
            FlightState rested = VectorFlightRuntime.Project(runtime.State);
            Assert.True(rested.IsAtRest);
            Assert.Equal(0.5, rested.YawRadians, 6);
        }

        [Fact]
        public void A_barely_tilted_hull_still_snaps_level_inside_the_labelled_threshold()
        {
            // Upright-rest behavior unchanged: within the half-degree threshold
            // the snap is the same one-step level-off it always was.
            VectorFlightRuntime runtime = new VectorFlightRuntime(
                VectorFlightRuntime.FromFlightState(new FlightState(
                    0, 300, 0, 0.5, 0, 0.4 * Math.PI / 180.0, 0, 0, 0, 0, 0)));

            VectorFlightStepResult result = runtime.Step(Input());

            Assert.True(result.SnappedToRest);
            Assert.True(VectorFlightRuntime.Project(runtime.State).IsAtRest);
        }

        [Fact]
        public void An_overloaded_hull_never_snaps_to_rest_at_its_apex()
        {
            var overloaded = new LiftRuntimeStepPolicy(500.0,
                GravityParameter.UnityDefaultApproximation, false);
            VectorFlightRuntime runtime = AtRestRuntime();

            VectorFlightStepResult result = runtime.Step(Input(lift: overloaded));

            Assert.False(result.SnappedToRest);
            Assert.True(result.Lift.Overloaded);
            Assert.True(runtime.State.VelocityMps.Y < 0.0);
        }

        [Fact]
        public void Invalid_input_coasts_deterministically_instead_of_freezing_or_inventing_forces()
        {
            VectorFlightRuntime runtime = new VectorFlightRuntime(
                VectorFlightRuntime.FromFlightState(new FlightState(
                    0, 300, 0, 0, 0, 0, 0, 0, 0.0, 0.0, 4.0)));
            var invalid = new VectorFlightStepInput("hull:test", Dt,
                new ShadowMassProperties(-1.0, ShadowVector3.Zero, ShadowVector3.Zero, true),
                new ShadowVector3(2, 1.5, 6), Array.Empty<ShadowPropulsor>(),
                Array.Empty<VectorWingSurface>(), 0.0, WindSample.Calm,
                default, GenerousLift(), Tuning);

            VectorFlightStepResult result = runtime.Step(invalid);

            Assert.False(result.Integrated);
            Assert.Equal("input-invalid", result.Disposition);
            Assert.Equal(4.0 * Dt, runtime.State.Position.Z, 12);
            Assert.Equal(4.0, runtime.State.VelocityMps.Z);
        }

        [Fact]
        public void Zero_inertia_refuses_angular_response_instead_of_dividing_by_it()
        {
            VectorFlightRuntime runtime = AtRestRuntime();

            runtime.Step(Input(new[] { Engine(5.0, 0.0) }, engineSpin: 1.0,
                mass: new ShadowMassProperties(MassKg, ShadowVector3.Zero,
                    ShadowVector3.Zero, isApproximation: true)));

            Assert.Equal(ShadowVector3.Zero, runtime.State.AngularVelocityRadPerSec);
            Assert.True(runtime.State.IsFinite);
        }

        [Fact]
        public void The_sail_yaw_seam_stays_visible()
        {
            Assert.Contains("packed mount rotation", VectorFlightRuntime.SailYawSeam);
            Assert.Contains("joint state unavailable", VectorFlightRuntime.SailYawSeam);
        }

        [Fact]
        public void Restart_resumes_the_exact_uninterrupted_trajectory()
        {
            VectorFlightStepInput step = Input(
                new[] { Engine(0.0, -2.0) }, engineSpin: 1.0,
                input: new FlightControlInput(1f, 1f, 0f, 0f, 0f));
            VectorFlightRuntime uninterrupted = AtRestRuntime();
            VectorFlightRuntime beforeRestart = AtRestRuntime();
            for (int i = 0; i < 100; i++)
            {
                uninterrupted.Step(step);
                beforeRestart.Step(step);
            }

            // Restart: durable capture -> base pose restore -> vector extension
            // restore, exactly the production seam.
            Multiplayer.Persistence.DurableVectorFlightState durable =
                Multiplayer.Persistence.DurableVectorFlightState.Capture(beforeRestart.State);
            FlightState basePose = VectorFlightRuntime.Project(beforeRestart.State);
            Assert.True(durable.TryRead(basePose, out VectorFlightState restoredState));
            VectorFlightRuntime restored = new VectorFlightRuntime(restoredState);

            for (int i = 0; i < 100; i++)
            {
                uninterrupted.Step(step);
                restored.Step(step);
            }

            Assert.Equal(uninterrupted.State.VelocityMps.Y, restored.State.VelocityMps.Y, 9);
            Assert.Equal(uninterrupted.State.CommandLiftForceNewtons,
                restored.State.CommandLiftForceNewtons, 9);
            Assert.Equal(uninterrupted.State.Position.Z, restored.State.Position.Z, 6);
        }

        [Fact]
        public void Losing_the_smoothing_state_would_have_dropped_the_climb_command()
        {
            // The guard the durable extension exists for: a climbing hull whose
            // command-lift smoothing state is zeroed loses climb force for the
            // next step. This asserts the failure mode is real, so the restore
            // test above is testing something.
            VectorFlightStepInput step = Input(input: new FlightControlInput(0f, 1f, 0f, 0f, 0f));
            VectorFlightRuntime climbing = AtRestRuntime();
            for (int i = 0; i < 100; i++) climbing.Step(step);
            double climbForce = climbing.State.CommandLiftForceNewtons;
            Assert.True(climbForce > 100.0);

            VectorFlightRuntime amnesiac = new VectorFlightRuntime(
                climbing.State with { CommandLiftForceNewtons = 0.0, CommandLiftSmoothingVelocity = 0.0 });
            VectorFlightStepResult lossy = amnesiac.Step(step);
            VectorFlightStepResult faithful = climbing.Step(step);

            Assert.True(lossy.Lift.CommandLiftForceNewtons
                < faithful.Lift.CommandLiftForceNewtons - 1.0);
        }
    }
}

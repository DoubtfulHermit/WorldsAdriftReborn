using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class RetailWorldBoundsPolicyTests
    {
        private static FlightState State(double x = 0, double y = 0, double z = 0,
            double vx = 0, double vy = 0, double vz = 0) =>
            new FlightState(x, y, z, 0.3, 0.02, -0.1, 0.05, 7, vx, vy, vz);

        [Fact]
        public void Release_map_extent_and_recovered_thresholds_are_exact()
        {
            var policy = new RetailWorldBoundsPolicy(true);

            Assert.Equal(36_000, RetailWorldBoundsPolicy.ReleaseWorldEdgeLengthMetres);
            Assert.Equal(17_700, policy.HorizontalHardLimitMetres);
            Assert.Equal(17_600, policy.HorizontalPushbackThresholdMetres);
            Assert.Equal(800, RetailWorldBoundsPolicy.VerticalPushbackMetres);
            Assert.Equal(1_000, RetailWorldBoundsPolicy.VerticalHardLimitMetres);
            Assert.Equal(0.02, RetailWorldBoundsPolicy.ReferenceStepSeconds);
        }

        [Fact]
        public void Evaluation_wake_uses_strict_retail_thresholds_on_every_enforced_axis()
        {
            var policy = new RetailWorldBoundsPolicy(true);

            Assert.False(policy.RequiresEvaluation(State(x: 17_600)));
            Assert.False(policy.RequiresEvaluation(State(y: 800)));
            Assert.False(policy.RequiresEvaluation(State(z: -17_600)));
            Assert.True(policy.RequiresEvaluation(State(x: 17_600.001)));
            Assert.True(policy.RequiresEvaluation(State(x: -17_600.001)));
            Assert.True(policy.RequiresEvaluation(State(y: 800.001)));
            Assert.True(policy.RequiresEvaluation(State(z: 17_600.001)));
            Assert.True(policy.RequiresEvaluation(State(z: -17_600.001)));
            Assert.True(policy.RequiresEvaluation(State(x: double.NaN)));
            Assert.False(new RetailWorldBoundsPolicy(false)
                .RequiresEvaluation(State(x: 18_000)));
        }

        [Theory]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NaN)]
        [InlineData(1.3)]
        [InlineData(0.0)]
        [InlineData(-0.02)]
        public void Invalid_or_unbounded_cadence_interval_is_rejected_without_moving(double interval)
        {
            FlightTuning tuning = FlightTuning.FromEnvironment(_ => null);
            FlightState initial = State(10, 20, 30, 1, 2, 3);
            var session = new FlightSession(initial);
            session.Man();

            session.Advance(1_000, interval, tuning,
                worldBounds: new RetailWorldBoundsPolicy(true));

            AssertStateEqual(initial, session.State);
            Assert.True(session.LastWorldBoundsTelemetry.Enabled);
            Assert.Equal(0, session.LastWorldBoundsTelemetry.ReferenceSubsteps);
        }

        [Fact]
        public void Parked_hull_inside_push_band_wakes_and_recovers_inward()
        {
            FlightTuning tuning = FlightTuning.FromEnvironment(_ => null);
            var session = new FlightSession(FlightState.AtRestAt(17_650, 100, 0));

            FlightEmit emit = session.Advance(1_000, 0.24, tuning,
                worldBounds: new RetailWorldBoundsPolicy(true));

            Assert.True(emit.Emit);
            Assert.True(session.State.X < 17_650);
            Assert.True(session.State.VxMps < 0);
            Assert.Equal(12, session.LastWorldBoundsTelemetry.ReferenceSubsteps);
            Assert.True(session.LastWorldBoundsTelemetry.PushbackDeltaVxMps < 0);
        }

        [Fact]
        public void Environment_is_opt_in_and_edge_length_is_configurable_with_safe_fallback()
        {
            RetailWorldBoundsPolicy off = RetailWorldBoundsPolicy.FromEnvironment(_ => null);
            Assert.False(off.Enabled);
            Assert.Equal(36_000, off.EdgeLengthMetres);

            RetailWorldBoundsPolicy configured = RetailWorldBoundsPolicy.FromEnvironment(name => name switch
            {
                "WAREBORN_FLIGHT_WORLD_BOUNDS" => "1",
                "WAREBORN_FLIGHT_WORLD_EDGE_LENGTH" => "48000",
                _ => null,
            });
            Assert.True(configured.Enabled);
            Assert.Equal(48_000, configured.EdgeLengthMetres);
            Assert.Equal(23_700, configured.HorizontalHardLimitMetres);

            RetailWorldBoundsPolicy malformed = RetailWorldBoundsPolicy.FromEnvironment(name =>
                name == "WAREBORN_FLIGHT_WORLD_BOUNDS" ? "1" : "NaN");
            Assert.True(malformed.Enabled);
            Assert.Equal(36_000, malformed.EdgeLengthMetres);
        }

        [Fact]
        public void Disabled_policy_is_bit_for_bit_noop_even_for_non_finite_input()
        {
            var off = new RetailWorldBoundsPolicy(false);
            FlightState candidate = State(x: double.NaN, vy: double.PositiveInfinity);

            RetailWorldBoundsStep result = off.Apply(State(1, 2, 3), candidate);

            Assert.True(double.IsNaN(result.State.X));
            Assert.True(double.IsPositiveInfinity(result.State.VyMps));
            Assert.False(result.Telemetry.Enabled);
        }

        [Fact]
        public void Interior_state_is_unchanged_and_reports_nearest_boundary()
        {
            var policy = new RetailWorldBoundsPolicy(true);
            FlightState candidate = State(100, 700, -300, 1, 2, 3);

            RetailWorldBoundsStep result = policy.Apply(State(), candidate);

            AssertStateEqual(candidate, result.State);
            Assert.Equal(300, result.Telemetry.BoundaryDistanceMetres);
            Assert.Equal(0, result.Telemetry.PushbackDeltaVxMps);
            Assert.False(result.Telemetry.HardClamped);
            Assert.False(result.Telemetry.InvalidState);
        }

        [Fact]
        public void Vertical_pushback_uses_recovered_quadratic_and_damping_math()
        {
            var policy = new RetailWorldBoundsPolicy(true);
            // Halfway through the band: damping factor 2/3, then -50 * .5^2.
            RetailWorldBoundsStep result = policy.Apply(State(), State(y: 900, vy: 30));

            Assert.Equal(900, result.State.Y);
            Assert.Equal(7.5, result.State.VyMps, 10);
            Assert.Equal(-22.5, result.Telemetry.PushbackDeltaVyMps, 10);
            Assert.Equal(100, result.Telemetry.BoundaryDistanceMetres);
            Assert.False(result.Telemetry.HardClamped);
        }

        [Fact]
        public void Damping_starts_after_one_quarter_of_push_band_not_at_it()
        {
            var policy = new RetailWorldBoundsPolicy(true);
            RetailWorldBoundsStep result = policy.Apply(State(), State(y: 850, vy: 10));

            // t=.25: no damping, only -50 * .25^2.
            Assert.Equal(6.875, result.State.VyMps, 10);
        }

        [Fact]
        public void Vertical_hard_limit_clamps_and_fully_negates_outward_velocity()
        {
            var policy = new RetailWorldBoundsPolicy(true);
            RetailWorldBoundsStep result = policy.Apply(State(), State(y: 1_500, vy: 90));

            Assert.Equal(1_000, result.State.Y);
            Assert.Equal(-50, result.State.VyMps);
            Assert.True(result.Telemetry.HardClamped);
            Assert.Equal(0, result.Telemetry.BoundaryDistanceMetres);
        }

        [Theory]
        [InlineData(17650, 0, 20, 0, -19.166666666666668)]
        [InlineData(-17650, 0, -20, 0, 19.166666666666668)]
        [InlineData(0, 17650, 0, 20, -19.166666666666668)]
        [InlineData(0, -17650, 0, -20, 19.166666666666668)]
        public void All_four_horizontal_edges_are_symmetric(
            double x, double z, double vx, double vz, double expectedDelta)
        {
            var policy = new RetailWorldBoundsPolicy(true);
            RetailWorldBoundsStep result = policy.Apply(State(), State(x: x, z: z, vx: vx, vz: vz));

            double actual = x == 0
                ? result.Telemetry.PushbackDeltaVzMps
                : result.Telemetry.PushbackDeltaVxMps;
            // t=.5 damps 20 -> 13.333..., then applies 12.5 inward.
            Assert.Equal(expectedDelta, actual, 9);
        }

        [Theory]
        [InlineData(18000, 0, 17700, 0, -50)]
        [InlineData(-18000, 0, -17700, 0, 50)]
        [InlineData(0, 18000, 0, 17700, -50)]
        [InlineData(0, -18000, 0, -17700, 50)]
        public void All_four_horizontal_hard_limits_clamp(
            double x, double z, double expectedX, double expectedZ, double expectedVelocity)
        {
            var policy = new RetailWorldBoundsPolicy(true);
            RetailWorldBoundsStep result = policy.Apply(State(), State(x: x, z: z, vx: x, vz: z));

            Assert.Equal(expectedX, result.State.X);
            Assert.Equal(expectedZ, result.State.Z);
            Assert.Equal(x == 0 ? expectedVelocity : expectedVelocity,
                x == 0 ? result.State.VzMps : result.State.VxMps);
            Assert.True(result.Telemetry.HardClamped);
        }

        [Theory]
        [InlineData("position")]
        [InlineData("velocity")]
        [InlineData("rotation")]
        [InlineData("command")]
        public void Non_finite_candidate_is_quarantined_to_last_finite_pose(string corrupt)
        {
            var policy = new RetailWorldBoundsPolicy(true);
            FlightState bad = corrupt switch
            {
                "position" => new FlightState(double.NaN, 2, 3, 0.5, 0, 0, 0, 0, 1, 2, 3),
                "velocity" => new FlightState(1, 2, 3, 0.5, 0, 0, 0, 0, 1, double.PositiveInfinity, 3),
                "rotation" => new FlightState(1, 2, 3, double.NaN, 0, 0, 0, 0, 1, 2, 3),
                _ => new FlightState(1, 2, 3, 0.5, 0, 0, 0, double.NegativeInfinity, 1, 2, 3),
            };
            FlightState anchor = State(10, 20, 30);

            RetailWorldBoundsStep result = policy.Apply(anchor, bad);

            Assert.Equal(10, result.State.X);
            Assert.Equal(20, result.State.Y);
            Assert.Equal(30, result.State.Z);
            Assert.Equal(anchor.YawRadians, result.State.YawRadians);
            Assert.True(result.State.IsAtRest);
            Assert.True(result.Telemetry.InvalidState);
            Assert.True(RetailWorldBoundsPolicy.IsFinite(result.State));
        }

        [Fact]
        public void Session_quarantine_ends_the_corrupt_cadence_instead_of_resuming_mid_interval()
        {
            FlightTuning tuning = FlightTuning.FromEnvironment(_ => null);
            var session = new FlightSession(new FlightState(
                double.NaN, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0));
            session.Man();
            session.SetInput(new FlightControlInput(1, 1, 1, 1, 1));

            session.Advance(1_000, 0.24, tuning,
                worldBounds: new RetailWorldBoundsPolicy(true));

            Assert.True(session.State.IsAtRest);
            Assert.Equal(0, session.State.X);
            Assert.Equal(0, session.State.Y);
            Assert.Equal(0, session.State.Z);
            Assert.True(session.LastWorldBoundsTelemetry.InvalidState);
            Assert.Equal(1, session.LastWorldBoundsTelemetry.ReferenceSubsteps);
        }

        [Fact]
        public void Cadence_step_is_exactly_twelve_reference_substeps_and_stable()
        {
            FlightTuning tuning = FlightTuning.FromEnvironment(_ => null);
            var policy = new RetailWorldBoundsPolicy(true);
            var session = new FlightSession(State(y: 799, vy: 20));
            session.Man();
            session.SetInput(new FlightControlInput(0, 1, 0, 0, 0));

            session.Advance(10_000, 0.24, tuning, worldBounds: policy);

            Assert.Equal(12, session.LastWorldBoundsTelemetry.ReferenceSubsteps);
            Assert.True(session.State.Y <= RetailWorldBoundsPolicy.VerticalHardLimitMetres);

            // The cadence wrapper is exactly the documented reference evaluation,
            // not merely repeatable by accident.
            FlightState manual = State(y: 799, vy: 20);
            FlightControlInput input = new FlightControlInput(0, 1, 0, 0, 0);
            for (int i = 0; i < 12; i++)
            {
                FlightState candidate = FlightIntegrator.Step(
                    manual, input, RetailWorldBoundsPolicy.ReferenceStepSeconds, tuning);
                manual = policy.Apply(manual, candidate).State;
            }
            AssertStateEqual(manual, session.State);

            var again = new FlightSession(State(y: 799, vy: 20));
            again.Man();
            again.SetInput(input);
            again.Advance(10_000, 0.24, tuning, worldBounds: policy);
            AssertStateEqual(session.State, again.State);
        }

        [Fact]
        public void Disabled_session_path_retains_legacy_single_step_parity()
        {
            FlightTuning tuning = FlightTuning.FromEnvironment(_ => null);
            FlightState initial = State(4, 5, 6, 1, 2, 3);
            FlightControlInput input = new FlightControlInput(0.7f, 0.2f, -0.1f, 0.3f, 0.4f);
            FlightState expected = FlightIntegrator.Step(initial, input, 0.24, tuning);
            var session = new FlightSession(initial);
            session.Man();
            session.SetInput(input);

            session.Advance(1_000, 0.24, tuning,
                worldBounds: new RetailWorldBoundsPolicy(false));

            AssertStateEqual(expected, session.State);
            Assert.False(session.LastWorldBoundsTelemetry.Enabled);
        }

        private static void AssertStateEqual(FlightState expected, FlightState actual)
        {
            Assert.Equal(expected.X, actual.X);
            Assert.Equal(expected.Y, actual.Y);
            Assert.Equal(expected.Z, actual.Z);
            Assert.Equal(expected.YawRadians, actual.YawRadians);
            Assert.Equal(expected.YawRateRadPerSec, actual.YawRateRadPerSec);
            Assert.Equal(expected.RollRadians, actual.RollRadians);
            Assert.Equal(expected.PitchRadians, actual.PitchRadians);
            Assert.Equal(expected.SpeedCmdMps, actual.SpeedCmdMps);
            Assert.Equal(expected.VxMps, actual.VxMps);
            Assert.Equal(expected.VyMps, actual.VyMps);
            Assert.Equal(expected.VzMps, actual.VzMps);
        }
    }
}

using System;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// The flight math, pinned. Every rule here fails SILENTLY on a live client
    /// (a wrong sign is a ship that turns the wrong way, a residual speed is a
    /// publisher that never sleeps, a NaN is a control point the client rejects
    /// without a word), so the tests are the only place they are visible.
    /// </summary>
    public class FlightIntegratorTests
    {
        private const double Step = ShipMotionPolicy.SendIntervalSeconds;
        private static readonly FlightTuning Tuning = new FlightTuning();

        private static FlightControlInput Input(
            float throttle = 0f, float vertical = 0f, float pitch = 0f, float yaw = 0f, float roll = 0f)
        {
            return new FlightControlInput(throttle, vertical, pitch, yaw, roll);
        }

        // ------------------------------------------------------------------
        // Throttle -> speed
        // ------------------------------------------------------------------

        [Fact]
        public void Full_throttle_ramps_at_the_accel_limit_and_caps_at_max_speed()
        {
            FlightState state = FlightState.AtRestAt(0, 100, 0);
            FlightControlInput full = Input(throttle: 1f);

            state = FlightIntegrator.Step(state, full, Step, Tuning);
            Assert.Equal(Tuning.AccelMps2 * Step, state.SpeedMps, 9);

            for (int i = 0; i < 200; i++)
            {
                state = FlightIntegrator.Step(state, full, Step, Tuning);
                Assert.True(state.SpeedMps <= Tuning.MaxSpeedMps + 1e-9);
            }
            Assert.Equal(Tuning.MaxSpeedMps, state.SpeedMps, 9);
        }

        [Fact]
        public void Reverse_is_slower_than_forward_by_the_reverse_factor()
        {
            FlightState state = FlightState.AtRestAt(0, 100, 0);
            FlightControlInput reverse = Input(throttle: -1f);

            for (int i = 0; i < 200; i++)
            {
                state = FlightIntegrator.Step(state, reverse, Step, Tuning);
            }

            Assert.Equal(-Tuning.MaxSpeedMps * Tuning.ReverseFactor, state.SpeedMps, 9);
        }

        [Fact]
        public void Released_throttle_decays_to_EXACTLY_zero_not_an_epsilon()
        {
            // The snap rule. A 1e-9 residual keeps IsAtRest false forever, which
            // keeps the publisher emitting forever.
            FlightState state = new FlightState(0, 100, 0, 0, Tuning.MaxSpeedMps, 0);

            for (int i = 0; i < 200; i++)
            {
                state = FlightIntegrator.Step(state, FlightControlInput.Neutral, Step, Tuning);
            }

            Assert.Equal(0.0, state.SpeedMps);
            Assert.True(state.IsAtRest);
        }

        // ------------------------------------------------------------------
        // Heading
        // ------------------------------------------------------------------

        [Fact]
        public void Yaw_zero_flies_due_north_plus_z()
        {
            // The hull spawns facing the identity rotation; yaw 0 must move it
            // along +Z (the same axis the ferry's "north hop" uses), or the ship
            // visibly flies sideways relative to its bow.
            FlightState state = new FlightState(0, 100, 0, 0, 10.0, 0);
            state = FlightIntegrator.Step(state, Input(throttle: 1f), Step, Tuning);

            Assert.Equal(0.0, state.X, 9);
            Assert.True(state.Z > 0);
        }

        [Fact]
        public void Positive_yaw_input_turns_toward_plus_x()
        {
            // Unity's left-handed +Y rotation: positive yaw swings the nose from
            // +Z toward +X. If live play shows the opposite, that is what
            // WAREBORN_FLIGHT_INVERT_YAW exists for - not a code edit.
            FlightState state = new FlightState(0, 100, 0, 0, 10.0, 0);
            FlightControlInput right = Input(throttle: 1f, yaw: 1f);

            for (int i = 0; i < 10; i++)
            {
                state = FlightIntegrator.Step(state, right, Step, Tuning);
            }

            Assert.True(state.YawRadians > 0);
            Assert.True(state.X > 0);
        }

        [Fact]
        public void Invert_yaw_flips_the_turn_direction()
        {
            FlightTuning inverted = new FlightTuning(invertYaw: true);
            FlightState state = new FlightState(0, 100, 0, 0, 10.0, 0);
            state = FlightIntegrator.Step(state, Input(yaw: 1f), Step, inverted);

            Assert.True(state.YawRadians < 0);
        }

        [Fact]
        public void The_heading_wraps_instead_of_walking_off()
        {
            FlightState state = FlightState.AtRestAt(0, 100, 0);
            FlightControlInput spin = Input(yaw: 1f);

            // An hour of full-stick spinning at 0.24 s steps.
            for (int i = 0; i < 15000; i++)
            {
                state = FlightIntegrator.Step(state, spin, Step, Tuning);
                Assert.InRange(state.YawRadians, -Math.PI, Math.PI + 1e-9);
            }
        }

        // ------------------------------------------------------------------
        // Vertical
        // ------------------------------------------------------------------

        [Fact]
        public void Vertical_input_climbs_and_centred_stick_stops_the_climb_exactly()
        {
            FlightState state = FlightState.AtRestAt(0, 100, 0);
            state = FlightIntegrator.Step(state, Input(vertical: 1f), Step, Tuning);

            Assert.Equal(Tuning.ClimbRateMps, state.VerticalMps, 9);
            Assert.Equal(100 + Tuning.ClimbRateMps * Step, state.Y, 9);

            state = FlightIntegrator.Step(state, FlightControlInput.Neutral, Step, Tuning);
            Assert.Equal(0.0, state.VerticalMps);
        }

        // ------------------------------------------------------------------
        // The wire numbers
        // ------------------------------------------------------------------

        [Fact]
        public void The_control_point_velocity_is_the_exact_path_derivative()
        {
            // PathFollower extrapolates along the reported velocity between
            // points; a velocity that disagrees with the position steps reads as
            // rubber-banding.
            FlightState state = new FlightState(10, 100, 20, Math.PI / 4, 8.0, 2.0);
            ShipControlPointSpec spec = FlightIntegrator.ToControlPoint(state, 1000);

            Assert.Equal(Math.Sin(Math.PI / 4) * 8.0, spec.Vx, 9);
            Assert.Equal(2.0, spec.Vy, 9);
            Assert.Equal(Math.Cos(Math.PI / 4) * 8.0, spec.Vz, 9);
            Assert.Equal(10, spec.X, 9);
            Assert.False(spec.Arrived);
        }

        [Fact]
        public void An_at_rest_state_makes_an_arrived_zero_velocity_point()
        {
            ShipControlPointSpec spec = FlightIntegrator.ToControlPoint(
                FlightState.AtRestAt(1, 2, 3, 0.5), 99);

            Assert.True(spec.Arrived);
            Assert.Equal(0.0, spec.Vx);
            Assert.Equal(0.0, spec.Vy);
            Assert.Equal(0.0, spec.Vz);
        }

        [Fact]
        public void Yaw_zero_packs_to_the_identity_sentinel()
        {
            // The encoder special-cases |w| == 1 to 1023 - so an unflown hull's
            // points stay byte-identical to the at-rest seed every client
            // already accepts.
            Assert.Equal(Quaternion32Packing.Identity,
                FlightIntegrator.PackedRotation(FlightState.AtRestAt(0, 0, 0, 0)));
        }

        [Fact]
        public void A_quarter_turn_packs_to_a_decodable_y_rotation()
        {
            uint packed = FlightIntegrator.PackedRotation(
                FlightState.AtRestAt(0, 0, 0, Math.PI / 2));
            (float w, float x, float y, float z) = Quaternion32Packing.Decode(packed);

            Assert.Equal(Math.Cos(Math.PI / 4), w, 2);
            Assert.Equal(0f, x, 2);
            Assert.Equal(Math.Sin(Math.PI / 4), y, 2);
            Assert.Equal(0f, z, 2);
        }

        // ------------------------------------------------------------------
        // Hostile input
        // ------------------------------------------------------------------

        [Fact]
        public void NaN_and_out_of_range_input_is_sanitized_at_the_edge()
        {
            FlightControlInput hostile = new FlightControlInput(
                float.NaN, float.PositiveInfinity, -5f, 7f, float.NegativeInfinity);

            Assert.Equal(0f, hostile.Throttle);
            Assert.Equal(0f, hostile.Vertical);
            Assert.Equal(-1f, hostile.AxisPitch);
            Assert.Equal(1f, hostile.AxisYaw);
            Assert.Equal(0f, hostile.AxisRoll);
        }

        [Fact]
        public void A_bad_dt_is_a_no_op_not_a_corruption()
        {
            FlightState state = new FlightState(1, 2, 3, 0.4, 5, 6);
            Assert.Equal(state.X, FlightIntegrator.Step(state, Input(throttle: 1f), 0.0, Tuning).X);
            Assert.Equal(state.X, FlightIntegrator.Step(state, Input(throttle: 1f), double.NaN, Tuning).X);
        }

        [Fact]
        public void Delta_merge_keeps_unsent_fields()
        {
            // The 1111 update is a DIFF: a packet that only says "throttle now 1"
            // must not zero the held yaw.
            FlightControlInput held = Input(throttle: 0.5f, yaw: 0.7f);
            FlightControlInput merged = held.Merge(1f, null, null, null, null);

            Assert.Equal(1f, merged.Throttle);
            Assert.Equal(0.7f, merged.AxisYaw, 5);
        }
    }

    public class FlightTuningTests
    {
        [Fact]
        public void Defaults_apply_when_the_environment_is_empty()
        {
            FlightTuning tuning = FlightTuning.FromEnvironment(_ => null);

            Assert.Equal(FlightTuning.DefaultMaxSpeedMps, tuning.MaxSpeedMps);
            Assert.Equal(FlightTuning.DefaultAccelMps2, tuning.AccelMps2);
            Assert.Equal(FlightTuning.DefaultClimbRateMps, tuning.ClimbRateMps);
            Assert.Equal(FlightTuning.DefaultReverseFactor, tuning.ReverseFactor);
            Assert.Equal(FlightTuning.DefaultRestKeepaliveSeconds, tuning.RestKeepaliveSeconds);
            Assert.False(tuning.InvertYaw);
        }

        [Fact]
        public void Every_knob_reads_its_env_var()
        {
            var env = new System.Collections.Generic.Dictionary<string, string>
            {
                ["WAREBORN_FLIGHT_MAX_SPEED"] = "20",
                ["WAREBORN_FLIGHT_ACCEL"] = "8",
                ["WAREBORN_FLIGHT_YAW_RATE"] = "45",
                ["WAREBORN_FLIGHT_CLIMB_RATE"] = "10",
                ["WAREBORN_FLIGHT_REVERSE_FACTOR"] = "0.5",
                ["WAREBORN_FLIGHT_REST_KEEPALIVE"] = "10",
                ["WAREBORN_FLIGHT_INVERT_YAW"] = "1",
            };
            FlightTuning tuning = FlightTuning.FromEnvironment(k => env.TryGetValue(k, out string? v) ? v : null);

            Assert.Equal(20.0, tuning.MaxSpeedMps);
            Assert.Equal(8.0, tuning.AccelMps2);
            Assert.Equal(45.0 * Math.PI / 180.0, tuning.YawRateRadPerSec, 9);
            Assert.Equal(10.0, tuning.ClimbRateMps);
            Assert.Equal(0.5, tuning.ReverseFactor);
            Assert.Equal(10.0, tuning.RestKeepaliveSeconds);
            Assert.True(tuning.InvertYaw);
        }

        [Fact]
        public void The_speed_ceiling_is_the_shared_control_point_safety_cap()
        {
            // Above ShipMotionPolicy.MaxSpeedMetresPerSecond the 0.24 s spacing
            // starts to read as teleporting; the flight knob must not be able to
            // exceed what the ferry knob already refuses.
            FlightTuning tuning = FlightTuning.FromEnvironment(
                k => k == "WAREBORN_FLIGHT_MAX_SPEED" ? "5000" : null);

            Assert.Equal(ShipMotionPolicy.MaxSpeedMetresPerSecond, tuning.MaxSpeedMps);
        }

        [Fact]
        public void Garbage_env_values_fall_back_and_never_throw()
        {
            FlightTuning tuning = FlightTuning.FromEnvironment(_ => "banana");
            Assert.Equal(FlightTuning.DefaultMaxSpeedMps, tuning.MaxSpeedMps);

            FlightTuning nan = FlightTuning.FromEnvironment(_ => "NaN");
            Assert.Equal(FlightTuning.DefaultAccelMps2, nan.AccelMps2);
        }
    }
}

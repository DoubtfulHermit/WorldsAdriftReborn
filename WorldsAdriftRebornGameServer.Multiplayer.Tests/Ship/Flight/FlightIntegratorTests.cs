using System;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// The v2 flight math, pinned. Every rule fails SILENTLY on a live client (a
    /// wrong sign banks OUT of turns, a residual rate keeps the publisher awake
    /// forever, a bad quaternion composition reads as a ship flying sideways),
    /// so the tests are where they are visible.
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

        private static FlightState Run(FlightState state, FlightControlInput input, int steps,
            FlightTuning? tuning = null)
        {
            FlightTuning t = tuning ?? Tuning;
            for (int i = 0; i < steps; i++)
            {
                state = FlightIntegrator.Step(state, input, Step, t);
            }
            return state;
        }

        // ------------------------------------------------------------------
        // Throttle -> commanded speed -> velocity
        // ------------------------------------------------------------------

        [Fact]
        public void Full_throttle_ramps_the_command_at_the_accel_limit_and_caps_at_max()
        {
            FlightState state = FlightState.AtRestAt(0, 100, 0);
            state = FlightIntegrator.Step(state, Input(throttle: 1f), Step, Tuning);
            Assert.Equal(Tuning.AccelMps2 * Step, state.SpeedCmdMps, 9);

            state = Run(state, Input(throttle: 1f), 200);
            Assert.Equal(Tuning.MaxSpeedMps, state.SpeedCmdMps, 9);
            Assert.Equal(Tuning.MaxSpeedMps, state.GroundSpeedMps, 6);
        }

        [Fact]
        public void The_velocity_lags_the_command_by_the_smoothing_constant()
        {
            // The inertia feel: one step in, the actual velocity is only the
            // dt/tau fraction of the command - the ship LEANS into motion
            // rather than snapping.
            FlightState state = FlightState.AtRestAt(0, 100, 0);
            state = FlightIntegrator.Step(state, Input(throttle: 1f), Step, Tuning);

            double expectedBlend = Step / Tuning.VelocitySmoothingSeconds;
            Assert.Equal(state.SpeedCmdMps * expectedBlend, state.VzMps, 9);
            Assert.True(state.VzMps < state.SpeedCmdMps);
        }

        [Fact]
        public void Zero_smoothing_restores_the_v1_instant_velocity()
        {
            FlightTuning instant = new FlightTuning(velocitySmoothingSeconds: 0.0);
            FlightState state = FlightState.AtRestAt(0, 100, 0);
            state = FlightIntegrator.Step(state, Input(throttle: 1f), Step, instant);

            Assert.Equal(state.SpeedCmdMps, state.VzMps, 9);
        }

        [Fact]
        public void Reverse_is_slower_than_forward_by_the_reverse_factor()
        {
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(throttle: -1f), 200);
            Assert.Equal(-Tuning.MaxSpeedMps * Tuning.ReverseFactor, state.SpeedCmdMps, 9);
        }

        [Fact]
        public void Released_throttle_settles_to_EXACT_rest_not_an_epsilon()
        {
            // The snap rule, now across command AND velocity AND attitude: any
            // 1e-9 residual keeps IsAtRest false and the publisher awake forever.
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(throttle: 1f, yaw: 1f, vertical: 1f), 50);
            state = Run(state, FlightControlInput.Neutral, 200);

            Assert.True(state.IsAtRest,
                "not at rest: " + state + " yawRate=" + state.YawRateRadPerSec);
            Assert.Equal(0.0, state.VxMps);
            Assert.Equal(0.0, state.VzMps);
            Assert.Equal(0.0, state.RollRadians);
            Assert.Equal(0.0, state.PitchRadians);
        }

        // ------------------------------------------------------------------
        // Heading: ease-in / ease-out
        // ------------------------------------------------------------------

        [Fact]
        public void The_turn_rate_ramps_instead_of_stepping()
        {
            FlightState state = FlightState.AtRestAt(0, 100, 0);
            state = FlightIntegrator.Step(state, Input(yaw: 1f), Step, Tuning);

            Assert.Equal(Tuning.YawAccelRadPerSec2 * Step, state.YawRateRadPerSec, 9);
            Assert.True(state.YawRateRadPerSec < Tuning.YawRateRadPerSec,
                "one step must not reach the full turn rate");

            state = Run(state, Input(yaw: 1f), 30);
            Assert.Equal(Tuning.YawRateRadPerSec, state.YawRateRadPerSec, 9);
        }

        [Fact]
        public void A_released_stick_unwinds_the_turn_to_exactly_zero_rate()
        {
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(yaw: 1f), 30);
            state = Run(state, FlightControlInput.Neutral, 30);

            Assert.Equal(0.0, state.YawRateRadPerSec);
            double heading = state.YawRadians;
            state = Run(state, FlightControlInput.Neutral, 10);
            Assert.Equal(heading, state.YawRadians, 12); // the heading HOLDS
        }

        [Fact]
        public void Positive_yaw_input_turns_toward_plus_x()
        {
            FlightState state = Run(
                new FlightState(0, 100, 0, 0, 0, 0, 0, 10, 0, 0, 10), Input(throttle: 1f, yaw: 1f), 12);

            Assert.True(state.YawRadians > 0);
            Assert.True(state.X > 0);
        }

        [Fact]
        public void Invert_yaw_flips_the_turn_direction()
        {
            FlightTuning inverted = new FlightTuning(invertYaw: true);
            FlightState state = FlightIntegrator.Step(
                FlightState.AtRestAt(0, 100, 0), Input(yaw: 1f), Step, inverted);

            Assert.True(state.YawRateRadPerSec < 0);
        }

        [Fact]
        public void The_heading_wraps_instead_of_walking_off()
        {
            FlightState state = FlightState.AtRestAt(0, 100, 0);
            FlightControlInput spin = Input(yaw: 1f);
            for (int i = 0; i < 15000; i++)
            {
                state = FlightIntegrator.Step(state, spin, Step, Tuning);
                Assert.InRange(state.YawRadians, -Math.PI, Math.PI + 1e-9);
            }
        }

        [Fact]
        public void A_turn_carves_the_velocity_lags_the_heading()
        {
            // Cruise north, then hold full right lock: the velocity direction
            // must trail BEHIND the nose (momentum drifting through the turn),
            // never snap to it - that lag is the carve.
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(throttle: 1f), 60);
            state = Run(state, Input(throttle: 1f, yaw: 1f), 8);

            double velocityHeading = Math.Atan2(state.VxMps, state.VzMps);
            Assert.True(state.YawRadians > 0.05, "the nose must have turned");
            Assert.True(velocityHeading < state.YawRadians - 1e-6,
                "velocity heading " + velocityHeading + " must trail the nose " + state.YawRadians);
        }

        // ------------------------------------------------------------------
        // Attitude: banking + pitch
        // ------------------------------------------------------------------

        [Fact]
        public void A_right_turn_banks_right_and_levels_out_after()
        {
            // Negative roll = right side dips (rotation about +Z lifts +X, so
            // banking INTO a right turn needs the negative angle).
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(yaw: 1f), 40);

            Assert.True(state.RollRadians < 0, "right turn must bank right (negative roll)");
            Assert.Equal(-Tuning.BankMaxRadians, state.RollRadians, 3);

            state = Run(state, FlightControlInput.Neutral, 60);
            Assert.Equal(0.0, state.RollRadians);
        }

        [Fact]
        public void A_left_turn_banks_left()
        {
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(yaw: -1f), 40);
            Assert.True(state.RollRadians > 0);
        }

        [Fact]
        public void The_bank_never_exceeds_the_configured_maximum()
        {
            FlightState state = FlightState.AtRestAt(0, 100, 0);
            for (int i = 0; i < 100; i++)
            {
                state = FlightIntegrator.Step(state, Input(yaw: 1f), Step, Tuning);
                Assert.True(Math.Abs(state.RollRadians) <= Tuning.BankMaxRadians + 1e-9);
            }
        }

        [Fact]
        public void Bank_angle_zero_disables_banking_entirely()
        {
            FlightTuning flat = new FlightTuning(bankAngleDeg: 0.0);
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(yaw: 1f), 40, flat);
            Assert.Equal(0.0, state.RollRadians);
        }

        [Fact]
        public void Climbing_noses_up_and_descending_noses_down()
        {
            // Negative pitch = nose up (positive X-rotation noses down).
            FlightState climb = Run(FlightState.AtRestAt(0, 100, 0), Input(vertical: 1f), 40);
            Assert.True(climb.PitchRadians < 0, "climb must nose up (negative pitch)");
            Assert.Equal(-Tuning.PitchMaxRadians, climb.PitchRadians, 3);

            FlightState dive = Run(FlightState.AtRestAt(0, 100, 0), Input(vertical: -1f), 40);
            Assert.True(dive.PitchRadians > 0, "descent must nose down (positive pitch)");

            FlightState level = Run(climb, FlightControlInput.Neutral, 60);
            Assert.Equal(0.0, level.PitchRadians);
        }

        [Fact]
        public void Attitude_is_cosmetic_it_never_steers_the_path()
        {
            // Banked or not, the position must advance along the YAW heading
            // only - roll/pitch are attitude, not aerodynamics. Same inputs,
            // banking on vs off, identical path.
            FlightTuning flat = new FlightTuning(bankAngleDeg: 0.0, pitchAngleDeg: 0.0);
            FlightControlInput input = Input(throttle: 1f, yaw: 0.5f, vertical: 0.5f);

            FlightState banked = Run(FlightState.AtRestAt(0, 100, 0), input, 50);
            FlightState unbanked = Run(FlightState.AtRestAt(0, 100, 0), input, 50, flat);

            Assert.Equal(unbanked.X, banked.X, 9);
            Assert.Equal(unbanked.Y, banked.Y, 9);
            Assert.Equal(unbanked.Z, banked.Z, 9);
        }

        // ------------------------------------------------------------------
        // v3: MOUSE STEERING - ShipAxes.x (pitch) and .z (roll) fly the ship.
        // Signs mirror the retail FSIM torque map (ShipControlVisualizer
        // .UpdateTorques: right*x + up*y + forward*(-z)): +pitch = nose down,
        // +roll = bank right.
        // ------------------------------------------------------------------

        [Fact]
        public void Mouse_roll_turns_the_ship_the_banked_turn()
        {
            // Mouse right (+roll) must turn the ship RIGHT (+yaw, toward +X) -
            // this is the whole "the mouse moves the helm but not the ship" fix.
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(throttle: 1f, roll: 1f), 40);

            Assert.True(state.YawRateRadPerSec > 0, "mouse roll right must produce a right turn");
            Assert.True(state.YawRadians > 0);
            Assert.True(state.X > 0);
            Assert.Equal(Tuning.RollTurnFactor * Tuning.YawRateRadPerSec, state.YawRateRadPerSec, 9);
        }

        [Fact]
        public void A_mouse_rolled_ship_visibly_banks_into_its_turn()
        {
            // The bank attitude follows the TOTAL turn rate, so a mouse turn
            // shows the same right-side-dips roll a key turn does.
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(roll: 1f), 40);
            Assert.True(state.RollRadians < 0, "a right banked turn must dip the right side");
        }

        [Fact]
        public void Keys_plus_mouse_together_never_exceed_the_turn_rate_cap()
        {
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(yaw: 1f, roll: 1f), 60);
            Assert.Equal(Tuning.YawRateRadPerSec, state.YawRateRadPerSec, 9);
        }

        [Fact]
        public void Opposite_roll_counters_a_key_turn()
        {
            // A/D right + mouse hard left at the default 0.7 factor = a slower
            // right turn (1 - 0.7), not a fight that oscillates.
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(yaw: 1f, roll: -1f), 60);
            Assert.Equal((1.0 - Tuning.RollTurnFactor) * Tuning.YawRateRadPerSec, state.YawRateRadPerSec, 9);
        }

        [Fact]
        public void Mouse_pitch_dives_the_ship_with_the_retail_sign()
        {
            // +pitch input = +X torque = nose DOWN in retail; our vy must go
            // negative and the nose attitude must dip (positive pitch angle).
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(pitch: 1f), 40);

            Assert.True(state.VyMps < 0, "positive pitch input must dive");
            Assert.Equal(-Tuning.PitchRateMps, state.VyMps, 6);
            Assert.True(state.PitchRadians > 0, "a dive must nose down (positive pitch attitude)");
            Assert.True(state.Y < 100.0);
        }

        [Fact]
        public void Mouse_pitch_up_climbs()
        {
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(pitch: -1f), 40);
            Assert.Equal(Tuning.PitchRateMps, state.VyMps, 6);
            Assert.True(state.PitchRadians < 0, "a climb must nose up");
        }

        [Fact]
        public void Mouse_pitch_BLENDS_with_the_vertical_axis_it_does_not_replace_it()
        {
            // LShift climb + mouse dive = the sum, so neither input is broken:
            // full Vertical (climbRate) minus full pitch (pitchRate).
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(vertical: 1f, pitch: 1f), 60);
            Assert.Equal(Tuning.ClimbRateMps - Tuning.PitchRateMps, state.VyMps, 6);

            // And the v1 keys-only behaviour is untouched with the mouse centred.
            FlightState keysOnly = Run(FlightState.AtRestAt(0, 100, 0), Input(vertical: 1f), 40);
            Assert.Equal(Tuning.ClimbRateMps, keysOnly.VyMps, 9);
        }

        [Fact]
        public void Invert_knobs_flip_each_mouse_axis_independently()
        {
            FlightTuning invPitch = new FlightTuning(invertPitch: true);
            FlightState climb = Run(FlightState.AtRestAt(0, 100, 0), Input(pitch: 1f), 20, invPitch);
            Assert.True(climb.VyMps > 0, "inverted pitch must climb on +input");

            FlightTuning invRoll = new FlightTuning(invertRoll: true);
            FlightState left = Run(FlightState.AtRestAt(0, 100, 0), Input(roll: 1f), 20, invRoll);
            Assert.True(left.YawRateRadPerSec < 0, "inverted roll must turn left on +input");
        }

        [Fact]
        public void Zeroed_knobs_disable_each_mouse_axis()
        {
            FlightTuning dead = new FlightTuning(pitchRateMps: 0.0, rollTurnFactor: 0.0);
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(pitch: 1f, roll: 1f), 40, dead);

            Assert.Equal(0.0, state.VyMps);
            Assert.Equal(0.0, state.YawRateRadPerSec);
            Assert.True(state.IsAtRest);
        }

        [Fact]
        public void Mouse_axes_settle_to_exact_rest_when_released()
        {
            // The centre-recentre: mouse input released mid-manoeuvre must
            // still reach the exact at-rest state the publisher sleeps on.
            FlightState state = Run(FlightState.AtRestAt(0, 100, 0), Input(throttle: 1f, pitch: 0.8f, roll: 0.6f), 50);
            state = Run(state, FlightControlInput.Neutral, 200);
            Assert.True(state.IsAtRest, "not at rest: " + state);
        }

        // ------------------------------------------------------------------
        // Vertical
        // ------------------------------------------------------------------

        [Fact]
        public void Vertical_input_climbs_with_inertia_and_settles_exactly()
        {
            FlightState state = FlightIntegrator.Step(
                FlightState.AtRestAt(0, 100, 0), Input(vertical: 1f), Step, Tuning);

            double blend = Step / Tuning.VelocitySmoothingSeconds;
            Assert.Equal(Tuning.ClimbRateMps * blend, state.VyMps, 9);

            state = Run(state, Input(vertical: 1f), 40);
            Assert.Equal(Tuning.ClimbRateMps, state.VyMps, 9);

            state = Run(state, FlightControlInput.Neutral, 60);
            Assert.Equal(0.0, state.VyMps);
        }

        // ------------------------------------------------------------------
        // The wire numbers
        // ------------------------------------------------------------------

        [Fact]
        public void The_control_point_velocity_is_the_exact_path_derivative()
        {
            // Position advances by the SMOOTHED vector, so the reported velocity
            // must equal (nextPos - pos) / dt exactly - that is what makes the
            // client's hermite tangents match the path.
            FlightState state = Run(FlightState.AtRestAt(5, 100, 7), Input(throttle: 1f, yaw: 0.6f), 9);
            FlightState next = FlightIntegrator.Step(state, Input(throttle: 1f, yaw: 0.6f), Step, Tuning);

            ShipControlPointSpec spec = FlightIntegrator.ToControlPoint(next, 1000);
            Assert.Equal((next.X - state.X) / Step, spec.Vx, 9);
            Assert.Equal((next.Y - state.Y) / Step, spec.Vy, 9);
            Assert.Equal((next.Z - state.Z) / Step, spec.Vz, 9);
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
        public void A_level_north_facing_state_packs_to_the_identity_sentinel()
        {
            Assert.Equal(Quaternion32Packing.Identity,
                FlightIntegrator.PackedRotation(FlightState.AtRestAt(0, 0, 0, 0)));
        }

        [Fact]
        public void A_level_quarter_turn_packs_to_a_pure_y_rotation()
        {
            uint packed = FlightIntegrator.PackedRotation(FlightState.AtRestAt(0, 0, 0, Math.PI / 2));
            (float w, float x, float y, float z) = Quaternion32Packing.Decode(packed);

            Assert.Equal(Math.Cos(Math.PI / 4), w, 2);
            Assert.Equal(0f, x, 2);
            Assert.Equal(Math.Sin(Math.PI / 4), y, 2);
            Assert.Equal(0f, z, 2);
        }

        [Fact]
        public void A_pure_roll_composes_to_a_z_axis_quaternion()
        {
            FlightState banked = new FlightState(0, 0, 0, 0, 0, -0.2, 0, 0, 0, 0, 0);
            (double w, double x, double y, double z) = FlightIntegrator.AttitudeQuaternion(banked);

            Assert.Equal(Math.Cos(-0.1), w, 9);
            Assert.Equal(0.0, x, 9);
            Assert.Equal(0.0, y, 9);
            Assert.Equal(Math.Sin(-0.1), z, 9);
        }

        [Fact]
        public void A_pure_pitch_composes_to_an_x_axis_quaternion()
        {
            FlightState nosed = new FlightState(0, 0, 0, 0, 0, 0, -0.15, 0, 0, 0, 0);
            (double w, double x, double y, double z) = FlightIntegrator.AttitudeQuaternion(nosed);

            Assert.Equal(Math.Cos(-0.075), w, 9);
            Assert.Equal(Math.Sin(-0.075), x, 9);
            Assert.Equal(0.0, y, 9);
            Assert.Equal(0.0, z, 9);
        }

        [Fact]
        public void The_composition_order_is_unitys_yaw_pitch_roll()
        {
            // q = qY * qX * qZ, the same order Quaternion.Euler uses. Computed
            // by hand for yaw=90deg, roll=-10deg and asserted component-wise;
            // a wrong order shows up here as swapped/signed components.
            double yaw = Math.PI / 2, roll = -Math.PI / 18;
            FlightState state = new FlightState(0, 0, 0, yaw, 0, roll, 0, 0, 0, 0, 0);
            (double w, double x, double y, double z) = FlightIntegrator.AttitudeQuaternion(state);

            double cy = Math.Cos(yaw / 2), sy = Math.Sin(yaw / 2);
            double cr = Math.Cos(roll / 2), sr = Math.Sin(roll / 2);
            // qY*qZ = (cy*cr, sy*sr, sy*cr, cy*sr)
            Assert.Equal(cy * cr, w, 9);
            Assert.Equal(sy * sr, x, 9);
            Assert.Equal(sy * cr, y, 9);
            Assert.Equal(cy * sr, z, 9);
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
            FlightState state = new FlightState(1, 2, 3, 0.4, 0.1, -0.05, 0.02, 5, 1, 0, 5);
            Assert.Equal(state.X, FlightIntegrator.Step(state, Input(throttle: 1f), 0.0, Tuning).X);
            Assert.Equal(state.X, FlightIntegrator.Step(state, Input(throttle: 1f), double.NaN, Tuning).X);
        }

        [Fact]
        public void Delta_merge_keeps_unsent_fields()
        {
            FlightControlInput held = Input(throttle: 0.5f, yaw: 0.7f);
            FlightControlInput merged = held.Merge(1f, null, null, null, null);

            Assert.Equal(1f, merged.Throttle);
            Assert.Equal(0.7f, merged.AxisYaw, 5);
        }

        [Fact]
        public void Input_equality_is_field_exact_for_the_echo_dedupe()
        {
            Assert.True(Input(throttle: 0.5f, yaw: 0.2f) == Input(throttle: 0.5f, yaw: 0.2f));
            Assert.True(Input(throttle: 0.5f) != Input(throttle: 0.50001f));
            Assert.True(FlightControlInput.Neutral == default);
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
            Assert.Equal(FlightTuning.DefaultBankAngleDeg * Math.PI / 180.0, tuning.BankMaxRadians, 9);
            Assert.Equal(FlightTuning.DefaultPitchAngleDeg * Math.PI / 180.0, tuning.PitchMaxRadians, 9);
            Assert.Equal(FlightTuning.DefaultVelocitySmoothingSeconds, tuning.VelocitySmoothingSeconds);
            Assert.Equal(0.0, tuning.IdleBobMetres); // the bob DEFAULTS OFF
            Assert.False(tuning.InvertYaw);
            Assert.Equal(FlightTuning.DefaultPitchRateMps, tuning.PitchRateMps);
            Assert.Equal(FlightTuning.DefaultRollTurnFactor, tuning.RollTurnFactor);
            Assert.False(tuning.InvertPitch);
            Assert.False(tuning.InvertRoll);
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
                ["WAREBORN_FLIGHT_YAW_ACCEL"] = "50",
                ["WAREBORN_FLIGHT_BANK_ANGLE"] = "12",
                ["WAREBORN_FLIGHT_PITCH_ANGLE"] = "7",
                ["WAREBORN_FLIGHT_ATTITUDE_SMOOTHING"] = "0.3",
                ["WAREBORN_FLIGHT_VELOCITY_SMOOTHING"] = "1.2",
                ["WAREBORN_FLIGHT_IDLE_BOB"] = "0.2",
                ["WAREBORN_FLIGHT_PITCH_RATE"] = "6",
                ["WAREBORN_FLIGHT_ROLL_TURN_FACTOR"] = "1.0",
                ["WAREBORN_FLIGHT_INVERT_PITCH"] = "1",
                ["WAREBORN_FLIGHT_INVERT_ROLL"] = "1",
            };
            FlightTuning tuning = FlightTuning.FromEnvironment(k => env.TryGetValue(k, out string? v) ? v : null);

            Assert.Equal(20.0, tuning.MaxSpeedMps);
            Assert.Equal(8.0, tuning.AccelMps2);
            Assert.Equal(45.0 * Math.PI / 180.0, tuning.YawRateRadPerSec, 9);
            Assert.Equal(10.0, tuning.ClimbRateMps);
            Assert.Equal(0.5, tuning.ReverseFactor);
            Assert.Equal(10.0, tuning.RestKeepaliveSeconds);
            Assert.True(tuning.InvertYaw);
            Assert.Equal(50.0 * Math.PI / 180.0, tuning.YawAccelRadPerSec2, 9);
            Assert.Equal(12.0 * Math.PI / 180.0, tuning.BankMaxRadians, 9);
            Assert.Equal(7.0 * Math.PI / 180.0, tuning.PitchMaxRadians, 9);
            Assert.Equal(0.3, tuning.AttitudeSmoothingSeconds);
            Assert.Equal(1.2, tuning.VelocitySmoothingSeconds);
            Assert.Equal(0.2, tuning.IdleBobMetres);
            Assert.Equal(6.0, tuning.PitchRateMps);
            Assert.Equal(1.0, tuning.RollTurnFactor);
            Assert.True(tuning.InvertPitch);
            Assert.True(tuning.InvertRoll);
        }

        [Fact]
        public void The_speed_ceiling_is_the_shared_control_point_safety_cap()
        {
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

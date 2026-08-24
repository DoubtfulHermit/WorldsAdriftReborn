using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// The force model as the integrator actually runs it - i.e. what a player at
    /// the helm would feel. The unit-level maths is pinned in
    /// <see cref="ShipForceModelTests"/>; these assert that it is genuinely WIRED,
    /// and that a ship flown without it is bit-identical to the ship players fly
    /// today.
    ///
    /// That second property is the one that makes this safe to merge: the force
    /// model is off by default, and "off" must mean *nothing changed*.
    /// </summary>
    public class FlightForceModelIntegrationTests
    {
        private static readonly FlightTuning Tuning = new FlightTuning();

        private static FlightState Origin => new FlightState(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        private static FlightState Fly(
            FlightControlInput input, int steps, ShipPropulsion? propulsion, int unfurledSails = 0)
        {
            FlightState state = Origin;
            for (int i = 0; i < steps; i++)
            {
                state = FlightIntegrator.Step(
                    state, input, 0.24, Tuning, unfurledSails, 1.0, propulsion);
            }
            return state;
        }

        /// <summary>A resting ship already pointed on a given heading, radians.</summary>
        private static FlightState HeadingOf(double yawRadians) =>
            new FlightState(0, 0, 0, yawRadians, 0, 0, 0, 0, 0, 0, 0);

        private static FlightState FlyFrom(
            FlightState start, FlightControlInput input, int steps,
            ShipPropulsion? propulsion, int unfurledSails = 0, FlightTuning? tuning = null)
        {
            FlightState state = start;
            for (int i = 0; i < steps; i++)
            {
                state = FlightIntegrator.Step(
                    state, input, 0.24, tuning ?? Tuning, unfurledSails, 1.0, propulsion);
            }
            return state;
        }

        private static FlightState FlyWith(
            FlightTuning Tuning, FlightControlInput input, int steps,
            ShipPropulsion? propulsion, int unfurledSails = 0) =>
            FlyFrom(Origin, input, steps, propulsion, unfurledSails, Tuning);

        private static FlightControlInput FullAhead =>
            new FlightControlInput(throttle: 1f, vertical: 0f, axisYaw: 0f, axisPitch: 0f, axisRoll: 0f);

        private static FlightControlInput HalfAhead =>
            new FlightControlInput(throttle: 0.5f, vertical: 0f, axisYaw: 0f, axisPitch: 0f, axisRoll: 0f);

        private static FlightControlInput FullAstern =>
            new FlightControlInput(throttle: -1f, vertical: 0f, axisYaw: 0f, axisPitch: 0f, axisRoll: 0f);

        private static FlightControlInput LeverCentred =>
            new FlightControlInput(throttle: 0f, vertical: 0f, axisYaw: 0f, axisPitch: 0f, axisRoll: 0f);

        // ------------------------------------------------------------------
        // The safety property.
        // ------------------------------------------------------------------

        [Fact]
        public void Without_propulsion_the_integrator_is_bit_identical_to_today()
        {
            // Every existing call site omits the parameter. If this ever fails, the
            // force model has leaked into the default path and silently retuned
            // every ship in the live world.
            var input = new FlightControlInput(
                throttle: 1f, vertical: 0.5f, axisYaw: 1f, axisPitch: 0.3f, axisRoll: -0.4f);

            FlightState implicitly = Origin;
            FlightState explicitly = Origin;
            for (int i = 0; i < 60; i++)
            {
                implicitly = FlightIntegrator.Step(implicitly, input, 0.24, Tuning, 2, 1.0);
                explicitly = FlightIntegrator.Step(explicitly, input, 0.24, Tuning, 2, 1.0, null);
            }

            Assert.Equal(implicitly.X, explicitly.X, 12);
            Assert.Equal(implicitly.Y, explicitly.Y, 12);
            Assert.Equal(implicitly.Z, explicitly.Z, 12);
            Assert.Equal(implicitly.SpeedCmdMps, explicitly.SpeedCmdMps, 12);
            Assert.Equal(implicitly.YawRadians, explicitly.YawRadians, 12);
        }

        [Fact]
        public void The_force_model_actually_changes_the_flight_when_it_is_supplied()
        {
            // The mutation guard for the wiring itself: if the propulsion argument
            // stops being consulted inside Step, these two become equal and this
            // test goes red. A model that is threaded but ignored is exactly the
            // failure this repo has shipped before.
            var featherweight = new ShipPropulsion(200.0, 2400.0, 0);
            FlightState kinematic = Fly(FullAhead, 120, null);
            FlightState forces = Fly(FullAhead, 120, featherweight);

            Assert.NotEqual(kinematic.SpeedCmdMps, forces.SpeedCmdMps, 3);
        }

        // ------------------------------------------------------------------
        // HOVER - *"sky generator and a simple ship should hover regardless"*.
        // ------------------------------------------------------------------

        [Fact]
        public void A_ship_holds_its_altitude_with_no_thrust_of_any_kind()
        {
            // The maintainer's first claim, and the one thing that must survive
            // turning the flag on: a ship does not sink. It holds today because
            // this integrator has NO gravity term at all - Y moves only on pilot
            // input - and retail agrees for a different reason, its sky core being
            // anti-gravity that exactly cancels weight rather than aerodynamic lift.
            //
            // Pinned as a test because it currently rests on an ABSENCE, and an
            // absence is invisible to review. Anyone adding a lift or weight term
            // to the force path (F2 is scheduled to) will be told here if they have
            // accidentally made bare hulls fall out of the sky.
            foreach (double massKg in new[] { 200.0, 800.0, 4000.0, 20_000.0 })
            {
                FlightState state = Fly(FullAhead, 900, new ShipPropulsion(massKg, 0.0, 0));
                Assert.Equal(0.0, state.Y, 9);
                Assert.Equal(0.0, state.VyMps, 9);
            }
        }

        [Fact]
        public void The_force_model_does_not_touch_the_vertical_axis_at_all()
        {
            // Climb and descent must be bit-identical with the flag on and off.
            // Vertical is the axis sitting on top of the sky-core machinery, so if
            // the force model ever starts perturbing it, that is the signal to stop
            // and re-read the lift notes rather than to retune a number.
            var climbing = new FlightControlInput(
                throttle: 1f, vertical: 1f, axisYaw: 0f, axisPitch: 0f, axisRoll: 0f);

            FlightState off = Origin;
            FlightState on = Origin;
            for (int i = 0; i < 200; i++)
            {
                off = FlightIntegrator.Step(off, climbing, 0.24, Tuning, 0, 1.0, null);
                on = FlightIntegrator.Step(
                    on, climbing, 0.24, Tuning, 0, 1.0, new ShipPropulsion(800.0, 1200.0, 0));
            }

            Assert.Equal(off.Y, on.Y, 9);
            Assert.Equal(off.VyMps, on.VyMps, 9);
            Assert.True(off.Y > 0.0, "the control input should actually have climbed");
        }

        // ------------------------------------------------------------------
        // Engines and mass.
        // ------------------------------------------------------------------

        [Fact]
        public void Full_throttle_settles_at_the_drag_limited_top_speed_of_that_ship()
        {
            // Note the wind and recovered residual-drag terms. EngineTopSpeedMps
            // intentionally remains the primary power-law figure; the runtime and
            // inspector use PredictedSettledSpeedMps for the complete GetDrag
            // equilibrium.
            var ship = new ShipPropulsion(800.0, 1200.0, 0);
            FlightState state = Fly(FullAhead, 600, ship);
            double expected = ShipForceModel.PredictedSettledSpeedMps(
                1200.0, 800.0, ShipForceModel.BaselineDriveSpeedMps(800.0));
            Assert.Equal(expected, state.SpeedCmdMps, 2);
        }

        [Fact]
        public void A_heavier_hull_reaches_a_genuinely_lower_top_speed()
        {
            // The single most important behavioural change: under the old model
            // every ship in the game held exactly 12 m/s, whatever it was made of.
            FlightState light = Fly(FullAhead, 600, new ShipPropulsion(400.0, 1200.0, 0));
            FlightState heavy = Fly(FullAhead, 600, new ShipPropulsion(3200.0, 1200.0, 0));

            Assert.True(heavy.SpeedCmdMps < light.SpeedCmdMps * 0.6,
                "mass barely mattered: " + heavy.SpeedCmdMps + " vs " + light.SpeedCmdMps);
        }

        [Fact]
        public void More_engines_make_the_same_hull_faster()
        {
            FlightState two = Fly(FullAhead, 600,
                new ShipPropulsion(800.0, 2 * ShipForceModel.DefaultEngineThrustNewtons, 0));
            FlightState six = Fly(FullAhead, 600,
                new ShipPropulsion(800.0, 6 * ShipForceModel.DefaultEngineThrustNewtons, 0));

            Assert.True(six.SpeedCmdMps > two.SpeedCmdMps);
            // ...but with the recovered square-root return, not linearly.
            Assert.True(six.SpeedCmdMps < 3.0 * two.SpeedCmdMps);
        }

        [Fact]
        public void An_engineless_hull_with_no_canvas_moves_slowly_rather_than_not_at_all()
        {
            // THIS TEST USED TO ASSERT THE OPPOSITE, and it was wrong. It read
            // "a hull with neither engines nor sails hangs where it is, however hard
            // the pilot pulls the lever", and that claim was the stated reason the
            // feature flag had to stay off.
            //
            // Retail's wind acts on the HULL, not only on the canvas, and
            // WindPhysicsVisualizer exempts any ship with a working sky core from
            // its at-rest early return - so a bare hull drifts. The maintainer, who
            // played it: *"the ship without sails can move too, but really slowly."*
            //
            // The test is kept rather than deleted because the corrected assertion
            // is the more valuable one: it pins BOTH halves - that a bare hull is
            // mobile, and that it is mobile only barely, so that canvas and engines
            // still mean something.
            FlightState state = Fly(FullAhead, 400, new ShipPropulsion(800.0, 0.0, 0));

            double drift = ShipForceModel.BaselineDriveSpeedMps(800.0);
            Assert.True(state.SpeedCmdMps > 0.5,
                "a bare hull must get under way: " + state.SpeedCmdMps);
            Assert.True(state.SpeedCmdMps <= drift + 1e-6,
                "a bare hull must not exceed its drift speed of " + drift
                + ": " + state.SpeedCmdMps);
            Assert.True(Math.Abs(state.Z) > 1.0, "the bare hull never actually went anywhere");
        }

        [Fact]
        public void A_bare_hull_is_far_slower_than_the_same_hull_under_canvas()
        {
            // The half of the correction that keeps the progression meaningful. A
            // bare hull moving is only the right answer if it is still decisively
            // worse than rigging a sail, otherwise canvas becomes decorative.
            //
            // HEADING MATTERS HERE, and that is the point rather than an
            // inconvenience: sail force is efficiency * |wind| * Power with the
            // hull-lateral component stripped, so a well-set ship gets ~66 N per
            // sail and one pointing the wrong way gets under 1 N. The favourable
            // heading is used because the claim under test is "canvas is worth
            // having", not "canvas is worth having on every heading" - the second
            // is false, deliberately, and is pinned separately below.
            var sailedShip = new ShipPropulsion(800.0, 0.0, 2);
            FlightState bare = FlyFrom(HeadingOf(2.82), FullAhead, 900, new ShipPropulsion(800.0, 0.0, 0));
            FlightState sailed = FlyFrom(HeadingOf(2.82), FullAhead, 900, sailedShip, unfurledSails: 2);

            Assert.True(sailed.SpeedCmdMps > 1.5 * bare.SpeedCmdMps,
                "canvas must be worth substantially more than a bare hull: bare="
                + bare.SpeedCmdMps + " sailed=" + sailed.SpeedCmdMps);
        }

        [Fact]
        public void Sailing_the_wrong_way_is_barely_better_than_bare_poles()
        {
            // The consequence of a CONSTANT wind, stated as a test so nobody
            // rediscovers it as a bug report. We serve no weather cells, so every
            // position falls through to the client's single fallback wind vector -
            // which means a ship's heading permanently decides how well it sails,
            // with no better wind to go and find. Roughly a fifth of headings give
            // under a tenth of the best drive.
            var ship = new ShipPropulsion(800.0, 0.0, 2);
            FlightState good = FlyFrom(HeadingOf(2.82), FullAhead, 900, ship, unfurledSails: 2);
            FlightState bad = FlyFrom(HeadingOf(5.76), FullAhead, 900, ship, unfurledSails: 2);

            Assert.True(good.SpeedCmdMps > 2.0 * bad.SpeedCmdMps,
                "heading must strongly decide sailing speed: good=" + good.SpeedCmdMps
                + " bad=" + bad.SpeedCmdMps);
        }

        [Fact]
        public void The_bare_hull_baseline_follows_the_lever_rather_than_being_all_or_nothing()
        {
            // Half throttle must be worth about half the drift. Without the
            // throttle factor the baseline is a binary "on" and a feathered lever
            // gives full drift, which at the helm reads as a ship that ignores the
            // one control the pilot is holding.
            FlightState half = Fly(HalfAhead, 900, new ShipPropulsion(800.0, 0.0, 0));
            FlightState full = Fly(FullAhead, 900, new ShipPropulsion(800.0, 0.0, 0));

            Assert.True(half.SpeedCmdMps > 0.0, "half throttle must still move a bare hull");
            Assert.True(half.SpeedCmdMps < 0.75 * full.SpeedCmdMps,
                "half throttle must be meaningfully slower than full: half="
                + half.SpeedCmdMps + " full=" + full.SpeedCmdMps);
        }

        [Fact]
        public void The_bare_hull_baseline_does_not_drive_a_ship_backwards()
        {
            // The baseline is a "get under way" affordance, not a full drive: it is
            // retail's wind, and wind does not reverse because a pilot pulled the
            // lever back. A hull with no engines therefore has nothing to reverse
            // WITH, and must sit still rather than sail backwards at drift speed.
            FlightState state = Fly(FullAstern, 900, new ShipPropulsion(800.0, 0.0, 0));
            Assert.Equal(0.0, state.SpeedCmdMps, 6);
        }

        [Fact]
        public void Turning_the_wind_knob_up_actually_reaches_the_bare_hull()
        {
            // The wiring guard. WindSpeedMps can be threaded all the way to
            // FlightTuning, unit-tested there, and still never be consulted inside
            // Step - which is the "threaded but ignored" shape this repo has
            // shipped before. Only an integration assertion catches it.
            var breezy = new FlightTuning(windSpeedMps: 9.0);
            var ship = new ShipPropulsion(800.0, 0.0, 0);

            FlightState calm = Fly(FullAhead, 900, ship);
            FlightState blowing = FlyWith(Tuning: breezy, FullAhead, 900, ship);

            Assert.True(blowing.SpeedCmdMps > 2.0 * calm.SpeedCmdMps,
                "the wind knob did not reach the bare hull: calm=" + calm.SpeedCmdMps
                + " blowing=" + blowing.SpeedCmdMps);
        }

        [Fact]
        public void Turning_the_wind_knob_up_actually_reaches_the_sails()
        {
            // The same guard for the other consumer. Sails and the baseline read
            // the SAME wind in retail, so a knob that moved only one of them would
            // be a physical inconsistency as well as a wiring bug.
            const double windSpeed = 9.0;
            const double heading = 2.82;
            const double massKg = 800.0;
            var breezy = new FlightTuning(windSpeedMps: windSpeed);
            var ship = new ShipPropulsion(massKg, 0.0, 3);

            FlightState blowing = FlyFrom(
                HeadingOf(heading), FullAhead, 900, ship, unfurledSails: 3, tuning: breezy);

            // Asserted against the closed form rather than against a ratio, because
            // a ratio is satisfied by a PARTIALLY wired wind - scaling one axis and
            // not the other changes both the magnitude and the DIRECTION, and still
            // makes the ship faster. Only the exact expectation catches that.
            double scale = windSpeed / ShipForceModel.DefaultWindSpeedMps;
            double sailN = ShipForceModel.SailForwardNewtons(
                3, heading, breezy.SailPowerNewtons,
                ShipForceModel.DefaultWindX * scale,
                ShipForceModel.DefaultWindZ * scale);
            double expected = ShipForceModel.PredictedSettledSpeedMps(
                sailN, massKg, ShipForceModel.BaselineDriveSpeedMps(massKg, windSpeed));

            Assert.Equal(expected, blowing.SpeedCmdMps, 2);
        }

        [Fact]
        public void An_unmanned_bare_hull_settles_instead_of_drifting_for_ever()
        {
            // The wire-safety half of the departure. Retail let a bare hull drift
            // downwind indefinitely; a world where every abandoned hull drifts is a
            // world where every abandoned hull emits control points, which is the
            // congestion class the standing multiplayer-safety rule exists to
            // prevent. The baseline is gated on the pilot ASKING for drive, so a
            // centred lever must still come to rest.
            FlightState state = Fly(LeverCentred, 600, new ShipPropulsion(800.0, 0.0, 0));
            Assert.Equal(0.0, state.SpeedCmdMps, 6);
        }

        // ------------------------------------------------------------------
        // Sails - the maintainer's acceptance test, at the helm.
        // ------------------------------------------------------------------

        [Fact]
        public void Unfurled_sails_move_a_stationary_ship_with_the_lever_centred()
        {
            // *"I read that if you have sails unfurled the ship goes forward even if
            // you are stationary."* Under the old model this was false - sails were
            // a multiplier on a throttle target of zero. Under the recovered model
            // it is true, because the wind does not care about the throttle.
            FlightState state = Fly(
                LeverCentred, 300, new ShipPropulsion(800.0, 0.0, 2), unfurledSails: 2);

            Assert.True(Math.Abs(state.SpeedCmdMps) > 0.5,
                "sails did not move the stationary ship: " + state.SpeedCmdMps);

            double travelled = Math.Sqrt((state.X * state.X) + (state.Z * state.Z));
            Assert.True(travelled > 10.0, "the ship never actually went anywhere: " + travelled + " m");
        }

        [Fact]
        public void Centred_lever_canvas_receives_the_same_relative_wind_velocity_as_full_throttle()
        {
            const double massKg = 3094.0;
            const double heading = 2.82;
            var ship = new ShipPropulsion(massKg, 0.0, 2);

            FlightState centred = FlyFrom(
                HeadingOf(heading), LeverCentred, 1200, ship, unfurledSails: 2);

            double sailN = ShipForceModel.SailForwardNewtons(
                2, heading, Tuning.SailPowerNewtons);
            double expected = ShipForceModel.PredictedSettledSpeedMps(
                sailN, massKg, ShipForceModel.BaselineDriveSpeedMps(massKg));

            Assert.Equal(expected, centred.SpeedCmdMps, 2);
        }

        [Fact]
        public void The_old_model_did_not_move_a_stationary_ship_under_sail()
        {
            // The counterpart to the test above, kept deliberately: it documents the
            // defect the force model exists to fix, and it will start failing the day
            // somebody makes the legacy path do the right thing - at which point this
            // test should be deleted, not weakened.
            FlightState state = Fly(LeverCentred, 300, null, unfurledSails: 4);
            Assert.Equal(0.0, state.SpeedCmdMps, 9);
        }

        [Fact]
        public void Canvas_adds_to_engines_rather_than_replacing_them()
        {
            var bare = new ShipPropulsion(800.0, 1200.0, 0);
            var rigged = new ShipPropulsion(800.0, 1200.0, 4);

            // Pick the heading where the canvas is actually pulling its weight, so
            // the test is about composition and not about trim.
            double best = 0.0, bestBare = 0.0;
            for (int degrees = 0; degrees < 360; degrees += 10)
            {
                var heading = new FlightControlInput(1f, 0f, 0f, 0f, 0f);
                FlightState r = Origin, b = Origin;
                for (int i = 0; i < 600; i++)
                {
                    r = FlightIntegrator.Step(r, heading, 0.24, Tuning, 4, 1.0, rigged);
                    b = FlightIntegrator.Step(b, heading, 0.24, Tuning, 0, 1.0, bare);
                }
                if (r.SpeedCmdMps > best) { best = r.SpeedCmdMps; bestBare = b.SpeedCmdMps; }
            }

            Assert.True(best > bestBare,
                "canvas did not add to engine thrust: " + best + " vs " + bestBare);
        }

        [Fact]
        public void The_wire_speed_clamp_still_holds_under_an_absurd_engine_stack()
        {
            // A malformed or maliciously large thrust must never put a hull above the
            // speed the 0.24 s control-point stream can carry, or the client's spline
            // correction starts fighting the server.
            FlightState state = Fly(FullAhead, 2000, new ShipPropulsion(50.0, 5_000_000.0, 0));
            Assert.True(state.SpeedCmdMps
                <= WorldsAdriftRebornGameServer.Multiplayer.ShipMotionPolicy.MaxSpeedMetresPerSecond + 1e-9);
        }

        [Fact]
        public void A_ship_under_the_force_model_still_settles_and_goes_quiet()
        {
            // IsAtRest gates the whole rest/silence machine. If the force model
            // leaves a residual crawl, every parked ship in the world keeps emitting
            // control points at the flying cadence forever - which is precisely the
            // congestion class the standing multiplayer-safety rule exists for.
            FlightState state = Fly(FullAhead, 300, new ShipPropulsion(800.0, 1200.0, 0));
            for (int i = 0; i < 4000; i++)
            {
                state = FlightIntegrator.Step(state, LeverCentred, 0.24, Tuning, 0, 1.0,
                    new ShipPropulsion(800.0, 1200.0, 0));
            }
            Assert.True(state.IsAtRest, "the ship never settled; speed=" + state.SpeedCmdMps);
        }
    }
}

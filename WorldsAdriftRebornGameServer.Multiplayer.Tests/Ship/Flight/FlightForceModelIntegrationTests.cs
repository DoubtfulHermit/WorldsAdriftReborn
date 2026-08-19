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

        private static FlightControlInput FullAhead =>
            new FlightControlInput(throttle: 1f, vertical: 0f, axisYaw: 0f, axisPitch: 0f, axisRoll: 0f);

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
        // Engines and mass.
        // ------------------------------------------------------------------

        [Fact]
        public void Full_throttle_settles_at_the_drag_limited_top_speed_of_that_ship()
        {
            var ship = new ShipPropulsion(800.0, 1200.0, 0);
            FlightState state = Fly(FullAhead, 600, ship);
            Assert.Equal(ship.EngineTopSpeedMps, state.SpeedCmdMps, 2);
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
        public void An_engineless_hull_with_no_canvas_never_gets_under_way()
        {
            // Retail ships were pushed by their engines. A hull with neither engines
            // nor sails hangs where it is, however hard the pilot pulls the lever.
            // This is the behaviour that makes the feature flag necessary.
            FlightState state = Fly(FullAhead, 400, new ShipPropulsion(800.0, 0.0, 0));
            Assert.Equal(0.0, state.SpeedCmdMps, 9);
            Assert.Equal(0.0, state.Z, 9);
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
            // IsAtRest gates the whole rest/keepalive machine. If the force model
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

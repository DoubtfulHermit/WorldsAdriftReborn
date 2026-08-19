using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// The RECOVERED force model. These pin the two things the maintainer asked
    /// for by name - "how much thrust the engines are doing" and "the weight of the
    /// ship affects what it can do and how much speed it gets" - plus the sail
    /// behaviour they described from memory of retail: *"if you have sails unfurled
    /// the ship goes forward even if you are stationary"*.
    ///
    /// Where a number here is read off the shipped client it is asserted exactly;
    /// where it is ours the test asserts the SHAPE (monotonicity, a ratio, a limit)
    /// rather than a magnitude, so retuning our numbers does not break the suite but
    /// breaking the physics does.
    /// </summary>
    public class ShipForceModelTests
    {
        // ------------------------------------------------------------------
        // Drag - RECOVERED constants, asserted exactly.
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(10.0, 1.0)]    // 0.01 * 10^2
        [InlineData(20.0, 4.0)]    // quadratic: double the speed, four times the drag
        [InlineData(12.0, 1.44)]
        public void Drag_is_the_clients_own_quadratic_law(double speed, double expected)
        {
            Assert.Equal(expected, ShipForceModel.DragDecelerationMps2(speed), 9);
        }

        [Fact]
        public void Drag_opposes_travel_in_both_directions_and_is_mass_independent()
        {
            // Retail computed a drag ACCELERATION and only then multiplied by mass,
            // so mass cancels. This is the reason top speed depends on thrust-TO-
            // WEIGHT rather than on either alone, and it must not quietly acquire a
            // mass term.
            Assert.Equal(
                ShipForceModel.DragDecelerationMps2(10.0),
                ShipForceModel.DragDecelerationMps2(-10.0), 9);
        }

        [Fact]
        public void A_malformed_speed_does_not_throw_or_fling_the_ship()
        {
            Assert.Equal(0.0, ShipForceModel.DragDecelerationMps2(double.NaN));
            Assert.Equal(0.0, ShipForceModel.DragDecelerationMps2(double.PositiveInfinity));
        }

        // ------------------------------------------------------------------
        // Top speed - the consequence, never an input.
        // ------------------------------------------------------------------

        [Fact]
        public void Top_speed_is_ten_times_the_root_of_thrust_to_weight()
        {
            // v = sqrt(F / (m * 0.01)) = 10 * sqrt(F/m).
            Assert.Equal(10.0, ShipForceModel.TerminalSpeedMps(800.0, 800.0), 6);
            Assert.Equal(20.0, ShipForceModel.TerminalSpeedMps(3200.0, 800.0), 6);
        }

        [Fact]
        public void Doubling_the_engines_buys_only_the_root_of_two()
        {
            // THE ship-building consequence, and the one most likely to surprise a
            // player: engines have sharply diminishing returns.
            double one = ShipForceModel.TerminalSpeedMps(600.0, 800.0);
            double two = ShipForceModel.TerminalSpeedMps(1200.0, 800.0);
            Assert.Equal(Math.Sqrt(2.0), two / one, 6);
        }

        [Fact]
        public void Doubling_the_mass_costs_the_root_of_two()
        {
            // The maintainer's question, in one assertion: weight decides speed.
            double light = ShipForceModel.TerminalSpeedMps(1200.0, 800.0);
            double heavy = ShipForceModel.TerminalSpeedMps(1200.0, 1600.0);
            Assert.Equal(1.0 / Math.Sqrt(2.0), heavy / light, 6);
            Assert.True(heavy < light);
        }

        [Fact]
        public void The_reference_two_engine_ship_still_flies_at_about_the_speed_it_always_has()
        {
            // THE CALIBRATION GUARD. Our chosen 600 N per engine exists to make the
            // force model re-derive the 12 m/s this server has flown at since flight
            // shipped, so that switching the model on does not lurch the live game.
            // If someone retunes engine thrust, this is the test that asks them
            // whether they meant to change how every existing ship handles.
            var reference = new ShipPropulsion(
                massKg: 800.0,
                engineThrustNewtons: 2 * ShipForceModel.DefaultEngineThrustNewtons,
                unfurledSails: 0);
            Assert.InRange(reference.EngineTopSpeedMps, 11.5, 13.0);
        }

        [Fact]
        public void A_ship_with_no_engines_has_no_thrust_and_no_top_speed()
        {
            var driftwood = new ShipPropulsion(800.0, 0.0, 0);
            Assert.Equal(0.0, driftwood.ThrustAccelerationMps2);
            Assert.Equal(0.0, driftwood.EngineTopSpeedMps);
        }

        [Fact]
        public void A_malformed_hull_is_never_weightless_and_never_divides_by_zero()
        {
            var broken = new ShipPropulsion(0.0, 600.0, 0);
            Assert.True(broken.MassKg > 0.0);
            Assert.True(double.IsFinite(broken.ThrustAccelerationMps2));

            var nonsense = new ShipPropulsion(double.NaN, double.NaN, -5);
            Assert.True(nonsense.MassKg > 0.0);
            Assert.Equal(0.0, nonsense.EngineThrustNewtons);
            Assert.Equal(0, nonsense.UnfurledSails);
        }

        // ------------------------------------------------------------------
        // Integration - speed converges on the balance point.
        // ------------------------------------------------------------------

        [Fact]
        public void Speed_converges_on_the_terminal_speed_and_stays_there()
        {
            const double thrustAccel = 1.44;   // the reference ship
            double expected = ShipForceModel.TerminalSpeedMps(1.44 * 800.0, 800.0);

            double v = 0.0;
            for (int i = 0; i < 2000; i++)
            {
                v = ShipForceModel.StepSpeed(v, thrustAccel, 0.24);
            }
            Assert.Equal(expected, v, 3);

            // And it is a FIXED POINT, not a fly-through.
            Assert.Equal(expected, ShipForceModel.StepSpeed(v, thrustAccel, 0.24), 3);
        }

        [Fact]
        public void A_coasting_ship_slows_to_exactly_zero_and_never_reverses()
        {
            // Without the clamp inside StepSpeed an idle ship oscillates around
            // zero at the control-point cadence, which on the wire is a hull
            // twitching back and forth forever.
            double v = 12.0;
            for (int i = 0; i < 5000; i++)
            {
                v = ShipForceModel.StepSpeed(v, 0.0, 0.24);
                Assert.True(v >= 0.0, "drag reversed the ship at step " + i);
            }
            Assert.Equal(0.0, v, 6);
        }

        [Fact]
        public void A_heavier_ship_takes_longer_to_reach_a_lower_top_speed()
        {
            // Both halves of the maintainer's intuition in one test: mass costs
            // acceleration AND final speed. The old model only ever cost
            // acceleration.
            double light = 0.0, heavy = 0.0;
            const double thrustN = 1200.0;
            for (int i = 0; i < 400; i++)
            {
                light = ShipForceModel.StepSpeed(light, thrustN / 800.0, 0.24);
                heavy = ShipForceModel.StepSpeed(heavy, thrustN / 3200.0, 0.24);
            }
            Assert.True(heavy < light);
            Assert.Equal(0.5, heavy / light, 2);   // 4x the mass = half the speed
        }

        // ------------------------------------------------------------------
        // Sails - the maintainer's specific question.
        // ------------------------------------------------------------------

        [Fact]
        public void An_unfurled_sail_pushes_regardless_of_throttle_or_speed()
        {
            // THE HEADLINE. Retail's sail force came from the WIND and the sail's
            // trim; the throttle and the ship's current speed appear nowhere in
            // SailBehaviour.Update. So a sail on a motionless ship pushes it.
            double atRest = ShipForceModel.SailForwardNewtons(
                1, 0.0, ShipForceModel.DefaultSailPowerNewtonsPerWind);
            Assert.True(Math.Abs(atRest) > 0.0);
        }

        [Fact]
        public void Sail_force_is_linear_in_the_number_of_unfurled_sails()
        {
            // Retail added ONE force per unfurled sail, so the plan is additive.
            double one = ShipForceModel.SailForwardNewtons(
                1, 0.7, ShipForceModel.DefaultSailPowerNewtonsPerWind);
            double three = ShipForceModel.SailForwardNewtons(
                3, 0.7, ShipForceModel.DefaultSailPowerNewtonsPerWind);
            Assert.Equal(3.0 * one, three, 9);
        }

        [Fact]
        public void A_furled_sail_plan_produces_nothing()
        {
            Assert.Equal(0.0, ShipForceModel.SailForwardNewtons(
                0, 1.2, ShipForceModel.DefaultSailPowerNewtonsPerWind));
        }

        [Fact]
        public void The_heading_the_ship_sails_decides_what_the_canvas_is_worth()
        {
            // If this ever collapses to a constant, the wind has stopped mattering
            // and sails have gone back to being a flat bonus.
            double best = 0.0, worst = double.MaxValue;
            for (int degrees = 0; degrees < 360; degrees += 5)
            {
                double f = Math.Abs(ShipForceModel.SailForwardNewtons(
                    1, degrees * Math.PI / 180.0, ShipForceModel.DefaultSailPowerNewtonsPerWind));
                if (f > best) best = f;
                if (f < worst) worst = f;
            }
            Assert.True(best > worst * 1.5, "sail force barely varies with heading: " + worst + ".." + best);
        }

        [Fact]
        public void A_badly_trimmed_sail_still_delivers_the_recovered_efficiency_floor()
        {
            // SailBehaviour.MinEfficiency = 0.3 is a real design decision: a player
            // can never be completely becalmed by pointing the wrong way. Assert no
            // heading drops the force below that floor's share of the ideal.
            double floor = ShipForceModel.SailMinEfficiency
                * ShipForceModel.DefaultWindSpeedMps
                * ShipForceModel.DefaultSailPowerNewtonsPerWind;

            for (int degrees = 0; degrees < 360; degrees += 3)
            {
                double magnitude = Math.Abs(ShipForceModel.SailForwardNewtons(
                    1, degrees * Math.PI / 180.0, ShipForceModel.DefaultSailPowerNewtonsPerWind));
                // The along-hull component can be less than the raw floor once the
                // keel strips the lateral part, but the force must never vanish.
                Assert.True(magnitude > 0.0, "sail went completely dead at " + degrees + " deg");
                Assert.True(magnitude <= (ShipForceModel.DefaultWindSpeedMps
                    * ShipForceModel.DefaultSailPowerNewtonsPerWind) + 1e-9);
            }
            Assert.True(floor > 0.0);
        }

        [Fact]
        public void The_new_magnitudes_are_tunable_from_the_environment()
        {
            // Both are WAREBORN TUNING, and the maintainer's complaint that started
            // this work was about speeds. Retuning must not need a rebuild.
            var tuned = FlightTuning.FromEnvironment(name => name switch
            {
                "WAREBORN_FLIGHT_ENGINE_THRUST" => "2400",
                "WAREBORN_FLIGHT_SAIL_POWER" => "75",
                _ => null,
            });
            Assert.Equal(2400.0, tuned.EngineThrustNewtons);
            Assert.Equal(75.0, tuned.SailPowerNewtons);

            // Garbage and absurdity fall back or clamp rather than taking the
            // server down or launching a hull off the map.
            var junk = FlightTuning.FromEnvironment(name => name switch
            {
                "WAREBORN_FLIGHT_ENGINE_THRUST" => "not-a-number",
                "WAREBORN_FLIGHT_SAIL_POWER" => "-40",
                _ => null,
            });
            Assert.Equal(ShipForceModel.DefaultEngineThrustNewtons, junk.EngineThrustNewtons);
            Assert.Equal(0.0, junk.SailPowerNewtons);
        }

        [Fact]
        public void A_dead_calm_produces_no_sail_force()
        {
            Assert.Equal(0.0, ShipForceModel.SailForwardNewtons(
                2, 0.0, ShipForceModel.DefaultSailPowerNewtonsPerWind, windX: 0.0, windZ: 0.0));
        }

        [Fact]
        public void The_default_wind_is_the_clients_own_fallback_vector()
        {
            // PROVED, GlobalWeather.GetCellSampleAt. If this ever changes, the
            // server has stopped agreeing with the wind the shipped client believes
            // in everywhere, and the sail model is inventing weather.
            Assert.Equal(1.0, ShipForceModel.DefaultWindX);
            Assert.Equal(0.0, ShipForceModel.DefaultWindY);
            Assert.Equal(-2.0, ShipForceModel.DefaultWindZ);
            Assert.Equal(Math.Sqrt(5.0), ShipForceModel.DefaultWindSpeedMps, 9);
        }

        [Fact]
        public void Sails_alone_move_a_reference_hull_at_a_believable_drift()
        {
            // Our sail power is WAREBORN TUNING, so this asserts the BAND it was
            // calibrated for rather than the number: canvas alone should be worth a
            // few metres per second - supplementary to engines, never a substitute.
            double best = 0.0;
            for (int degrees = 0; degrees < 360; degrees += 5)
            {
                double heading = degrees * Math.PI / 180.0;
                double v = 0.0;
                for (int i = 0; i < 600; i++)
                {
                    double sailN = ShipForceModel.SailForwardNewtons(
                        2, heading, ShipForceModel.DefaultSailPowerNewtonsPerWind);
                    v = ShipForceModel.StepSpeed(v, sailN / 800.0, 0.24);
                }
                if (Math.Abs(v) > best) best = Math.Abs(v);
            }
            Assert.InRange(best, 1.0, 8.0);
        }
    }
}

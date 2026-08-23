using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// THE BARE-HULL BASELINE - *"the ship without sails can move too, but really
    /// slowly"*, and the three-tier progression the maintainer remembers from
    /// retail: a hull with a sky core hovers and drifts, sails make it faster, and
    /// engines faster still.
    ///
    /// The mechanism is recovered rather than invented, and these tests pin the
    /// recovered half exactly (the mass attenuation constants, and the fact that the
    /// recovered power law acts on the RELATIVE wind so one expression is both drag and
    /// thrust) while asserting only the SHAPE of the half that is ours (the aim
    /// along the heading, and the gate on the pilot asking for drive).
    ///
    /// Why this file exists at all: the claim it defends was denied twice in the
    /// repo's own comments - "a hull with neither simply hangs in the air" - and a
    /// comment cannot fail a build. These can.
    /// </summary>
    public class BareHullBaselineDriveTests
    {
        // ------------------------------------------------------------------
        // The mass attenuation - PROVED, asserted exactly.
        // WindPhysicsVisualizer.ApplyDrag: 1f - Clamp01(mass / 4000f) * 0.75f
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(0.0, 1.0)]         // guard: a malformed mass feels the full wind
        [InlineData(4000.0, 0.25)]     // the reference barge feels a quarter
        [InlineData(2000.0, 0.625)]
        [InlineData(400.0, 0.925)]
        public void Wind_attenuation_is_the_clients_own_mass_law(double massKg, double expected)
        {
            Assert.Equal(expected, ShipForceModel.WindMultiplier(massKg), 9);
        }

        [Fact]
        public void Wind_attenuation_saturates_rather_than_going_negative()
        {
            // Clamp01 in the original. Without it a 40,000 kg hull would feel a
            // NEGATIVE wind - i.e. be sucked upwind - which is the kind of sign
            // inversion that only ever shows up as an unexplainable live report.
            Assert.Equal(0.25, ShipForceModel.WindMultiplier(40_000.0), 9);
            Assert.Equal(0.25, ShipForceModel.WindMultiplier(double.MaxValue), 9);
            Assert.True(ShipForceModel.WindMultiplier(1e9) > 0.0);
        }

        [Fact]
        public void A_heavier_hull_feels_less_wind_and_so_drifts_more_slowly()
        {
            double skiff = ShipForceModel.BaselineDriveSpeedMps(325.0);
            double stock = ShipForceModel.BaselineDriveSpeedMps(595.0);
            double barge = ShipForceModel.BaselineDriveSpeedMps(4000.0);

            Assert.True(skiff > stock, "a light hull must drift faster than a stock one");
            Assert.True(stock > barge, "a stock hull must drift faster than a barge");
        }

        [Fact]
        public void A_bare_hull_drifts_slowly_enough_to_read_as_drifting()
        {
            // The calibration claim, in the client's OWN units. Its helm wind VFX
            // does not switch on below 5 knots and marks 30 knots as "fast", so a
            // bare hull has to sit UNDER the VFX onset or it reads as sailing
            // rather than as drifting - while still being unmistakably in motion.
            const double knotsPerMps = 1.9438;
            double stockKnots = ShipForceModel.BaselineDriveSpeedMps(595.0) * knotsPerMps;

            Assert.InRange(stockKnots, 1.0, 5.0);
        }

        // ------------------------------------------------------------------
        // The one-term insight - PROVED. GetDrag(wind - velocity) is drag when
        // the ship outruns the air and thrust when the air outruns the ship.
        // ------------------------------------------------------------------

        [Fact]
        public void With_no_wind_the_step_is_exactly_the_old_pure_drag_law()
        {
            // The compatibility guarantee that lets this ship without re-tuning
            // anything: the new parameter DEFAULTS to zero and must reduce to the
            // behaviour every existing caller and test already depends on.
            for (double v = -20.0; v <= 20.0; v += 2.5)
            {
                double withDefault = ShipForceModel.StepSpeed(v, 0.5, 0.24);
                double withExplicitZero = ShipForceModel.StepSpeed(v, 0.5, 0.24, 0.0);
                Assert.Equal(withDefault, withExplicitZero, 12);
            }
        }

        [Fact]
        public void A_stationary_bare_hull_gets_under_way_on_the_wind_alone()
        {
            // THE CLAIM. Zero thrust - no engines, no canvas - and the ship still
            // starts moving, because the power law is evaluated on the relative
            // wind and a stationary ship's relative wind is the whole wind.
            double wind = ShipForceModel.BaselineDriveSpeedMps(595.0);
            double speed = ShipForceModel.StepSpeed(0.0, 0.0, 0.24, wind);

            Assert.True(speed > 0.0,
                "a bare hull with a working sky core must get under way on the wind");
        }

        [Fact]
        public void A_bare_hull_settles_at_the_wind_speed_and_does_not_run_away()
        {
            // It moves, but it must not KEEP accelerating: the terminal drift is
            // the wind itself. A bare hull that crept up to engine speeds would
            // make sails and engines pointless.
            double wind = ShipForceModel.BaselineDriveSpeedMps(595.0);
            // Long enough to cover both phases: the primary law closes most of
            // the gap and then hands over to the 0.03 m/s^2 settle term for the
            // last metre per second. Roughly 90 s of flight.
            double speed = 0.0;
            for (int i = 0; i < 1000; i++)
            {
                speed = ShipForceModel.StepSpeed(speed, 0.0, 0.24, wind);
            }

            Assert.Equal(wind, speed, 3);
        }

        [Fact]
        public void The_baseline_never_overshoots_the_wind_inside_one_step()
        {
            // Retail clamped the same way (number.Clamp(0f, magnitude/deltaTime)).
            // Without it a coarse step can jump PAST the wind speed and the hull
            // hunts around it for ever - which on the wire is a control-point
            // stream that never goes quiet, on every drifting hull at once.
            double wind = 2.0;
            foreach (double dt in new[] { 0.24, 1.0, 5.0, 60.0 })
            {
                double speed = ShipForceModel.StepSpeed(0.0, 0.0, dt, wind);
                Assert.InRange(speed, 0.0, wind);
            }
        }

        [Fact]
        public void Above_the_wind_speed_the_same_term_becomes_a_brake()
        {
            // The other half of "retail had ONE term". A ship travelling faster
            // than the air must DECELERATE, and it must decelerate less than it
            // would in still air, because the air is moving with it.
            double wind = 2.0;
            double inMovingAir = ShipForceModel.StepSpeed(15.0, 0.0, 0.24, wind);
            double inStillAir = ShipForceModel.StepSpeed(15.0, 0.0, 0.24, 0.0);

            Assert.True(inMovingAir < 15.0, "a ship outrunning the air must slow down");
            Assert.True(inMovingAir > inStillAir,
                "a tailwind must brake a ship LESS than still air does");
        }

        [Fact]
        public void The_settling_term_does_not_fight_the_wind_that_is_driving_the_ship()
        {
            // The trap this guards. The settle term aims at ZERO and fires only on
            // an "undriven" ship below 1 m/s. A wind-driven bare hull has zero
            // THRUST, so a naive undriven test counts it as undriven, points a
            // 0.03 m/s^2 brake at a ship the wind is trying to move, and the bare
            // hull crawls or sticks. That would look exactly like the bug this
            // whole change exists to fix, so it is asserted rather than assumed.
            double wind = ShipForceModel.BaselineDriveSpeedMps(595.0);

            double driven = 0.0;
            for (int i = 0; i < 1000; i++)
            {
                driven = ShipForceModel.StepSpeed(driven, 0.0, 0.24, wind);
            }
            Assert.True(driven > 0.9 * wind,
                "a wind-driven hull must reach the wind, not be settled to a stop short of it");

            // And with the wind removed it must still come to a true rest.
            double coasting = 0.5;
            for (int i = 0; i < 200; i++)
            {
                coasting = ShipForceModel.StepSpeed(coasting, 0.0, 0.24, 0.0);
            }
            Assert.Equal(0.0, coasting, 6);
        }

        [Fact]
        public void A_malformed_wind_leaves_the_ship_flying_rather_than_NaN()
        {
            // Same contract as every other guard in this model: a bad number must
            // not propagate into the control-point stream, which would strand the
            // hull for every client watching it.
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                double speed = ShipForceModel.StepSpeed(8.0, 0.0, 0.24, bad);
                Assert.True(double.IsFinite(speed));
            }
            Assert.True(double.IsFinite(ShipForceModel.BaselineDriveSpeedMps(double.NaN)));
        }

        // ------------------------------------------------------------------
        // The three tiers, as one ordering assertion. This is the acceptance
        // shape the maintainer described, and it is the thing most likely to be
        // broken by a future retune of the WAREBORN TUNING magnitudes.
        // ------------------------------------------------------------------

        [Fact]
        public void Hull_then_canvas_then_a_twin_engine_rig_is_a_strict_progression()
        {
            const double massKg = 595.0;
            double Terminal(double thrustNewtons, double windAlong)
            {
                double speed = 0.0;
                for (int i = 0; i < 6000; i++)
                {
                    speed = ShipForceModel.StepSpeed(speed, thrustNewtons / massKg, 0.24, windAlong);
                }
                return speed;
            }

            double wind = ShipForceModel.BaselineDriveSpeedMps(massKg);
            // A well-trimmed sail, taken from the model itself rather than hardcoded
            // so that retuning SailPowerNewtonsPerWind moves the test with it.
            double oneSail = Math.Abs(ShipForceModel.SailForwardNewtons(
                1, Math.PI * 0.9, ShipForceModel.DefaultSailPowerNewtonsPerWind));

            double bare = Terminal(0.0, wind);
            double sailed = Terminal(oneSail, wind);
            double engined = Terminal(2.0 * ShipForceModel.DefaultEngineThrustNewtons, wind);

            Assert.True(bare > 0.0, "tier 1: a bare hull moves");
            Assert.True(sailed > bare, "tier 2: canvas beats a bare hull");
            Assert.True(engined > sailed, "tier 3: a twin-engine rig beats one sail");
        }

        // ------------------------------------------------------------------
        // The wind knob - the only lever on the bare-hull tier.
        // ------------------------------------------------------------------

        [Fact]
        public void The_wind_speed_knob_defaults_to_the_clients_own_fallback()
        {
            // The default must be retail's (1, 0, -2), or turning the force model
            // on would silently be a balance change as well as a physics change.
            Assert.Equal(ShipForceModel.DefaultWindSpeedMps,
                new FlightTuning().WindSpeedMps, 9);
            Assert.Equal(Math.Sqrt(5.0), ShipForceModel.DefaultWindSpeedMps, 9);
        }

        [Fact]
        public void A_windier_world_carries_a_bare_hull_faster()
        {
            double calm = ShipForceModel.BaselineDriveSpeedMps(595.0, 2.236);
            double blowing = ShipForceModel.BaselineDriveSpeedMps(595.0, 8.0);

            Assert.True(blowing > calm);
            // Linear in the wind, so the ratio is the wind's ratio exactly - which
            // is what makes this a predictable knob to turn rather than a dial to
            // fiddle with.
            Assert.Equal(8.0 / 2.236, blowing / calm, 6);
        }

        [Fact]
        public void A_dead_calm_world_leaves_a_bare_hull_where_it_is()
        {
            // 0 must be a legal setting and must mean what it says: an operator who
            // wants the strict "a bare hull does not move" reading can have it
            // without a rebuild, and it must not divide by zero getting there.
            Assert.Equal(0.0, ShipForceModel.BaselineDriveSpeedMps(595.0, 0.0), 9);
            Assert.Equal(0.0, ShipForceModel.BaselineDriveSpeedMps(595.0, -3.0), 9);
        }

        [Fact]
        public void The_wind_knob_reads_the_environment_and_survives_rubbish()
        {
            Assert.Equal(9.5, FlightTuning.FromEnvironment(
                n => n == "WAREBORN_FLIGHT_WIND_SPEED" ? "9.5" : null).WindSpeedMps, 9);

            // Unset, garbage and out-of-range must all leave the ship flying - the
            // same contract every other knob in this file has.
            foreach (string? bad in new string?[] { null, "", "  ", "not-a-number", "-4" })
            {
                double w = FlightTuning.FromEnvironment(
                    n => n == "WAREBORN_FLIGHT_WIND_SPEED" ? bad : null).WindSpeedMps;
                Assert.True(w >= 0.0 && double.IsFinite(w), "bad input '" + bad + "' gave " + w);
            }

            // Retail's own ceiling: GlobalWeather returns a ZERO field above
            // 100 m/s rather than a stronger one, so nothing above it is meaningful.
            Assert.Equal(100.0, FlightTuning.FromEnvironment(
                n => n == "WAREBORN_FLIGHT_WIND_SPEED" ? "1e9" : null).WindSpeedMps, 9);
        }

        [Fact]
        public void Bare_hull_multiplier_is_explicit_bounded_and_defaults_to_parity()
        {
            Assert.Equal(1.0, FlightTuning.FromEnvironment(_ => null).BareHullDriveMultiplier, 9);
            Assert.Equal(2.0, FlightTuning.FromEnvironment(
                n => n == "WAREBORN_FLIGHT_BARE_HULL_MULTIPLIER" ? "2" : null)
                .BareHullDriveMultiplier, 9);
            Assert.Equal(4.0, FlightTuning.FromEnvironment(
                n => n == "WAREBORN_FLIGHT_BARE_HULL_MULTIPLIER" ? "999" : null)
                .BareHullDriveMultiplier, 9);
            Assert.Equal(1.0, FlightTuning.FromEnvironment(
                n => n == "WAREBORN_FLIGHT_BARE_HULL_MULTIPLIER" ? "NaN" : null)
                .BareHullDriveMultiplier, 9);
        }

        [Fact]
        public void Bare_hull_multiplier_doubles_only_throttle_requested_baseline_carry()
        {
            const double massKg = 3094.0;
            var propulsion = new ShipPropulsion(massKg, 0.0, 0);
            var input = new FlightControlInput(1f, 0f, 0f, 0f, 0f);
            var one = new FlightTuning(bareHullDriveMultiplier: 1.0);
            var two = new FlightTuning(bareHullDriveMultiplier: 2.0);

            ShipForceEvaluation baseline = ShipForceEvaluator.Evaluate(
                0, 0, 0, input, propulsion, one, 0);
            ShipForceEvaluation doubled = ShipForceEvaluator.Evaluate(
                0, 0, 0, input, propulsion, two, 0);

            Assert.Equal(2.0 * baseline.WindAlongHeadingMps,
                doubled.WindAlongHeadingMps, 9);
            Assert.Equal(0.0, doubled.EngineForceNewtons, 9);
            Assert.Equal(0.0, doubled.SailForceNewtons, 9);
        }

        [Fact]
        public void Bare_hull_multiplier_does_not_change_canvas_wind()
        {
            const double massKg = 3094.0;
            var sailed = new ShipPropulsion(massKg, 0.0, 1);
            var input = new FlightControlInput(1f, 0f, 0f, 0f, 0f);
            var one = new FlightTuning(bareHullDriveMultiplier: 1.0);
            var two = new FlightTuning(bareHullDriveMultiplier: 2.0);

            ShipForceEvaluation sailedOne = ShipForceEvaluator.Evaluate(
                0, 0, System.Math.PI, input, sailed, one, 0);
            ShipForceEvaluation sailedTwo = ShipForceEvaluator.Evaluate(
                0, 0, System.Math.PI, input, sailed, two, 0);

            Assert.NotEqual(0.0, sailedOne.SailForceNewtons);
            Assert.Equal(sailedOne.WindAlongHeadingMps, sailedTwo.WindAlongHeadingMps, 9);
            Assert.Equal(sailedOne.SailForceNewtons, sailedTwo.SailForceNewtons, 9);
        }

    }
}

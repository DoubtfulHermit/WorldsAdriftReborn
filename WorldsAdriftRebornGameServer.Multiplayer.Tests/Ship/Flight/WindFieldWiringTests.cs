using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// That the wind field REACHES A SHIP. This repo has twice shipped a green
    /// suite over a feature that was never plugged in, so the tests that matter
    /// most here are the ones that drive the real integrator and would go red if
    /// somebody computed a beautiful wind and then passed the old constant.
    /// </summary>
    public class WindFieldWiringTests
    {
        private const double Step = 0.24;

        private static FlightTuning Tuning(double windSpeed, double windField) =>
            new FlightTuning(windSpeedMps: windSpeed, windFieldVariation: windField);

        /// <summary>A bare hull: real mass, no engines, no canvas.</summary>
        private static ShipPropulsion BareHull(double massKg = 595.0) =>
            new ShipPropulsion(massKg, 0.0, 0);

        private static FlightControlInput FullAhead =>
            new FlightControlInput(1f, 0f, 0f, 0f, 0f);

        // unfurledSails is a SEPARATE argument to Step from the count inside
        // ShipPropulsion, and passing 0 here while handing in a rigged hull
        // silently exercises no canvas at all. That mistake made the sail test
        // below pass vacuously on its first draft.
        private static double SpeedAfter(
            FlightState start, FlightTuning tuning, int seconds, ShipPropulsion propulsion,
            int unfurledSails = 0)
        {
            FlightState state = start;
            long nowMs = 0;
            for (int i = 0; i < (int)(seconds / Step); i++)
            {
                nowMs += (long)(Step * 1000.0);
                state = FlightIntegrator.Step(
                    state, FullAhead, Step, tuning, unfurledSails, 1.0, propulsion, nowMs / 1000.0);
            }
            return state.SpeedCmdMps;
        }

        // ------------------------------------------------------------------
        // The knob reaches the ship.
        // ------------------------------------------------------------------

        [Fact]
        public void Turning_the_wind_field_on_makes_a_bare_hulls_speed_depend_on_its_heading()
        {
            // THE POINT OF THE WHOLE FEATURE. With the field off, a bare hull is
            // pushed along whatever bearing it is pointing, so heading is worth
            // nothing; with it on, the wind's component along the bow decides.
            var on = Tuning(4.0, 1.0);

            double downwind = SpeedAfter(
                FlightState.AtRestAt(0.0, 100.0, 0.0, WindField.PublishedBearingRadians),
                on, 120, BareHull());
            double upwind = SpeedAfter(
                FlightState.AtRestAt(0.0, 100.0, 0.0, WindField.PublishedBearingRadians + Math.PI),
                on, 120, BareHull());

            Assert.True(
                downwind > upwind + 0.5,
                $"heading should matter: downwind {downwind:0.00} vs upwind {upwind:0.00} m/s");
        }

        [Fact]
        public void With_the_field_off_a_bare_hull_flies_the_same_on_every_heading()
        {
            // The behaviour production has today, pinned, so enabling the field is
            // an opt-in change and not a discovery.
            var off = Tuning(4.0, 0.0);

            double north = SpeedAfter(
                FlightState.AtRestAt(0.0, 100.0, 0.0, 0.0), off, 120, BareHull());
            double east = SpeedAfter(
                FlightState.AtRestAt(0.0, 100.0, 0.0, Math.PI / 2.0), off, 120, BareHull());

            Assert.Equal(north, east, 9);
        }

        [Fact]
        public void With_the_field_off_the_integrator_is_unchanged_by_where_the_ship_is()
        {
            // The guard against a half-landed field: if SampleAt started reading
            // position while the knob was 0, THIS is what would catch it.
            var off = Tuning(4.0, 0.0);

            double atOrigin = SpeedAfter(
                FlightState.AtRestAt(0.0, 100.0, 0.0, 0.0), off, 60, BareHull());
            double farAway = SpeedAfter(
                FlightState.AtRestAt(14_000.0, 100.0, -9_000.0, 0.0), off, 60, BareHull());

            Assert.Equal(atOrigin, farAway, 12);
        }

        [Fact]
        public void With_the_field_on_the_same_ship_flies_differently_in_a_different_part_of_the_world()
        {
            var on = Tuning(4.0, 1.0);
            double heading = WindField.PublishedBearingRadians;

            double here = SpeedAfter(
                FlightState.AtRestAt(0.0, 100.0, 0.0, heading), on, 120, BareHull());
            double overThere = SpeedAfter(
                FlightState.AtRestAt(
                    WindFieldVariation.CellMetres * 0.5, 100.0, 0.0, heading),
                on, 120, BareHull());

            Assert.True(
                Math.Abs(here - overThere) > 0.05,
                $"the wind should differ across the map: {here:0.000} vs {overThere:0.000} m/s");
        }

        [Fact]
        public void The_field_reaches_the_SAILS_and_not_only_the_bare_hull()
        {
            // Sails already took a wind VECTOR, so this is the arm most likely to
            // be left reading the old constant while the baseline gets the new
            // field. Two headings that the constant wind rates identically but a
            // veered wind does not. A QUARTER cell apart, because the veer term
            // is a sine of the cell fraction and two points a HALF cell apart both
            // sit on its zero crossing.
            var on = Tuning(4.0, 1.0);
            var sailed = new ShipPropulsion(595.0, 0.0, 1);

            // THREE seconds, not two minutes: the hull MOVES, so a long run lets
            // both ships wander into each other's part of the field and the
            // measurement washes out. A short run reads the sail force where the
            // ship was put. (Two minutes was the first attempt, and it passed for
            // the wrong reason until the drift was noticed.)
            double a = SpeedAfter(
                FlightState.AtRestAt(0.0, 100.0, 0.0, 0.3), on, 3, sailed, 1);
            double b = SpeedAfter(
                FlightState.AtRestAt(
                    WindFieldVariation.CellMetres * 0.25, 100.0, 0.0, 0.3), on, 3, sailed, 1);

            Assert.True(
                Math.Abs(a - b) > 0.01,
                $"a sailed ship should feel the field too: {a:0.000} vs {b:0.000} m/s");
        }

        [Fact]
        public void The_field_reaches_the_ship_through_a_real_FlightSession_tick()
        {
            // One level up from Step(): if FlightSession stopped passing its clock
            // the field would silently freeze at t=0 and every test above would
            // still pass.
            var on = Tuning(4.0, 1.0);
            var session = new FlightSession(
                FlightState.AtRestAt(0.0, 100.0, 0.0, WindField.PublishedBearingRadians));
            session.Man();
            session.SetInput(FullAhead);

            double first = SampleSessionSpeed(session, on, 0L);

            var later = new FlightSession(
                FlightState.AtRestAt(0.0, 100.0, 0.0, WindField.PublishedBearingRadians));
            later.Man();
            later.SetInput(FullAhead);
            double second = SampleSessionSpeed(
                later, on, (long)(WindFieldVariation.PeriodSeconds * 250.0));

            Assert.True(
                Math.Abs(first - second) > 1e-6,
                $"the session's clock should reach the wind: {first:0.0000} vs {second:0.0000}");
        }

        private static double SampleSessionSpeed(FlightSession session, FlightTuning tuning, long startMs)
        {
            long nowMs = startMs;
            for (int i = 0; i < 200; i++)
            {
                nowMs += 240;
                session.Advance(nowMs, Step, tuning, 0, 1.0, BareHull());
            }
            return session.State.SpeedCmdMps;
        }

        // ------------------------------------------------------------------
        // The environment.
        // ------------------------------------------------------------------

        [Fact]
        public void The_field_is_off_unless_an_operator_asks_for_it()
        {
            FlightTuning defaults = FlightTuning.FromEnvironment(_ => null);

            Assert.False(defaults.WindVariation.IsEnabled);
            Assert.Equal(0.0, defaults.WindVariation.Scale);
        }

        [Fact]
        public void The_field_knob_reads_the_environment_and_survives_rubbish()
        {
            Assert.Equal(
                0.5,
                FlightTuning.FromEnvironment(
                    name => name == "WAREBORN_FLIGHT_WIND_FIELD" ? "0.5" : null)
                    .WindVariation.Scale,
                9);

            Assert.Equal(
                0.0,
                FlightTuning.FromEnvironment(
                    name => name == "WAREBORN_FLIGHT_WIND_FIELD" ? "breezy" : null)
                    .WindVariation.Scale);

            Assert.Equal(
                1.0,
                FlightTuning.FromEnvironment(
                    name => name == "WAREBORN_FLIGHT_WIND_FIELD" ? "50" : null)
                    .WindVariation.Scale);
        }

        [Fact]
        public void The_wind_speed_knob_still_means_what_it_meant_with_the_field_on()
        {
            // Speed and shape are separate knobs: turning the field on must not
            // quietly change how windy the world is.
            var field = new WindFieldVariation(1.0);
            double total = 0.0;
            const int samples = 2000;
            for (int i = 0; i < samples; i++)
            {
                total += WindField.SampleAt(i * 173.0, i * 409.0, i * 11.0, 4.0, field).SpeedMps;
            }

            Assert.Equal(4.0, total / samples, 1);
        }
    }
}

using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using WorldsAdriftRebornGameServer.Multiplayer.Walls;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public class ShipForceEvaluationTests
    {
        private static readonly ShipPropulsion EngineOnly =
            new ShipPropulsion(800.0, 2800.0, 0);

        [Fact]
        public void Manned_centre_detent_produces_exactly_zero_engine_force()
        {
            var input = new FlightControlInput(0.009f, 0f, 0f, 0f, 0f);

            ShipForceEvaluation result = ShipForceEvaluator.Evaluate(
                0, 0, 0, input, EngineOnly, new FlightTuning(), 12.5);

            Assert.Equal(0f, input.Throttle);
            Assert.Equal(0.0, result.EngineForceNewtons);
            Assert.Equal(0.0, result.PropulsionAccelerationMps2);
        }

        [Fact]
        public void Runtime_and_telemetry_share_the_same_wall_affected_sample()
        {
            var walls = new List<WeatherWallSegment>
            {
                new WeatherWallSegment(0, -1000, 0, 1000,
                    WeatherWallType.WindRift, windMultiplier: 0.0),
            };
            FlightState initial = FlightState.AtRestAt(0, 0, 0);
            var tuning = new FlightTuning();
            var input = new FlightControlInput(1f, 0f, 0f, 0f, 0f);

            FlightIntegrator.StepEvaluated(initial, input, 0.24, tuning,
                out ShipForceEvaluation runtime, propulsion: EngineOnly,
                windTimeSeconds: 77.25, walls: walls);
            ShipForceEvaluation telemetry = ShipForceEvaluator.Evaluate(
                0, 0, 0, input, EngineOnly, tuning, 77.25, walls);

            Assert.Equal(1.0, runtime.Wind.WallIntensity);
            Assert.Equal(telemetry.SampledAtSeconds, runtime.SampledAtSeconds);
            Assert.Equal(telemetry.Wind.WindX, runtime.Wind.WindX);
            Assert.Equal(telemetry.Wind.WindZ, runtime.Wind.WindZ);
            Assert.Equal(telemetry.EngineForceNewtons, runtime.EngineForceNewtons);
            Assert.Equal(telemetry.PredictedSettledSpeedMps, runtime.PredictedSettledSpeedMps);
        }

        [Fact]
        public void Session_retains_the_exact_varying_wind_tick_time_for_telemetry()
        {
            var tuning = new FlightTuning(windFieldVariation: 1.0);
            var session = new FlightSession(FlightState.AtRestAt(1234, 0, -987));
            session.Man();
            session.SetInput(new FlightControlInput(1f, 0f, 0f, 0f, 0f));

            session.Advance(12_345, 0.24, tuning, propulsion: EngineOnly);
            ShipForceEvaluation retained = session.LastForceEvaluation;
            ShipForceEvaluation direct = ShipForceEvaluator.Evaluate(
                1234, -987, 0, session.Input, EngineOnly, tuning, 12.345);
            ShipForceEvaluation later = ShipForceEvaluator.Evaluate(
                1234, -987, 0, session.Input, EngineOnly, tuning, 512.345);

            Assert.Equal(12.345, retained.SampledAtSeconds, 12);
            Assert.Equal(direct.Wind.WindX, retained.Wind.WindX, 12);
            Assert.Equal(direct.Wind.WindZ, retained.Wind.WindZ, 12);
            Assert.NotEqual(later.Wind.WindX, retained.Wind.WindX, 6);
        }

        [Fact]
        public void Mount_and_detach_change_total_mass_without_breaking_hull_override()
        {
            ShipMassSnapshot Total(int parts, string? overrideRaw)
            {
                var inputs = new List<ShipMassPartInput>();
                for (int i = 0; i < parts; i++)
                {
                    inputs.Add(new ShipMassPartInput(100 + i, "trunk", "Trunk01", "deck", 0, 0, 0));
                }
                return ShipMassEvaluator.Build(new ShipMassInput(
                    7, null, planDecoded: false, 0, 0, 0, 0, 0, overrideRaw, inputs), previous: null);
            }

            double mounted = Total(2, null).TotalFlightMassKg;
            double detached = Total(1, null).TotalFlightMassKg;
            double overriddenMounted = Total(2, "1000").TotalFlightMassKg;
            double overriddenDetached = Total(1, "1000").TotalFlightMassKg;

            Assert.Equal(900.0, mounted);
            Assert.Equal(850.0, detached);
            Assert.Equal(1100.0, overriddenMounted);
            Assert.Equal(1050.0, overriddenDetached);
            Assert.Equal(50.0, mounted - detached);
            Assert.Equal(50.0, overriddenMounted - overriddenDetached);
        }

    }
}

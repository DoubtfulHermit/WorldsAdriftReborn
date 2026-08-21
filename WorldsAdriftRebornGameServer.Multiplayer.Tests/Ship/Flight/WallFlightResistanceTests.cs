using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public class WallFlightResistanceTests
    {
        private static readonly FlightTuning Tuning = new FlightTuning(
            velocitySmoothingSeconds: 0.0, windFieldVariation: 0.0);

        private static IReadOnlyList<WeatherWallSegment> Headwind(double speed) =>
            new[]
            {
                // North/south wall at x=0. A hull west of it gets a westward
                // Wind-Rift vector, opposite a +X crossing attempt.
                new WeatherWallSegment(0, -1000, 0, 1000,
                    WeatherWallType.WindRift, speed),
            };

        [Fact]
        public void A_wind_rift_resists_a_head_on_crossing_without_varying_world_wind()
        {
            FlightState initial = FlightState.AtRestAt(-50, 100, 0, Math.PI / 2.0);
            var input = new FlightControlInput(throttle: 1, vertical: 0,
                axisYaw: 0, axisPitch: 0, axisRoll: 0);
            var propulsion = new ShipPropulsion(800, 400, 1);

            FlightState open = FlightIntegrator.Step(initial, input, 0.24, Tuning,
                propulsion: propulsion);
            FlightState wall = FlightIntegrator.Step(initial, input, 0.24, Tuning,
                propulsion: propulsion, walls: Headwind(10));

            Assert.True(open.SpeedCmdMps > 0);
            Assert.True(wall.SpeedCmdMps < open.SpeedCmdMps,
                "relative wall wind must subtract from a crossing attempt");
            Assert.Equal(0.0, Tuning.WindVariation.Scale);
        }

        [Fact]
        public void The_recovered_four_to_one_mass_ramp_makes_a_heavy_ship_less_wall_driven()
        {
            FlightState initial = FlightState.AtRestAt(-50, 100, 0, Math.PI / 2.0);
            FlightControlInput neutral = FlightControlInput.Neutral;

            FlightState light = FlightIntegrator.Step(initial, neutral, 0.24, Tuning,
                propulsion: new ShipPropulsion(1, 0, 0), walls: Headwind(10));
            FlightState heavy = FlightIntegrator.Step(initial, neutral, 0.24, Tuning,
                propulsion: new ShipPropulsion(4000, 0, 0), walls: Headwind(10));

            Assert.True(light.SpeedCmdMps < 0);
            Assert.True(heavy.SpeedCmdMps < 0);
            Assert.True(Math.Abs(light.SpeedCmdMps) > Math.Abs(heavy.SpeedCmdMps) * 3.5);
        }
    }
}

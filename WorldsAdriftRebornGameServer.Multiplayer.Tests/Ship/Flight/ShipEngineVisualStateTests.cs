using System;
using System.IO;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class ShipEngineVisualStateTests
    {
        private static string Source(params string[] parts)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "WorldsAdriftReborn.sln")))
                    return File.ReadAllText(Path.Combine(dir.FullName, Path.Combine(parts)));
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate repo root.");
        }

        [Theory]
        [InlineData(1.0, 1.0)]
        [InlineData(0.5, 0.5)]
        [InlineData(-1.0, -1.0)]
        [InlineData(4.0, 1.0)]
        [InlineData(-4.0, -1.0)]
        public void PoweredEngineSpinTracksBoundedAuthoritativeThrottle(double throttle, double expected)
        {
            ShipEngineVisualState state = ShipEngineVisualState.From(throttle, true, 0.25, 1400);
            Assert.Equal((float)expected, state.Throttle);
            Assert.Equal((float)expected, state.CurrentPercentSpin);
        }

        [Fact]
        public void DryEngineReportsCommandButCannotVisuallySpin()
        {
            ShipEngineVisualState state = ShipEngineVisualState.From(0.75, false, 0.25, 1400);
            Assert.Equal(0.75f, state.Throttle);
            Assert.Equal(0f, state.CurrentPercentSpin);
        }

        [Fact]
        public void NonFiniteAndOutOfRangeTuningFailsClosed()
        {
            ShipEngineVisualState state = ShipEngineVisualState.From(
                double.NaN, true, double.PositiveInfinity, double.NegativeInfinity);
            Assert.Equal(default, state);

            state = ShipEngineVisualState.From(0.5, true, 10, 1_000_000);
            Assert.Equal(0.25f, state.Consumption);
            Assert.Equal(100_000f, state.Power);
        }

        [Fact]
        public void MountedEnginesAreSeededAndUpdatedInTheCoherentShipFrame()
        {
            string serializer = Source("WorldsAdriftRebornGameServer", "Game", "Components",
                "ComponentsSerializer.cs");
            string flight = Source("WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs");
            string wire = Source("WorldsAdriftRebornGameServer", "Game", "ShipEngineStateWire.cs");

            Assert.Contains("componentId == ShipEngineStateWire.ComponentId", serializer,
                StringComparison.Ordinal);
            Assert.Contains("ShipPartKinds.Engine", serializer, StringComparison.Ordinal);
            Assert.Contains("ShipEngineStateWire.BuildData(", serializer, StringComparison.Ordinal);
            Assert.Contains("ShipEngineStateWire.BuildUpdate(", flight, StringComparison.Ordinal);
            Assert.Contains("ShipEngineStateWire.ComponentId", flight, StringComparison.Ordinal);
            Assert.Contains("PropulsionDemandFor(hullEntityId)", wire, StringComparison.Ordinal);
            Assert.Contains("ShipFuel.EnginesPowered(hullEntityId)", wire, StringComparison.Ordinal);
        }
    }
}

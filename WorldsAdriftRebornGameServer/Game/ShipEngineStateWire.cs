using Bossa.Travellers.Ship;
using Improbable.Collections;
using Improbable.Math;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// Projects the existing authoritative hull command onto retail component
    /// 1116. The shipped client consumes this component for propeller rotation,
    /// engine VFX and audio; it is not a second propulsion authority.
    /// </summary>
    internal static class ShipEngineStateWire
    {
        internal const uint ComponentId = 1116;

        internal static ShipEngineState.Data BuildData(long hullEntityId, double powerNewtons)
        {
            ShipEngineVisualState state = StateFor(hullEntityId, powerNewtons);
            return new ShipEngineState.Data(
                state.Power,
                state.Throttle,
                new Vector3d(0.0, 0.0, 1.0),
                state.Consumption,
                state.CurrentPercentSpin,
                0f,
                new Option<float>(),
                new Option<float>(state.Throttle),
                new Option<float>(),
                new Option<float>(),
                0f);
        }

        internal static ShipEngineState.Update BuildUpdate(long hullEntityId, double powerNewtons)
        {
            ShipEngineVisualState state = StateFor(hullEntityId, powerNewtons);
            return new ShipEngineState.Update()
                .SetPower(state.Power)
                .SetThrottle(state.Throttle)
                .SetConsumption(state.Consumption)
                .SetCurrentPercentSpin(state.CurrentPercentSpin)
                .SetShipThrottle(new Option<float>(state.Throttle));
        }

        private static ShipEngineVisualState StateFor(long hullEntityId, double powerNewtons)
        {
            Multiplayer.Ship.Fuel.HullPropulsionDemand demand =
                WorldsAdriftRebornGameServer.Flight.PropulsionDemandFor(hullEntityId);
            bool powered = WorldsAdriftRebornGameServer.ShipFuel.EnginesPowered(hullEntityId);
            return ShipEngineVisualState.From(
                demand.Throttle, powered, ShipFuelService.BurnRate, powerNewtons);
        }
    }
}

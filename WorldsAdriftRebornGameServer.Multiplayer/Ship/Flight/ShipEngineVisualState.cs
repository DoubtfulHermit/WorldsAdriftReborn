using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// Pure, bounded projection from authoritative hull propulsion to the retail
    /// 1116 ShipEngineState fields the client uses for propeller, VFX and audio.
    /// It does not author force: flight remains the sole physics authority.
    /// </summary>
    public readonly record struct ShipEngineVisualState(
        float Throttle,
        float CurrentPercentSpin,
        float Consumption,
        float Power)
    {
        public static ShipEngineVisualState From(
            double throttle,
            bool enginesPowered,
            double consumptionPerSecond,
            double powerNewtons)
        {
            float command = FiniteClamped(throttle, -1.0, 1.0);
            float spin = enginesPowered ? command : 0f;
            float consumption = FiniteClamped(consumptionPerSecond, 0.0, 0.25);
            float power = FiniteClamped(powerNewtons, 0.0, 100_000.0);
            return new ShipEngineVisualState(command, spin, consumption, power);
        }

        private static float FiniteClamped(double value, double min, double max)
        {
            if (!double.IsFinite(value)) return 0f;
            return (float)Math.Clamp(value, min, max);
        }
    }
}

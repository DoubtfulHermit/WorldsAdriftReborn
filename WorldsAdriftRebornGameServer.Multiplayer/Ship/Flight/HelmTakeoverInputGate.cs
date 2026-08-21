using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// Separates character controls held while entering a neutral helm from a new
    /// ship command. A neutral throttle/climb edge arms each control independently;
    /// there is deliberately no timer. A genuinely latched non-zero throttle is
    /// already armed because it is physical ship state that re-manning must preserve.
    /// </summary>
    public sealed class HelmTakeoverInputGate
    {
        private const float NeutralDeadzone = 0.01f;
        private bool _throttleArmed;
        private bool _verticalArmed;

        public HelmTakeoverInputGate(FlightControlInput shipInput)
        {
            _throttleArmed = MathF.Abs(shipInput.Throttle) >= NeutralDeadzone;
            _verticalArmed = false;
        }

        public HelmTakeoverInputDelta Filter(float? throttle, float? vertical)
        {
            bool suppressedThrottle = false;
            bool suppressedVertical = false;
            float? acceptedThrottle = FilterAxis(
                throttle, ref _throttleArmed, ref suppressedThrottle);
            float? acceptedVertical = FilterAxis(
                vertical, ref _verticalArmed, ref suppressedVertical);
            return new HelmTakeoverInputDelta(
                acceptedThrottle, acceptedVertical,
                suppressedThrottle, suppressedVertical);
        }

        private static float? FilterAxis(float? value, ref bool armed, ref bool suppressed)
        {
            if (!value.HasValue || armed) return value;
            if (IsNeutral(value.Value))
            {
                armed = true;
                return 0f;
            }
            suppressed = true;
            return null;
        }

        private static bool IsNeutral(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value)
            && MathF.Abs(value) < NeutralDeadzone;
    }

    public readonly struct HelmTakeoverInputDelta
    {
        public HelmTakeoverInputDelta(float? throttle, float? vertical,
            bool suppressedThrottle, bool suppressedVertical)
        {
            Throttle = throttle;
            Vertical = vertical;
            SuppressedThrottle = suppressedThrottle;
            SuppressedVertical = suppressedVertical;
        }

        public float? Throttle { get; }
        public float? Vertical { get; }
        public bool SuppressedThrottle { get; }
        public bool SuppressedVertical { get; }
    }
}

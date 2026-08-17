namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// Final E-hold clamp for ship-part interactions. Retail's
    /// observer can append a ten-second non-friendly-ship penalty after the
    /// visualizer resolves its own interaction time, so both the helm's Man and
    /// the sail's Activate must be clamped again at TimedInteractionController.
    /// The caller selects the complete ShipPartVisualizer family, so lamps, horns,
    /// storage interactions added later, and every other part receive the same short
    /// response as helm and sail. Non-ship world interactions remain untouched.
    /// </summary>
    public static class ShipInteractionHoldPolicy
    {
        public const float MaxImmediateHoldSeconds = 0.15f;

        public static float Clamp(bool isShipPartInteraction, float seconds)
        {
            if (!isShipPartInteraction || seconds <= MaxImmediateHoldSeconds)
            {
                return seconds;
            }
            return MaxImmediateHoldSeconds;
        }
    }
}

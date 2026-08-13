namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// Final E-hold clamp for immediately operated ship controls. Retail's
    /// observer can append a ten-second non-friendly-ship penalty after the
    /// visualizer resolves its own interaction time, so both the helm's Man and
    /// the sail's Activate must be clamped again at TimedInteractionController.
    /// Lamps/horns and unrelated Activate objects are deliberately not selected
    /// by this pure policy's caller.
    /// </summary>
    public static class ShipInteractionHoldPolicy
    {
        public const float MaxImmediateHoldSeconds = 0.15f;

        public static float Clamp(bool isImmediateShipControl, float seconds)
        {
            if (!isImmediateShipControl || seconds <= MaxImmediateHoldSeconds)
            {
                return seconds;
            }
            return MaxImmediateHoldSeconds;
        }
    }
}

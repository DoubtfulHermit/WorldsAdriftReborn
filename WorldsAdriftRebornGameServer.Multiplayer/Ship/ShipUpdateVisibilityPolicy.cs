namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// Decides whether one peer should receive a live ship-motion update.
    /// A component update is meaningful only after that peer checked the target
    /// entity out; distance then bounds background traffic, while a pilot or
    /// passenger always receives the ship they are interacting with.
    /// </summary>
    public static class ShipUpdateVisibilityPolicy
    {
        public static bool ShouldPublish(
            bool targetCheckedOut,
            bool isPilot,
            bool isAboard,
            FixedPointPosition peerPosition,
            FixedPointPosition hullPosition,
            double radiusMetres)
        {
            if (!targetCheckedOut)
            {
                return false;
            }

            if (isPilot || isAboard)
            {
                return true;
            }

            return InterestPolicy.InRange(peerPosition, hullPosition, radiusMetres);
        }
    }
}

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Selects when a player 190602 transform is a safe source for world-space
    /// interest. While aboard, 1073 plus the authoritative hull pose owns this
    /// job; accepting both sources makes nearest-island classification alternate
    /// across a zone boundary. Once the canonical aboard tracker confirms the
    /// player is off the ship, 190602 follows ordinary on-foot movement.
    /// </summary>
    public static class PlayerWorldInterestPolicy
    {
        public static bool MayUseTransform190602(FallVerdict fallVerdict, bool isAboard) =>
            fallVerdict != FallVerdict.Parented && !isAboard;
    }
}

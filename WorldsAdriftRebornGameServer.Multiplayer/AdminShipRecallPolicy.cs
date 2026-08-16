namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Safe and visually predictable operator recall placement.
    ///
    /// A recalled ship appears directly over the selected player's current
    /// world-interest position. Thirty metres clears tall built frames and most
    /// island decoration while remaining close enough for immediate checkout
    /// and easy visual confirmation. There is deliberately no lateral offset:
    /// "above player" must not secretly mean eight metres to one side.
    /// </summary>
    public static class AdminShipRecallPolicy
    {
        public const double HeightAbovePlayerMetres = 30.0;

        public static FixedPointPosition DestinationAbove(FixedPointPosition playerPosition)
        {
            return new FixedPointPosition(
                playerPosition.X,
                playerPosition.Y + (long)(HeightAbovePlayerMetres * FixedPointPosition.UnitsPerMetre),
                playerPosition.Z);
        }
    }
}

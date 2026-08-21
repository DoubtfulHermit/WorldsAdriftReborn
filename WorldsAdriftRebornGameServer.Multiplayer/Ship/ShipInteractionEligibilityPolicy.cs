namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// Server-side physical eligibility for a mounted ship-part interaction.
    /// The client uses the same advertised radius to decide whether to show its
    /// prompt, but that prompt is presentation rather than authority: a modified
    /// client can name any entity id in 1211. The server therefore requires the
    /// sender to own the player entity, to have received the exact target entity,
    /// and to remain inside the prompt's world-space radius.
    /// </summary>
    public static class ShipInteractionEligibilityPolicy
    {
        public static bool Allows(bool ownsPlayer, bool targetCheckedOut,
            bool playerPositionKnown, FixedPointPosition playerPosition,
            FixedPointPosition targetPosition, double radiusMetres)
        {
            if (!ownsPlayer || !targetCheckedOut || !playerPositionKnown
                || !double.IsFinite(radiusMetres) || radiusMetres <= 0.0)
            {
                return false;
            }

            double radiusSquared = radiusMetres * radiusMetres;
            return ResourceInterestPolicy.DistanceSquared(playerPosition, targetPosition)
                <= radiusSquared;
        }
    }
}

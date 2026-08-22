namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// Server-side eligibility for a mounted ship-part interaction.
    ///
    /// RECOVERED CLIENT CONTRACT: PlayerLookingAt only exposes an interactive
    /// object when <c>distance + 0.5 &lt; InteractionEntry.radius</c>. After any
    /// hold, InteractAgentObserver checks again with 2 m leeway before it emits
    /// the 1211 InteractWithObject event. That is the shipped client's intended
    /// interaction geometry; no recovered client code describes a second exact
    /// server-radius test.
    ///
    /// ENDPOINTS: both checks use the interactive entity's transform position and
    /// the player's transform position - not the raycast hit point or collider's
    /// closest point. The 40 m raycast only discovers a collider and is not an
    /// interaction allowance.
    ///
    /// WAREBORN SECURITY POLICY: the sender must own the player, the exact target
    /// must be checked out to that peer, the reconstructed player position must be
    /// known, and the event must fall inside the recovered completion envelope.
    /// Aboard state does not widen reach. This is the smallest server revalidation
    /// available because 1211 carries target and verb but no unforgeable proof that
    /// the authentic client displayed a prompt.
    /// </summary>
    public static class ShipInteractionEligibilityPolicy
    {
        public const double ClientCompletionLeewayMetres = 2.0;
        public const double ClientPlayerOriginBiasMetres = 0.5;

        public static bool Allows(bool ownsPlayer, bool targetCheckedOut,
            bool playerPositionKnown, FixedPointPosition playerPosition,
            FixedPointPosition targetPosition, double radiusMetres)
        {
            if (!ownsPlayer || !targetCheckedOut
                || !double.IsFinite(radiusMetres) || radiusMetres <= 0.0)
            {
                return false;
            }

            if (!playerPositionKnown)
            {
                return false;
            }

            double maximumReach = radiusMetres
                + ClientCompletionLeewayMetres
                - ClientPlayerOriginBiasMetres;
            double radiusSquared = maximumReach * maximumReach;
            return ResourceInterestPolicy.DistanceSquared(playerPosition, targetPosition)
                < radiusSquared;
        }
    }
}

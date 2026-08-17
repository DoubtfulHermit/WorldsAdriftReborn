namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Keeps a remote avatar in one coordinate frame while the canonical aboard
    /// tracker is deliberately bridging a short moving-deck contact gap.
    /// </summary>
    public static class AboardRelayPolicy
    {
        /// <summary>
        /// A raw client sample can briefly say Invalid/zero between adjacent ship
        /// colliders. While the accumulated tracker still says the player is aboard,
        /// forwarding that edge would make PlayerVisualizer blend from its ship-local
        /// pose toward a stale world pose. Hold only the coordinate-frame fields; bone,
        /// grounded and other state in the update remain eligible for relay.
        /// </summary>
        public static bool HoldRelativeFrame(
            bool canonicalAboard,
            bool relativeToChanged,
            long relativeTo,
            bool relativeBiasChanged,
            float relativeBias)
        {
            if (!canonicalAboard) return false;
            return (relativeToChanged && relativeTo <= 0)
                || (relativeBiasChanged && relativeBias <= AboardPolicy.AttachedBiasThreshold);
        }

        /// <summary>
        /// The grace deadline commonly matures on a position-only tick. The raw
        /// Invalid edge was intentionally withheld earlier, so that tick must
        /// synthesize the confirmed detach or remote readers would stay attached.
        /// </summary>
        public static bool SynthesizeConfirmedDetach(
            AboardChange change, bool relativeToChanged) =>
            change == AboardChange.Disembarked && !relativeToChanged;
    }
}

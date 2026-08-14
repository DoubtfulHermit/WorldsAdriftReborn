namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Keeps the 20 Hz avatar relay while guaranteeing an aboard avatar sample
    /// immediately after every authoritative ship-domain frame.
    /// </summary>
    public static class DomainAlignedRelayPolicy
    {
        public static bool ShouldEmitSender(bool regularCadenceDue,
            bool senderAboardEmittedDomain) =>
            regularCadenceDue || senderAboardEmittedDomain;
    }
}

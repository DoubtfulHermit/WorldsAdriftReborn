namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// The identity of one accepted per-hull simulation frame. FixedStep is the hull's
    /// FixedFlightClock step number; AuthorityGeneration is ShipDomain.Generation.Value.
    /// Minted only by the hull's authority adapter; everyone else may only read/compare.
    /// Stale or mismatched stamps fail closed: consumers reject, they never upgrade old
    /// evidence to the current frame.
    /// </summary>
    public readonly record struct FlightAuthorityStamp(long FixedStep, long AuthorityGeneration)
    {
        public bool IsValid => FixedStep >= 0 && AuthorityGeneration > 0;

        /// <summary>Strictly-newer-in-same-generation acceptance used by every consumer.</summary>
        public bool SupersedesWithinGeneration(FlightAuthorityStamp last) =>
            IsValid && AuthorityGeneration == last.AuthorityGeneration && FixedStep > last.FixedStep;
    }
}

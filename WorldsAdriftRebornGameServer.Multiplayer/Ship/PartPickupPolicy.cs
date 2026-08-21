namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    public enum PartPickupReject
    {
        Accept,
        UnknownPart,
        AlreadyCarried,
        MountedShipNotOwned,
    }

    /// <summary>
    /// Server authority gate for the client-authored 1239 PickedUpEvent. Retail's
    /// placement walk already checks ship ownership, but a modified client can emit
    /// the event directly; no ledger mutation may happen before this mirror passes.
    /// Loose parts remain common world objects because no recovered rule makes their
    /// creator a permanent exclusive owner.
    /// </summary>
    public static class PartPickupPolicy
    {
        public static PartPickupReject Evaluate(bool isKnownPart,
            bool carriedByAnotherPlayer, bool isMounted, bool requesterOwnsMountedShip)
        {
            if (!isKnownPart) return PartPickupReject.UnknownPart;
            if (carriedByAnotherPlayer) return PartPickupReject.AlreadyCarried;
            if (isMounted && !requesterOwnsMountedShip) return PartPickupReject.MountedShipNotOwned;
            return PartPickupReject.Accept;
        }
    }
}

namespace WorldsAdriftRebornGameServer.Multiplayer.Placement
{
    /// <summary>
    /// The client-side completion gate for station pickup. Sending PickUp is only
    /// a REQUEST; the station may disappear only after the server answers by
    /// changing that exact entity's InteractiveState to unavailable. This keeps a
    /// rejected pickup (wrong owner, busy station, full inventory) visible.
    ///
    /// Kept pure and linked into the net35 client so the authoritative-response
    /// rule is unit tested without Unity.
    /// </summary>
    public static class StationPickupVisibilityPolicy
    {
        public static bool ShouldHide(
            long pendingStationEntityId,
            long observedStationEntityId,
            bool interactionEnabled)
        {
            return pendingStationEntityId > 0
                && observedStationEntityId == pendingStationEntityId
                && !interactionEnabled;
        }
    }
}

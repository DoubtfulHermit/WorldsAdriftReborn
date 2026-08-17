namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    public enum TerrainDestinationStatus
    {
        Disabled,
        Unknown,
        Queued,
        WaitingForAsset,
        Ready,
    }

    /// <summary>
    /// Pure per-peer terrain truth. Keeping this separate from wire queues makes
    /// reconnect cleanup and peer isolation directly testable without ENet/Unity.
    /// </summary>
    public sealed class IslandTerrainPeerLedger<TPeer> where TPeer : notnull
    {
        private sealed class Entry
        {
            public readonly HashSet<long> Loaded = new();
            public IslandId? RequestedDestination;
        }

        private readonly Dictionary<TPeer, Entry> _peers = new();

        public void NotePeer(TPeer peer) => EntryFor(peer);

        public void NoteLoaded(TPeer peer, long entityId) => EntryFor(peer).Loaded.Add(entityId);

        public void NoteRemoved(TPeer peer, long entityId)
        {
            if (_peers.TryGetValue(peer, out Entry? entry)) entry.Loaded.Remove(entityId);
        }

        public bool IsLoaded(TPeer peer, long entityId) =>
            _peers.TryGetValue(peer, out Entry? entry) && entry.Loaded.Contains(entityId);

        public ISet<long> LoadedFor(TPeer peer) => EntryFor(peer).Loaded;

        public IslandId? RequestedDestination(TPeer peer) =>
            _peers.TryGetValue(peer, out Entry? entry) ? entry.RequestedDestination : null;

        public void ClearDestination(TPeer peer)
        {
            if (_peers.TryGetValue(peer, out Entry? entry)) entry.RequestedDestination = null;
        }

        /// <summary>
        /// A server-issued teleport has been proved at its destination. The
        /// request is no longer a reason to retain the previous checkout, even
        /// when the client omits the sparse 1073 relative-to acknowledgement.
        /// </summary>
        public void ConfirmTeleportLanding(TPeer peer)
        {
            EntryFor(peer).RequestedDestination = null;
        }

        public TerrainDestinationStatus RequestDestination(
            TPeer peer,
            IslandId islandId,
            IslandId unconditionalIsland,
            IReadOnlyDictionary<IslandId, long> managedEntityByIsland,
            bool enabled,
            bool assetWaiting)
        {
            if (!enabled) return TerrainDestinationStatus.Disabled;
            if (islandId == unconditionalIsland) return TerrainDestinationStatus.Ready;
            if (!managedEntityByIsland.TryGetValue(islandId, out long entityId))
                return TerrainDestinationStatus.Unknown;
            Entry entry = EntryFor(peer);
            entry.RequestedDestination = islandId;
            if (entry.Loaded.Contains(entityId)) return TerrainDestinationStatus.Ready;
            return assetWaiting
                ? TerrainDestinationStatus.WaitingForAsset
                : TerrainDestinationStatus.Queued;
        }

        public bool Forget(TPeer peer) => _peers.Remove(peer);

        public void Clear() => _peers.Clear();

        public bool IsTracking(TPeer peer) => _peers.ContainsKey(peer);

        private Entry EntryFor(TPeer peer)
        {
            if (_peers.TryGetValue(peer, out Entry? entry)) return entry;
            entry = new Entry();
            _peers.Add(peer, entry);
            return entry;
        }
    }
}

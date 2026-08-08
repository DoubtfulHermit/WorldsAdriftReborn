namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Hands out entity ids, and owns the one id that must be identical on every
    /// client: the island.
    ///
    /// Cross-client references resolve BY ENTITY ID on the receiving client. A
    /// remote player's rig positions itself by parenting - the publisher sends
    /// TransformState with Parent = its island's entity id, and the receiver
    /// looks that id up locally. With a per-client island id (0 for one client,
    /// 2 for the next) the lookup found nothing and every remote avatar stayed
    /// frozen at the seed position, ~90km off-island.
    ///
    /// Ids are never reused. A departed player's id must not come back around to
    /// a new player while other clients still hold stale references to it.
    /// </summary>
    public sealed class EntityIdAllocator
    {
        private long _next;
        private long? _islandEntityId;

        /// <summary>
        /// A fresh entity id. Monotonic from 0 and never reused, including after
        /// the owning peer disconnects.
        /// </summary>
        public long Next()
        {
            return _next++;
        }

        /// <summary>
        /// The single island entity id shared by EVERY client. Allocated from the
        /// same counter on first use and then constant for the process lifetime.
        /// </summary>
        public long SharedIslandEntityId
        {
            get
            {
                if (_islandEntityId == null)
                {
                    _islandEntityId = Next();
                }
                return _islandEntityId.Value;
            }
        }

        /// <summary>Whether the island id has been handed out yet.</summary>
        public bool IslandAllocated => _islandEntityId != null;
    }
}

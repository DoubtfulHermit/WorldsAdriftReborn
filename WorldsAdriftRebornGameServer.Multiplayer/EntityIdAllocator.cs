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
        /// <summary>
        /// The shared-id key the island is filed under. It is a key rather than a
        /// dedicated field because the island is no longer the only thing every
        /// client must agree on: any world entity the server spawns (a tree, a
        /// ship hull) is one object seen by N clients, so it needs exactly the
        /// same allocate-once-then-constant treatment.
        /// </summary>
        public const string IslandKey = "island";

        private long _next;
        private readonly Dictionary<string, long> _shared = new Dictionary<string, long>();

        /// <summary>
        /// A fresh entity id. Monotonic from 0 and never reused, including after
        /// the owning peer disconnects.
        /// </summary>
        public long Next()
        {
            return _next++;
        }

        /// <summary>
        /// The entity id shared by EVERY client for one named world object.
        /// Allocated from the same counter on first use and then constant for the
        /// process lifetime.
        ///
        /// Keyed, and allocate-on-read, for the same reason the island was:
        /// cross-client references resolve BY ENTITY ID on the RECEIVING client,
        /// so two clients holding different ids for the same object is a silent
        /// failure (the lookup finds nothing and nothing is reported).
        /// </summary>
        public long SharedEntityId(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("a shared entity id needs a stable key", nameof(key));
            }

            if (!_shared.TryGetValue(key, out long id))
            {
                id = Next();
                _shared[key] = id;
            }
            return id;
        }

        /// <summary>
        /// Whether a shared id has been handed out yet. Asking must never be what
        /// allocates it - see <see cref="SharedEntityId"/> - or the answer would
        /// depend on who asked first.
        /// </summary>
        public bool IsAllocated(string key) => _shared.ContainsKey(key);

        /// <summary>
        /// The single island entity id shared by EVERY client. The degenerate
        /// case of <see cref="SharedEntityId"/>, kept because the island is named
        /// explicitly in the spawn backbone and in 1041 IslandState.
        /// </summary>
        public long SharedIslandEntityId => SharedEntityId(IslandKey);

        /// <summary>Whether the island id has been handed out yet.</summary>
        public bool IslandAllocated => IsAllocated(IslandKey);
    }
}

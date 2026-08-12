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

        /// <summary>
        /// The lowest id ever handed out. It is 1, not 0, because on the CLIENT
        /// <c>Improbable.Worker.EntityId.IsValid()</c> is <c>Id &gt; 0</c>: entity id
        /// 0 is an INVALID id there, indistinguishable from a default-constructed /
        /// unset <c>EntityId</c>, and <c>InvalidEntityId</c> is -1. A real entity that
        /// lands on id 0 collides with the client's default/invalid slot - a
        /// cross-client parent reference to it resolves to nothing, and the entity
        /// store treats it as the same key as any other default id, so its components
        /// get AddComponent'd on top of one that "already exists" and the SpatialOS
        /// store throws. This bit the boot-restored shipyard: it was the FIRST
        /// SharedEntityId the server allocated (restore runs before any client
        /// connects, so before the island), landed on 0, and every joining client
        /// crashed re-seeding "entity 0". Basing the counter at 1 keeps id 0 reserved
        /// for the sentinel it already is on the wire.
        /// </summary>
        public const long FirstEntityId = 1;

        private long _next = FirstEntityId;
        private readonly Dictionary<string, long> _shared = new Dictionary<string, long>();

        /// <summary>
        /// A fresh entity id. Monotonic from <see cref="FirstEntityId"/> (1, never 0 -
        /// id 0 is invalid on the client) and never reused, including after the owning
        /// peer disconnects.
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
        /// The shared id for a key IF it has already been handed out, without
        /// allocating one otherwise. The read-only counterpart to
        /// <see cref="SharedEntityId"/>, for code that needs to NAME an
        /// already-spawned world entity (the helm's 8066 seed naming its hull) but
        /// must not be what brings that entity into being by asking.
        /// </summary>
        public bool TryGetSharedEntityId(string key, out long id) => _shared.TryGetValue(key, out id);

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

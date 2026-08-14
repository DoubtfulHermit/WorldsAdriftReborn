namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Every non-player thing this server puts in the world, and the one entity
    /// id each of them is known by on EVERY client.
    ///
    /// This is the seam the whole "spawn something that is not a player" job
    /// hangs off. A caller registers a <see cref="WorldEntity"/> once at startup
    /// and gets three things for free:
    ///
    /// 1. <see cref="SpawnPlan"/> puts an asset request and an AddEntityOp for it
    ///    into every joining client's handshake, in the right place.
    /// 2. <see cref="SpawnPolicy.TransformSeedFor(long, WorldEntityRegistry)"/>
    ///    hands the component serializer that entity's OWN 190602 position, so
    ///    the serializer no longer has to know what kinds of thing exist.
    /// 3. The entity id is allocated exactly once and is the same number on every
    ///    client, which is not a nicety: cross-client references resolve BY ID on
    ///    the receiving client, and a mismatch resolves to nothing, silently.
    ///
    /// Pure: no ENet, no Improbable types, no game install.
    ///
    /// NOT THREAD-SAFE, deliberately. The server is a single poll loop; adding a
    /// lock here would imply it is safe to spawn from somewhere else, which it is
    /// not - <see cref="EntityIdAllocator"/> is not thread-safe either.
    /// </summary>
    public sealed class WorldEntityRegistry
    {
        private readonly EntityIdAllocator _ids;
        private readonly List<WorldEntity> _registrations = new List<WorldEntity>();
        private readonly Dictionary<string, WorldEntity> _byKey = new Dictionary<string, WorldEntity>();
        private readonly Dictionary<long, WorldEntity> _byEntityId = new Dictionary<long, WorldEntity>();

        public WorldEntityRegistry(EntityIdAllocator ids)
        {
            _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        }

        /// <summary>
        /// Declares a world entity. Registration is pure bookkeeping: it allocates
        /// no id and sends nothing, so registering something the world never
        /// reaches costs nothing.
        ///
        /// Duplicate keys throw rather than overwrite. Two registrations under one
        /// key would share an entity id, which means the second one's AddEntityOp
        /// re-uses the first one's id and the client either ignores it or replaces
        /// a live entity - both silent.
        /// </summary>
        public WorldEntity Register(WorldEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }
            if (_byKey.ContainsKey(entity.Key))
            {
                throw new ArgumentException(
                    "a world entity is already registered under the key '" + entity.Key
                    + "'; two registrations under one key would share an entity id", nameof(entity));
            }

            _byKey.Add(entity.Key, entity);
            _registrations.Add(entity);
            return entity;
        }

        /// <summary>
        /// Retires a live registration without reusing its allocated id. It disappears
        /// from future spawn plans and all registry lookups; the allocator deliberately
        /// keeps the old key/id reservation so a stale packet can never name a new object.
        /// </summary>
        public bool Unregister(long entityId)
        {
            if (!_byEntityId.TryGetValue(entityId, out WorldEntity? entity)) return false;
            _byEntityId.Remove(entityId);
            _byKey.Remove(entity.Key);
            _registrations.Remove(entity);
            return true;
        }

        /// <summary>Updates a surviving entity's parentless seed pose, preserving key and id.</summary>
        public bool Relocate(long entityId, FixedPointPosition position, uint packedRotation)
        {
            if (!_byEntityId.TryGetValue(entityId, out WorldEntity? old)) return false;
            var replacement = new WorldEntity(old.Key, old.AssetName, old.AssetContext, position,
                old.SeedComponents, old.Order, packedRotation);
            int index = _registrations.IndexOf(old);
            _registrations[index] = replacement;
            _byKey[old.Key] = replacement;
            _byEntityId[entityId] = replacement;
            return true;
        }

        /// <summary>Everything registered, in registration order.</summary>
        public IReadOnlyList<WorldEntity> Registrations => _registrations;

        /// <summary>
        /// The bolted ship parts that are actually registered, in registration order -
        /// the deck, the helm, and the opt-in engine/sail, but never the hull itself.
        /// This is the "which parts" half of the wake-the-parts policy: the heartbeat
        /// re-publishes each of these entities' 190602 so its follow-visualizer never
        /// sleeps while the hull moves (see Game.ShipPartMotionService). Empty when the
        /// deck is switched off and no parts are present.
        /// </summary>
        public IReadOnlyList<WorldEntity> BoltedParts()
        {
            List<WorldEntity> parts = new List<WorldEntity>();
            foreach (WorldEntity entity in _registrations)
            {
                if (WorldEntities.IsBoltedPartKey(entity.Key))
                {
                    parts.Add(entity);
                }
            }
            return parts;
        }

        /// <summary>Those registered to spawn at a given point in the handshake, in registration order.</summary>
        public IReadOnlyList<WorldEntity> InOrder(SpawnOrder order)
        {
            List<WorldEntity> matching = new List<WorldEntity>();
            foreach (WorldEntity entity in _registrations)
            {
                if (entity.Order == order)
                {
                    matching.Add(entity);
                }
            }
            return matching;
        }

        /// <summary>The registration under a key, or null.</summary>
        public WorldEntity? ByKey(string key)
        {
            return key != null && _byKey.TryGetValue(key, out WorldEntity? entity) ? entity : null;
        }

        /// <summary>
        /// The entity id every client knows this registration by. ALLOCATES on
        /// first call - that is the point - so call it from the code that actually
        /// sends the AddEntityOp, never from code that is merely asking a
        /// question. <see cref="ByEntityId"/> is the question-asking direction.
        /// </summary>
        public long EntityIdFor(WorldEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }
            if (!_byKey.ContainsKey(entity.Key))
            {
                throw new ArgumentException(
                    "world entity '" + entity.Key + "' was never registered", nameof(entity));
            }

            long id = _ids.SharedEntityId(entity.Key);
            _byEntityId[id] = entity;
            return id;
        }

        /// <summary>Whether this registration's id has been handed out yet. Never allocates.</summary>
        public bool IsBound(WorldEntity entity)
        {
            return entity != null && _ids.IsAllocated(entity.Key);
        }

        /// <summary>
        /// The entity id a registration is known by IF it has already been handed
        /// out, or null. NEVER allocates - the question-asking counterpart to
        /// <see cref="EntityIdFor"/>, for code that must NAME an already-spawned
        /// entity by key (the helm's 8066 seed pointing at its hull) without being
        /// what spawns it. Null means either the key is not registered or its
        /// AddEntityOp has not run yet.
        /// </summary>
        public long? BoundEntityIdFor(string key)
        {
            return key != null && _byKey.ContainsKey(key) && _ids.TryGetSharedEntityId(key, out long id)
                ? id
                : (long?)null;
        }

        /// <summary>
        /// Which registration an entity id belongs to, or null if the id is not a
        /// world entity's - which is what a player avatar's id looks like from
        /// here, and also what ANY id looks like before its AddEntityOp.
        ///
        /// Never allocates. Asking "is this the island?" must not be what creates
        /// the island, or the answer would depend on which entity happened to be
        /// serialized first, and entity 0 would be mistaken for it.
        /// </summary>
        public WorldEntity? ByEntityId(long entityId)
        {
            return _byEntityId.TryGetValue(entityId, out WorldEntity? entity) ? entity : null;
        }

        /// <summary>
        /// The 190602 TransformState.localPosition seed for one entity. THE
        /// question the component serializer asks, and the reason this type
        /// exists.
        ///
        /// A registered world entity gets ITS OWN position; everything else is a
        /// player avatar and gets the spawn point. That is the whole
        /// generalisation - the serializer no longer has to know what kinds of
        /// thing exist, only how to ask - and it replaces a two-branch ternary
        /// that could only ever answer "island" or "player".
        ///
        /// Before <see cref="SpawnPolicy"/> existed the serializer switched on
        /// component id alone and every entity got the same transform. That was
        /// survivable only while the island sat at the world origin; with Haven -
        /// ONE asset placed at TWELVE world positions - there is no default that
        /// is right for anybody.
        /// </summary>
        public FixedPointPosition TransformSeedFor(long entityId)
        {
            WorldEntity? entity = ByEntityId(entityId);
            return entity != null ? entity.Position : SpawnPolicy.PlayerSpawnPosition;
        }

        /// <summary>
        /// The 190602 localRotation seed for one entity, as a packed
        /// <c>Quaternion32</c> uint. The rotation counterpart to
        /// <see cref="TransformSeedFor"/>: a registered world entity gets its own
        /// packed facing; everything else (players, unregistered ids) gets the
        /// identity SENTINEL 1023, which is exactly the value the 190602 seed
        /// hard-coded before rotation was a field - so nothing that does not set a
        /// rotation changes by a single bit.
        /// </summary>
        public uint RotationSeedFor(long entityId)
        {
            WorldEntity? entity = ByEntityId(entityId);
            return entity != null ? entity.PackedRotation : Placement.Quaternion32Packing.Identity;
        }

        /// <summary>
        /// Which kind of entity a seed is being fabricated for, given everything
        /// this server has put in the world. For logs and for the handful of
        /// component branches that are genuinely island-specific (1041
        /// IslandState); the POSITION comes from
        /// <see cref="TransformSeedFor"/>, not from the kind.
        ///
        /// Anything unregistered is a player avatar - the peer's own or a mirrored
        /// remote - which is also, correctly, what every id looks like before its
        /// AddEntityOp has run.
        /// </summary>
        public SeededEntityKind KindOf(long entityId)
        {
            WorldEntity? entity = ByEntityId(entityId);
            if (entity == null)
            {
                return SeededEntityKind.Player;
            }
            return entity.Key == WorldEntities.IslandKey
                ? SeededEntityKind.Island
                : SeededEntityKind.World;
        }

        /// <summary>
        /// What an entity id is, for a log line. Names the REGISTRATION KEY, not
        /// just the kind: with an open set of world entities "World" on its own
        /// stops being enough to tell two log lines apart.
        /// </summary>
        public string Describe(long entityId)
        {
            WorldEntity? entity = ByEntityId(entityId);
            return entity == null
                ? SeededEntityKind.Player.ToString()
                : KindOf(entityId) + " '" + entity.Key + "' " + entity.AssetName;
        }
    }
}

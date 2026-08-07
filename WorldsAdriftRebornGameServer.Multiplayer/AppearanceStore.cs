namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Remembers each player entity's customisation map (the appearance data the
    /// owning client published) so that mirrors seeded AFTER the publish carry
    /// the real look instead of defaults.
    ///
    /// Pure storage: ownership validation (only the sender's own entity may be
    /// recorded, per rule 6 in docs/multiplayer.md) is the caller's job, because
    /// this project stays free of ENet and game types.
    /// </summary>
    public sealed class AppearanceStore
    {
        private readonly Dictionary<long, IReadOnlyDictionary<string, string>> _byEntity = new();

        /// <summary>Number of entities with recorded appearance.</summary>
        public int Count => _byEntity.Count;

        /// <summary>
        /// Records (or replaces) an entity's customisation map. A defensive copy
        /// is taken so later mutation of the source cannot corrupt the store.
        /// </summary>
        public void Record(long entityId, IReadOnlyDictionary<string, string> customisation)
        {
            if (customisation == null)
            {
                return;
            }

            _byEntity[entityId] = new Dictionary<string, string>(customisation.ToDictionary(p => p.Key, p => p.Value));
        }

        /// <summary>The recorded map for an entity, or null if never published.</summary>
        public IReadOnlyDictionary<string, string>? Get(long entityId)
        {
            return _byEntity.TryGetValue(entityId, out IReadOnlyDictionary<string, string>? map) ? map : null;
        }

        /// <summary>Drops an entity's record (e.g. when its player disconnects).</summary>
        public void Forget(long entityId)
        {
            _byEntity.Remove(entityId);
        }
    }
}

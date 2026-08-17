namespace WorldsAdriftRebornGameServer.Multiplayer.Regions
{
    /// <summary>
    /// Pure Phase 3 candidate-selection boundary over the world directory.
    /// Spatial policy remains with each consumer; this query answers only which
    /// offered entities belong to a region, while preserving caller order.
    /// </summary>
    public sealed class RegionInterestQuery
    {
        private readonly Dictionary<string, WorldOwner> _ownersByKey;

        public RegionInterestQuery(WorldDirectory directory)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));

            _ownersByKey = directory.Entries.ToDictionary(
                entry => entry.Entity.Key,
                entry => entry.Owner,
                StringComparer.Ordinal);
        }

        /// <summary>
        /// Adds a region-owned entity created after the immutable boot directory
        /// was built. Re-registering the same ownership is idempotent; changing
        /// ownership requires a new directory snapshot and is rejected here.
        /// </summary>
        public void Register(WorldEntity entity, RegionId regionId)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            WorldOwner owner = WorldOwner.ForRegion(regionId);
            if (_ownersByKey.TryGetValue(entity.Key, out WorldOwner existing))
            {
                if (existing != owner)
                {
                    throw new InvalidOperationException(
                        "interest ownership for '" + entity.Key + "' is already "
                        + existing + "; cannot change it to " + owner);
                }
                return;
            }
            _ownersByKey.Add(entity.Key, owner);
        }

        /// <summary>
        /// Selects entities owned by <paramref name="regionId"/>, plus stable
        /// keys a lifecycle consumer must retain for one more reconciliation
        /// (for example, already-loaded resources that now need unloading).
        /// The offered sequence order is retained exactly.
        /// </summary>
        public IReadOnlyList<WorldEntity> Candidates(
            RegionId regionId,
            IEnumerable<WorldEntity> offered,
            ISet<string>? retainedKeys = null)
        {
            if (offered == null) throw new ArgumentNullException(nameof(offered));
            WorldOwner regionOwner = WorldOwner.ForRegion(regionId);
            List<WorldEntity> result = new();
            foreach (WorldEntity entity in offered)
            {
                if (!_ownersByKey.TryGetValue(entity.Key, out WorldOwner owner))
                {
                    throw new InvalidOperationException(
                        "interest candidate '" + entity.Key
                        + "' has no world-directory ownership");
                }
                if (owner == regionOwner || (retainedKeys?.Contains(entity.Key) ?? false))
                {
                    result.Add(entity);
                }
            }
            return result;
        }
    }
}

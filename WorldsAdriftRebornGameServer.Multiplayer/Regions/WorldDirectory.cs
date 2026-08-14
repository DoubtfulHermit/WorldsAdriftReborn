using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;

namespace WorldsAdriftRebornGameServer.Multiplayer.Regions
{
    /// <summary>One immutable, read-only classification of a world registration.</summary>
    public sealed class WorldDirectoryEntry
    {
        internal WorldDirectoryEntry(WorldEntity entity, WorldOwner owner)
        {
            Entity = entity;
            Owner = owner;
        }

        public WorldEntity Entity { get; }
        public WorldOwner Owner { get; }
    }

    /// <summary>
    /// A diagnostic directory over the world registry. It describes ownership
    /// boundaries but does not participate in spawn, interest, persistence or
    /// networking decisions.
    /// </summary>
    public sealed class WorldDirectory
    {
        private readonly Dictionary<string, WorldDirectoryEntry> _byKey;
        private readonly IReadOnlyList<WorldDirectoryEntry> _entries;

        private WorldDirectory(
            Dictionary<string, WorldDirectoryEntry> byKey,
            IReadOnlyList<WorldDirectoryEntry> entries)
        {
            _byKey = byKey;
            _entries = entries;
        }

        public IReadOnlyList<WorldDirectoryEntry> Entries => _entries;

        public WorldDirectoryEntry? ByEntityKey(string? key) =>
            key != null && _byKey.TryGetValue(key, out WorldDirectoryEntry? entry) ? entry : null;

        public IReadOnlyList<WorldDirectoryEntry> OwnedBy(WorldOwner owner) =>
            _entries.Where(entry => entry.Owner == owner).ToArray();

        /// <param name="shipRootOverrides">
        /// Stable entity-key to stable hull-key mappings for mounted loose parts.
        /// Static and built hull/deck keys are derived without overrides.
        /// </param>
        public static WorldDirectory Build(
            WorldEntityRegistry entities,
            IslandRegistry islands,
            RegionRegistry regions,
            IReadOnlyDictionary<string, string>? shipRootOverrides = null)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            if (islands == null) throw new ArgumentNullException(nameof(islands));
            if (regions == null) throw new ArgumentNullException(nameof(regions));

            IReadOnlyDictionary<string, string> overrides = shipRootOverrides
                ?? new Dictionary<string, string>();
            var byKey = new Dictionary<string, WorldDirectoryEntry>(StringComparer.Ordinal);
            var entries = new List<WorldDirectoryEntry>(entities.Registrations.Count);

            foreach (KeyValuePair<string, string> entry in overrides)
            {
                if (entities.ByKey(entry.Key) == null)
                    throw new ArgumentException("ship override entity '" + entry.Key + "' is not registered", nameof(shipRootOverrides));
                if (entities.ByKey(entry.Value) == null)
                    throw new ArgumentException("ship override root '" + entry.Value + "' is not registered", nameof(shipRootOverrides));
            }

            foreach (WorldEntity entity in entities.Registrations)
            {
                WorldOwner owner = Classify(entity, islands, regions, overrides);
                if (owner.Kind == WorldOwnerKind.Ship && entities.ByKey(owner.Id) == null)
                {
                    throw new InvalidOperationException(
                        "ship member '" + entity.Key + "' names missing hull root '" + owner.Id + "'");
                }
                var entry = new WorldDirectoryEntry(entity, owner);
                byKey.Add(entity.Key, entry);
                entries.Add(entry);
            }

            entries.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.Entity.Key, right.Entity.Key));
            return new WorldDirectory(byKey, entries.AsReadOnly());
        }

        private static WorldOwner Classify(
            WorldEntity entity,
            IslandRegistry islands,
            RegionRegistry regions,
            IReadOnlyDictionary<string, string> overrides)
        {
            if (string.Equals(entity.Key, WorldEntities.GlobalEntityKey, StringComparison.Ordinal))
                return WorldOwner.Global;

            if (overrides.TryGetValue(entity.Key, out string? overrideRoot))
                return WorldOwner.ForShip(overrideRoot);

            string? derivedRoot = ShipRootKeyFor(entity.Key);
            if (derivedRoot != null)
                return WorldOwner.ForShip(derivedRoot);

            IslandDefinition? terrainIsland = islands.ByWorldEntityKey(entity.Key);
            IslandDefinition island = terrainIsland ?? NearestIsland(entity.Position, islands);
            RegionDefinition region = regions.ByIsland(island.Id)
                ?? throw new InvalidOperationException(
                    "island '" + island.Id + "' has no registered region owner");
            return WorldOwner.ForRegion(region.Id);
        }

        internal static string? ShipRootKeyFor(string? entityKey)
        {
            if (entityKey == null) return null;

            if (string.Equals(entityKey, WorldEntities.ShipFrameKey, StringComparison.Ordinal)
                || WorldEntities.IsBoltedPartKey(entityKey))
                return WorldEntities.ShipFrameKey;

            if (!BuiltShipPlacement.IsBuiltShipEntityKey(entityKey)) return null;
            if (entityKey.EndsWith(":hull", StringComparison.Ordinal)) return entityKey;
            return BuiltShipPlacement.HullKeyForDeckKey(entityKey);
        }

        private static IslandDefinition NearestIsland(
            FixedPointPosition position,
            IslandRegistry islands)
        {
            IslandDefinition? nearest = null;
            decimal nearestDistance = decimal.MaxValue;
            foreach (IslandDefinition island in islands.All)
            {
                decimal dx = position.X - island.GlobalOrigin.X;
                decimal dy = position.Y - island.GlobalOrigin.Y;
                decimal dz = position.Z - island.GlobalOrigin.Z;
                decimal distance = dx * dx + dy * dy + dz * dz;
                if (nearest == null || distance < nearestDistance)
                {
                    nearest = island;
                    nearestDistance = distance;
                }
            }

            return nearest ?? throw new InvalidOperationException(
                "world directory cannot classify an entity without any registered islands");
        }
    }
}

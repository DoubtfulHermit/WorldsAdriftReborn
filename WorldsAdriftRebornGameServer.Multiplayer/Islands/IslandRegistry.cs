namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>Startup registry keyed by stable island identity.</summary>
    public sealed class IslandRegistry
    {
        private readonly Dictionary<IslandId, IslandDefinition> _byId = new();

        public IslandDefinition Register(IslandDefinition island)
        {
            if (island == null)
                throw new ArgumentNullException(nameof(island));
            if (_byId.ContainsKey(island.Id))
                throw new ArgumentException("an island is already registered under id '" + island.Id + "'", nameof(island));

            _byId.Add(island.Id, island);
            return island;
        }

        public IslandDefinition? ById(IslandId id) =>
            _byId.TryGetValue(id, out IslandDefinition? island) ? island : null;

        /// <summary>
        /// Resolves the stable registration key carried by a terrain
        /// <see cref="WorldEntity"/> back to its island definition. This is the
        /// bridge from a client's 1073 <c>relativeTo</c> entity id (resolved by the
        /// world registry) to island identity; no boot-time entity id is persisted.
        /// </summary>
        public IslandDefinition? ByWorldEntityKey(string? worldEntityKey)
        {
            if (worldEntityKey == null) return null;
            foreach (IslandDefinition island in _byId.Values)
            {
                if (string.Equals(island.WorldEntityKey, worldEntityKey, StringComparison.Ordinal))
                {
                    return island;
                }
            }
            return null;
        }

        public IslandDefinition Require(IslandId id) =>
            ById(id) ?? throw new KeyNotFoundException("no island is registered under id '" + id + "'");

        /// <summary>All definitions sorted by stable id, independent of registration order.</summary>
        public IReadOnlyList<IslandDefinition> All
        {
            get
            {
                List<IslandDefinition> islands = new(_byId.Values);
                islands.Sort((left, right) => left.Id.CompareTo(right.Id));
                return islands;
            }
        }

        public static IslandRegistry CreateDefault()
        {
            IslandRegistry registry = new();
            registry.Register(IslandCatalog.Haven);
            registry.Register(IslandCatalog.TradesChallenge);
            return registry;
        }

        /// <summary>
        /// Builds the evidenced first-region terrain registry without changing the
        /// production default. Haven and the proven Trades topology remain present;
        /// <paramref name="optionalCount"/> selects a bounded prefix of twelve tier-1
        /// B3 after-player candidates.
        /// </summary>
        public static IslandRegistry CreateWithFirstRegionTerrain(int optionalCount)
        {
            int bounded = FirstRegionTerrainCountPolicy.Clamp(optionalCount);
            IslandRegistry registry = CreateDefault();
            for (int i = 0; i < bounded; i++)
                registry.Register(IslandCatalog.FirstRegionTerrain[i + 1]);
            return registry;
        }

        /// <summary>Builds Haven plus the exact district-selected release terrain.</summary>
        public static IslandRegistry CreateReleaseWorld(string? districts)
        {
            IReadOnlyList<ReleaseIslandRecord> selected =
                ReleaseWorldRolloutPolicy.Select(districts);
            if (selected.Count == 0)
                return CreateDefault();
            IslandRegistry registry = new();
            registry.Register(IslandCatalog.Haven);
            foreach (ReleaseIslandRecord record in selected)
                registry.Register(record.Definition);
            return registry;
        }
    }
}

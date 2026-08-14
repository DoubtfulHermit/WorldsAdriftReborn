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
    }
}

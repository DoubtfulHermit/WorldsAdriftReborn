using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer.Regions
{
    /// <summary>
    /// Deterministic startup topology. Registration validates that every member
    /// is a known island and that no island has two region owners.
    /// </summary>
    public sealed class RegionRegistry
    {
        private readonly IslandRegistry _islands;
        private readonly Dictionary<RegionId, RegionDefinition> _byId = new();
        private readonly Dictionary<IslandId, RegionDefinition> _byIsland = new();

        public RegionRegistry(IslandRegistry islands)
        {
            _islands = islands ?? throw new ArgumentNullException(nameof(islands));
        }

        public RegionDefinition Register(RegionDefinition region)
        {
            if (region == null)
                throw new ArgumentNullException(nameof(region));
            if (_byId.ContainsKey(region.Id))
                throw new ArgumentException(
                    "a region is already registered under id '" + region.Id + "'", nameof(region));

            foreach (IslandId islandId in region.IslandIds)
            {
                _islands.Require(islandId);
                if (_byIsland.TryGetValue(islandId, out RegionDefinition? owner))
                {
                    throw new ArgumentException(
                        "island '" + islandId + "' already belongs to region '" + owner.Id + "'",
                        nameof(region));
                }
            }

            _byId.Add(region.Id, region);
            foreach (IslandId islandId in region.IslandIds)
                _byIsland.Add(islandId, region);

            return region;
        }

        public RegionDefinition? ById(RegionId id) =>
            _byId.TryGetValue(id, out RegionDefinition? region) ? region : null;

        public RegionDefinition? ByIsland(IslandId islandId) =>
            _byIsland.TryGetValue(islandId, out RegionDefinition? region) ? region : null;

        public RegionDefinition Require(RegionId id) =>
            ById(id) ?? throw new KeyNotFoundException("no region is registered under id '" + id + "'");

        public IReadOnlyList<RegionDefinition> All
        {
            get
            {
                List<RegionDefinition> regions = new(_byId.Values);
                regions.Sort((left, right) => left.Id.CompareTo(right.Id));
                return regions;
            }
        }

        public static RegionRegistry CreateDefault(IslandRegistry? islands = null)
        {
            RegionRegistry registry = new(islands ?? IslandRegistry.CreateDefault());
            registry.Register(RegionCatalog.Haven);
            registry.Register(RegionCatalog.TradesChallenge);
            return registry;
        }
    }
}

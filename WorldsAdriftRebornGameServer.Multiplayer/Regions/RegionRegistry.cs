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

        /// <summary>
        /// Creates the existing Haven/Trades regions plus one tier-1 B3 region owning
        /// the selected prefix of optional terrain. The supplied island registry must be the exact result
        /// of <see cref="IslandRegistry.CreateWithFirstRegionTerrain(int)"/> for the
        /// same bounded count; mismatched topology is rejected before registration.
        /// </summary>
        public static RegionRegistry CreateWithFirstRegionTerrain(
            IslandRegistry islands,
            int optionalCount)
        {
            if (islands == null)
                throw new ArgumentNullException(nameof(islands));

            int bounded = FirstRegionTerrainCountPolicy.Clamp(optionalCount);
            IReadOnlyList<IslandDefinition> selected =
                IslandCatalog.FirstRegionTerrain.Skip(1).Take(bounded).ToArray();
            IReadOnlyList<IslandDefinition> expected = new[]
                { IslandCatalog.Haven, IslandCatalog.TradesChallenge }
                .Concat(selected).ToArray();
            IReadOnlyList<IslandDefinition> actual = islands.All;
            if (actual.Count != expected.Count
                || expected.Any(island => !ReferenceEquals(islands.ById(island.Id), island)))
            {
                throw new ArgumentException(
                    "island registry does not match the selected first-region terrain prefix",
                    nameof(islands));
            }

            RegionRegistry registry = new(islands);
            registry.Register(RegionCatalog.Haven);
            registry.Register(RegionCatalog.TradesChallenge);
            if (selected.Count > 0)
                registry.Register(RegionCatalog.FirstTierOne(selected.Select(island => island.Id)));
            return registry;
        }
    }
}

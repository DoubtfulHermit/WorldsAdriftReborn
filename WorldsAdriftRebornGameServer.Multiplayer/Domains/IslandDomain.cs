using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;

namespace WorldsAdriftRebornGameServer.Multiplayer.Domains
{
    /// <summary>One local ownership aggregate for the static state of an island.</summary>
    public sealed class IslandDomain : ILocalSimulationDomain
    {
        private readonly HashSet<long> _entityIds = new();

        public IslandDomain(IslandId islandId, RegionId regionId)
        {
            if (string.IsNullOrWhiteSpace(islandId.Value))
                throw new ArgumentException("island id is required", nameof(islandId));
            if (string.IsNullOrWhiteSpace(regionId.Value))
                throw new ArgumentException("region id is required", nameof(regionId));
            IslandId = islandId;
            RegionId = regionId;
            Id = SimulationDomainId.ForIsland(islandId);
        }

        public SimulationDomainId Id { get; }
        public SimulationDomainKind Kind => SimulationDomainKind.Island;
        public IslandId IslandId { get; }
        public RegionId RegionId { get; }
        public IReadOnlyList<long> EntityIds => _entityIds.OrderBy(x => x).ToArray();

        internal bool AddOwnedEntity(long entityId)
        {
            if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            return _entityIds.Add(entityId);
        }

        internal bool RemoveOwnedEntity(long entityId) => _entityIds.Remove(entityId);
    }
}

using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;

namespace WorldsAdriftRebornGameServer.Multiplayer.Domains
{
    /// <summary>
    /// Ownership-only aggregate for the optional legacy static test ship. It is
    /// deliberately not a ShipDomain and must never enter live flight/interest lifecycle.
    /// </summary>
    public sealed class StaticShipDomain : ILocalSimulationDomain
    {
        private readonly HashSet<long> _members = new();

        public StaticShipDomain(long hullEntityId, IEnumerable<long> memberEntityIds)
        {
            if (hullEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(hullEntityId));
            HullEntityId = hullEntityId;
            Id = SimulationDomainId.ForShip(hullEntityId);
            foreach (long entityId in memberEntityIds ?? throw new ArgumentNullException(nameof(memberEntityIds)))
            {
                if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(memberEntityIds));
                if (entityId != hullEntityId) _members.Add(entityId);
            }
        }

        public SimulationDomainId Id { get; }
        public SimulationDomainKind Kind => SimulationDomainKind.Ship;
        public long HullEntityId { get; }
        public IReadOnlyList<long> EntityIds => new[] { HullEntityId }
            .Concat(_members).OrderBy(x => x).ToArray();

        internal bool AddOwnedEntity(long entityId)
        {
            if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            return entityId != HullEntityId && _members.Add(entityId);
        }

        internal bool RemoveOwnedEntity(long entityId) =>
            entityId != HullEntityId && _members.Remove(entityId);
    }
}

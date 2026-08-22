using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;

namespace WorldsAdriftRebornGameServer.Multiplayer.Domains
{
    public readonly record struct DomainOwnershipSummary(
        int DomainCount, int IslandDomainCount, int ShipDomainCount,
        int OwnedEntityCount, int GlobalEntityCount, IReadOnlyList<long> UnownedEntityIds,
        IReadOnlyList<string> Inconsistencies);

    /// <summary>
    /// Single-process ownership host. It schedules nothing: existing services keep
    /// their exact poll-loop order while this host proves unique, complete domains.
    /// </summary>
    public sealed class LocalDomainHost
    {
        private readonly Dictionary<SimulationDomainId, ILocalSimulationDomain> _domains = new();
        private readonly Dictionary<long, SimulationDomainId> _ownerByEntity = new();
        // This is the inverse of _ownerByEntity, not a second source of truth.
        // Keeping it current makes one-domain refresh/removal proportional to that
        // domain's membership instead of scanning every entity in the world.
        private readonly Dictionary<SimulationDomainId, HashSet<long>> _membersByDomain = new();
        private readonly HashSet<long> _globals = new();

        public IReadOnlyList<ILocalSimulationDomain> Domains =>
            _domains.Values.OrderBy(x => x.Id).ToArray();

        public ILocalSimulationDomain? ById(SimulationDomainId id) =>
            _domains.TryGetValue(id, out ILocalSimulationDomain? domain) ? domain : null;

        public SimulationDomainId? OwnerOf(long entityId) =>
            _ownerByEntity.TryGetValue(entityId, out SimulationDomainId owner) ? owner : null;

        public bool IsGlobal(long entityId) => _globals.Contains(entityId);

        public void Register(ILocalSimulationDomain domain)
        {
            if (domain == null) throw new ArgumentNullException(nameof(domain));
            if (_domains.ContainsKey(domain.Id))
                throw new ArgumentException("domain '" + domain.Id + "' is already hosted", nameof(domain));
            long[] desired = domain.EntityIds.ToArray();
            ValidateAssignments(domain.Id, desired);
            _domains.Add(domain.Id, domain);
            var members = new HashSet<long>(desired);
            _membersByDomain.Add(domain.Id, members);
            foreach (long entityId in members) _ownerByEntity.Add(entityId, domain.Id);
        }

        /// <summary>Atomically replaces the indexed membership with the domain's live membership.</summary>
        public void Synchronize(ILocalSimulationDomain domain)
        {
            if (domain == null) throw new ArgumentNullException(nameof(domain));
            if (!_domains.TryGetValue(domain.Id, out ILocalSimulationDomain? hosted)
                || !ReferenceEquals(hosted, domain))
                throw new InvalidOperationException("domain '" + domain.Id + "' is not this host's instance");

            // Do not silently normalize a malformed live domain. Duplicate entity
            // IDs are an ownership-integrity failure and must be rejected before
            // either side of the forward/reverse index is mutated.
            long[] desired = domain.EntityIds.ToArray();
            ValidateAssignments(domain.Id, desired);
            HashSet<long> indexed = _membersByDomain[domain.Id];
            foreach (long current in indexed)
                _ownerByEntity.Remove(current);
            indexed.Clear();
            foreach (long entityId in desired)
            {
                _ownerByEntity.Add(entityId, domain.Id);
                indexed.Add(entityId);
            }
        }

        public void Assign(long entityId, SimulationDomainId domainId)
        {
            RequireEntity(entityId);
            if (!_domains.TryGetValue(domainId, out ILocalSimulationDomain? domain))
                throw new KeyNotFoundException("domain '" + domainId + "' is not hosted");
            if (_globals.Contains(entityId))
                throw new InvalidOperationException("global entity " + entityId + " cannot be domain-owned");
            if (_ownerByEntity.TryGetValue(entityId, out SimulationDomainId current))
            {
                if (current == domainId)
                {
                    if (!domain.EntityIds.Contains(entityId))
                        throw new InvalidOperationException("host index and domain membership diverged for entity " + entityId);
                    return;
                }
                throw new InvalidOperationException("entity " + entityId + " is already owned by " + current);
            }
            AddToDomain(domain, entityId);
            _ownerByEntity.Add(entityId, domainId);
            _membersByDomain[domainId].Add(entityId);
        }

        public void Move(long entityId, SimulationDomainId expectedSource, SimulationDomainId destination)
        {
            RequireEntity(entityId);
            if (!_domains.TryGetValue(destination, out ILocalSimulationDomain? destinationDomain))
                throw new KeyNotFoundException("domain '" + destination + "' is not hosted");
            if (!_ownerByEntity.TryGetValue(entityId, out SimulationDomainId current) || current != expectedSource)
                throw new InvalidOperationException("entity " + entityId + " is not owned by expected source " + expectedSource);
            if (expectedSource == destination) return;
            ILocalSimulationDomain sourceDomain = _domains[expectedSource];
            AddToDomain(destinationDomain, entityId);
            if (!RemoveFromDomain(sourceDomain, entityId))
            {
                RemoveFromDomain(destinationDomain, entityId);
                throw new InvalidOperationException("source domain '" + expectedSource
                    + "' did not contain entity " + entityId);
            }
            _ownerByEntity[entityId] = destination;
            _membersByDomain[expectedSource].Remove(entityId);
            _membersByDomain[destination].Add(entityId);
        }

        public bool Unassign(long entityId, SimulationDomainId expectedOwner)
        {
            if (!_ownerByEntity.TryGetValue(entityId, out SimulationDomainId current)
                || current != expectedOwner) return false;
            if (!RemoveFromDomain(_domains[expectedOwner], entityId))
                throw new InvalidOperationException("host index and domain membership diverged for entity " + entityId);
            _membersByDomain[expectedOwner].Remove(entityId);
            return _ownerByEntity.Remove(entityId);
        }

        public void MarkGlobal(long entityId)
        {
            RequireEntity(entityId);
            if (_ownerByEntity.TryGetValue(entityId, out SimulationDomainId owner))
                throw new InvalidOperationException("entity " + entityId + " is already owned by " + owner);
            _globals.Add(entityId);
        }

        public bool RemoveDomain(SimulationDomainId id)
        {
            if (!_domains.ContainsKey(id)) return false;
            if (!_membersByDomain.TryGetValue(id, out HashSet<long>? members))
                throw new InvalidOperationException("domain '" + id + "' has no reverse membership index");
            _domains.Remove(id);
            foreach (long entityId in members)
                _ownerByEntity.Remove(entityId);
            _membersByDomain.Remove(id);
            return true;
        }

        public DomainOwnershipSummary Inspect(IEnumerable<long> expectedEntityIds)
        {
            if (expectedEntityIds == null) throw new ArgumentNullException(nameof(expectedEntityIds));
            long[] expected = expectedEntityIds.Distinct().OrderBy(x => x).ToArray();
            var expectedSet = new HashSet<long>(expected);
            long[] unowned = expected.Where(id => !_ownerByEntity.ContainsKey(id) && !_globals.Contains(id)).ToArray();
            var inconsistencies = new List<string>();
            foreach (ILocalSimulationDomain domain in _domains.Values)
            {
                HashSet<long> live = new HashSet<long>(domain.EntityIds);
                if (!_membersByDomain.TryGetValue(domain.Id, out HashSet<long>? indexedMembers))
                {
                    inconsistencies.Add("domain " + domain.Id + " has no reverse membership index");
                    indexedMembers = new HashSet<long>();
                }
                foreach (long entityId in live)
                {
                    if (!_ownerByEntity.TryGetValue(entityId, out SimulationDomainId indexed)
                        || indexed != domain.Id)
                        inconsistencies.Add("domain " + domain.Id + " contains " + entityId
                            + " but host index says " + (indexed.Value ?? "<none>"));
                }
                foreach (long entityId in indexedMembers)
                {
                    if (!live.Contains(entityId))
                        inconsistencies.Add("reverse index assigns " + entityId + " to " + domain.Id
                            + " but the domain does not contain it");
                }
            }
            foreach ((long entityId, SimulationDomainId owner) in _ownerByEntity)
            {
                if (!_domains.TryGetValue(owner, out ILocalSimulationDomain? domain))
                    inconsistencies.Add("host index points " + entityId + " at missing domain " + owner);
                else if (!_membersByDomain.TryGetValue(owner, out HashSet<long>? members)
                    || !members.Contains(entityId))
                    inconsistencies.Add("host index assigns " + entityId + " to " + owner
                        + " but the reverse index does not contain it");
                if (_globals.Contains(entityId))
                    inconsistencies.Add("entity " + entityId + " is both global and domain-owned");
                if (!expectedSet.Contains(entityId))
                    inconsistencies.Add("domain-owned entity " + entityId + " is absent from the expected world set");
            }
            foreach (long entityId in _globals)
            {
                if (!expectedSet.Contains(entityId))
                    inconsistencies.Add("global entity " + entityId + " is absent from the expected world set");
            }
            return new DomainOwnershipSummary(
                _domains.Count,
                _domains.Values.Count(x => x.Kind == SimulationDomainKind.Island),
                _domains.Values.Count(x => x.Kind == SimulationDomainKind.Ship),
                _ownerByEntity.Count, _globals.Count, unowned, inconsistencies);
        }

        public DomainOwnershipSummary EnsureComplete(IEnumerable<long> expectedEntityIds)
        {
            DomainOwnershipSummary summary = Inspect(expectedEntityIds);
            if (summary.UnownedEntityIds.Count > 0 || summary.Inconsistencies.Count > 0)
                throw new InvalidOperationException("local domain ownership audit failed; unowned: "
                    + (summary.UnownedEntityIds.Count == 0 ? "none" : string.Join(", ", summary.UnownedEntityIds))
                    + "; inconsistencies: "
                    + (summary.Inconsistencies.Count == 0 ? "none" : string.Join(" | ", summary.Inconsistencies)));
            return summary;
        }

        private void ValidateAssignments(SimulationDomainId domainId, IEnumerable<long> entityIds)
        {
            var seen = new HashSet<long>();
            foreach (long entityId in entityIds)
            {
                RequireEntity(entityId);
                if (!seen.Add(entityId)) throw new ArgumentException("domain contains duplicate entity " + entityId);
                if (_globals.Contains(entityId))
                    throw new InvalidOperationException("global entity " + entityId + " cannot be domain-owned");
                if (_ownerByEntity.TryGetValue(entityId, out SimulationDomainId owner) && owner != domainId)
                    throw new InvalidOperationException("entity " + entityId + " is already owned by " + owner);
            }
        }

        private static void RequireEntity(long entityId)
        {
            if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId));
        }

        private static void AddToDomain(ILocalSimulationDomain domain, long entityId)
        {
            bool added = domain switch
            {
                IslandDomain island => island.AddOwnedEntity(entityId),
                ShipDomain ship => ship.AddOwnedEntity(entityId),
                StaticShipDomain ship => ship.AddOwnedEntity(entityId),
                _ => throw new NotSupportedException("unsupported local domain type " + domain.GetType().FullName),
            };
            if (!added && !domain.EntityIds.Contains(entityId))
                throw new InvalidOperationException("domain '" + domain.Id + "' rejected entity " + entityId);
        }

        private static bool RemoveFromDomain(ILocalSimulationDomain domain, long entityId) => domain switch
        {
            IslandDomain island => island.RemoveOwnedEntity(entityId),
            ShipDomain ship => ship.RemoveOwnedEntity(entityId),
            StaticShipDomain ship => ship.RemoveOwnedEntity(entityId),
            _ => throw new NotSupportedException("unsupported local domain type " + domain.GetType().FullName),
        };
    }
}

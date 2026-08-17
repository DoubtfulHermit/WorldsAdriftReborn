using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains
{
    /// <summary>
    /// Single-process host directory for whole-ship domains. It deliberately owns
    /// no threads: the existing server poll loop remains the only caller/ticker.
    /// </summary>
    public sealed class ShipDomainRegistry
    {
        private readonly Dictionary<long, ShipDomain> _byHull = new();

        public IReadOnlyCollection<ShipDomain> All => _byHull.Values;

        public ShipDomain Register(ShipDomain domain)
        {
            if (domain == null) throw new ArgumentNullException(nameof(domain));
            if (_byHull.ContainsKey(domain.HullEntityId))
                throw new ArgumentException("hull already has a ship domain", nameof(domain));
            _byHull.Add(domain.HullEntityId, domain);
            return domain;
        }

        public ShipDomain GetOrAdd(long hullEntityId, Func<ShipDomain> factory)
        {
            if (_byHull.TryGetValue(hullEntityId, out ShipDomain? existing)) return existing;
            ShipDomain created = factory?.Invoke() ?? throw new ArgumentNullException(nameof(factory));
            if (created.HullEntityId != hullEntityId)
                throw new ArgumentException("factory returned a domain for another hull", nameof(factory));
            _byHull.Add(hullEntityId, created);
            return created;
        }

        public ShipDomain? ByHull(long hullEntityId) =>
            _byHull.TryGetValue(hullEntityId, out ShipDomain? domain) ? domain : null;

        public AuthorityGeneration GenerationFor(long hullEntityId) =>
            ByHull(hullEntityId)?.Generation ?? AuthorityGeneration.Initial;

        public bool Remove(long hullEntityId) => _byHull.Remove(hullEntityId);
    }
}

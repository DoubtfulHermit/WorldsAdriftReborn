using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;

namespace WorldsAdriftRebornGameServer.Multiplayer.Simulation
{
    /// <summary>
    /// The shadow model: what the world would look like if something were reasoning
    /// about how to place it. It is fed observations and it answers questions. It
    /// never reaches back into the server, owns no authority, and moves nothing.
    ///
    /// <para>
    /// This is the layer ABOVE <c>LocalDomainHost</c>, not a replacement for it.
    /// LocalDomainHost is the real ownership registry and stays authoritative for
    /// who owns which entity; this class only records that ownership alongside the
    /// coupling between entities, which the ownership registry has no opinion about.
    /// Nothing here has a Tick, for the same reason ILocalSimulationDomain does not:
    /// describing the world must not become a second place that runs it.
    /// </para>
    ///
    /// <para>Not thread-safe, like every other state holder in this single-poll-loop server.</para>
    /// </summary>
    public sealed class SimulationWorldModel
    {
        private readonly Dictionary<SimulationDomainId, string> _domainKinds = new();
        private readonly Dictionary<SimulationDomainId, string?> _domainDescriptors = new();
        private readonly Dictionary<SimulationDomainId, HashSet<SimulationEntityId>> _members = new();
        private readonly Dictionary<SimulationEntityId, SimulationDomainId?> _entities = new();
        private readonly Dictionary<InteractionEdgeKey, InteractionEdge> _edges = new();

        public int DomainCount => _domainKinds.Count;
        public int EntityCount => _entities.Count;
        public int InteractionCount => _edges.Count;

        public bool HasDomain(SimulationDomainId id) => _domainKinds.ContainsKey(id);
        public bool HasEntity(SimulationEntityId id) => _entities.ContainsKey(id);

        /// <summary>The domain an entity belongs to, or null when it is registered but unassigned.</summary>
        public SimulationDomainId? DomainOf(SimulationEntityId entityId) =>
            _entities.TryGetValue(entityId, out SimulationDomainId? domain) ? domain : null;

        /// <param name="kind">
        /// Free text ("island", "ship"). Deliberately not an enum: the core must not
        /// learn the names of Worlds Adrift's domain types, and the two that exist
        /// today are not evidence for a closed set.
        /// </param>
        /// <param name="descriptor">
        /// Optional opaque note. See <see cref="DomainSnapshot.Descriptor"/> - it is
        /// NOT a contract object.
        /// </param>
        public void RegisterDomain(SimulationDomainId id, string kind, string? descriptor = null)
        {
            RequireDomainId(id);
            if (string.IsNullOrWhiteSpace(kind))
                throw new ArgumentException("domain kind is required", nameof(kind));
            if (_domainKinds.ContainsKey(id))
                throw new ArgumentException("domain '" + id + "' is already registered", nameof(id));

            _domainKinds.Add(id, kind.Trim());
            _domainDescriptors.Add(id, string.IsNullOrWhiteSpace(descriptor) ? null : descriptor.Trim());
            _members.Add(id, new HashSet<SimulationEntityId>());
        }

        /// <summary>
        /// Forgets a domain. Its members stay REGISTERED but become unassigned: an
        /// entity does not stop existing because the thing hosting it went away, and
        /// silently dropping them would make entity counts lie.
        /// </summary>
        public bool RemoveDomain(SimulationDomainId id)
        {
            if (!_domainKinds.Remove(id)) return false;
            _domainDescriptors.Remove(id);
            HashSet<SimulationEntityId> members = _members[id];
            _members.Remove(id);
            foreach (SimulationEntityId member in members) _entities[member] = null;
            return true;
        }

        /// <summary>
        /// Registers an entity, optionally in a domain. Idempotent for an entity
        /// already in the requested domain; re-registering into a DIFFERENT domain
        /// moves it, because the adapter rebuilds its whole observation each pass and
        /// a re-observation that had to be spelled differently is a bug waiting.
        /// </summary>
        public void RegisterEntity(SimulationEntityId entityId, SimulationDomainId? domainId = null)
        {
            RequireEntityId(entityId);
            if (domainId.HasValue) RequireHostedDomain(domainId.Value);

            if (_entities.TryGetValue(entityId, out SimulationDomainId? current))
            {
                if (current == domainId) return;
                if (domainId.HasValue) { MoveEntityToDomain(entityId, domainId.Value); return; }
                DetachFromDomain(entityId, current);
                _entities[entityId] = null;
                return;
            }

            _entities.Add(entityId, domainId);
            if (domainId.HasValue) _members[domainId.Value].Add(entityId);
        }

        /// <summary>Moves a registered entity into a hosted domain. Idempotent.</summary>
        public void MoveEntityToDomain(SimulationEntityId entityId, SimulationDomainId destination)
        {
            RequireEntityId(entityId);
            RequireHostedDomain(destination);
            if (!_entities.TryGetValue(entityId, out SimulationDomainId? current))
                throw new KeyNotFoundException("entity '" + entityId + "' is not registered");
            if (current == destination) return;

            DetachFromDomain(entityId, current);
            _members[destination].Add(entityId);
            _entities[entityId] = destination;
        }

        /// <summary>
        /// Forgets an entity and every edge it was an endpoint of. An edge to
        /// something that no longer exists is not an observation, it is a leak.
        /// </summary>
        public bool RemoveEntity(SimulationEntityId entityId)
        {
            if (!_entities.TryGetValue(entityId, out SimulationDomainId? current)) return false;
            DetachFromDomain(entityId, current);
            _entities.Remove(entityId);

            foreach (InteractionEdgeKey key in _edges.Keys
                .Where(k => k.A == entityId || k.B == entityId).ToArray())
            {
                _edges.Remove(key);
            }
            return true;
        }

        /// <summary>
        /// Records an observed interaction. Idempotent on (A, B, kind): re-observing
        /// the same relationship with new weights replaces it rather than adding a
        /// second edge. Both endpoints must already be registered - an edge to an
        /// entity the model has never heard of is an adapter bug, not a discovery.
        /// </summary>
        public void UpsertInteraction(InteractionEdge edge)
        {
            if (!_entities.ContainsKey(edge.A))
                throw new KeyNotFoundException("entity '" + edge.A + "' is not registered");
            if (!_entities.ContainsKey(edge.B))
                throw new KeyNotFoundException("entity '" + edge.B + "' is not registered");
            _edges[edge.Key] = edge;
        }

        public bool RemoveInteraction(SimulationEntityId a, SimulationEntityId b, InteractionKind kind) =>
            _edges.Remove(new InteractionEdgeKey(a, b, kind));

        public bool HasInteraction(SimulationEntityId a, SimulationEntityId b, InteractionKind kind) =>
            _edges.ContainsKey(new InteractionEdgeKey(a, b, kind));

        /// <summary>
        /// The whole world, ordered. Everything sorts ordinally by id so the result
        /// is independent of insertion order and of Dictionary enumeration order.
        /// </summary>
        public WorldSnapshot Snapshot()
        {
            InteractionSnapshot[] interactions = _edges.Values
                .OrderBy(e => e.Key)
                .Select(e => new InteractionSnapshot(e, DomainOf(e.A), DomainOf(e.B)))
                .ToArray();

            var pressureByDomain = new Dictionary<SimulationDomainId, double>();
            var activeByDomain = new Dictionary<SimulationDomainId, int>();
            foreach (InteractionSnapshot interaction in interactions)
            {
                if (!interaction.IsCrossDomain) continue;
                if (interaction.DomainA.HasValue)
                    Accumulate(pressureByDomain, activeByDomain, interaction.DomainA.Value, interaction.Pressure);
                if (interaction.DomainB.HasValue)
                    Accumulate(pressureByDomain, activeByDomain, interaction.DomainB.Value, interaction.Pressure);
            }

            DomainSnapshot[] domains = _domainKinds.Keys
                .OrderBy(id => id)
                .Select(id => new DomainSnapshot(
                    id,
                    _domainKinds[id],
                    _members[id].OrderBy(m => m).ToArray(),
                    activeByDomain.TryGetValue(id, out int active) ? active : 0,
                    InteractionPressure.Round(
                        pressureByDomain.TryGetValue(id, out double pressure) ? pressure : 0),
                    _domainDescriptors[id]))
                .ToArray();

            return new WorldSnapshot(domains, interactions, _entities.Count);
        }

        private static void Accumulate(
            Dictionary<SimulationDomainId, double> pressure,
            Dictionary<SimulationDomainId, int> active,
            SimulationDomainId domainId,
            double edgePressure)
        {
            pressure[domainId] = (pressure.TryGetValue(domainId, out double current) ? current : 0) + edgePressure;
            if (edgePressure > 0)
                active[domainId] = (active.TryGetValue(domainId, out int count) ? count : 0) + 1;
        }

        private void DetachFromDomain(SimulationEntityId entityId, SimulationDomainId? domainId)
        {
            if (domainId.HasValue && _members.TryGetValue(domainId.Value, out HashSet<SimulationEntityId>? set))
                set.Remove(entityId);
        }

        private void RequireHostedDomain(SimulationDomainId id)
        {
            if (!_domainKinds.ContainsKey(id))
                throw new KeyNotFoundException("domain '" + id + "' is not registered");
        }

        private static void RequireDomainId(SimulationDomainId id)
        {
            if (string.IsNullOrEmpty(id.Value))
                throw new ArgumentException("domain id is required", nameof(id));
        }

        private static void RequireEntityId(SimulationEntityId id)
        {
            if (string.IsNullOrEmpty(id.Value))
                throw new ArgumentException("entity id is required", nameof(id));
        }
    }
}

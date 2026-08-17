using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains
{
    public readonly struct ShipPilotBinding
    {
        public ShipPilotBinding(long playerEntityId, long helmEntityId, AuthorityGeneration generation)
        {
            if (playerEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(playerEntityId));
            if (helmEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(helmEntityId));
            PlayerEntityId = playerEntityId;
            HelmEntityId = helmEntityId;
            Generation = generation;
        }

        public long PlayerEntityId { get; }
        public long HelmEntityId { get; }
        public AuthorityGeneration Generation { get; }
    }

    public readonly struct ShipAuthorityToken
    {
        public ShipAuthorityToken(SimulationDomainId domainId, AuthorityGeneration generation, long playerEntityId)
        {
            DomainId = domainId;
            Generation = generation;
            PlayerEntityId = playerEntityId;
        }

        public SimulationDomainId DomainId { get; }
        public AuthorityGeneration Generation { get; }
        public long PlayerEntityId { get; }
    }

    /// <summary>
    /// Versioned, process-independent state needed to reconstruct one whole ship
    /// authority unit. Arrays are defensive copies: a captured handoff cannot be
    /// mutated underneath the source domain.
    /// </summary>
    public sealed class ShipDomainSnapshot
    {
        public const int CurrentVersion = 1;

        public ShipDomainSnapshot(
            SimulationDomainId id,
            long hullEntityId,
            int? persistentIndex,
            AuthorityGeneration generation,
            FlightSessionSnapshot flight,
            ShipPilotBinding? pilot,
            IEnumerable<long> deckEntityIds,
            IEnumerable<long> mountedPartEntityIds,
            IEnumerable<ulong> aboardPeerIds)
        {
            if (flight == null) throw new ArgumentNullException(nameof(flight));
            if (hullEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(hullEntityId));
            if (id != SimulationDomainId.ForShip(hullEntityId))
                throw new ArgumentException("ship domain id must identify its hull", nameof(id));
            if (persistentIndex.HasValue && persistentIndex.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(persistentIndex));
            if (generation.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            if (pilot.HasValue && pilot.Value.Generation != generation)
                throw new ArgumentException("pilot authority must match the snapshot generation", nameof(pilot));
            if ((pilot.HasValue) != flight.Manned)
                throw new ArgumentException("pilot binding and flight manned state must agree", nameof(pilot));
            Id = id;
            HullEntityId = hullEntityId;
            PersistentIndex = persistentIndex;
            Generation = generation;
            Flight = flight;
            Pilot = pilot;
            DeckEntityIds = CopyDistinct(deckEntityIds, nameof(deckEntityIds));
            MountedPartEntityIds = CopyDistinct(mountedPartEntityIds, nameof(mountedPartEntityIds));
            if (DeckEntityIds.Contains(hullEntityId) || MountedPartEntityIds.Contains(hullEntityId)
                || DeckEntityIds.Intersect(MountedPartEntityIds).Any())
                throw new ArgumentException("ship domain members must be unique and cannot include the hull");
            AboardPeerIds = (aboardPeerIds ?? throw new ArgumentNullException(nameof(aboardPeerIds)))
                .Distinct().OrderBy(x => x).ToArray();
        }

        public int Version => CurrentVersion;
        public SimulationDomainId Id { get; }
        public long HullEntityId { get; }
        public int? PersistentIndex { get; }
        public AuthorityGeneration Generation { get; }
        public FlightSessionSnapshot Flight { get; }
        public ShipPilotBinding? Pilot { get; }
        public IReadOnlyList<long> DeckEntityIds { get; }
        public IReadOnlyList<long> MountedPartEntityIds { get; }
        public IReadOnlyList<ulong> AboardPeerIds { get; }

        private static long[] CopyDistinct(IEnumerable<long> values, string name)
        {
            if (values == null) throw new ArgumentNullException(name);
            long[] result = values.Distinct().OrderBy(x => x).ToArray();
            if (result.Any(x => x <= 0)) throw new ArgumentOutOfRangeException(name);
            return result;
        }
    }
}

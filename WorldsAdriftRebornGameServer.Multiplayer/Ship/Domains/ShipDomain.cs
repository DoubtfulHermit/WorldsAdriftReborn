using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using WorldsAdriftRebornGameServer.Multiplayer.Domains;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains
{
    /// <summary>
    /// The first local whole-ship authority aggregate. It owns flight/control,
    /// pilot authority, structural membership and aboard affinity while still
    /// running synchronously in the existing process and poll loop.
    /// </summary>
    public sealed class ShipDomain : ILocalSimulationDomain
    {
        private readonly HashSet<long> _decks = new();
        private readonly HashSet<long> _mountedParts = new();
        private readonly HashSet<ulong> _aboardPeers = new();

        public ShipDomain(long hullEntityId, int? persistentIndex, FlightSession flight)
            : this(SimulationDomainId.ForShip(hullEntityId), hullEntityId, persistentIndex,
                AuthorityGeneration.Initial, flight, null)
        {
        }

        private ShipDomain(SimulationDomainId id, long hullEntityId, int? persistentIndex,
            AuthorityGeneration generation, FlightSession flight, ShipPilotBinding? pilot)
        {
            if (hullEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(hullEntityId));
            Id = id;
            HullEntityId = hullEntityId;
            PersistentIndex = persistentIndex;
            Generation = generation;
            Flight = flight ?? throw new ArgumentNullException(nameof(flight));
            Pilot = pilot;
        }

        public SimulationDomainId Id { get; }
        public long HullEntityId { get; }
        public int? PersistentIndex { get; }
        public AuthorityGeneration Generation { get; private set; }
        public FlightSession Flight { get; }
        public ShipPilotBinding? Pilot { get; private set; }
        public IReadOnlyList<long> DeckEntityIds => _decks.OrderBy(x => x).ToArray();
        public IReadOnlyList<long> MountedPartEntityIds => _mountedParts.OrderBy(x => x).ToArray();
        public IReadOnlyList<ulong> AboardPeerIds => _aboardPeers.OrderBy(x => x).ToArray();
        public SimulationDomainKind Kind => SimulationDomainKind.Ship;
        public IReadOnlyList<long> EntityIds => new[] { HullEntityId }
            .Concat(_decks).Concat(_mountedParts).Distinct().OrderBy(x => x).ToArray();

        internal bool AddOwnedEntity(long entityId)
        {
            if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            if (entityId == HullEntityId || _decks.Contains(entityId)) return false;
            return _mountedParts.Add(entityId);
        }

        internal bool RemoveOwnedEntity(long entityId)
        {
            if (entityId == HullEntityId) return false;
            return _mountedParts.Remove(entityId) || _decks.Remove(entityId);
        }

        public ShipAuthorityToken AcquirePilot(long playerEntityId, long helmEntityId)
        {
            if (Pilot.HasValue)
            {
                ShipPilotBinding current = Pilot.Value;
                if (current.PlayerEntityId == playerEntityId && current.HelmEntityId == helmEntityId)
                {
                    return TokenFor(current);
                }
                throw new InvalidOperationException("ship domain already has a pilot");
            }

            Generation = Generation.Next();
            Pilot = new ShipPilotBinding(playerEntityId, helmEntityId, Generation);
            Flight.Man();
            return TokenFor(Pilot.Value);
        }

        public bool ReleasePilot(ShipAuthorityToken token, bool abandoned)
        {
            if (!Owns(token) || !Pilot.HasValue || Pilot.Value.PlayerEntityId != token.PlayerEntityId)
            {
                return false;
            }

            if (abandoned) Flight.Abandon();
            else Flight.Dismount();
            Pilot = null;
            Generation = Generation.Next();
            return true;
        }

        public bool TrySetInput(ShipAuthorityToken token, FlightControlInput input)
        {
            if (!Owns(token) || !Pilot.HasValue || Pilot.Value.PlayerEntityId != token.PlayerEntityId)
            {
                return false;
            }
            Flight.SetInput(input);
            return true;
        }

        public bool Owns(ShipAuthorityToken token) =>
            token.DomainId == Id && token.Generation == Generation;

        public void ReplaceMembers(IEnumerable<long> deckEntityIds, IEnumerable<long> mountedPartEntityIds)
        {
            // Validate both categories before mutating either. A malformed restore or
            // refresh must not leave a previously valid live domain half-updated.
            HashSet<long> decks = ValidatedMembers(deckEntityIds, nameof(deckEntityIds));
            HashSet<long> mountedParts = ValidatedMembers(mountedPartEntityIds, nameof(mountedPartEntityIds));
            if (decks.Contains(HullEntityId) || mountedParts.Contains(HullEntityId)
                || decks.Overlaps(mountedParts))
            {
                throw new ArgumentException("ship domain members must be unique and cannot include the hull");
            }
            _decks.Clear();
            _decks.UnionWith(decks);
            _mountedParts.Clear();
            _mountedParts.UnionWith(mountedParts);
        }

        public void ReplaceAboard(IEnumerable<ulong> peerIds)
        {
            if (peerIds == null) throw new ArgumentNullException(nameof(peerIds));
            _aboardPeers.Clear();
            foreach (ulong peerId in peerIds) _aboardPeers.Add(peerId);
        }

        public ShipDomainSnapshot Capture() => new ShipDomainSnapshot(
            Id, HullEntityId, PersistentIndex, Generation, Flight.Capture(), Pilot,
            _decks, _mountedParts, _aboardPeers);

        public static ShipDomain Restore(ShipDomainSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Version != ShipDomainSnapshot.CurrentVersion)
                throw new NotSupportedException("unsupported ship-domain snapshot version " + snapshot.Version);
            var domain = new ShipDomain(snapshot.Id, snapshot.HullEntityId, snapshot.PersistentIndex,
                snapshot.Generation, FlightSession.Restore(snapshot.Flight), snapshot.Pilot);
            domain.ReplaceMembers(snapshot.DeckEntityIds, snapshot.MountedPartEntityIds);
            domain.ReplaceAboard(snapshot.AboardPeerIds);
            return domain;
        }

        /// <summary>
        /// Reconstitutes durable process state without reviving connection-scoped
        /// authority. The persisted generation is advanced once, pilot is empty,
        /// and the session is abandoned so stale pre-restart input cannot move it.
        /// </summary>
        public static ShipDomain RestoreAfterProcessRestart(long hullEntityId,
            int? persistentIndex, AuthorityGeneration savedGeneration, FlightSession flight)
        {
            if (flight == null) throw new ArgumentNullException(nameof(flight));
            flight.Abandon();
            return new ShipDomain(SimulationDomainId.ForShip(hullEntityId), hullEntityId,
                persistentIndex, savedGeneration.Next(), flight, null);
        }

        private ShipAuthorityToken TokenFor(ShipPilotBinding pilot) =>
            new ShipAuthorityToken(Id, pilot.Generation, pilot.PlayerEntityId);

        private static HashSet<long> ValidatedMembers(IEnumerable<long> source, string name)
        {
            if (source == null) throw new ArgumentNullException(name);
            var result = new HashSet<long>();
            foreach (long id in source)
            {
                if (id <= 0) throw new ArgumentOutOfRangeException(name);
                result.Add(id);
            }
            return result;
        }
    }
}

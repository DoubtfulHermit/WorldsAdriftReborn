using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>Stable scope labels for the authenticated World Inspector contract.</summary>
    public static class WorldInspectorScope
    {
        public const string World = "WORLD";
        public const string Simulation = "SIMULATION";
        public const string Infrastructure = "INFRASTRUCTURE";
    }

    public readonly record struct WorldInspectorDomainObservation(
        string DomainId, int EntityCount);

    public readonly record struct WorldInspectorCheckoutObservation(
        long PlayerEntityId, int ResourceCount, int FaunaCount, int ShipDomainCount);

    public readonly record struct WorldInspectorEntityOwnershipObservation(
        long EntityId, string DomainId);

    public readonly record struct WorldInspectorShipObservation(
        string DomainId, long AuthorityGeneration, bool Active, bool Piloted);

    public readonly record struct WorldInspectorTerrainObservation(
        string Subject, string State);

    /// <summary>
    /// One read-only pass over facts the server already owns. It deliberately has
    /// no worker, migration, fidelity or sleep fields: none of those systems exist.
    /// </summary>
    public sealed class WorldInspectorObservation
    {
        public WorldInspectorObservation(
            long generatedAtUnixMs,
            string hostMode,
            string hostId,
            int processId,
            long processUptimeSeconds,
            int connectedPlayerCount,
            int islandDomainCount,
            int shipDomainCount,
            int ownedEntityCount,
            int globalEntityCount,
            int unownedEntityCount,
            int ownershipIssueCount,
            int terrainReadyCount,
            bool shadowEnabled,
            bool shadowHasSnapshot,
            int shadowRefreshCount,
            IReadOnlyList<WorldInspectorDomainObservation>? domains,
            IReadOnlyList<WorldInspectorEntityOwnershipObservation>? ownership,
            IReadOnlyList<WorldInspectorCheckoutObservation>? checkouts,
            IReadOnlyList<WorldInspectorShipObservation>? ships,
            IReadOnlyList<WorldInspectorTerrainObservation>? terrain)
        {
            GeneratedAtUnixMs = generatedAtUnixMs;
            HostMode = hostMode ?? string.Empty;
            HostId = hostId ?? string.Empty;
            ProcessId = Math.Max(0, processId);
            ProcessUptimeSeconds = Math.Max(0, processUptimeSeconds);
            ConnectedPlayerCount = Math.Max(0, connectedPlayerCount);
            IslandDomainCount = Math.Max(0, islandDomainCount);
            ShipDomainCount = Math.Max(0, shipDomainCount);
            OwnedEntityCount = Math.Max(0, ownedEntityCount);
            GlobalEntityCount = Math.Max(0, globalEntityCount);
            UnownedEntityCount = Math.Max(0, unownedEntityCount);
            OwnershipIssueCount = Math.Max(0, ownershipIssueCount);
            TerrainReadyCount = Math.Max(0, terrainReadyCount);
            ShadowEnabled = shadowEnabled;
            ShadowHasSnapshot = shadowHasSnapshot;
            ShadowRefreshCount = Math.Max(0, shadowRefreshCount);
            Domains = Copy(domains);
            Ownership = Copy(ownership);
            Checkouts = Copy(checkouts);
            Ships = Copy(ships);
            Terrain = Copy(terrain);
        }

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T>? values) =>
            Array.AsReadOnly((values ?? Array.Empty<T>()).ToArray());

        public long GeneratedAtUnixMs { get; }
        public string HostMode { get; }
        public string HostId { get; }
        public int ProcessId { get; }
        public long ProcessUptimeSeconds { get; }
        public int ConnectedPlayerCount { get; }
        public int IslandDomainCount { get; }
        public int ShipDomainCount { get; }
        public int OwnedEntityCount { get; }
        public int GlobalEntityCount { get; }
        public int UnownedEntityCount { get; }
        public int OwnershipIssueCount { get; }
        public int TerrainReadyCount { get; }
        public bool ShadowEnabled { get; }
        public bool ShadowHasSnapshot { get; }
        public int ShadowRefreshCount { get; }
        public IReadOnlyList<WorldInspectorDomainObservation> Domains { get; }
        public IReadOnlyList<WorldInspectorEntityOwnershipObservation> Ownership { get; }
        public IReadOnlyList<WorldInspectorCheckoutObservation> Checkouts { get; }
        public IReadOnlyList<WorldInspectorShipObservation> Ships { get; }
        public IReadOnlyList<WorldInspectorTerrainObservation> Terrain { get; }
    }

    public readonly record struct WorldInspectorEventStat(
        long Sequence, long AtUnixMs, string Scope, string Kind,
        string Subject, string From, string To);

    public readonly record struct WorldInspectorWorldStat(
        int ConnectedPlayerCount, int IslandDomainCount, int ShipDomainCount,
        int OwnedEntityCount, int GlobalEntityCount, int UnownedEntityCount,
        int OwnershipIssueCount, int ResourceCheckoutCount, int FaunaCheckoutCount,
        int ShipCheckoutCount, int TerrainReadyCount);

    public readonly record struct WorldInspectorSimulationStat(
        bool ShadowEnabled, bool ShadowHasSnapshot, int ShadowRefreshCount,
        int ActiveFlightCount, int PilotedFlightCount, long HighestAuthorityGeneration);

    public readonly record struct WorldInspectorInfrastructureStat(
        string HostMode, string HostId, int ProcessId, long ProcessUptimeSeconds);

    /// <summary>The versioned, immutable observer view carried only by admin telemetry.</summary>
    public readonly struct WorldInspectorRuntimeStat
    {
        public const int ContractVersion = 1;
        public const int EventCapacity = 128;

        public WorldInspectorRuntimeStat(long generatedAtUnixMs,
            WorldInspectorWorldStat world,
            WorldInspectorSimulationStat simulation,
            WorldInspectorInfrastructureStat infrastructure,
            IReadOnlyList<WorldInspectorEventStat>? events)
        {
            Present = true;
            GeneratedAtUnixMs = generatedAtUnixMs;
            World = world;
            Simulation = simulation;
            Infrastructure = infrastructure;
            _events = Array.AsReadOnly((events ?? Array.Empty<WorldInspectorEventStat>())
                .Take(EventCapacity).ToArray());
        }

        public static WorldInspectorRuntimeStat Absent => default;
        public bool Present { get; }
        public long GeneratedAtUnixMs { get; }
        public WorldInspectorWorldStat World { get; }
        public WorldInspectorSimulationStat Simulation { get; }
        public WorldInspectorInfrastructureStat Infrastructure { get; }
        private readonly IReadOnlyList<WorldInspectorEventStat>? _events;
        public IReadOnlyList<WorldInspectorEventStat> Events =>
            _events ?? Array.Empty<WorldInspectorEventStat>();
    }

    /// <summary>
    /// Diffs slow admin observations and retains only a bounded recent window.
    /// This observer owns no gameplay state and has no timer; the existing stats
    /// writer decides when an observation occurs.
    /// </summary>
    public sealed class WorldInspectorObserver
    {
        private readonly WorldInspectorEventStat[] _ring =
            new WorldInspectorEventStat[WorldInspectorRuntimeStat.EventCapacity];
        private int _next;
        private int _count;
        private long _sequence;
        private WorldInspectorObservation? _previous;

        public WorldInspectorRuntimeStat Observe(WorldInspectorObservation current)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (_previous != null) RecordTransitions(_previous, current);
            _previous = current;

            int resources = current.Checkouts.Sum(x => x.ResourceCount);
            int fauna = current.Checkouts.Sum(x => x.FaunaCount);
            int shipsCheckedOut = current.Checkouts.Sum(x => x.ShipDomainCount);
            int activeFlights = current.Ships.Count(x => x.Active);
            int pilotedFlights = current.Ships.Count(x => x.Piloted);
            long highestGeneration = current.Ships.Count == 0
                ? 0 : current.Ships.Max(x => x.AuthorityGeneration);

            return new WorldInspectorRuntimeStat(
                current.GeneratedAtUnixMs,
                new WorldInspectorWorldStat(
                    current.ConnectedPlayerCount, current.IslandDomainCount,
                    current.ShipDomainCount, current.OwnedEntityCount,
                    current.GlobalEntityCount, current.UnownedEntityCount,
                    current.OwnershipIssueCount, resources, fauna,
                    shipsCheckedOut, current.TerrainReadyCount),
                new WorldInspectorSimulationStat(
                    current.ShadowEnabled, current.ShadowHasSnapshot,
                    current.ShadowRefreshCount, activeFlights, pilotedFlights,
                    highestGeneration),
                new WorldInspectorInfrastructureStat(
                    current.HostMode, current.HostId, current.ProcessId,
                    current.ProcessUptimeSeconds),
                SnapshotEvents());
        }

        private void RecordTransitions(WorldInspectorObservation before,
            WorldInspectorObservation after)
        {
            DiffDomains(before, after);
            DiffOwnership(before, after);
            DiffCheckouts(before, after);
            DiffShips(before, after);
            DiffTerrain(before, after);

            if (before.ShadowRefreshCount != after.ShadowRefreshCount)
                Record(after, WorldInspectorScope.Simulation, "snapshot-refreshed",
                    "simulation-shadow", before.ShadowRefreshCount.ToString(),
                    after.ShadowRefreshCount.ToString());
        }

        private void DiffDomains(WorldInspectorObservation before,
            WorldInspectorObservation after)
        {
            Dictionary<string, int> old = before.Domains.ToDictionary(x => x.DomainId, x => x.EntityCount);
            Dictionary<string, int> now = after.Domains.ToDictionary(x => x.DomainId, x => x.EntityCount);
            foreach (string id in old.Keys.Union(now.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                bool had = old.TryGetValue(id, out int oldCount);
                bool has = now.TryGetValue(id, out int newCount);
                if (!had) Record(after, WorldInspectorScope.World, "domain-added", id, "absent", newCount.ToString());
                else if (!has) Record(after, WorldInspectorScope.World, "domain-removed", id, oldCount.ToString(), "absent");
                else if (oldCount != newCount) Record(after, WorldInspectorScope.World,
                    "domain-membership-changed", id, oldCount.ToString(), newCount.ToString());
            }
        }

        private void DiffOwnership(WorldInspectorObservation before,
            WorldInspectorObservation after)
        {
            Dictionary<long, string> old = before.Ownership.ToDictionary(
                x => x.EntityId, x => x.DomainId);
            Dictionary<long, string> now = after.Ownership.ToDictionary(
                x => x.EntityId, x => x.DomainId);
            foreach (long id in old.Keys.Union(now.Keys).OrderBy(x => x))
            {
                string from = old.TryGetValue(id, out string? a) ? a : "unowned";
                string to = now.TryGetValue(id, out string? b) ? b : "unowned";
                if (!string.Equals(from, to, StringComparison.Ordinal))
                    Record(after, WorldInspectorScope.World, "entity-ownership-changed",
                        "entity:" + id, from, to);
            }
        }

        private void DiffCheckouts(WorldInspectorObservation before,
            WorldInspectorObservation after)
        {
            Dictionary<long, string> old = before.Checkouts.ToDictionary(
                x => x.PlayerEntityId, Checkout);
            Dictionary<long, string> now = after.Checkouts.ToDictionary(
                x => x.PlayerEntityId, Checkout);
            foreach (long id in old.Keys.Union(now.Keys).OrderBy(x => x))
            {
                string from = old.TryGetValue(id, out string? a) ? a : "absent";
                string to = now.TryGetValue(id, out string? b) ? b : "absent";
                if (!string.Equals(from, to, StringComparison.Ordinal))
                    Record(after, WorldInspectorScope.World, "checkout-interest-changed",
                        "player:" + id, from, to);
            }
        }

        private void DiffShips(WorldInspectorObservation before,
            WorldInspectorObservation after)
        {
            Dictionary<string, WorldInspectorShipObservation> old = before.Ships
                .ToDictionary(x => x.DomainId);
            Dictionary<string, WorldInspectorShipObservation> now = after.Ships
                .ToDictionary(x => x.DomainId);
            foreach (string id in old.Keys.Intersect(now.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                WorldInspectorShipObservation a = old[id];
                WorldInspectorShipObservation b = now[id];
                if (a.AuthorityGeneration != b.AuthorityGeneration)
                    Record(after, WorldInspectorScope.Simulation, "authority-generation-changed",
                        id, a.AuthorityGeneration.ToString(), b.AuthorityGeneration.ToString());
                string from = Flight(a);
                string to = Flight(b);
                if (!string.Equals(from, to, StringComparison.Ordinal))
                    Record(after, WorldInspectorScope.Simulation, "flight-activity-changed",
                        id, from, to);
            }
        }

        private void DiffTerrain(WorldInspectorObservation before,
            WorldInspectorObservation after)
        {
            Dictionary<string, string> old = before.Terrain.ToDictionary(x => x.Subject, x => x.State);
            Dictionary<string, string> now = after.Terrain.ToDictionary(x => x.Subject, x => x.State);
            foreach (string id in old.Keys.Union(now.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                string from = old.TryGetValue(id, out string? a) ? a : "absent";
                string to = now.TryGetValue(id, out string? b) ? b : "absent";
                if (!string.Equals(from, to, StringComparison.Ordinal))
                    Record(after, WorldInspectorScope.World, "terrain-readiness-changed", id, from, to);
            }
        }

        private void Record(WorldInspectorObservation at, string scope, string kind,
            string subject, string from, string to)
        {
            _ring[_next] = new WorldInspectorEventStat(
                ++_sequence, at.GeneratedAtUnixMs, scope, kind,
                subject ?? string.Empty, from ?? string.Empty, to ?? string.Empty);
            _next = (_next + 1) % _ring.Length;
            if (_count < _ring.Length) _count++;
        }

        private IReadOnlyList<WorldInspectorEventStat> SnapshotEvents()
        {
            WorldInspectorEventStat[] result = new WorldInspectorEventStat[_count];
            for (int i = 0; i < _count; i++)
            {
                int index = ((_next - 1 - i) % _ring.Length + _ring.Length) % _ring.Length;
                result[i] = _ring[index];
            }
            return Array.AsReadOnly(result);
        }

        private static string Checkout(WorldInspectorCheckoutObservation x) =>
            "resources:" + x.ResourceCount + ",fauna:" + x.FaunaCount
            + ",ships:" + x.ShipDomainCount;

        private static string Flight(WorldInspectorShipObservation x) =>
            x.Piloted ? "piloted" : x.Active ? "active" : "resting";

    }
}

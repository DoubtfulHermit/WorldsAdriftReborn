using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public sealed class WorldInspectorTelemetryTests
    {
        private static WorldInspectorObservation Observation(
            long at = 1000,
            int refresh = 1,
            int domainEntities = 4,
            int resources = 2,
            long generation = 1,
            bool active = false,
            bool piloted = false,
            string terrain = "waiting-ack",
            string ownershipDomain = "ship:9") =>
            new WorldInspectorObservation(
                at, "local-single-process", "local:primary", 321, 20,
                connectedPlayerCount: 1, islandDomainCount: 1, shipDomainCount: 1,
                ownedEntityCount: 8, globalEntityCount: 3,
                unownedEntityCount: 0, ownershipIssueCount: 0,
                terrainReadyCount: terrain == "ready" ? 1 : 0,
                shadowEnabled: true, shadowHasSnapshot: true,
                shadowRefreshCount: refresh,
                domains: new[] { new WorldInspectorDomainObservation("ship:9", domainEntities) },
                ownership: new[] { new WorldInspectorEntityOwnershipObservation(99, ownershipDomain) },
                checkouts: new[] { new WorldInspectorCheckoutObservation(7, resources, 1, 1) },
                ships: new[] { new WorldInspectorShipObservation("ship:9", generation, active, piloted) },
                terrain: new[] { new WorldInspectorTerrainObservation("player:7/island:haven", terrain) });

        [Fact]
        public void First_observation_is_a_baseline_and_sections_report_only_current_truth()
        {
            var observer = new WorldInspectorObserver();
            WorldInspectorRuntimeStat stat = observer.Observe(Observation());

            Assert.Empty(stat.Events);
            Assert.Equal(1, stat.World.ConnectedPlayerCount);
            Assert.Equal(2, stat.World.ResourceCheckoutCount);
            Assert.Equal(1, stat.World.FaunaCheckoutCount);
            Assert.Equal(1, stat.World.ShipCheckoutCount);
            Assert.Equal("local-single-process", stat.Infrastructure.HostMode);
            Assert.Equal("local:primary", stat.Infrastructure.HostId);
            Assert.Equal(321, stat.Infrastructure.ProcessId);
            Assert.Equal(1, stat.Simulation.HighestAuthorityGeneration);
            Assert.Equal(0, stat.Simulation.ActiveFlightCount);
        }

        [Fact]
        public void Existing_sources_produce_concrete_lifecycle_transitions()
        {
            var observer = new WorldInspectorObserver();
            observer.Observe(Observation());
            WorldInspectorRuntimeStat stat = observer.Observe(Observation(
                at: 2000, refresh: 2, domainEntities: 6, resources: 5,
                generation: 2, active: true, piloted: true, terrain: "ready",
                ownershipDomain: "island:haven"));

            string[] kinds = stat.Events.Select(x => x.Kind).ToArray();
            Assert.Contains("domain-membership-changed", kinds);
            Assert.Contains("entity-ownership-changed", kinds);
            Assert.Contains("checkout-interest-changed", kinds);
            Assert.Contains("authority-generation-changed", kinds);
            Assert.Contains("flight-activity-changed", kinds);
            Assert.Contains("terrain-readiness-changed", kinds);
            Assert.Contains("snapshot-refreshed", kinds);
            Assert.All(stat.Events, e => Assert.Contains(e.Scope,
                new[] { "WORLD", "SIMULATION", "INFRASTRUCTURE" }));
            Assert.Equal(1, stat.Simulation.ActiveFlightCount);
            Assert.Equal(1, stat.Simulation.PilotedFlightCount);
            Assert.Equal(2, stat.Simulation.HighestAuthorityGeneration);
        }

        [Fact]
        public void Transition_ring_is_bounded_and_newest_first()
        {
            var observer = new WorldInspectorObserver();
            observer.Observe(Observation(refresh: 0));
            WorldInspectorRuntimeStat stat = default;
            for (int i = 1; i <= WorldInspectorRuntimeStat.EventCapacity + 40; i++)
                stat = observer.Observe(Observation(at: 1000 + i, refresh: i));

            Assert.Equal(WorldInspectorRuntimeStat.EventCapacity, stat.Events.Count);
            Assert.Equal(WorldInspectorRuntimeStat.EventCapacity + 40,
                stat.Events[0].Sequence);
            Assert.True(stat.Events[0].Sequence > stat.Events[^1].Sequence);
            Assert.All(stat.Events, e => Assert.Equal("snapshot-refreshed", e.Kind));
        }

        [Fact]
        public void Stats_file_serializes_the_versioned_three_scope_contract()
        {
            WorldInspectorRuntimeStat inspector = new WorldInspectorObserver()
                .Observe(Observation());
            string json = new StatsSnapshot(
                1, 2, 3, "raw", 0, "test", 0, 0, 0, 0,
                Array.Empty<PlayerStat>(), worldInspector: inspector).ToJson();
            JObject root = JObject.Parse(json);
            JObject section = (JObject)root["worldInspector"]!;

            Assert.Equal(19, StatsSnapshot.SchemaVersion);
            Assert.Equal(19, (int)root["schemaVersion"]!);
            Assert.True((bool)section["present"]!);
            Assert.Equal(WorldInspectorRuntimeStat.ContractVersion,
                (int)section["contractVersion"]!);
            Assert.NotNull(section["WORLD"]);
            Assert.NotNull(section["SIMULATION"]);
            Assert.NotNull(section["INFRASTRUCTURE"]);
            Assert.Equal(WorldInspectorRuntimeStat.EventCapacity,
                (int)section["eventCapacity"]!);
            Assert.Null(section["workers"]);
            Assert.Null(section["migrations"]);
            Assert.Null(section["sleep"]);
        }
    }
}

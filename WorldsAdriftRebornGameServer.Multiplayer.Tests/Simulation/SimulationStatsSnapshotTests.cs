using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Simulation;
using WorldsAdriftRebornGameServer.Multiplayer.Simulation.Wareborn;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Simulation
{
    /// <summary>
    /// The cross-process contract: what the shadow model looks like in the stats
    /// file the login server reads.
    /// </summary>
    public class SimulationStatsSnapshotTests
    {
        private static WorldSnapshot SampleWorld() => WarebornSimulationProjection.Project(
            new WarebornWorldObservation(
                new[] { new ObservedIsland("haven", new long[] { 1, 2, 3 }) },
                new[] { new ObservedShip(893, new long[] { 900 }, new long[] { 7 }, 7, true, "haven", 60) },
                new[] { new ObservedPlayer(7, 893, new[] { "haven" }) })).Snapshot();

        private static JObject SectionOf(SimulationRuntimeStat simulation)
        {
            StatsSnapshot snapshot = new StatsSnapshot(
                bootTimeUnixMs: 1, generatedAtUnixMs: 2, uptimeSeconds: 3,
                relayMode: "v2@20Hz", relayHz: 20, build: "test",
                totalConnects: 0, totalDisconnects: 0, currentOnline: 0, peakOnline: 0,
                players: Array.Empty<PlayerStat>(),
                simulation: simulation);
            JObject parsed = JObject.Parse(snapshot.ToJson());
            return (JObject)parsed["simulation"]!;
        }

        [Fact]
        public void The_current_stats_schema_includes_the_inspector_section() =>
            Assert.Equal(19, StatsSnapshot.SchemaVersion);

        [Fact]
        public void A_server_that_never_built_the_section_writes_an_explicit_absence()
        {
            JObject section = SectionOf(SimulationRuntimeStat.Off);
            Assert.False((bool)section["present"]!);
            Assert.False((bool)section["enabled"]!);
            Assert.False((bool)section["hasSnapshot"]!);
            Assert.Empty((JArray)section["domains"]!);
        }

        [Fact]
        public void A_server_with_the_flag_off_is_present_but_not_enabled()
        {
            JObject section = SectionOf(new SimulationRuntimeStat(
                enabled: false, refreshCount: 0, refreshIntervalSeconds: 5,
                error: null, snapshot: null));

            // Three distinguishable facts, all on the wire.
            Assert.True((bool)section["present"]!);
            Assert.False((bool)section["enabled"]!);
            Assert.False((bool)section["hasSnapshot"]!);
        }

        [Fact]
        public void An_enabled_server_writes_its_domains_interactions_and_totals()
        {
            JObject section = SectionOf(new SimulationRuntimeStat(
                enabled: true, refreshCount: 4, refreshIntervalSeconds: 5,
                error: null, snapshot: SampleWorld()));

            Assert.True((bool)section["present"]!);
            Assert.True((bool)section["enabled"]!);
            Assert.True((bool)section["hasSnapshot"]!);
            Assert.Equal(4, (int)section["refreshCount"]!);
            Assert.Equal(2, (int)section["domainCount"]!);
            Assert.Equal(7, (int)section["entityCount"]!);
            Assert.Equal(4, (int)section["interactionCount"]!);

            JArray domains = (JArray)section["domains"]!;
            JObject ship = domains.Cast<JObject>().Single(d => (string?)d["domainId"] == "ship:893");
            Assert.Equal("ship", (string?)ship["kind"]);
            Assert.Equal(2, (int)ship["memberCount"]!);
            Assert.Equal("live hull, under way", (string?)ship["descriptor"]);
            // The reserved inspector slots ride the wire as explicit nulls.
            Assert.Equal(JTokenType.Null, ship["fidelity"]!.Type);
            Assert.Equal(JTokenType.Null, ship["authorityOwner"]!.Type);
            Assert.Equal(JTokenType.Null, ship["migrationGeneration"]!.Type);

            JObject control = ((JArray)section["interactions"]!).Cast<JObject>()
                .Single(i => (string?)i["kind"] == "Control");
            Assert.Equal("player:7", (string?)control["a"]);
            Assert.Equal("ship:893", (string?)control["b"]);
            Assert.Equal("VeryStrong", (string?)control["strength"]);
            Assert.Equal("VeryHigh", (string?)control["latencySensitivity"]);
            Assert.Equal("Active", (string?)control["activity"]);
            Assert.True((bool)control["crossDomain"]!);
            Assert.Equal(JTokenType.Null, control["domainA"]!.Type);
            Assert.Equal("ship:893", (string?)control["domainB"]);
        }

        [Fact]
        public void An_observer_fault_is_reported_rather_than_hidden()
        {
            JObject section = SectionOf(new SimulationRuntimeStat(
                enabled: true, refreshCount: 0, refreshIntervalSeconds: 5,
                error: "InvalidOperationException: boom", snapshot: null));

            Assert.Equal("InvalidOperationException: boom", (string?)section["error"]);
        }

        [Fact]
        public void The_heaviest_rows_survive_the_cap()
        {
            var model = new SimulationWorldModel();
            model.RegisterEntity(new SimulationEntityId("hub"));
            for (int i = 0; i < SimulationRuntimeStat.MaxInteractionRows + 40; i++)
            {
                var peer = new SimulationEntityId("peer:" + i.ToString("D4"));
                model.RegisterEntity(peer);
                // Every tenth edge is the heavy one.
                bool heavy = i % 10 == 0;
                model.UpsertInteraction(new InteractionEdge(
                    new SimulationEntityId("hub"), peer, InteractionKind.Interest,
                    heavy ? InteractionStrength.VeryStrong : InteractionStrength.Weak,
                    heavy ? InteractionLatencySensitivity.VeryHigh : InteractionLatencySensitivity.Low,
                    InteractionActivity.Active));
            }

            SimulationRuntimeStat stat = new SimulationRuntimeStat(
                true, 1, 5, null, model.Snapshot());

            Assert.Equal(SimulationRuntimeStat.MaxInteractionRows, stat.Interactions.Count);
            // The totals above the cap stay exact.
            Assert.Equal(SimulationRuntimeStat.MaxInteractionRows + 40, stat.InteractionCount);
            Assert.Equal(1.0, stat.Interactions[0].Pressure);
        }

        [Fact]
        public void The_section_is_byte_identical_for_the_same_world()
        {
            WorldSnapshot world = SampleWorld();
            string first = SectionOf(new SimulationRuntimeStat(true, 1, 5, null, world)).ToString();
            string second = SectionOf(new SimulationRuntimeStat(true, 1, 5, null, world)).ToString();
            Assert.Equal(first, second);
        }

        [Fact]
        public void A_hostile_descriptor_cannot_break_the_file()
        {
            var model = new SimulationWorldModel();
            model.RegisterDomain(
                SimulationDomainId.ForIsland(new IslandId("haven")),
                "island",
                "quote \" backslash \\ newline \n end");

            JObject section = SectionOf(new SimulationRuntimeStat(true, 1, 5, null, model.Snapshot()));
            Assert.Contains("quote \" backslash \\", (string?)((JArray)section["domains"]!)[0]["descriptor"]);
        }
    }
}

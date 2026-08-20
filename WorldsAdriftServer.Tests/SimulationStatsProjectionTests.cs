using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Simulation;
using WorldsAdriftRebornGameServer.Multiplayer.Simulation.Wareborn;
using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The login server's projection of the interaction shadow model (schema v14).
    ///
    /// The property throughout, same as every other section: an older game server, a
    /// server with the observer off, and a server with nothing coupled are THREE
    /// different answers and must never collapse into one. Plus the usual
    /// allowlisting - the admin page is authenticated output and a field the writer
    /// never promised must not reach it.
    /// </summary>
    public class SimulationStatsProjectionTests
    {
        [Fact]
        public void A_v13_file_with_no_simulation_section_projects_to_absent()
        {
            GameSimulationStat stat = GameSimulationStat.Parse(null);

            Assert.False(stat.Present);
            Assert.False((bool)stat.Json["present"]!);
            Assert.False((bool)stat.Json["enabled"]!);
            Assert.False((bool)stat.Json["hasSnapshot"]!);
            Assert.Equal(JTokenType.Null, stat.Json["error"]!.Type);
            Assert.Empty((JArray)stat.Json["domains"]!);
            Assert.Empty((JArray)stat.Json["interactions"]!);
        }

        [Fact]
        public void A_present_but_disabled_observer_is_distinguishable_from_an_older_server()
        {
            GameSimulationStat stat = GameSimulationStat.Parse(JObject.Parse(@"{
                ""present"": true, ""enabled"": false, ""hasSnapshot"": false,
                ""refreshCount"": 0, ""refreshIntervalSeconds"": 5, ""error"": null,
                ""domainCount"": 0, ""entityCount"": 0, ""interactionCount"": 0,
                ""activeInteractionCount"": 0, ""totalCrossDomainPressure"": 0,
                ""domains"": [], ""interactions"": [] }"));

            Assert.True(stat.Present);
            Assert.False((bool)stat.Json["enabled"]!);
        }

        [Fact]
        public void A_live_section_is_rebuilt_field_by_field_and_nothing_else_gets_through()
        {
            GameSimulationStat stat = GameSimulationStat.Parse(JObject.Parse(@"{
                ""present"": true, ""enabled"": true, ""hasSnapshot"": true,
                ""refreshCount"": 12, ""refreshIntervalSeconds"": 5, ""error"": null,
                ""domainCount"": 2, ""entityCount"": 143, ""interactionCount"": 6,
                ""activeInteractionCount"": 4, ""totalCrossDomainPressure"": 1.02,
                ""fictionalLease"": ""must-not-pass"",
                ""domains"": [ { ""domainId"": ""ship:893"", ""kind"": ""ship"",
                    ""memberCount"": 9, ""activeInteractionCount"": 3, ""pressure"": 0.84,
                    ""descriptor"": ""live hull, under way"", ""fidelity"": null,
                    ""authorityOwner"": null, ""migrationGeneration"": null,
                    ""fictionalWorker"": ""must-not-pass"" } ],
                ""interactions"": [ { ""a"": ""player:7"", ""b"": ""ship:893"",
                    ""kind"": ""Control"", ""strength"": ""VeryStrong"",
                    ""latencySensitivity"": ""VeryHigh"", ""activity"": ""Active"",
                    ""pressure"": 1.0, ""domainA"": null, ""domainB"": ""ship:893"",
                    ""crossDomain"": true, ""fictionalCost"": 9 } ] }"));

            Assert.True(stat.Present);
            Assert.Null(stat.Json["fictionalLease"]);
            Assert.Equal(143, (int)stat.Json["entityCount"]!);
            Assert.Equal(1.02, (double)stat.Json["totalCrossDomainPressure"]!);

            JObject domain = (JObject)((JArray)stat.Json["domains"]!)[0];
            Assert.Null(domain["fictionalWorker"]);
            Assert.Equal("ship:893", (string?)domain["domainId"]);
            Assert.Equal(9, (int)domain["memberCount"]!);
            Assert.Equal(0.84, (double)domain["pressure"]!);
            Assert.Equal("live hull, under way", (string?)domain["descriptor"]);
            // The reserved slots stay explicitly unknown all the way to the browser.
            Assert.Equal(JTokenType.Null, domain["fidelity"]!.Type);
            Assert.Equal(JTokenType.Null, domain["authorityOwner"]!.Type);
            Assert.Equal(JTokenType.Null, domain["migrationGeneration"]!.Type);

            JObject edge = (JObject)((JArray)stat.Json["interactions"]!)[0];
            Assert.Null(edge["fictionalCost"]);
            Assert.Equal("Control", (string?)edge["kind"]);
            Assert.Equal(JTokenType.Null, edge["domainA"]!.Type);
            Assert.Equal("ship:893", (string?)edge["domainB"]);
            Assert.True((bool)edge["crossDomain"]!);
        }

        [Fact]
        public void An_unrecognised_enum_string_becomes_unknown_rather_than_reaching_the_page()
        {
            GameSimulationStat stat = GameSimulationStat.Parse(JObject.Parse(@"{
                ""present"": true,
                ""interactions"": [ { ""a"": ""x"", ""b"": ""y"",
                    ""kind"": ""<script>"", ""strength"": ""Infinite"",
                    ""latencySensitivity"": ""Absolute"", ""activity"": ""Frantic"",
                    ""pressure"": 1 } ] }"));

            JObject edge = (JObject)((JArray)stat.Json["interactions"]!)[0];
            Assert.Equal("unknown", (string?)edge["kind"]);
            Assert.Equal("unknown", (string?)edge["strength"]);
            Assert.Equal("unknown", (string?)edge["latencySensitivity"]);
            Assert.Equal("unknown", (string?)edge["activity"]);
        }

        [Theory]
        [InlineData(9999.0, 1.0)]
        [InlineData(-1.0, 0.0)]
        public void A_hostile_edge_pressure_is_clamped_into_its_real_band(double written, double expected)
        {
            GameSimulationStat stat = GameSimulationStat.Parse(JObject.Parse(@"{
                ""present"": true,
                ""interactions"": [ { ""a"": ""x"", ""b"": ""y"", ""kind"": ""Control"",
                    ""strength"": ""Strong"", ""latencySensitivity"": ""High"",
                    ""activity"": ""Active"", ""pressure"": " + written + @" } ] }"));

            Assert.Equal(expected, (double)((JArray)stat.Json["interactions"]!)[0]["pressure"]!);
        }

        [Fact]
        public void Rows_are_capped_so_a_malformed_file_cannot_become_an_unbounded_dom()
        {
            JArray edges = new JArray();
            for (int i = 0; i < 500; i++)
            {
                edges.Add(new JObject
                {
                    ["a"] = "player:" + i, ["b"] = "ship:1", ["kind"] = "Containment",
                    ["strength"] = "Strong", ["latencySensitivity"] = "High",
                    ["activity"] = "Active", ["pressure"] = 0.5,
                });
            }
            GameSimulationStat stat = GameSimulationStat.Parse(
                new JObject { ["present"] = true, ["interactions"] = edges });

            Assert.Equal(128, ((JArray)stat.Json["interactions"]!).Count);
        }

        [Fact]
        public void A_nameless_domain_or_edge_is_dropped_rather_than_rendered_blank()
        {
            GameSimulationStat stat = GameSimulationStat.Parse(JObject.Parse(@"{
                ""present"": true,
                ""domains"": [ { ""domainId"": """", ""kind"": ""ship"" } ],
                ""interactions"": [ { ""a"": ""x"", ""b"": """", ""kind"": ""Control"" } ] }"));

            Assert.Empty((JArray)stat.Json["domains"]!);
            Assert.Empty((JArray)stat.Json["interactions"]!);
        }

        [Fact]
        public void The_admin_payload_carries_the_section_end_to_end()
        {
            WorldSnapshot world = WarebornSimulationProjection.Project(
                new WarebornWorldObservation(
                    new[] { new ObservedIsland("haven", new long[] { 1, 2, 3 }) },
                    new[] { new ObservedShip(893, new long[] { 900 }, new long[] { 7 }, 7, true, "haven", 60) },
                    new[] { new ObservedPlayer(7, 893, new[] { "haven" }) })).Snapshot();

            string json = new StatsSnapshot(
                bootTimeUnixMs: 0, generatedAtUnixMs: 0, uptimeSeconds: 0,
                relayMode: "raw", relayHz: 0, build: "t",
                totalConnects: 0, totalDisconnects: 0, currentOnline: 0, peakOnline: 0,
                players: Array.Empty<PlayerStat>(),
                simulation: new SimulationRuntimeStat(true, 3, 5, null, world)).ToJson();

            GameStatsSnapshot parsed = GameStatsSnapshot.Parse(JObject.Parse(json));

            Assert.True(parsed.Simulation.Present);
            Assert.Equal(StatsSnapshot.SchemaVersion, parsed.SchemaVersion);
            Assert.True((bool)parsed.Simulation.Json["enabled"]!);
            Assert.Equal(2, (int)parsed.Simulation.Json["domainCount"]!);

            // Joinable with the ownership topology the panel already renders: the two
            // halves must spell the same domain id or the inspector cannot pair them.
            string[] shadowIds = ((JArray)parsed.Simulation.Json["domains"]!)
                .Select(d => (string?)d["domainId"] ?? "").OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "island:haven", "ship:893" }, shadowIds);
        }

        [Fact]
        public void An_old_snapshot_with_every_section_missing_still_parses()
        {
            GameStatsSnapshot parsed = GameStatsSnapshot.Parse(JObject.Parse(
                @"{ ""schemaVersion"": 1, ""players"": [] }"));

            Assert.False(parsed.Simulation.Present);
            Assert.Empty((JArray)parsed.Simulation.Json["domains"]!);
        }
    }
}

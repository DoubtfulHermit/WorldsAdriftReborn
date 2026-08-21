using System;
using System.IO;
using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    public sealed class WorldInspectorStatsProjectionTests
    {
        [Fact]
        public void Older_snapshot_projects_explicit_absence()
        {
            GameWorldInspectorStat stat = GameWorldInspectorStat.Parse(null);

            Assert.False(stat.Present);
            Assert.False((bool)stat.Json["present"]!);
            Assert.False((bool)stat.Json["supported"]!);
            Assert.Equal(0, (int)stat.Json["contractVersion"]!);
            Assert.Empty((JArray)stat.Json["events"]!);
        }

        [Fact]
        public void Supported_contract_is_allowlisted_and_preserves_three_scopes()
        {
            GameWorldInspectorStat stat = GameWorldInspectorStat.Parse(JObject.Parse(@"{
              ""present"":true,""contractVersion"":1,""generatedAtUnixMs"":100,
              ""eventCapacity"":128,""fictionalWorker"":""nope"",
              ""WORLD"":{""connectedPlayerCount"":2,""ownedEntityCount"":20,
                ""resourceCheckoutCount"":7,""fictionalLease"":9},
              ""SIMULATION"":{""shadowEnabled"":true,""shadowHasSnapshot"":true,
                ""shadowRefreshCount"":3,""activeFlightCount"":1,
                ""pilotedFlightCount"":1,""highestAuthorityGeneration"":4},
              ""INFRASTRUCTURE"":{""hostMode"":""local-single-process"",
                ""hostId"":""local:primary"",""processId"":42,
                ""processUptimeSeconds"":90,""remoteWorkers"":99},
              ""events"":[{""sequence"":1,""atUnixMs"":99,""scope"":""WORLD"",
                ""kind"":""domain-added"",""subject"":""ship:9"",
                ""from"":""absent"",""to"":""4"",""secret"":""nope""}] }"));

            Assert.True(stat.Present);
            Assert.True((bool)stat.Json["supported"]!);
            Assert.Null(stat.Json["fictionalWorker"]);
            Assert.Equal(7, (int)stat.Json["WORLD"]!["resourceCheckoutCount"]!);
            Assert.Null(stat.Json["WORLD"]!["fictionalLease"]);
            Assert.Equal("local-single-process",
                (string?)stat.Json["INFRASTRUCTURE"]!["hostMode"]);
            Assert.Null(stat.Json["INFRASTRUCTURE"]!["remoteWorkers"]);
            JObject e = (JObject)((JArray)stat.Json["events"]!)[0];
            Assert.Equal("domain-added", (string?)e["kind"]);
            Assert.Null(e["secret"]);
        }

        [Fact]
        public void Unknown_contract_is_reported_but_not_interpreted()
        {
            GameWorldInspectorStat stat = GameWorldInspectorStat.Parse(JObject.Parse(@"{
              ""present"":true,""contractVersion"":99,
              ""WORLD"":{""ownedEntityCount"":123},
              ""events"":[{""scope"":""WORLD"",""kind"":""domain-added"",
                ""subject"":""ship:9""}] }"));

            Assert.True(stat.Present);
            Assert.False((bool)stat.Json["supported"]!);
            Assert.Equal(99, (int)stat.Json["contractVersion"]!);
            Assert.Equal(0, (int)stat.Json["WORLD"]!["ownedEntityCount"]!);
            Assert.Empty((JArray)stat.Json["events"]!);
        }

        [Fact]
        public void Login_projection_caps_event_rows_and_drops_unknown_labels()
        {
            JArray events = new JArray();
            for (int i = 0; i < 200; i++)
            {
                events.Add(new JObject
                {
                    ["sequence"] = i + 1, ["atUnixMs"] = i,
                    ["scope"] = "WORLD", ["kind"] = "domain-added",
                    ["subject"] = "island:" + i, ["from"] = "absent", ["to"] = "1",
                });
            }
            events.Insert(0, new JObject
            {
                ["scope"] = "SECRET", ["kind"] = "invented-worker",
                ["subject"] = "bad",
            });
            GameWorldInspectorStat stat = GameWorldInspectorStat.Parse(new JObject
            {
                ["present"] = true, ["contractVersion"] = 1,
                ["eventCapacity"] = 9999, ["events"] = events,
            });

            Assert.Equal(128, ((JArray)stat.Json["events"]!).Count);
            Assert.Equal(128, (int)stat.Json["eventCapacity"]!);
            Assert.DoesNotContain((JArray)stat.Json["events"]!,
                x => (string?)x["subject"] == "bad");
        }

        [Fact]
        public void Contract_is_only_wired_into_authenticated_admin_handler()
        {
            string root = RepoRoot();
            string admin = File.ReadAllText(Path.Combine(root,
                "WorldsAdriftServer", "Handlers", "Admin", "AdminHandler.cs"));
            string publicProjection = File.ReadAllText(Path.Combine(root,
                "WorldsAdriftServer", "PublicMap", "PublicMapProjection.cs"));

            Assert.Contains("game[\"worldInspector\"] = s.WorldInspector.Json", admin,
                StringComparison.Ordinal);
            Assert.Contains("if (!authed)", admin, StringComparison.Ordinal);
            Assert.DoesNotContain("worldInspector", publicProjection,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "WorldsAdriftReborn.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate repo root.");
        }
    }
}

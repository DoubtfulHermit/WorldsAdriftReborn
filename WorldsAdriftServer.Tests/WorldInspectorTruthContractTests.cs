using System;
using System.IO;
using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// Trust-boundary and truthfulness checks for the authenticated World Inspector.
    /// Rendering tests elsewhere cover appearance; these pin what the surface is
    /// allowed to claim and how much hostile stats data can reach it.
    /// </summary>
    public sealed class WorldInspectorTruthContractTests
    {
        [Fact]
        public void Runtime_rows_are_bounded_sanitized_and_unique_by_stable_domain_id()
        {
            JArray rows = new JArray();
            rows.Add(new JObject
            {
                ["domainId"] = "ship:0",
                ["kind"] = "<script>",
                ["label"] = new string('l', 300),
                ["hostId"] = new string('h', 300),
                ["entityCount"] = -4,
                ["warningCount"] = int.MaxValue,
                ["x"] = double.NaN,
                ["y"] = double.PositiveInfinity,
                ["z"] = double.NegativeInfinity,
                ["fictionalWorkerLease"] = "must-not-pass",
            });
            rows.Add(new JObject { ["domainId"] = "ship:0", ["label"] = "duplicate" });
            rows.Add(new JObject { ["domainId"] = "   ", ["label"] = "blank" });
            for (int i = 1; i < 700; i++)
                rows.Add(new JObject { ["domainId"] = "ship:" + i, ["kind"] = "ship" });

            GameStatsSnapshot snapshot = GameStatsSnapshot.Parse(new JObject
            {
                ["runtime"] = new JObject { ["domains"] = rows, ["shipDomains"] = new JArray() },
                ["players"] = new JArray(),
            });

            Assert.Equal(GameStatsSnapshot.MaxRuntimeDomains, snapshot.RuntimeDomains.Count);
            JObject first = snapshot.RuntimeDomains[0].Json;
            Assert.Equal("ship:0", (string?)first["domainId"]);
            Assert.Equal("unknown", (string?)first["kind"]);
            Assert.Equal(128, ((string?)first["label"])!.Length);
            Assert.Equal(128, ((string?)first["hostId"])!.Length);
            Assert.Equal(0, (int)first["entityCount"]!);
            Assert.Equal(10_000_000, (int)first["warningCount"]!);
            Assert.Equal(0, (double)first["x"]!);
            Assert.Equal(0, (double)first["y"]!);
            Assert.Equal(0, (double)first["z"]!);
            Assert.Null(first["fictionalWorkerLease"]);
            Assert.DoesNotContain(snapshot.RuntimeDomains,
                row => (string?)row.Json["label"] == "duplicate");
            Assert.DoesNotContain(snapshot.RuntimeDomains,
                row => string.IsNullOrWhiteSpace((string?)row.Json["domainId"]));
        }

        [Fact]
        public void Every_inspector_table_and_event_window_is_capped_at_the_reader_boundary()
        {
            JArray players = Rows(1500, i => new JObject { ["playerEntityId"] = i });
            players[0]!["islands"] = Rows(700, j => new JObject
            {
                ["islandId"] = "island:" + j,
                ["state"] = "ready",
            });
            JArray islands = Rows(900, i => new JObject { ["islandId"] = "island:" + i });
            JArray events = Rows(900, i => new JObject
            {
                ["kind"] = "request", ["islandId"] = "island:" + i,
            });

            GameTerrainStat terrain = GameTerrainStat.Parse(new JObject
            {
                ["players"] = players, ["islands"] = islands, ["events"] = events,
            });

            Assert.Equal(GameTerrainStat.MaxPlayers, ((JArray)terrain.Json["players"]!).Count);
            Assert.Equal(GameTerrainStat.MaxIslands, ((JArray)terrain.Json["islands"]!).Count);
            Assert.Equal(GameTerrainStat.MaxEvents, ((JArray)terrain.Json["events"]!).Count);
            Assert.Equal(GameTerrainStat.MaxIslands,
                ((JArray)terrain.Json["players"]![0]!["islands"]!).Count);
        }

        [Fact]
        public void Old_schema_absence_and_exact_selection_identity_remain_supported()
        {
            GameStatsSnapshot old = GameStatsSnapshot.Parse(
                JObject.Parse(@"{ ""schemaVersion"": 1, ""players"": [] }"));
            Assert.Empty(old.RuntimeDomains);
            Assert.Empty(old.ShipDomains);
            Assert.False(old.Simulation.Present);

            string html = AdminPage.Dashboard("{}", new string('a', 64));
            Assert.Contains("d.domainId===selectedRuntimeDomainId", html, StringComparison.Ordinal);
            Assert.Contains("selectedRuntimeDomainId='';renderDomainInventory()", html,
                StringComparison.Ordinal);
            Assert.DoesNotContain("d.label===selectedRuntimeDomainId", html,
                StringComparison.Ordinal);
        }

        [Fact]
        public void Unimplemented_distributed_states_are_explicitly_unavailable()
        {
            string html = AdminPage.Dashboard("{}", new string('b', 64));
            Assert.Contains("Unavailable in this build:", html, StringComparison.Ordinal);
            Assert.Contains("remote workers", html, StringComparison.Ordinal);
            Assert.Contains("migration of domain ownership", html, StringComparison.Ordinal);
            Assert.Contains("domain sleep", html, StringComparison.Ordinal);
            Assert.Contains("resting", html, StringComparison.Ordinal);
            Assert.Contains("not a sleeping simulation domain", html, StringComparison.Ordinal);
        }

        [Fact]
        public void Inspector_stats_are_session_gated_and_html_escaped_before_bootstrap()
        {
            string source = Source("WorldsAdriftServer", "Handlers", "Admin", "AdminHandler.cs");
            int route = source.IndexOf("path == \"/admin/api/stats\"", StringComparison.Ordinal);
            int refusal = source.IndexOf("if (!authed)", route, StringComparison.Ordinal);
            int payload = source.IndexOf("Json(session, 200, BuildStatsJson())", route,
                StringComparison.Ordinal);
            Assert.True(route >= 0 && refusal > route && payload > refusal,
                "the inspector payload must not be served before the session gate");
            Assert.Contains("StringEscapeHandling.EscapeHtml", source, StringComparison.Ordinal);

            string publicPage = PublicMapPage.Html("{}", "{}");
            Assert.DoesNotContain("domainInventory", publicPage, StringComparison.Ordinal);
            Assert.DoesNotContain("simulationInteractions", publicPage, StringComparison.Ordinal);
        }

        private static JArray Rows(int count, Func<int, JObject> row)
        {
            JArray result = new JArray();
            for (int i = 0; i < count; i++) result.Add(row(i));
            return result;
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        private static string RepoRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "WorldsAdriftReborn.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}

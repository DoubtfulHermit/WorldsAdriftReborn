using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The interest section of the stats snapshot (schema v10): the cross-process
    /// CONTRACT the login server's streaming view renders from. Shapes are pinned
    /// here the same way the terrain and fauna sections pin theirs - by parsing
    /// the emitted document, so a renamed field has to walk past a red test.
    /// </summary>
    public class InterestStatsSnapshotTests
    {
        private static StatsSnapshot Snapshot(InterestRuntimeStat interest) =>
            new StatsSnapshot(
                bootTimeUnixMs: 1000, generatedAtUnixMs: 2000, uptimeSeconds: 1,
                relayMode: "v2@20Hz", relayHz: 20, build: "test",
                totalConnects: 0, totalDisconnects: 0, currentOnline: 0, peakOnline: 0,
                players: System.Array.Empty<PlayerStat>(),
                interest: interest);

        private static InterestRuntimeStat Built(
            System.Collections.Generic.IReadOnlyList<InterestPeerStat>? peers = null) =>
            new InterestRuntimeStat(
                resourcesEnabled: true,
                resourceLoadRadiusMetres: 600,
                resourceUnloadRadiusMetres: 800,
                resourcePerPeerBudget: 512,
                resourceConnectRadiusMetres: 45,
                faunaEnabled: true,
                faunaLoadRadiusMetres: 600,
                faunaUnloadRadiusMetres: 800,
                shipLoadRadiusMetres: 800,
                shipUnloadRadiusMetres: 1000,
                terrainConnectRadiusMetres: 4000,
                loadBarrier: true,
                spawnPaceMs: 40,
                peers: peers);

        [Fact]
        public void A_server_that_predates_the_section_reports_present_false_not_numbers()
        {
            // default(InterestRuntimeStat) is what an unwired caller passes; it
            // must serialise as an explicit absence a reader can distinguish
            // from "interest is configured to zero".
            JObject o = JObject.Parse(Snapshot(InterestRuntimeStat.Off).ToJson());
            JObject interest = (JObject)o["interest"]!;

            Assert.False((bool)interest["present"]!);
            Assert.Empty((JArray)interest["peers"]!);
            // And the document as a whole is still one valid object.
            Assert.Equal(10, (int)o["schemaVersion"]!);
        }

        [Fact]
        public void The_configured_radii_budgets_and_gates_ride_the_wire_by_their_names()
        {
            JObject interest = (JObject)JObject.Parse(Snapshot(Built()).ToJson())["interest"]!;

            Assert.True((bool)interest["present"]!);
            Assert.True((bool)interest["resources"]!["enabled"]!);
            Assert.Equal(600, (double)interest["resources"]!["loadRadiusMetres"]!);
            Assert.Equal(800, (double)interest["resources"]!["unloadRadiusMetres"]!);
            Assert.Equal(512, (int)interest["resources"]!["perPeerBudget"]!);
            Assert.Equal(45, (double)interest["resources"]!["connectRadiusMetres"]!);
            Assert.Equal(600, (double)interest["fauna"]!["loadRadiusMetres"]!);
            Assert.Equal(800, (double)interest["fauna"]!["unloadRadiusMetres"]!);
            Assert.Equal(800, (double)interest["ship"]!["loadRadiusMetres"]!);
            Assert.Equal(1000, (double)interest["ship"]!["unloadRadiusMetres"]!);
            Assert.Equal(4000, (double)interest["terrainConnectRadiusMetres"]!);
            Assert.True((bool)interest["gates"]!["loadBarrier"]!);
            Assert.Equal(40, (int)interest["gates"]!["spawnPaceMs"]!);
        }

        [Fact]
        public void The_ship_connect_step_IS_the_ship_load_radius()
        {
            // One value, two names on the wire, sourced from the same field so
            // they cannot drift apart.
            JObject ship = (JObject)JObject.Parse(Snapshot(Built()).ToJson())["interest"]!["ship"]!;
            Assert.Equal((double)ship["loadRadiusMetres"]!, (double)ship["connectRadiusMetres"]!);
        }

        [Fact]
        public void A_peers_holdings_are_counted_per_island_and_summed_consistently()
        {
            InterestPeerStat peer = new InterestPeerStat(
                42,
                new[]
                {
                    new InterestPeerIslandStat("haven", 19),
                    new InterestPeerIslandStat("mental-facility", 7),
                },
                faunaCheckedOut: 12,
                shipDomainIds: new[] { "ship:7001" });

            // The total is DERIVED from the per-island rows, so the two numbers
            // a reader shows side by side cannot disagree.
            Assert.Equal(26, peer.ResourceCheckedOut);

            JObject row = (JObject)((JArray)JObject.Parse(
                Snapshot(Built(new[] { peer })).ToJson())["interest"]!["peers"]!)[0];
            Assert.Equal(42, (long)row["playerEntityId"]!);
            Assert.Equal(26, (int)row["resourceCheckedOut"]!);
            Assert.Equal(12, (int)row["faunaCheckedOut"]!);
            Assert.Equal(19, (int)row["resourceIslands"]![0]!["checkedOut"]!);
            Assert.Equal("mental-facility", (string?)row["resourceIslands"]![1]!["islandId"]);
            Assert.Equal("ship:7001", (string?)((JArray)row["shipDomainIds"]!)[0]);
        }

        [Fact]
        public void Nonsense_negative_counts_clamp_to_zero_rather_than_riding_the_wire()
        {
            InterestRuntimeStat stat = new InterestRuntimeStat(
                resourcesEnabled: false,
                resourceLoadRadiusMetres: 0, resourceUnloadRadiusMetres: 0,
                resourcePerPeerBudget: -5, resourceConnectRadiusMetres: 0,
                faunaEnabled: false, faunaLoadRadiusMetres: 0, faunaUnloadRadiusMetres: 0,
                shipLoadRadiusMetres: 0, shipUnloadRadiusMetres: 0,
                terrainConnectRadiusMetres: 0,
                loadBarrier: false, spawnPaceMs: -1,
                peers: new[] { new InterestPeerStat(1, null, faunaCheckedOut: -3, shipDomainIds: null) });

            JObject interest = (JObject)JObject.Parse(Snapshot(stat).ToJson())["interest"]!;
            Assert.Equal(0, (int)interest["resources"]!["perPeerBudget"]!);
            Assert.Equal(0, (int)interest["gates"]!["spawnPaceMs"]!);
            Assert.Equal(0, (int)((JArray)interest["peers"]!)[0]!["faunaCheckedOut"]!);
        }
    }
}

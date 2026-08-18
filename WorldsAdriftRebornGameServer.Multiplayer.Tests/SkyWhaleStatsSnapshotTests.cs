using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The game server's end of the sky whale contract (schema v12).
    ///
    /// The property under test is the one every section of this file has: a
    /// server with the whale switched OFF must be distinguishable from a server
    /// that PREDATES it. The first writes <c>skyWhale.enabled=false</c>; the
    /// second writes no section at all, and the login server's projection turns
    /// that into <c>present:false</c>. A map that draws nothing must be able to
    /// say which one it is looking at, and neither may masquerade as the other.
    /// </summary>
    public class SkyWhaleStatsSnapshotTests
    {
        private static JObject Whale(SkyWhaleRuntimeStat? whale) =>
            (JObject)JObject.Parse(new StatsSnapshot(
                bootTimeUnixMs: 0, generatedAtUnixMs: 0, uptimeSeconds: 0,
                relayMode: "v2", relayHz: 20, build: "test",
                totalConnects: 0, totalDisconnects: 0, currentOnline: 0, peakOnline: 0,
                players: Array.Empty<PlayerStat>(),
                skyWhale: whale).ToJson())["skyWhale"]!;

        private static SkyWhaleStat Row(string regionId = "release-b3-region",
            string nextRegionId = "release-b2-region") =>
            new SkyWhaleStat(
                SkyWhaleRoute.RouteIdFor(new[] { "b3" }),
                SkyWhalePolicy.FirstWhaleEntityId,
                SkyWhalePolicy.FirstWhaleEntityId + 1,
                42, 7000.5, 480.25, -6100.75,
                regionId, nextRegionId, "island-entry", 930.5, "island-next", 61.25);

        [Fact]
        public void A_server_with_the_whale_off_says_so_rather_than_omitting_the_section()
        {
            JObject w = Whale(null);
            Assert.False((bool)w["enabled"]!);
            Assert.Equal(0, (int)w["whaleCount"]!);
            Assert.Empty((JArray)w["whales"]!);
        }

        [Fact]
        public void A_live_whale_carries_its_route_its_ids_its_call_and_where_it_is_going()
        {
            JObject w = Whale(new SkyWhaleRuntimeStat(
                enabled: true, clockSeconds: 1234.5,
                loadRadiusMetres: 1200, callRadiusMetres: 4000,
                poseIntervalMs: 500, callIntervalSeconds: 120,
                whales: new[] { Row() }));

            Assert.True((bool)w["enabled"]!);
            Assert.Equal(1234.5, (double)w["clockSeconds"]!);
            Assert.Equal(1, (int)w["whaleCount"]!);

            JObject whale = (JObject)((JArray)w["whales"]!)[0];
            Assert.Equal(SkyWhaleRoute.RouteIdFor(new[] { "b3" }), (string?)whale["routeId"]);
            Assert.Equal(SkyWhalePolicy.FirstWhaleEntityId, (long)whale["entityId"]!);
            Assert.Equal(SkyWhalePolicy.FirstWhaleEntityId + 1, (long)whale["callEntityId"]!);
            Assert.Equal(42L, (long)whale["callIndex"]!);
            Assert.Equal(7000.5, (double)whale["callX"]!);
            Assert.Equal(480.25, (double)whale["callY"]!);
            Assert.Equal(-6100.75, (double)whale["callZ"]!);

            // THE MIGRATION, on the wire. Without these a reader can draw the animal
            // and still cannot answer the only question a single whale raises.
            Assert.Equal("release-b3-region", (string?)whale["regionId"]);
            Assert.Equal("release-b2-region", (string?)whale["nextRegionId"]);
            Assert.Equal("island-entry", (string?)whale["nextRegionIslandId"]);
            Assert.Equal(930.5, (double)whale["nextRegionSeconds"]!);
            Assert.Equal("island-next", (string?)whale["nextIslandId"]);
            Assert.Equal(61.25, (double)whale["nextIslandSeconds"]!);
        }

        [Fact]
        public void A_whale_between_zones_reports_an_EMPTY_zone_rather_than_the_next_one()
        {
            // "Between zones" is a REAL answer and it has to survive the wire
            // intact. Filling it in with nextRegionId would send a player to a cell
            // the animal has not reached, and filling it in with the LAST zone would
            // send them to one it has already left - the two failures a single
            // migrating whale makes possible for the first time.
            JObject whale = (JObject)((JArray)Whale(new SkyWhaleRuntimeStat(
                enabled: true, clockSeconds: 5.0, loadRadiusMetres: 1200,
                callRadiusMetres: 4000, poseIntervalMs: 500, callIntervalSeconds: 120,
                whales: new[] { Row(regionId: "") }))["whales"]!)[0];

            Assert.Equal("", (string?)whale["regionId"]);
            Assert.Equal("release-b2-region", (string?)whale["nextRegionId"]);
        }

        [Fact]
        public void The_section_carries_no_position_for_the_animal_itself()
        {
            // THE DESIGN CLAIM, pinned. The whale's pose is a closed form of
            // clockSeconds, so a reader that has the clock and the circuit places
            // it exactly; shipping a position three times a minute and calling
            // that live would be the thing this whole architecture avoids. The
            // CALL is carried, and only the call, because it is a discrete event
            // pinned to one place for two minutes rather than a pose that moves.
            JObject whale = (JObject)((JArray)Whale(new SkyWhaleRuntimeStat(
                enabled: true, clockSeconds: 1.0, loadRadiusMetres: 1200,
                callRadiusMetres: 4000, poseIntervalMs: 500, callIntervalSeconds: 120,
                whales: new[] { Row() }))["whales"]!)[0];

            foreach (string forbidden in new[] { "x", "y", "z", "lap", "position" })
            {
                Assert.Null(whale[forbidden]);
            }
        }
    }
}

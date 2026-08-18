using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The game server's end of the sky whale contract (schema v11).
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

        [Fact]
        public void A_server_with_the_whale_off_says_so_rather_than_omitting_the_section()
        {
            JObject w = Whale(null);
            Assert.False((bool)w["enabled"]!);
            Assert.Equal(0, (int)w["whaleCount"]!);
            Assert.Empty((JArray)w["regions"]!);
        }

        [Fact]
        public void A_live_whale_carries_its_region_its_ids_and_its_current_call()
        {
            JObject w = Whale(new SkyWhaleRuntimeStat(
                enabled: true, clockSeconds: 1234.5,
                loadRadiusMetres: 1200, callRadiusMetres: 4000,
                poseIntervalMs: 500, callIntervalSeconds: 120,
                regions: new[]
                {
                    new SkyWhaleRegionStat("release-b3-region",
                        SkyWhalePolicy.FirstWhaleEntityId,
                        SkyWhalePolicy.FirstWhaleEntityId + 1,
                        42, 7000.5, 480.25, -6100.75),
                }));

            Assert.True((bool)w["enabled"]!);
            Assert.Equal(1234.5, (double)w["clockSeconds"]!);
            Assert.Equal(1, (int)w["whaleCount"]!);

            JObject region = (JObject)((JArray)w["regions"]!)[0];
            Assert.Equal("release-b3-region", (string?)region["regionId"]);
            Assert.Equal(SkyWhalePolicy.FirstWhaleEntityId, (long)region["entityId"]!);
            Assert.Equal(SkyWhalePolicy.FirstWhaleEntityId + 1, (long)region["callEntityId"]!);
            Assert.Equal(42L, (long)region["callIndex"]!);
            Assert.Equal(7000.5, (double)region["callX"]!);
            Assert.Equal(480.25, (double)region["callY"]!);
            Assert.Equal(-6100.75, (double)region["callZ"]!);
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
            JObject region = (JObject)((JArray)Whale(new SkyWhaleRuntimeStat(
                enabled: true, clockSeconds: 1.0, loadRadiusMetres: 1200,
                callRadiusMetres: 4000, poseIntervalMs: 500, callIntervalSeconds: 120,
                regions: new[]
                {
                    new SkyWhaleRegionStat("release-b3-region", 1, 2, 0, 0, 0, 0),
                }))["regions"]!)[0];

            foreach (string forbidden in new[] { "x", "y", "z", "lap", "position" })
            {
                Assert.Null(region[forbidden]);
            }
        }
    }
}

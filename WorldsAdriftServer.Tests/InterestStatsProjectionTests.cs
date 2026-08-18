using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The login server's projection of the game server's interest section
    /// (schema v10). The property throughout: absence and zero are DIFFERENT
    /// answers. The streaming view draws these radii as world-metre circles on
    /// the operator map, so the magnitude clamp is load-bearing.
    /// </summary>
    public class InterestStatsProjectionTests
    {
        [Fact]
        public void A_v8_file_with_no_interest_section_projects_to_absent_not_zeroes()
        {
            GameInterestStat stat = GameInterestStat.Parse(null);

            Assert.False(stat.Present);
            Assert.False((bool)stat.Json["present"]!);
            // The gate answer is null - "never said" - not false.
            Assert.Equal(JTokenType.Null, stat.Json["gates"]!["loadBarrier"]!.Type);
            Assert.Empty((JArray)stat.Json["peers"]!);
        }

        [Fact]
        public void A_live_section_is_rebuilt_field_by_field()
        {
            GameInterestStat stat = GameInterestStat.Parse(JObject.Parse(@"{
                ""present"": true,
                ""resources"": { ""enabled"": true, ""loadRadiusMetres"": 600,
                    ""unloadRadiusMetres"": 800, ""perPeerBudget"": 512,
                    ""connectRadiusMetres"": 45 },
                ""fauna"": { ""enabled"": true, ""loadRadiusMetres"": 600, ""unloadRadiusMetres"": 800 },
                ""ship"": { ""loadRadiusMetres"": 800, ""unloadRadiusMetres"": 1000, ""connectRadiusMetres"": 800 },
                ""terrainConnectRadiusMetres"": 4000,
                ""gates"": { ""loadBarrier"": true, ""spawnPaceMs"": 40 },
                ""peers"": [ { ""playerEntityId"": 42, ""resourceCheckedOut"": 26,
                    ""faunaCheckedOut"": 12,
                    ""resourceIslands"": [ { ""islandId"": ""haven"", ""checkedOut"": 19 } ],
                    ""shipDomainIds"": [ ""ship:7001"" ] } ]
            }"));

            Assert.True(stat.Present);
            Assert.Equal(600, (double)stat.Json["resources"]!["loadRadiusMetres"]!);
            Assert.Equal(512, (int)stat.Json["resources"]!["perPeerBudget"]!);
            Assert.Equal(45, (double)stat.Json["resources"]!["connectRadiusMetres"]!);
            Assert.Equal(4000, (double)stat.Json["terrainConnectRadiusMetres"]!);
            Assert.True((bool)stat.Json["gates"]!["loadBarrier"]!);
            Assert.Equal(40, (int)stat.Json["gates"]!["spawnPaceMs"]!);
            JObject peer = (JObject)((JArray)stat.Json["peers"]!)[0];
            Assert.Equal(42, (long)peer["playerEntityId"]!);
            Assert.Equal("haven", (string?)peer["resourceIslands"]![0]!["islandId"]);
            Assert.Equal("ship:7001", (string?)((JArray)peer["shipDomainIds"]!)[0]);
        }

        [Fact]
        public void Hostile_radii_are_clamped_before_they_can_stretch_the_map()
        {
            GameInterestStat stat = GameInterestStat.Parse(JObject.Parse(@"{
                ""present"": true,
                ""resources"": { ""enabled"": true, ""loadRadiusMetres"": 1e12,
                    ""unloadRadiusMetres"": -50, ""perPeerBudget"": -1, ""connectRadiusMetres"": ""NaN"" },
                ""gates"": { ""spawnPaceMs"": 999999999 }
            }"));

            Assert.Equal(100_000, (double)stat.Json["resources"]!["loadRadiusMetres"]!);
            Assert.Equal(0, (double)stat.Json["resources"]!["unloadRadiusMetres"]!);
            Assert.Equal(0, (int)stat.Json["resources"]!["perPeerBudget"]!);
            Assert.Equal(3_600_000, (int)stat.Json["gates"]!["spawnPaceMs"]!);
        }

        [Fact]
        public void Peer_lists_are_capped_so_a_malformed_file_stays_a_bounded_page()
        {
            JArray peers = new JArray();
            for (int i = 0; i < 400; i++)
            {
                peers.Add(new JObject { ["playerEntityId"] = i });
            }
            GameInterestStat stat = GameInterestStat.Parse(new JObject
            {
                ["present"] = true,
                ["peers"] = peers,
            });

            Assert.Equal(256, ((JArray)stat.Json["peers"]!).Count);
        }

        [Fact]
        public void The_admin_payload_carries_the_section_end_to_end()
        {
            // The same tolerance GameStatsReaderTests pins for older sections:
            // a full v10 snapshot from the game side must round-trip through
            // the reader with the interest section attached.
            string json = new WorldsAdriftRebornGameServer.Multiplayer.StatsSnapshot(
                bootTimeUnixMs: 0, generatedAtUnixMs: 0, uptimeSeconds: 0,
                relayMode: "raw", relayHz: 0, build: "t",
                totalConnects: 0, totalDisconnects: 0, currentOnline: 0, peakOnline: 0,
                players: System.Array.Empty<WorldsAdriftRebornGameServer.Multiplayer.PlayerStat>(),
                interest: new WorldsAdriftRebornGameServer.Multiplayer.InterestRuntimeStat(
                    resourcesEnabled: true, resourceLoadRadiusMetres: 600,
                    resourceUnloadRadiusMetres: 800, resourcePerPeerBudget: 512,
                    resourceConnectRadiusMetres: 45,
                    faunaEnabled: true, faunaLoadRadiusMetres: 600, faunaUnloadRadiusMetres: 800,
                    shipLoadRadiusMetres: 800, shipUnloadRadiusMetres: 1000,
                    terrainConnectRadiusMetres: 4000,
                    loadBarrier: true, spawnPaceMs: 40, peers: null)).ToJson();

            GameStatsSnapshot parsed = GameStatsSnapshot.Parse(JObject.Parse(json));

            Assert.True(parsed.Interest.Present);
            Assert.Equal(WorldsAdriftRebornGameServer.Multiplayer.StatsSnapshot.SchemaVersion,
                parsed.SchemaVersion);
            Assert.True((bool)parsed.Interest.Json["gates"]!["loadBarrier"]!);
        }
    }
}

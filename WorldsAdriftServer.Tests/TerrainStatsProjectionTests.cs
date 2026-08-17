using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The login server's end of the terrain contract. It REBUILDS an allowlisted
    /// object rather than forwarding the file's, so these tests pin two things:
    /// that a v5 game server's terrain lifecycle survives the crossing intact,
    /// and that a v4 one - or a malformed section - degrades to a state the
    /// dashboard can render instead of throwing or echoing junk.
    /// </summary>
    public class TerrainStatsProjectionTests
    {
        private static readonly DateTimeOffset Now =
            DateTimeOffset.FromUnixTimeMilliseconds(1_723_200_123_000);

        private const string Head = @"
          ""schemaVersion"":5,
          ""bootTimeUnixMs"":1723200000000,
          ""generatedAtUnixMs"":1723200120000,
          ""uptimeSeconds"":120,
          ""relayMode"":""v2@20Hz"",
          ""relayHz"":20,
          ""build"":""abc1234"",
          ""totalConnects"":1,
          ""totalDisconnects"":0,
          ""currentOnline"":2,
          ""peakOnline"":2,
          ""players"":[],";

        private const string TerrainJson = @"{" + Head + @"
          ""terrain"":{
            ""requested"":true,""enabled"":true,""mode"":""on"",
            ""hostId"":""local:primary"",""authority"":""process-local-poll-loop"",
            ""loadRadiusMetres"":1200.0,""unloadRadiusMetres"":1600.0,
            ""assetAckTimeoutMs"":30000,""settleDelayMs"":3000,
            ""candidateCount"":2,""trackedPeerCount"":2,""readyCount"":2,
            ""warningCount"":1,""errorCount"":0,""eventCapacity"":64,
            ""stateCounts"":{""absent"":1,""requesting"":0,""waiting-ack"":1,""ready"":1,
              ""draining"":0,""unloading"":0,""retained-legacy"":1,""error"":0},
            ""players"":[
              {""playerEntityId"":11,""slot"":1,""x"":1.5,""y"":2.5,""z"":3.5,
               ""confirmedGroundIslandId"":""haven"",""requestedDestinationIslandId"":""mental-facility"",
               ""pendingAction"":""load"",""pendingIslandId"":""mental-facility"",
               ""correlatedAckObserved"":true,""removeSupported"":true,""mayRemove"":true,
               ""legacyRetaining"":false,""connectPlanComplete"":true,""settleWaiting"":false,
               ""destinationWaiting"":true,""readyCount"":0,""warning"":""waiting for requested destination terrain"",
               ""asset"":{""islandId"":""mental-facility"",""assetName"":""TerrainAsset"",
                 ""requestAgeMs"":4200,""lastRetryAgeMs"":1200,""retryCount"":2,
                 ""acknowledged"":false,""fallbackDue"":false},
               ""islands"":[{""islandId"":""mental-facility"",""state"":""waiting-ack""},
                            {""islandId"":""highlands-hills"",""state"":""absent""}]},
              {""playerEntityId"":22,""slot"":2,""x"":0,""y"":0,""z"":0,
               ""confirmedGroundIslandId"":null,""requestedDestinationIslandId"":null,
               ""pendingAction"":""none"",""pendingIslandId"":null,
               ""correlatedAckObserved"":false,""removeSupported"":true,""mayRemove"":false,
               ""legacyRetaining"":true,""connectPlanComplete"":true,""settleWaiting"":false,
               ""destinationWaiting"":false,""readyCount"":2,""warning"":"""",
               ""asset"":null,
               ""islands"":[{""islandId"":""mental-facility"",""state"":""ready""},
                            {""islandId"":""highlands-hills"",""state"":""retained-legacy""}]}
            ],
            ""islands"":[
              {""islandId"":""mental-facility"",""displayName"":""Mental Facility"",
               ""terrainEntityId"":900,""registered"":true,""locallyOwned"":true,
               ""hasEnvelope"":true,""managed"":true,""unconditional"":false,
               ""readyPeerCount"":1,""loadingPeerCount"":1,""drainingPeerCount"":0,
               ""unloadingPeerCount"":0,""retainedLegacyPeerCount"":0,""errorPeerCount"":0,
               ""resourceNodeCount"":12,""checkedOutResourceCount"":3,""resourceDrainWired"":true,
               ""envelope"":{""minX"":-176.7,""minY"":-92.4,""minZ"":-115.7,
                 ""maxX"":176.8,""maxY"":48.4,""maxZ"":104.9,
                 ""spanX"":353.5,""spanY"":140.8,""spanZ"":220.6}},
              {""islandId"":""haven"",""displayName"":""Haven"",""terrainEntityId"":100,
               ""registered"":true,""locallyOwned"":true,""hasEnvelope"":true,
               ""managed"":false,""unconditional"":true,
               ""readyPeerCount"":0,""loadingPeerCount"":0,""drainingPeerCount"":0,
               ""unloadingPeerCount"":0,""retainedLegacyPeerCount"":0,""errorPeerCount"":0,
               ""resourceNodeCount"":-1,""checkedOutResourceCount"":-1,""resourceDrainWired"":false,
               ""envelope"":null}
            ],
            ""events"":[
              {""ageMs"":1200,""kind"":""asset-ack"",""islandId"":""mental-facility"",
               ""playerEntityId"":11,""slot"":1,""success"":true},
              {""ageMs"":9000,""kind"":""remove-failed"",""islandId"":""highlands-hills"",
               ""playerEntityId"":22,""slot"":2,""success"":false}
            ]
          }
        }";

        /// <summary>A schema-v4 game server: correct, complete, and terrain-less.</summary>
        private const string LegacyJson = @"{
          ""schemaVersion"":4,
          ""bootTimeUnixMs"":1723200000000,
          ""generatedAtUnixMs"":1723200120000,
          ""uptimeSeconds"":120,
          ""relayMode"":""v2@20Hz"",
          ""relayHz"":20,
          ""build"":""abc1234"",
          ""totalConnects"":1,
          ""totalDisconnects"":0,
          ""currentOnline"":0,
          ""peakOnline"":1,
          ""players"":[]
        }";

        private static GameStatsSnapshot Parse(string json)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "wareborn-terrain-test-" + Guid.NewGuid().ToString("n") + ".json");
            File.WriteAllText(path, json);
            try
            {
                GameStatsResult result = GameStats.ReadFrom(path, Now);
                Assert.Equal(GameStatsState.Ok, result.State);
                return result.Snapshot!;
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void An_older_game_server_without_a_terrain_section_parses_to_a_defined_absent_state()
        {
            GameStatsSnapshot s = Parse(LegacyJson);

            Assert.Equal(4, s.SchemaVersion);
            Assert.False(s.Terrain.Present);
            Assert.Equal("unknown", s.Terrain.Mode);

            JObject t = s.Terrain.Json;
            Assert.False((bool)t["present"]!);
            Assert.Equal("unknown", (string)t["mode"]!);
            Assert.False((bool)t["requested"]!);
            Assert.False((bool)t["enabled"]!);
            Assert.Equal("unknown", (string)t["hostId"]!);
            Assert.Empty((JArray)t["players"]!);
            Assert.Empty((JArray)t["islands"]!);
            Assert.Empty((JArray)t["events"]!);
            // The count keys still exist, so the dashboard renders zeros rather
            // than undefined.
            Assert.Equal(0, (int)t["stateCounts"]!["ready"]!);
        }

        [Fact]
        public void A_v5_terrain_section_crosses_the_process_boundary_intact()
        {
            GameStatsSnapshot s = Parse(TerrainJson);
            JObject t = s.Terrain.Json;

            Assert.True(s.Terrain.Present);
            Assert.Equal("on", s.Terrain.Mode);
            Assert.True((bool)t["requested"]!);
            Assert.True((bool)t["enabled"]!);
            Assert.Equal("local:primary", (string)t["hostId"]!);
            Assert.Equal("process-local-poll-loop", (string)t["authority"]!);
            Assert.Equal(1200.0, (double)t["loadRadiusMetres"]!);
            Assert.Equal(1600.0, (double)t["unloadRadiusMetres"]!);
            Assert.Equal(30000, (long)t["assetAckTimeoutMs"]!);
            Assert.Equal(3000, (long)t["settleDelayMs"]!);
            Assert.Equal(2, (int)t["candidateCount"]!);
            Assert.Equal(64, (int)t["eventCapacity"]!);
            Assert.Equal(1, (int)t["stateCounts"]!["retained-legacy"]!);
        }

        [Fact]
        public void Two_players_keep_separate_lifecycles_and_neither_leaks_into_the_other()
        {
            JArray players = (JArray)Parse(TerrainJson).Terrain.Json["players"]!;

            Assert.Equal(2, players.Count);
            Assert.Equal(11, (long)players[0]["playerEntityId"]!);
            Assert.Equal("load", (string)players[0]["pendingAction"]!);
            Assert.Equal("mental-facility", (string)players[0]["pendingIslandId"]!);
            Assert.Equal("haven", (string)players[0]["confirmedGroundIslandId"]!);
            Assert.True((bool)players[0]["destinationWaiting"]!);
            Assert.Equal("waiting-ack", (string)players[0]["islands"]![0]!["state"]!);
            Assert.Equal(2, (int)players[0]["asset"]!["retryCount"]!);

            Assert.Equal(22, (long)players[1]["playerEntityId"]!);
            Assert.Equal("none", (string)players[1]["pendingAction"]!);
            // A peer whose ground is unconfirmed serializes as null, never as an
            // empty island id that would read as a real place.
            Assert.Null(((JValue)players[1]["confirmedGroundIslandId"]!).Value);
            Assert.Contains("\"confirmedGroundIslandId\":null", players[1].ToString(
                Newtonsoft.Json.Formatting.None));
            Assert.Equal(JTokenType.Null, players[1]["asset"]!.Type);
            Assert.True((bool)players[1]["legacyRetaining"]!);
            Assert.False((bool)players[1]["mayRemove"]!);
            Assert.Equal("retained-legacy", (string)players[1]["islands"]![1]!["state"]!);
        }

        [Fact]
        public void Island_rows_carry_registration_peer_counts_and_resource_truth()
        {
            JArray islands = (JArray)Parse(TerrainJson).Terrain.Json["islands"]!;

            Assert.Equal(2, islands.Count);
            Assert.True((bool)islands[0]["managed"]!);
            Assert.Equal(900, (long)islands[0]["terrainEntityId"]!);
            Assert.Equal(1, (int)islands[0]["readyPeerCount"]!);
            Assert.Equal(1, (int)islands[0]["loadingPeerCount"]!);
            Assert.Equal(12, (int)islands[0]["resourceNodeCount"]!);
            Assert.Equal(3, (int)islands[0]["checkedOutResourceCount"]!);
            Assert.True((bool)islands[0]["resourceDrainWired"]!);
            Assert.Equal(353.5, (double)islands[0]["envelope"]!["spanX"]!);

            Assert.True((bool)islands[1]["unconditional"]!);
            Assert.Equal(JTokenType.Null, islands[1]["envelope"]!.Type);
            // Unknown stays unknown: -1 must never be normalised to a truthful-
            // looking zero.
            Assert.Equal(-1, (int)islands[1]["resourceNodeCount"]!);
        }

        [Fact]
        public void Recent_events_survive_with_their_kind_island_and_result()
        {
            JArray events = (JArray)Parse(TerrainJson).Terrain.Json["events"]!;

            Assert.Equal(2, events.Count);
            Assert.Equal("asset-ack", (string)events[0]["kind"]!);
            Assert.Equal(1200, (long)events[0]["ageMs"]!);
            Assert.True((bool)events[0]["success"]!);
            Assert.Equal("remove-failed", (string)events[1]["kind"]!);
            Assert.False((bool)events[1]["success"]!);
        }

        [Fact]
        public void Unrecognised_labels_and_unexpected_fields_never_reach_the_console()
        {
            string hostile = @"{" + Head + @"
              ""terrain"":{
                ""mode"":""definitely-fine"",
                ""players"":[{""playerEntityId"":1,""slot"":1,
                  ""pendingAction"":""rm -rf"",
                  ""secretPath"":""/etc/shadow"",
                  ""islands"":[{""islandId"":""a"",""state"":""exploded""}]}],
                ""islands"":[{""islandId"":""a"",""displayName"":""A"",""surprise"":true}],
                ""events"":[{""kind"":""arbitrary"",""islandId"":""a"",""peerPointer"":""0xdeadbeef""}]
              }
            }";

            JObject t = Parse(hostile).Terrain.Json;

            Assert.Equal("unknown", (string)t["mode"]!);
            Assert.Equal("unknown", (string)t["players"]![0]!["pendingAction"]!);
            Assert.Equal("unknown", (string)t["players"]![0]!["islands"]![0]!["state"]!);
            Assert.Equal("unknown", (string)t["events"]![0]!["kind"]!);
            Assert.Null(t["players"]![0]!["secretPath"]);
            Assert.Null(t["islands"]![0]!["surprise"]);
            Assert.Null(t["events"]![0]!["peerPointer"]);
            Assert.DoesNotContain("0xdeadbeef", t.ToString());
            Assert.DoesNotContain("/etc/shadow", t.ToString());
        }

        [Fact]
        public void A_truncated_terrain_section_still_renders_as_a_defined_state()
        {
            JObject t = Parse(@"{" + Head + @"""terrain"":{}}").Terrain.Json;

            Assert.True((bool)t["present"]!);
            Assert.Equal("unknown", (string)t["mode"]!);
            Assert.Equal(0, (int)t["candidateCount"]!);
            Assert.Empty((JArray)t["players"]!);
            Assert.Empty((JArray)t["islands"]!);
            Assert.Empty((JArray)t["events"]!);
        }

        [Fact]
        public void A_prerequisite_disabled_server_is_not_reported_as_merely_off()
        {
            JObject t = Parse(@"{" + Head + @"
              ""terrain"":{""requested"":true,""enabled"":false,
                ""mode"":""prerequisite-disabled"",""candidateCount"":0}}").Terrain.Json;

            Assert.Equal("prerequisite-disabled", (string)t["mode"]!);
            Assert.True((bool)t["requested"]!);
            Assert.False((bool)t["enabled"]!);
        }
    }
}

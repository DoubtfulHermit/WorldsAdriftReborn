using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The login server's end of the island-fauna contract.
    ///
    /// The stats file is a contract between two processes that are deployed
    /// separately, so the case that matters most here is the OLD one: a game
    /// server running yesterday's build writes no fauna section at all, and that
    /// must produce a defined, absent state the console renders as "this server
    /// predates fauna telemetry" - never an exception, and never a roster drawn
    /// against a clock nobody reported.
    /// </summary>
    public class FaunaStatsProjectionTests
    {
        private static readonly DateTimeOffset Now =
            DateTimeOffset.FromUnixTimeMilliseconds(1_723_200_123_000);

        private const string Head = @"
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
          ""players"":[],";

        private const string LiveJson = @"{""schemaVersion"":7," + Head + @"
          ""fauna"":{
            ""enabled"":true,""clockSeconds"":86400.125,""liveCount"":460,
            ""budget"":4000,""demand"":460,""perPeerBudget"":24,""poseIntervalMs"":250,
            ""islands"":[
              {""islandId"":""release-a"",""mantaRays"":4,""jellyFish"":6},
              {""islandId"":""release-b"",""mantaRays"":5,""jellyFish"":8}
            ]}
        }";

        /// <summary>A schema-v6 game server: correct, complete, and fauna-less.</summary>
        private const string LegacyJson = @"{""schemaVersion"":6," + Head + @"
          ""terrain"":{""requested"":false,""enabled"":false,""mode"":""off""}
        }";

        private static GameStatsSnapshot Parse(string json)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "wareborn-fauna-test-" + Guid.NewGuid().ToString("n") + ".json");
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
        public void An_older_game_server_without_a_fauna_section_parses_to_a_defined_absent_state()
        {
            GameStatsSnapshot s = Parse(LegacyJson);

            Assert.Equal(6, s.SchemaVersion);
            Assert.False(s.Fauna.Present);
            Assert.False(s.Fauna.Enabled);
            Assert.Equal(0, s.Fauna.LiveCount);

            JObject f = s.Fauna.Json;
            Assert.False((bool)f["present"]!);
            Assert.False((bool)f["enabled"]!);
            Assert.Equal(0.0, (double)f["clockSeconds"]!);
            Assert.Empty((JArray)f["islands"]!);
            // The rest of the snapshot is untouched by the missing section.
            Assert.Equal(120, s.UptimeSeconds);
        }

        [Fact]
        public void A_reporting_game_servers_roster_and_clock_survive_the_crossing()
        {
            GameStatsSnapshot s = Parse(LiveJson);
            JObject f = s.Fauna.Json;

            Assert.True(s.Fauna.Present);
            Assert.True(s.Fauna.Enabled);
            Assert.Equal(460, s.Fauna.LiveCount);
            // The fraction is the point: a whole-second clock moves every manta
            // on the map by up to eight metres.
            Assert.Equal(86_400.125, (double)f["clockSeconds"]!);
            Assert.Equal(4000, (int)f["budget"]!);
            Assert.Equal(460, (int)f["demand"]!);
            Assert.Equal(24, (int)f["perPeerBudget"]!);
            Assert.Equal(250, (int)f["poseIntervalMs"]!);

            JArray islands = (JArray)f["islands"]!;
            Assert.Equal(2, islands.Count);
            Assert.Equal("release-a", (string?)islands[0]!["islandId"]);
            Assert.Equal(4, (int)islands[0]!["mantaRays"]!);
            Assert.Equal(6, (int)islands[0]!["jellyFish"]!);
            Assert.Equal(8, (int)islands[1]!["jellyFish"]!);
        }

        /// <summary>
        /// The projection REBUILDS an allowlisted object. A field the writer never
        /// promised must not reach the console just because something wrote it -
        /// the dashboard is authenticated output, and this is the same rule the
        /// terrain and ship-domain projections keep.
        /// </summary>
        [Fact]
        public void An_unpromised_field_does_not_travel()
        {
            GameStatsSnapshot s = Parse(@"{""schemaVersion"":7," + Head + @"
              ""fauna"":{""enabled"":true,""clockSeconds"":1.0,""liveCount"":1,
                ""operatorSecret"":""hunter2"",
                ""islands"":[{""islandId"":""release-a"",""mantaRays"":1,""jellyFish"":0,
                             ""operatorSecret"":""hunter2""}]}
            }");

            Assert.DoesNotContain("hunter2", s.Fauna.Json.ToString(Newtonsoft.Json.Formatting.None));
        }

        [Fact]
        public void A_v8_file_without_an_ecology_block_parses_to_a_defined_absent_ecology()
        {
            // The v8-and-earlier shape: a fauna section with no ecology object.
            GameStatsSnapshot s = Parse(@"{""schemaVersion"":8," + Head + @"
              ""fauna"":{""enabled"":true,""clockSeconds"":1.0,""liveCount"":1,
                ""islands"":[{""islandId"":""release-a"",""mantaRays"":1,""jellyFish"":0}]}
            }");

            JObject ecology = (JObject)s.Fauna.Json["ecology"]!;
            Assert.False((bool)ecology["enabled"]!);
            Assert.Empty((JArray)ecology["islands"]!);
        }

        [Fact]
        public void A_hostile_ecology_is_clamped_sanitized_and_stripped_of_surprises()
        {
            GameStatsSnapshot s = Parse(@"{""schemaVersion"":9," + Head + @"
              ""fauna"":{""enabled"":true,""clockSeconds"":1.0,""liveCount"":1,
                ""islands"":[{""islandId"":""release-a"",""mantaRays"":1,""jellyFish"":0}],
                ""ecology"":{""enabled"":true,""worldSeed"":7,
                  ""islands"":[{""islandId"":""release-a"",
                    ""quietFactor"":99.5,
                    ""mantaCapacity"":-3,""jellyCapacity"":999999,
                    ""mantaExpressed"":2,""jellyExpressed"":3,
                    ""operatorSecret"":""hunter2"",
                    ""groups"":[{""species"":""kraken"",""index"":-1,""bloom"":2,
                      ""members"":5,""behaviour"":""<script>alert(1)</script>"",
                      ""epochSeconds"":""NaN""}],
                    ""blooms"":[{""species"":""jelly"",""index"":0,
                      ""sigma"":40.5,""annulusRadius"":445.25,""omegaRadial"":0.011,
                      ""surprise"":""hunter2""}]}]}}
            }");

            JObject ecology = (JObject)s.Fauna.Json["ecology"]!;
            string serialized = ecology.ToString(Newtonsoft.Json.Formatting.None);
            Assert.DoesNotContain("hunter2", serialized);
            Assert.DoesNotContain("script", serialized);

            JObject island = (JObject)((JArray)ecology["islands"]!).Single();
            Assert.Equal(1.0, (double)island["quietFactor"]!);       // clamped to [0,1]
            Assert.Equal(0, (int)island["mantaCapacity"]!);           // negative floors
            Assert.Equal(4096, (int)island["jellyCapacity"]!);        // absurd caps

            JObject group = (JObject)((JArray)island["groups"]!).Single();
            Assert.Equal("manta", (string?)group["species"]);         // unknown species defaults
            Assert.Equal("Cruise", (string?)group["behaviour"]);      // malformed label defaults
            Assert.Equal(0, (int)group["index"]!);

            JObject bloom = (JObject)((JArray)island["blooms"]!).Single();
            Assert.Equal("jelly", (string?)bloom["species"]);
            Assert.Equal(40.5, (double)bloom["sigma"]!);
            Assert.Equal(0.0, (double)bloom["radialDrift"]!);          // absent fields become 0, not undefined
        }

        /// <summary>
        /// The console feeds these numbers into an animation loop, so a malformed
        /// snapshot has to come out as a smaller world rather than as a hung tab.
        /// </summary>
        [Fact]
        public void A_hostile_roster_is_clamped_rather_than_believed()
        {
            GameStatsSnapshot s = Parse(@"{""schemaVersion"":7," + Head + @"
              ""fauna"":{""enabled"":true,""clockSeconds"":-5.0,""liveCount"":-9,
                ""budget"":-1,""demand"":-1,""perPeerBudget"":-1,""poseIntervalMs"":-1,
                ""islands"":[
                  {""islandId"":""release-a"",""mantaRays"":-4,""jellyFish"":999999999},
                  {""islandId"":"""",""mantaRays"":1,""jellyFish"":1},
                  {""mantaRays"":1,""jellyFish"":1}
                ]}
            }");

            JObject f = s.Fauna.Json;
            Assert.Equal(0.0, (double)f["clockSeconds"]!);
            Assert.Equal(0, (int)f["liveCount"]!);
            Assert.Equal(0, (int)f["poseIntervalMs"]!);

            JArray islands = (JArray)f["islands"]!;
            // The two rows with no usable island id are dropped, not defaulted:
            // an unnamed island cannot be joined to a drawn placement anyway.
            Assert.Single(islands);
            Assert.Equal(0, (int)islands[0]!["mantaRays"]!);
            Assert.Equal(4096, (int)islands[0]!["jellyFish"]!);
        }

        /// <summary>
        /// A section that is present but switched off is a DIFFERENT fact from an
        /// absent one, and the console says different things about the two.
        /// </summary>
        [Fact]
        public void Off_and_absent_are_distinguishable()
        {
            GameStatsSnapshot off = Parse(@"{""schemaVersion"":7," + Head + @"
              ""fauna"":{""enabled"":false,""clockSeconds"":0,""liveCount"":0,""islands"":[]}
            }");

            Assert.True(off.Fauna.Present);
            Assert.False(off.Fauna.Enabled);
            Assert.True((bool)off.Fauna.Json["present"]!);
            Assert.False((bool)Parse(LegacyJson).Fauna.Json["present"]!);
        }
    }
}

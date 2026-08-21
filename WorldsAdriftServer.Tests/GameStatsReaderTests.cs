using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The login-server end of the cross-process bridge: reading the file the
    /// game server writes. The JSON below is exactly the game side's
    /// StatsSnapshot contract, so these assertions fail if the reader and the
    /// writer drift apart. Missing and unreadable files must degrade to states
    /// the dashboard can show, never to an exception.
    /// </summary>
    public class GameStatsReaderTests
    {
        private static readonly DateTimeOffset Now =
            DateTimeOffset.FromUnixTimeMilliseconds(1_723_200_123_000);

        private const string ValidJson = @"{
          ""schemaVersion"":1,
          ""bootTimeUnixMs"":1723200000000,
          ""generatedAtUnixMs"":1723200120000,
          ""uptimeSeconds"":120,
          ""relayMode"":""v2@20Hz"",
          ""relayHz"":20,
          ""build"":""abc1234"",
          ""totalConnects"":5,
          ""totalDisconnects"":3,
          ""currentOnline"":2,
          ""peakOnline"":4,
          ""wireHealthWarning"":true,
          ""secondIslandRegistered"":true,
          ""firstRegionTerrainCount"":1,
          ""players"":[
            {""entityId"":3,""peerId"":""0x2f00"",""connectedAtUnixMs"":1723200100000,
             ""position"":{""x"":14734.5,""y"":-55.25,""z"":15208.75},
             ""health"":{""rttMs"":640,""rttVarianceMs"":30,""packetsLost"":9,""packetsSent"":1290,""inFlightBytes"":4096,""spiral"":true}},
            {""entityId"":7,""peerId"":""0x9900"",""connectedAtUnixMs"":1723200050000,""health"":null}
          ]
        }";

        private static string TempFile()
        {
            return System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "wareborn-stats-test-" + Guid.NewGuid().ToString("n") + ".json");
        }

        [Fact]
        public void A_valid_file_parses_into_an_ok_result()
        {
            string path = TempFile();
            File.WriteAllText(path, ValidJson);
            try
            {
                GameStatsResult r = GameStats.ReadFrom(path, Now);

                Assert.Equal(GameStatsState.Ok, r.State);
                Assert.NotNull(r.Snapshot);

                GameStatsSnapshot s = r.Snapshot!;
                Assert.Equal(120, s.UptimeSeconds);
                Assert.Equal("v2@20Hz", s.RelayMode);
                Assert.Equal("abc1234", s.Build);
                Assert.Equal(5, s.TotalConnects);
                Assert.Equal(3, s.TotalDisconnects);
                Assert.Equal(2, s.CurrentOnline);
                Assert.Equal(4, s.PeakOnline);
                Assert.True(s.WireHealthWarning);
                Assert.True(s.SecondIslandRegistered);
                Assert.Equal(1, s.FirstRegionTerrainCount);
                Assert.Equal(2, s.Players.Count);

                // Age is now - generatedAt = 3s, which is not yet stale.
                Assert.Equal(3.0, r.Age.TotalSeconds, 0);
                Assert.False(r.Stale);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void A_spiralling_player_is_read_with_its_health()
        {
            string path = TempFile();
            File.WriteAllText(path, ValidJson);
            try
            {
                GamePlayerStat p0 = GameStats.ReadFrom(path, Now).Snapshot!.Players[0];
                Assert.Equal(3, p0.EntityId);
                Assert.Equal("0x2f00", p0.PeerId);
                Assert.True(p0.HasHealth);
                Assert.Equal(640u, p0.RttMs);
                Assert.True(p0.Spiral);
                Assert.True(p0.HasPosition);
                Assert.Equal(14734.5, p0.X, 3);
                Assert.Equal(-55.25, p0.Y, 3);
                Assert.Equal(15208.75, p0.Z, 3);

                GamePlayerStat p1 = GameStats.ReadFrom(path, Now).Snapshot!.Players[1];
                Assert.False(p1.HasHealth);
                Assert.False(p1.HasPosition);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void An_old_file_is_flagged_stale()
        {
            string path = TempFile();
            File.WriteAllText(path, ValidJson);
            try
            {
                // generatedAt is 1723200120000; read 60s later.
                GameStatsResult r = GameStats.ReadFrom(path,
                    DateTimeOffset.FromUnixTimeMilliseconds(1_723_200_180_000));

                Assert.Equal(GameStatsState.Ok, r.State);
                Assert.True(r.Stale);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void A_missing_file_is_reported_as_missing()
        {
            GameStatsResult r = GameStats.ReadFrom(TempFile(), Now);
            Assert.Equal(GameStatsState.Missing, r.State);
            Assert.Null(r.Snapshot);
        }

        [Fact]
        public void Runtime_ship_domains_are_allowlist_parsed_and_old_snapshots_still_work()
        {
            string path = TempFile();
            string json = ValidJson.TrimEnd().TrimEnd('}') + @",
              ""runtime"":{""hostMode"":""local-single-process"",""hostId"":""local:primary"",
                ""ownedEntityCount"":72,""globalEntityCount"":1,""unownedEntityCount"":0,""ownershipIssueCount"":0,
                ""domains"":[{""domainId"":""island:haven"",""kind"":""island"",""label"":""Haven"",
                  ""hostId"":""local:primary"",""affinityDomainId"":null,""entityCount"":60,
                  ""active"":true,""warningCount"":0,""x"":1,""y"":2,""z"":3,
                  ""fictionalLease"":""must-not-pass""}],""shipDomains"":[{
                ""domainId"":""ship:83"",""hullEntityId"":83,""authorityGeneration"":4,
                ""replicationSequence"":91,""cadenceMs"":240,""deliveryAgeMs"":35,
                ""x"":1.5,""y"":2.5,""z"":3.5,""active"":true,""piloted"":true,
                ""liveCadenceExpected"":true,""pilotPlayerEntityId"":3,
                ""aboardPlayerEntityIds"":[3,7],""deckCount"":8,""mountedPartCount"":3,
                ""subscriberCount"":2,""staleDelivery"":false,""aboardCheckoutWarning"":false,
                ""fictionalWorker"":""must-not-pass""}]}}";
            File.WriteAllText(path, json);
            try
            {
                GameStatsSnapshot s = GameStats.ReadFrom(path, Now).Snapshot!;
                Assert.Equal("local-single-process", s.RuntimeHostMode);
                Assert.Equal("local:primary", s.RuntimeHostId);
                Assert.Equal(72, s.RuntimeOwnedEntityCount);
                Assert.Single(s.RuntimeDomains);
                Assert.Equal("island", (string)s.RuntimeDomains[0].Json["kind"]!);
                Assert.Null(s.RuntimeDomains[0].Json["fictionalLease"]);
                Assert.Single(s.ShipDomains);
                Assert.Equal(83, (long)s.ShipDomains[0].Json["hullEntityId"]!);
                Assert.Null(s.ShipDomains[0].Json["fictionalWorker"]);
            }
            finally { File.Delete(path); }

            // Compatibility is intentional: the existing v1 fixture has no runtime.
            path = TempFile(); File.WriteAllText(path, ValidJson);
            try
            {
                GameStatsSnapshot old = GameStats.ReadFrom(path, Now).Snapshot!;
                Assert.Equal("unknown", old.RuntimeHostMode);
                Assert.Empty(old.ShipDomains);
            }
            finally { File.Delete(path); }
        }

        /// <summary>
        /// A v12 FILE FROM A GAME SERVER THAT PREDATES THE GEOMETRY MUST STILL
        /// PARSE, AND MUST SAY SO.
        ///
        /// v12 is what production ran while the ship-card work sat on a branch:
        /// the sky-whale rework's number, written by a game server that knows
        /// nothing about hull geometry. The two binaries are shipped coupled, but
        /// a file on disk outlives a restart, so the login server reads v12 files
        /// on the way past every deploy.
        ///
        /// The doctrine this repo holds to is that a missing section projects to
        /// an explicit ABSENT rather than to a default - "never said" has to stay
        /// distinguishable from "said no". So the hull below carries everything a
        /// v12 hull carries and no `geometry` and no `geometryRevision`, and what
        /// comes back must be a geometry block that reports itself absent with
        /// empty arrays, not a hull with zero decks and no parts. The card reads
        /// the difference: absent prints "this server publishes no elevation",
        /// where an empty drawing would be a claim about the ship.
        /// </summary>
        [Fact]
        public void A_v12_snapshot_parses_with_its_ship_geometry_explicitly_absent()
        {
            string path = TempFile();
            string json = ValidJson.TrimEnd().TrimEnd('}').Replace(
                @"""schemaVersion"":1", @"""schemaVersion"":12") + @",
              ""runtime"":{""hostMode"":""local-single-process"",""hostId"":""local:primary"",
                ""ownedEntityCount"":1,""globalEntityCount"":1,""unownedEntityCount"":0,
                ""ownershipIssueCount"":0,""domains"":[],""shipDomains"":[{
                ""domainId"":""ship:83"",""hullEntityId"":83,""authorityGeneration"":4,
                ""replicationSequence"":91,""cadenceMs"":240,""deliveryAgeMs"":35,
                ""x"":1.5,""y"":2.5,""z"":3.5,""active"":true,""piloted"":false,
                ""liveCadenceExpected"":true,""pilotPlayerEntityId"":null,
                ""aboardPlayerEntityIds"":[],""deckCount"":8,""mountedPartCount"":3,
                ""subscriberCount"":0,""staleDelivery"":false,""aboardCheckoutWarning"":false,
                ""hull"":{""present"":true,""beamMetres"":12.1,""keelMetres"":20.6,
                  ""deckPlaneMetres"":3.4,""bowLocalZMetres"":16.8,""sternLocalZMetres"":-3.8,
                  ""cellCount"":4,""hullDeckCount"":1,""sectionCount"":5,
                  ""keelIsLongestAxis"":true,""outline"":[1,2,3,4,5,6]}}]}}";
            File.WriteAllText(path, json);
            try
            {
                GameStatsResult result = GameStats.ReadFrom(path, Now);
                Assert.Equal(GameStatsState.Ok, result.State);

                GameStatsSnapshot s = result.Snapshot!;
                Assert.Equal(12, s.SchemaVersion);
                Assert.Single(s.ShipDomains);

                // The v12 hull itself still reads: the outline is older than the
                // geometry and must not be collateral damage.
                GameShipDomainStat ship = s.ShipDomains[0];
                Assert.Equal(83, (long)ship.Json["hullEntityId"]!);
                Assert.True((bool)ship.Json["hull"]!["present"]!);

                // ABSENT, stated - not a default, and not an empty ship.
                Assert.False((bool)ship.Geometry["present"]!);
                Assert.Empty((Newtonsoft.Json.Linq.JArray)ship.Geometry["profile"]!);
                Assert.Empty((Newtonsoft.Json.Linq.JArray)ship.Geometry["decks"]!);
                Assert.Empty((Newtonsoft.Json.Linq.JArray)ship.Geometry["parts"]!);

                // Revision zero is the "never published" value, and it is what
                // stops a card asking the geometry endpoint for a drawing that
                // cannot exist.
                Assert.Equal(0, ship.GeometryRevision);
                Assert.Equal(0, (long)ship.Json["hull"]!["geometryRevision"]!);

                // The parts COUNT is a v8 field and survives independently of the
                // drawing: the tile may honestly say three where the card says it
                // has no elevation to draw them on.
                Assert.Equal(3, (int)ship.Json["mountedPartCount"]!);
                Assert.False((bool)ship.Json["flight"]!["present"]!);
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData("")]
        [InlineData("   ")]
        public void A_garbage_file_is_reported_as_unreadable(string contents)
        {
            string path = TempFile();
            File.WriteAllText(path, contents);
            try
            {
                Assert.Equal(GameStatsState.Unreadable, GameStats.ReadFrom(path, Now).State);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}

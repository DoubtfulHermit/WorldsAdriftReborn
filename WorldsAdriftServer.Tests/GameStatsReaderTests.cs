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
          ""players"":[
            {""entityId"":3,""peerId"":""0x2f00"",""connectedAtUnixMs"":1723200100000,
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

                GamePlayerStat p1 = GameStats.ReadFrom(path, Now).Snapshot!.Players[1];
                Assert.False(p1.HasHealth);
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

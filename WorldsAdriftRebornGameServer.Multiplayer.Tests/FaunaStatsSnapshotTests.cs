using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The fauna half of the cross-process file contract, parsed with the same
    /// library the login server reads it with.
    ///
    /// The field that earns its own suite is <c>clockSeconds</c>. The operator
    /// console does not receive creature positions; it receives this number and
    /// evaluates the server's own movement against it. If the field is dropped,
    /// renamed, or written as something other than the movement clock, the console
    /// does not fail loudly - it draws four hundred and sixty animals in the wrong
    /// place and looks fine doing it.
    /// </summary>
    public class FaunaStatsSnapshotTests
    {
        private static StatsSnapshot Snapshot(FaunaRuntimeStat? fauna) =>
            new StatsSnapshot(
                bootTimeUnixMs: 1_723_200_000_000,
                generatedAtUnixMs: 1_723_200_123_000,
                uptimeSeconds: 123,
                relayMode: "v2@20Hz",
                relayHz: 20,
                build: "abc1234",
                totalConnects: 1,
                totalDisconnects: 0,
                currentOnline: 1,
                peakOnline: 1,
                players: Array.Empty<PlayerStat>(),
                fauna: fauna);

        private static JObject Fauna(FaunaRuntimeStat? fauna) =>
            (JObject)JObject.Parse(Snapshot(fauna).ToJson())["fauna"]!;

        [Fact]
        public void A_server_with_fauna_off_reports_off_rather_than_an_absent_section()
        {
            JObject f = Fauna(null);

            Assert.False((bool)f["enabled"]!);
            Assert.Equal(0, (int)f["liveCount"]!);
            Assert.Empty((JArray)f["islands"]!);
        }

        [Fact]
        public void The_movement_clock_travels_with_the_roster()
        {
            JObject f = Fauna(new FaunaRuntimeStat(
                enabled: true, clockSeconds: 4321.5, liveCount: 10, budget: 4000,
                demand: 460, perPeerBudget: 24, poseIntervalMs: 250,
                islands: new[] { new FaunaIslandStat("release-a", 4, 6) }));

            Assert.True((bool)f["enabled"]!);
            Assert.Equal(4321.5, (double)f["clockSeconds"]!);
            Assert.Equal(10, (int)f["liveCount"]!);
            Assert.Equal(4000, (int)f["budget"]!);
            Assert.Equal(460, (int)f["demand"]!);
            Assert.Equal(24, (int)f["perPeerBudget"]!);
            Assert.Equal(250, (int)f["poseIntervalMs"]!);
        }

        /// <summary>
        /// The clock must survive as a REAL, because a snapshot written after a
        /// day of uptime carries a value with a fraction on it and truncating that
        /// to a whole second moves every creature on the map by up to eight metres.
        /// </summary>
        [Fact]
        public void The_clock_keeps_its_fraction()
        {
            JObject f = Fauna(new FaunaRuntimeStat(true, 86_400.125, 1, 4000, 1, 24, 250, null));
            Assert.Equal(86_400.125, (double)f["clockSeconds"]!);
        }

        [Fact]
        public void An_island_row_carries_counts_by_species()
        {
            JArray islands = (JArray)Fauna(new FaunaRuntimeStat(
                enabled: true, clockSeconds: 1, liveCount: 20, budget: 4000, demand: 20,
                perPeerBudget: 24, poseIntervalMs: 250,
                islands: new[]
                {
                    new FaunaIslandStat("release-a", 4, 6),
                    new FaunaIslandStat("release-b", 5, 8),
                }))["islands"]!;

            Assert.Equal(2, islands.Count);
            Assert.Equal("release-a", (string?)islands[0]!["islandId"]);
            Assert.Equal(4, (int)islands[0]!["mantaRays"]!);
            Assert.Equal(6, (int)islands[0]!["jellyFish"]!);
            Assert.Equal("release-b", (string?)islands[1]!["islandId"]);
            Assert.Equal(5, (int)islands[1]!["mantaRays"]!);
            Assert.Equal(8, (int)islands[1]!["jellyFish"]!);
        }

        /// <summary>
        /// Negative counts are clamped rather than written. The reader is an
        /// animation loop: a negative creature count there is a loop bound, not a
        /// wrong number on a page.
        /// </summary>
        [Fact]
        public void Negative_counts_are_clamped_at_the_writer()
        {
            FaunaIslandStat island = new FaunaIslandStat("release-a", -4, -6);
            Assert.Equal(0, island.MantaRays);
            Assert.Equal(0, island.JellyFish);
            Assert.Equal(0, island.Total);

            FaunaRuntimeStat stat = new FaunaRuntimeStat(true, 0, -1, -1, -1, -1, -1, null);
            Assert.Equal(0, stat.LiveCount);
            Assert.Equal(0, stat.Budget);
            Assert.Equal(0, stat.Demand);
            Assert.Equal(0, stat.PerPeerBudget);
            Assert.Equal(0, stat.PoseIntervalMs);
        }

        /// <summary>
        /// The whole file must stay parseable with the section on it, because the
        /// login server parses the document, not the section.
        /// </summary>
        [Fact]
        public void The_snapshot_is_still_one_valid_document_with_the_section_on_it()
        {
            JObject o = JObject.Parse(Snapshot(new FaunaRuntimeStat(
                enabled: true, clockSeconds: 12.25, liveCount: 10, budget: 4000, demand: 10,
                perPeerBudget: 24, poseIntervalMs: 250,
                islands: new[] { new FaunaIslandStat("release-\"quoted\"", 1, 2) })).ToJson());

            Assert.NotNull(o["terrain"]);
            Assert.NotNull(o["fauna"]);
            Assert.Equal("release-\"quoted\"",
                (string?)((JArray)o["fauna"]!["islands"]!)[0]!["islandId"]);
        }
    }
}

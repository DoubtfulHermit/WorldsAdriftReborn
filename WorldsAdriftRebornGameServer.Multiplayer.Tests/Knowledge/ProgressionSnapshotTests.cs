using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Knowledge
{
    /// <summary>
    /// The progression round trip. Any field lost here is knowledge the player
    /// loses on relog - the whole reason this table exists.
    /// </summary>
    public class ProgressionSnapshotTests
    {
        private static ProgressionState Sample()
        {
            return new ProgressionState
            {
                Knowledge = 8781,
                LifetimeKnowledge = 10001,
                NodeUses = new Dictionary<string, int> { { "Shipbuilding", 1 }, { "Stairs", 2 } },
                LearnedSchematics = new List<string> { "engine", "wing" },
                AlreadyScanned = new List<string> { "50", "53" },
            };
        }

        [Fact]
        public void A_progression_survives_a_round_trip_field_for_field()
        {
            ProgressionState original = Sample();

            ProgressionState? restored = ProgressionSnapshot.Read(ProgressionSnapshot.Write(original));

            Assert.NotNull(restored);
            Assert.Equal(original.Knowledge, restored!.Knowledge);
            Assert.Equal(original.LifetimeKnowledge, restored.LifetimeKnowledge);
            Assert.Equal(original.NodeUses, restored.NodeUses);
            Assert.Equal(original.LearnedSchematics, restored.LearnedSchematics);
            Assert.Equal(original.AlreadyScanned, restored.AlreadyScanned);
        }

        [Fact]
        public void A_seed_progression_round_trips_and_reports_no_progress()
        {
            ProgressionState seed = new ProgressionState();

            Assert.False(seed.HasProgress);

            ProgressionState? restored = ProgressionSnapshot.Read(ProgressionSnapshot.Write(seed));

            Assert.NotNull(restored);
            Assert.False(restored!.HasProgress);
        }

        [Fact]
        public void Any_scan_or_purchase_makes_a_state_report_progress()
        {
            Assert.True(new ProgressionState { Knowledge = 2 }.HasProgress);
            Assert.True(new ProgressionState { LifetimeKnowledge = 2 }.HasProgress);
            Assert.True(new ProgressionState
            {
                NodeUses = new Dictionary<string, int> { { "x", 1 } },
            }.HasProgress);
            Assert.True(new ProgressionState
            {
                LearnedSchematics = new List<string> { "x" },
            }.HasProgress);
            Assert.True(new ProgressionState
            {
                AlreadyScanned = new List<string> { "1" },
            }.HasProgress);
        }

        [Fact]
        public void An_unreadable_payload_reads_back_as_null_rather_than_throwing()
        {
            // The caller's response to a corrupt row is "keep the live state",
            // which only works if Read hands back null instead of an exception.
            Assert.Null(ProgressionSnapshot.Read(null));
            Assert.Null(ProgressionSnapshot.Read(""));
            Assert.Null(ProgressionSnapshot.Read("not json at all"));
            Assert.Null(ProgressionSnapshot.Read("{\"Version\":999,\"Knowledge\":5}"));
        }
    }
}

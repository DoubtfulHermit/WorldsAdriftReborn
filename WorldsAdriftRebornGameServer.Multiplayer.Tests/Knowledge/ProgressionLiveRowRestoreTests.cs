using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Knowledge
{
    /// <summary>
    /// A regression pinned to the ACTUAL row that lives in Postgres right now, so
    /// the "does knowledge really come back?" question is answered by the exact
    /// bytes the database holds, not a hand-built sample.
    ///
    /// The payload below is a verbatim copy of
    ///   select data_json from character_progression
    ///   where character_uid='9bae0367-1c48-4139-bef9-e5f0a68ca14c';
    /// (171 bytes, updated 2026-08-11 20:40:40 UTC), the row a player produced by
    /// farming +10000 knowledge and buying Shipbuilding/shipyard. The bind that
    /// logged "no stored progression" happened BEFORE this row existed, which is
    /// why nothing was restored that session; this test proves the NEXT bind will
    /// restore it rather than repeat the "keep session" branch.
    /// </summary>
    public class ProgressionLiveRowRestoreTests
    {
        private const string LiveRowJson =
            "{\"Version\":1,\"Knowledge\":9881,\"LifetimeKnowledge\":10001,"
            + "\"NodeUses\":{\"RevivalChamberInterface\":1,\"Shipbuilding\":1},"
            + "\"LearnedSchematics\":[\"shipyard\"],\"AlreadyScanned\":[\"53\"]}";

        [Fact]
        public void The_live_row_decodes_field_for_field()
        {
            ProgressionState? state = ProgressionSnapshot.Read(LiveRowJson);

            Assert.NotNull(state);
            Assert.Equal(9881, state!.Knowledge);
            Assert.Equal(10001, state.LifetimeKnowledge);
            Assert.Equal(1, state.NodeUses["RevivalChamberInterface"]);
            Assert.Equal(1, state.NodeUses["Shipbuilding"]);
            Assert.Equal(new[] { "shipyard" }, state.LearnedSchematics);
            Assert.Equal(new[] { "53" }, state.AlreadyScanned);
            Assert.True(state.HasProgress);
        }

        [Fact]
        public void The_next_relog_restores_the_live_row_over_a_fresh_seed()
        {
            // BindIdentity seeds a fresh PlayerProgression (knowledge 1, currentHasProgress
            // == false) before consulting the store, so this is the exact input the
            // load policy sees on the user's next login.
            ProgressionState? stored = ProgressionSnapshot.Read(LiveRowJson);

            Assert.True(ProgressionLoadPolicy.ShouldApplyStored(
                currentHasProgress: false, stored: stored));
        }

        [Fact]
        public void A_progress_bearing_live_row_wins_even_over_a_session_that_has_progress()
        {
            // The seed-only guard must only ever refuse a seed-only STORED row. A
            // real 9881-knowledge row is never mistaken for one, so it restores even
            // if the session had somehow earned knowledge before 1088 arrived.
            ProgressionState? stored = ProgressionSnapshot.Read(LiveRowJson);

            Assert.True(ProgressionLoadPolicy.ShouldApplyStored(
                currentHasProgress: true, stored: stored));
        }
    }
}

using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Knowledge
{
    /// <summary>
    /// The wipe-safety rules for enabling progression persistence against a live
    /// server. Same asymmetric bet as the inventory: when a stored record is
    /// missing, unreadable or seed-only, never overwrite knowledge the session
    /// has actually earned - a transient database error must not read as a reset.
    /// </summary>
    public class ProgressionLoadPolicyTests
    {
        private static ProgressionState WithProgress()
        {
            return new ProgressionState
            {
                Knowledge = 8781,
                LearnedSchematics = new List<string> { "engine" },
            };
        }

        [Fact]
        public void A_stored_progression_with_progress_restores_over_a_fresh_seed()
        {
            // The relog this whole track exists for.
            Assert.True(ProgressionLoadPolicy.ShouldApplyStored(
                currentHasProgress: false, stored: WithProgress()));
        }

        [Fact]
        public void A_missing_row_keeps_the_session_rather_than_wiping_it()
        {
            // No row, an unreadable database and an unparseable payload all reach
            // here as null, and all mean "keep what the session holds".
            Assert.False(ProgressionLoadPolicy.ShouldApplyStored(
                currentHasProgress: true, stored: null));
            Assert.False(ProgressionLoadPolicy.ShouldApplyStored(
                currentHasProgress: false, stored: null));
        }

        [Fact]
        public void A_seed_only_row_never_resets_a_session_that_has_progress()
        {
            // The dangerous case: a truncated or half-written row parses to a seed
            // and would silently reset a player who just scanned a databank.
            Assert.False(ProgressionLoadPolicy.ShouldApplyStored(
                currentHasProgress: true, stored: new ProgressionState()));
        }

        [Fact]
        public void A_seed_row_onto_a_fresh_session_is_a_harmless_apply()
        {
            Assert.True(ProgressionLoadPolicy.ShouldApplyStored(
                currentHasProgress: false, stored: new ProgressionState()));
        }
    }
}

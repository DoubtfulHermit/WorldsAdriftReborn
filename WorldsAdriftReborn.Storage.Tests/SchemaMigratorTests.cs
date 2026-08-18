using WorldsAdriftReborn.Storage.Schema;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// The migrator is pure, so these need no database - which is the point of
    /// keeping it pure. The ordering rules are the kind of thing that is obvious
    /// at version 1 and expensive at version 4.
    /// </summary>
    public class SchemaMigratorTests
    {
        private static readonly string[] Scripts = { "one", "two", "three" };

        [Fact]
        public void A_fresh_database_runs_every_script_in_order()
        {
            Assert.Equal(Scripts, SchemaMigrator.ScriptsToApply(0, Scripts));
        }

        [Fact]
        public void A_database_already_at_the_current_version_runs_nothing()
        {
            Assert.Empty(SchemaMigrator.ScriptsToApply(3, Scripts));
        }

        [Fact]
        public void A_partly_migrated_database_runs_only_what_it_is_missing()
        {
            Assert.Equal(new[] { "two", "three" }, SchemaMigrator.ScriptsToApply(1, Scripts));
            Assert.Equal(new[] { "three" }, SchemaMigrator.ScriptsToApply(2, Scripts));
        }

        [Fact]
        public void A_database_written_by_a_newer_build_is_refused_rather_than_ignored()
        {
            // Doing nothing here means running new code against an old schema and
            // finding out one INSERT at a time, in production, at whatever hour
            // the rollback happened.
            Assert.Throws<InvalidOperationException>(
                () => SchemaMigrator.ScriptsToApply(4, Scripts));
        }

        [Fact]
        public void A_nonsense_version_is_refused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SchemaMigrator.ScriptsToApply(-1, Scripts));
            Assert.Throws<ArgumentNullException>(
                () => SchemaMigrator.ScriptsToApply(0, null!));
        }

        [Fact]
        public void The_target_version_is_the_number_of_scripts()
        {
            Assert.Equal(3, SchemaMigrator.TargetVersion(Scripts));
            Assert.Equal(0, SchemaMigrator.TargetVersion(Array.Empty<string>()));
        }

        [Fact]
        public void The_shipped_schema_is_at_version_eight()
        {
            // If this fails, a script was added: check it was APPENDED and that
            // no existing one was edited, then update the number.
            // v1 accounts/sessions/characters, v2 character_inventories,
            // v3 server_config, v4 character_progression, v5 character_positions,
            // v6 crews + crew_members, v7 social_invites,
            // v8 alliances + alliance_ranks + alliance_members.
            Assert.Equal(8, SchemaMigrator.TargetVersion(SchemaScripts.All));
        }

        [Fact]
        public void A_database_at_version_seven_is_brought_forward_by_exactly_one_script()
        {
            // The upgrade a live server will actually run when alliances ship. It
            // must not re-run v1..v7 against tables that exist - the whole reason
            // the scripts are append-only.
            IReadOnlyList<string> pending = SchemaMigrator.ScriptsToApply(7, SchemaScripts.All);

            Assert.Single(pending);
            Assert.Contains("alliances", pending[0]);
            Assert.Contains("alliance_ranks", pending[0]);
            Assert.Contains("alliance_members", pending[0]);
        }

        /// <summary>
        /// v8 is purely ADDITIVE. It creates three tables and touches nothing that
        /// already exists, which is what makes it safe to run against a live
        /// database that players are connected to.
        /// </summary>
        [Fact]
        public void The_alliance_script_only_creates_and_never_alters_or_drops()
        {
            string script = SchemaMigrator.ScriptsToApply(7, SchemaScripts.All)[0];

            Assert.DoesNotContain("ALTER TABLE", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DROP ", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE ", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE FROM", script, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_database_at_version_one_still_runs_the_later_scripts_in_order()
        {
            // An older database jumps six versions; the order is load-bearing
            // (a script must never see a table a later script creates - every one
            // of these references characters, which only v1 creates).
            IReadOnlyList<string> pending = SchemaMigrator.ScriptsToApply(1, SchemaScripts.All);

            Assert.Equal(7, pending.Count);
            Assert.Contains("character_inventories", pending[0]);
            Assert.Contains("server_config", pending[1]);
            Assert.Contains("character_progression", pending[2]);
            Assert.Contains("character_positions", pending[3]);
            Assert.Contains("crews", pending[4]);
            Assert.Contains("social_invites", pending[5]);
            Assert.Contains("alliances", pending[6]);
        }
    }
}

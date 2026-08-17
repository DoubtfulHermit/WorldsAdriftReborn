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
        public void The_shipped_schema_is_at_version_five()
        {
            // If this fails, a script was added: check it was APPENDED and that
            // no existing one was edited, then update the number.
            // v1 accounts/sessions/characters, v2 character_inventories,
            // v3 server_config, v4 character_progression, v5 character_positions.
            Assert.Equal(5, SchemaMigrator.TargetVersion(SchemaScripts.All));
        }

        [Fact]
        public void A_database_at_version_four_is_brought_forward_by_exactly_one_script()
        {
            // The upgrade an operator who already has a v4 database will actually
            // run when the logout-position table ships. It must not re-run v1..v4
            // against tables that exist.
            IReadOnlyList<string> pending = SchemaMigrator.ScriptsToApply(4, SchemaScripts.All);

            Assert.Single(pending);
            Assert.Contains("character_positions", pending[0]);
        }

        [Fact]
        public void A_database_at_version_one_still_runs_the_later_scripts_in_order()
        {
            // An older database jumps four versions; the order is load-bearing
            // (a script must never see a table a later script creates - every one
            // of these references characters, which only v1 creates).
            IReadOnlyList<string> pending = SchemaMigrator.ScriptsToApply(1, SchemaScripts.All);

            Assert.Equal(4, pending.Count);
            Assert.Contains("character_inventories", pending[0]);
            Assert.Contains("server_config", pending[1]);
            Assert.Contains("character_progression", pending[2]);
            Assert.Contains("character_positions", pending[3]);
        }
    }
}

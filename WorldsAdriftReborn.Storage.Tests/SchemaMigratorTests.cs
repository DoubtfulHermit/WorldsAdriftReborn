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
        public void The_shipped_schema_is_at_version_one()
        {
            // If this fails, a script was added: check it was APPENDED and that
            // no existing one was edited, then update the number.
            Assert.Equal(1, SchemaMigrator.TargetVersion(SchemaScripts.All));
        }
    }
}

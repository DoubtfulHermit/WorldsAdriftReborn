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
        public void The_shipped_schema_is_at_version_ten()
        {
            // If this fails, a script was added: check it was APPENDED and that
            // no existing one was edited, then update the number.
            // v1 accounts/sessions/characters, v2 character_inventories,
            // v3 server_config, v4 character_progression, v5 character_positions,
            // v6 crews + crew_members, v7 social_invites,
            // v8 alliances + alliance_ranks + alliance_members,
            // v9 map_viewer_samples, v10 durable ship-relative logout anchors.
            Assert.Equal(10, SchemaMigrator.TargetVersion(SchemaScripts.All));
        }

        [Fact]
        public void A_database_at_version_seven_runs_alliances_and_then_the_viewer_series()
        {
            // A database that has not been updated since before alliances shipped.
            // It must not re-run v1..v7 against tables that exist - the whole
            // reason the scripts are append-only - and it must run what it does
            // owe in order.
            IReadOnlyList<string> pending = SchemaMigrator.ScriptsToApply(7, SchemaScripts.All);

            Assert.Equal(3, pending.Count);
            Assert.Contains("alliances", pending[0]);
            Assert.Contains("alliance_ranks", pending[0]);
            Assert.Contains("alliance_members", pending[0]);
            Assert.Contains("map_viewer_samples", pending[1]);
            Assert.Contains("built_ship_index", pending[2]);
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

            Assert.Equal(9, pending.Count);
            Assert.Contains("character_inventories", pending[0]);
            Assert.Contains("server_config", pending[1]);
            Assert.Contains("character_progression", pending[2]);
            Assert.Contains("character_positions", pending[3]);
            Assert.Contains("crews", pending[4]);
            Assert.Contains("social_invites", pending[5]);
            Assert.Contains("alliances", pending[6]);
            Assert.Contains("map_viewer_samples", pending[7]);
            Assert.Contains("built_ship_index", pending[8]);
        }

        [Fact]
        public void A_database_at_version_eight_is_brought_forward_by_two_scripts()
        {
            // The upgrade a live server will actually run when the map viewer
            // count ships. It must not re-run v1..v8 against tables that exist.
            IReadOnlyList<string> pending = SchemaMigrator.ScriptsToApply(8, SchemaScripts.All);

            Assert.Equal(2, pending.Count);
            Assert.Contains("map_viewer_samples", pending[0]);
            Assert.Contains("built_ship_index", pending[1]);
        }

        /// <summary>
        /// v9 is purely ADDITIVE, like v8: one new table and nothing touched, so
        /// it is safe against a live database with players connected.
        /// </summary>
        [Fact]
        public void The_viewer_sample_script_only_creates_and_never_alters_or_drops()
        {
            string script = SchemaMigrator.ScriptsToApply(8, SchemaScripts.All)[0];

            Assert.DoesNotContain("ALTER TABLE", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DROP ", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE ", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE FROM", script, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The privacy property of the recorded series, asserted against the SQL
        /// itself rather than against a comment: the table it creates has exactly
        /// two columns, an instant and a count.
        ///
        /// This is the tripwire for the recorded half of the viewer count. Adding
        /// a column that could name a visitor - an address, a session, a country,
        /// a user agent - fails here by name, and has to be a deliberate act with
        /// this test edited alongside it.
        /// </summary>
        [Fact]
        public void The_recorded_viewer_series_has_nowhere_to_put_a_visitor()
        {
            string script = SchemaMigrator.ScriptsToApply(8, SchemaScripts.All)[0];

            Assert.Contains("sampled_at", script, StringComparison.Ordinal);
            Assert.Contains("viewer_count", script, StringComparison.Ordinal);

            foreach (string forbidden in new[]
            {
                "ip", "address", "addr", "host", "agent", "referer", "referrer",
                "country", "region", "city", "geo", "session", "cookie", "token",
                "fingerprint", "account", "character", "player", "visitor", "uid",
            })
            {
                Assert.DoesNotContain(forbidden, ColumnNames(script), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// The column names declared by a CREATE TABLE script, and nothing else -
        /// not the prose around them, which legitimately discusses addresses at
        /// length precisely because there are none in the table.
        /// </summary>
        private static string ColumnNames(string script)
        {
            System.Text.StringBuilder names = new System.Text.StringBuilder();

            foreach (string rawLine in script.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith(")", StringComparison.Ordinal))
                {
                    continue;
                }

                int space = line.IndexOf(' ');
                names.Append(space > 0 ? line.Substring(0, space) : line).Append(' ');
            }

            return names.ToString();
        }
    }
}

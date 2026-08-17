using WorldsAdriftReborn.Storage.Schema;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// The connection and migration plumbing, at the level an operator sees it.
    /// </summary>
    public class DbTests
    {
        [Fact]
        public void The_connection_string_comes_from_the_environment_like_the_data_dir_does()
        {
            Assert.Equal("WAREBORN_DB", Db.ConnectionStringVariable);
            Assert.Equal(Db.Configured, new Db().ConnectionString);
            Assert.Equal("a=b", new Db("a=b").ConnectionString);
            Assert.Equal(Db.Configured, new Db("   ").ConnectionString);
        }

        [Fact]
        public void The_default_connection_string_carries_no_password()
        {
            // A default with a credential in it becomes the credential everybody
            // ships, and this file is in a public repository.
            Assert.DoesNotContain(
                "password", Db.DefaultConnectionString, StringComparison.OrdinalIgnoreCase);
        }

        [PostgresFact]
        public void Migrating_a_fresh_database_brings_it_to_the_current_version()
        {
            using TempDb db = new TempDb();

            Assert.Equal(SchemaMigrator.TargetVersion(SchemaScripts.All), db.Db.SchemaVersion());
        }

        [PostgresFact]
        public void Migrating_again_is_a_no_op_so_it_is_safe_on_every_start()
        {
            using TempDb db = new TempDb();

            int before = db.Db.SchemaVersion();

            Assert.Equal(before, db.Db.EnsureSchema());
            Assert.Equal(before, db.Db.EnsureSchema());
            Assert.Equal(before, db.Db.SchemaVersion());
        }

        [PostgresFact]
        public void The_schema_contains_exactly_the_tables_the_shipped_scripts_declare()
        {
            // v1 declared three, all login-server-owned. v2 adds
            // character_inventories, v4 character_progression and v5
            // character_positions, all three GAME-server-owned - the tables in
            // this database written by the other process. v3 adds server_config.
            using TempDb db = new TempDb();

            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema() AND table_name = 'accounts';"));
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema() AND table_name = 'sessions';"));
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema() AND table_name = 'characters';"));
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema() AND table_name = 'character_inventories';"));
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema() AND table_name = 'server_config';"));
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema() AND table_name = 'character_progression';"));
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema() AND table_name = 'character_positions';"));

            // accounts, sessions, characters, character_inventories,
            // server_config, character_progression, character_positions,
            // schema_version - and nothing else.
            Assert.Equal(8, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema();"));
        }

        [PostgresFact]
        public void Foreign_keys_are_enforced_without_anyone_having_to_switch_them_on()
        {
            // The reason this is a one-line test rather than a per-connection
            // pragma the whole library has to remember.
            //
            // Five: sessions -> accounts, characters -> accounts,
            // character_inventories -> characters, character_progression ->
            // characters and character_positions -> characters. The last three
            // matter most, because their key arrives from outside the database -
            // the game server digs the character uid out of a JSON blob a client
            // published - so they are the only place a made-up key could get in.
            using TempDb db = new TempDb();

            Assert.Equal(5, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.table_constraints "
                + "WHERE table_schema = current_schema() AND constraint_type = 'FOREIGN KEY';"));
        }

        [PostgresFact]
        public void The_partial_unique_indexes_that_encode_the_client_crashes_all_exist()
        {
            using TempDb db = new TempDb();

            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = current_schema() "
                + "AND indexname = 'ux_accounts_steam_user_key' AND indexdef LIKE '%WHERE%';"));

            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = current_schema() "
                + "AND indexname = 'ux_characters_account_empty_slot' AND indexdef LIKE '%WHERE%';"));

            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = current_schema() "
                + "AND indexname = 'ux_characters_account_slot';"));
        }
    }
}

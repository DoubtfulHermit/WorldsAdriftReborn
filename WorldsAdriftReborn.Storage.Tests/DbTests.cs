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
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema() AND table_name = 'crews';"));
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema() AND table_name = 'crew_members';"));
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema() AND table_name = 'social_invites';"));

            // v8 - alliances, and the two tables that make one openable.
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema() AND table_name = 'alliances';"));
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema() AND table_name = 'alliance_ranks';"));
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema() AND table_name = 'alliance_members';"));

            // accounts, sessions, characters, character_inventories,
            // server_config, character_progression, character_positions, crews,
            // crew_members, social_invites, alliances, alliance_ranks,
            // alliance_members, schema_version - and nothing else.
            Assert.Equal(14, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = current_schema();"));
        }

        [PostgresFact]
        public void Foreign_keys_are_enforced_without_anyone_having_to_switch_them_on()
        {
            // The reason this is a one-line test rather than a per-connection
            // pragma the whole library has to remember.
            //
            // Fourteen: sessions -> accounts, characters -> accounts,
            // character_inventories/character_progression/character_positions ->
            // characters, crews -> characters, crew_members -> BOTH characters
            // and crews, social_invites -> characters TWICE (once for the invitee
            // and once for the inviter), alliances -> characters for the founder,
            // alliance_ranks -> alliances, and alliance_members -> BOTH characters
            // and alliances. The character-keyed ones matter most, because their
            // key arrives from
            // outside the database - the game server digs the character uid out of
            // a JSON blob a client published, and the login server takes it off an
            // HTTP header - so they are the only place a made-up key could get in.
            //
            // alliance_members.rank_id is deliberately NOT among them: both it and
            // alliance_ranks cascade from alliances, Postgres does not order
            // sibling cascades, and either RESTRICT or CASCADE there would break a
            // disband or turn a rank deletion into a mass boot. See the comment in
            // SchemaScripts.V8.
            using TempDb db = new TempDb();

            Assert.Equal(14, db.Scalar<int>(
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

            // v7: one LIVE offer per (character, target), partial so a rejection
            // does not block a later invite.
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = current_schema() "
                + "AND indexname = 'social_invites_one_live_per_pair' AND indexdef LIKE '%WHERE%';"));

            // v8: exactly one default rank of each kind per alliance. The client
            // fills its Leader and BasicMember fields by scanning for them and then
            // dereferences the result, so a second of either silently changes which
            // one wins and a missing one is a null the alliance panel walks into.
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = current_schema() "
                + "AND indexname = 'alliance_ranks_one_default_leader' AND indexdef LIKE '%WHERE%';"));

            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = current_schema() "
                + "AND indexname = 'alliance_ranks_one_default_member' AND indexdef LIKE '%WHERE%';"));

            // v8: one alliance per name, folded to lower case. Two alliances a
            // player cannot tell apart in a list that shows nothing but the name.
            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = current_schema() "
                + "AND indexname = 'alliances_one_name' AND indexdef LIKE '%lower%';"));
        }
    }
}

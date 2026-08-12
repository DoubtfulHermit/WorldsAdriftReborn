using Npgsql;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftReborn.Storage.Schema;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// Proves the schema actually refuses the rows it is supposed to refuse.
    ///
    /// These write raw SQL rather than going through the repositories, on purpose.
    /// A repository already refuses the bad value before the database sees it, so
    /// a test that inserts through one would pass with every CHECK deleted. The
    /// constraints exist for the call sites that do not exist yet - the migration
    /// importer, the sign-up route, whatever is written in six months - and the
    /// only way to show they are doing anything is to attack them directly.
    ///
    /// Each name is the client misbehaviour the constraint forecloses.
    /// </summary>
    public class ConstraintTests
    {
        private static void AssertRejected(Action write, string expectedConstraintFragment)
        {
            PostgresException e = Assert.Throws<PostgresException>(() => write());

            Assert.True(
                (e.ConstraintName ?? string.Empty).Contains(
                    expectedConstraintFragment, StringComparison.OrdinalIgnoreCase)
                || (e.Message ?? string.Empty).Contains(
                    expectedConstraintFragment, StringComparison.OrdinalIgnoreCase),
                "expected a violation naming '" + expectedConstraintFragment
                + "' but got: " + e.ConstraintName + " / " + e.Message);
        }

        // ---- accounts --------------------------------------------------------

        [PostgresFact]
        public void Two_accounts_cannot_claim_one_username()
        {
            using TempDb db = new TempDb();

            Assert.NotNull(db.AnAccount("timu"));

            // Through the repository this is an ordinary "name taken" answer...
            Assert.Null(db.Accounts.Create("timu", "timu", "hunter22", null, TempDb.Now));

            // ...and underneath it, the database is what actually enforces it, so
            // two sign-ups racing cannot both win.
            AssertRejected(
                () => db.Execute(
                    "INSERT INTO accounts (username_key, username, display_name, password_hash, created_at) "
                    + "VALUES ('timu', 'timu', 'timu', 'pbkdf2$sha256$1$a$b', @now);",
                    ("now", TempDb.Now.UtcDateTime)),
                "username_key");
        }

        [PostgresFact]
        public void An_account_with_no_screen_name_cannot_be_stored_because_it_ends_in_the_QUIT_dialog()
        {
            // screenName is read unconditionally on the password path with no
            // null guard: blank means a throw, caught, shown as
            // "Connection Error ... QUIT".
            using TempDb db = new TempDb();

            AssertRejected(
                () => db.Execute(
                    "INSERT INTO accounts (username_key, username, display_name, password_hash, created_at) "
                    + "VALUES ('timu', 'timu', '   ', 'pbkdf2$sha256$1$a$b', @now);",
                    ("now", TempDb.Now.UtcDateTime)),
                "display_name");
        }

        [PostgresFact]
        public void A_username_key_that_was_not_lowercased_is_refused()
        {
            // Otherwise 'Timu' and 'timu' become two accounts, and only for
            // players who capitalise - which is the hardest kind of bug to see.
            using TempDb db = new TempDb();

            AssertRejected(
                () => db.Execute(
                    "INSERT INTO accounts (username_key, username, display_name, password_hash, created_at) "
                    + "VALUES ('Timu', 'Timu', 'Timu', 'pbkdf2$sha256$1$a$b', @now);",
                    ("now", TempDb.Now.UtcDateTime)),
                "lowercase");
        }

        [PostgresFact]
        public void A_cleartext_password_cannot_be_written_into_the_hash_column()
        {
            using TempDb db = new TempDb();

            AssertRejected(
                () => db.Execute(
                    "INSERT INTO accounts (username_key, username, display_name, password_hash, created_at) "
                    + "VALUES ('timu', 'timu', 'timu', 'hunter22', @now);",
                    ("now", TempDb.Now.UtcDateTime)),
                "password_hash");
        }

        [PostgresFact]
        public void Two_accounts_cannot_claim_one_steam_id()
        {
            // The 28-minute refresh re-authenticates Steam-only. If that could
            // resolve to a different account, the player's roster identity would
            // flip mid-session - which would look like corruption.
            using TempDb db = new TempDb();

            Assert.NotNull(db.AnAccount("timu", "76561198012345678"));

            AssertRejected(
                () => db.Execute(
                    "INSERT INTO accounts (username_key, username, display_name, password_hash, "
                    + "steam_user_key, created_at) "
                    + "VALUES ('friend', 'friend', 'friend', 'pbkdf2$sha256$1$a$b', "
                    + "'76561198012345678', @now);",
                    ("now", TempDb.Now.UtcDateTime)),
                "steam_user_key");
        }

        [PostgresFact]
        public void Any_number_of_accounts_may_have_no_steam_id()
        {
            // The unique index has to be partial: NULL is the normal state for a
            // player with no Steam client, and a plain unique index would let
            // exactly one of them exist.
            using TempDb db = new TempDb();

            Assert.NotNull(db.AnAccount("timu"));
            Assert.NotNull(db.AnAccount("friend"));
            Assert.NotNull(db.AnAccount("another"));

            Assert.Equal(3, db.Accounts.Count());
        }

        // ---- sessions --------------------------------------------------------

        [PostgresFact]
        public void A_session_cannot_belong_to_an_account_that_does_not_exist()
        {
            using TempDb db = new TempDb();

            AssertRejected(
                () => db.Execute(
                    "INSERT INTO sessions (token, account_id, issued_at, last_seen_at, expires_at) "
                    + "VALUES (@token, 999999, @now, @now, @later);",
                    ("token", new string('t', 43)),
                    ("now", TempDb.Now.UtcDateTime),
                    ("later", TempDb.Now.AddDays(30).UtcDateTime)),
                "account_id");
        }

        [PostgresFact]
        public void Deleting_an_account_takes_its_sessions_with_it()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            SessionRecord session = db.Sessions.Issue(account.AccountId, TempDb.Now);

            db.Execute("DELETE FROM accounts WHERE account_id = @id;", ("id", account.AccountId));

            Assert.Null(db.Sessions.Peek(session.Token));
        }

        [PostgresFact]
        public void A_truncated_session_token_is_refused()
        {
            // The token is a bearer credential; a short one is a weak one, and a
            // short one in the table means something truncated it in transit.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            AssertRejected(
                () => db.Execute(
                    "INSERT INTO sessions (token, account_id, issued_at, last_seen_at, expires_at) "
                    + "VALUES ('short', @id, @now, @now, @later);",
                    ("id", account.AccountId),
                    ("now", TempDb.Now.UtcDateTime),
                    ("later", TempDb.Now.AddDays(30).UtcDateTime)),
                "token");
        }

        [PostgresFact]
        public void A_session_that_expires_before_it_was_issued_is_refused()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            AssertRejected(
                () => db.Execute(
                    "INSERT INTO sessions (token, account_id, issued_at, last_seen_at, expires_at) "
                    + "VALUES (@token, @id, @now, @now, @earlier);",
                    ("token", new string('t', 43)),
                    ("id", account.AccountId),
                    ("now", TempDb.Now.UtcDateTime),
                    ("earlier", TempDb.Now.AddDays(-1).UtcDateTime)),
                "expiry_after_issue");
        }

        // ---- characters ------------------------------------------------------

        [PostgresFact]
        public void A_character_cannot_belong_to_an_account_that_does_not_exist()
        {
            using TempDb db = new TempDb();

            Assert.Throws<PostgresException>(
                () => db.Characters.Save(TempDb.ACharacter(999999)));
        }

        [PostgresFact]
        public void Deleting_an_account_takes_its_characters_with_it()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord character = TempDb.ACharacter(account.AccountId);

            db.Characters.Save(character);

            db.Execute("DELETE FROM accounts WHERE account_id = @id;", ("id", account.AccountId));

            Assert.Null(db.Characters.Find(character.CharacterUid));
        }

        [PostgresFact]
        public void The_upstream_placeholder_uid_is_not_storable_at_all()
        {
            // 'valid-UIDs-have-at-least-one-' passes the client's Contains("-")
            // check and then throws in new Guid(uid). As a uuid column it cannot
            // be written in the first place.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            Assert.ThrowsAny<Exception>(
                () => db.Execute(
                    "INSERT INTO characters (character_uid, account_id, name, slot_index, "
                    + "data_json, created_at, updated_at) "
                    + "VALUES ('valid-UIDs-have-at-least-one-', @id, 'Billy', 0, '{}', @now, @now);",
                    ("id", account.AccountId),
                    ("now", TempDb.Now.UtcDateTime)));
        }

        [PostgresFact]
        public void A_nameless_character_is_refused()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            AssertRejected(
                () => db.Execute(
                    "INSERT INTO characters (character_uid, account_id, name, slot_index, "
                    + "data_json, created_at, updated_at) "
                    + "VALUES (@uid, @id, '  ', 0, '{}', @now, @now);",
                    ("uid", Guid.NewGuid()),
                    ("id", account.AccountId),
                    ("now", TempDb.Now.UtcDateTime)),
                "name");
        }

        [PostgresFact]
        public void An_empty_data_blob_is_refused_because_the_client_reads_it_unguarded()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            AssertRejected(
                () => db.Execute(
                    "INSERT INTO characters (character_uid, account_id, name, slot_index, "
                    + "data_json, created_at, updated_at) "
                    + "VALUES (@uid, @id, 'Billy', 0, '', @now, @now);",
                    ("uid", Guid.NewGuid()),
                    ("id", account.AccountId),
                    ("now", TempDb.Now.UtcDateTime)),
                "data_json");
        }

        [PostgresFact]
        public void A_slot_beyond_the_roster_is_refused()
        {
            // The roster is MaxCharacters real characters plus one trailing empty
            // slot, so the last legal index is MaxCharacters itself. This is the
            // test that ties SchemaScripts.MaxCharacters to the number in the DDL.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            db.Characters.Save(
                TempDb.ACharacter(account.AccountId, slot: SchemaScripts.MaxCharacters));

            AssertRejected(
                () => db.Characters.Save(
                    TempDb.ACharacter(account.AccountId, slot: SchemaScripts.MaxCharacters + 1)),
                "slot_in_range");

            AssertRejected(
                () => db.Characters.Save(TempDb.ACharacter(account.AccountId, slot: -1)),
                "slot_in_range");
        }

        [PostgresFact]
        public void Two_characters_cannot_hold_one_slot()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            db.Characters.Save(TempDb.ACharacter(account.AccountId, "Billy", slot: 0));

            AssertRejected(
                () => db.Characters.Save(TempDb.ACharacter(account.AccountId, "Silver", slot: 0)),
                "slot");
        }

        [PostgresFact]
        public void Two_accounts_may_each_use_slot_zero()
        {
            // The slot index is unique per account, not globally - an easy index
            // to get wrong in a way nothing notices until the second player.
            using TempDb db = new TempDb();

            AccountRecord one = db.AnAccount("timu");
            AccountRecord two = db.AnAccount("friend");

            db.Characters.Save(TempDb.ACharacter(one.AccountId, "Billy", slot: 0));
            db.Characters.Save(TempDb.ACharacter(two.AccountId, "Silver", slot: 0));

            Assert.Single(db.Characters.ListForAccount(one.AccountId));
            Assert.Single(db.Characters.ListForAccount(two.AccountId));
        }

        [PostgresFact]
        public void An_account_cannot_have_two_create_a_character_slots()
        {
            // The client only ever shows one. A second is a row the player can
            // select and then cannot use.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            db.Characters.Save(
                TempDb.ACharacter(account.AccountId, "New Traveller", slot: 0, empty: true));

            AssertRejected(
                () => db.Characters.Save(
                    TempDb.ACharacter(account.AccountId, "New Traveller", slot: 1, empty: true)),
                "empty_slot");
        }

        [PostgresFact]
        public void An_account_may_hold_many_real_characters_alongside_one_empty_slot()
        {
            // The partial index must not accidentally restrict the normal case.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            for (int slot = 0; slot < SchemaScripts.MaxCharacters; slot++)
            {
                db.Characters.Save(
                    TempDb.ACharacter(account.AccountId, "Traveller " + slot, slot));
            }

            db.Characters.Save(TempDb.ACharacter(
                account.AccountId,
                "New Traveller",
                SchemaScripts.MaxCharacters,
                empty: true));

            Assert.Equal(
                SchemaScripts.MaxCharacters + 1,
                db.Characters.ListForAccount(account.AccountId).Count);
        }

        // ---- the version stamp -----------------------------------------------

        [PostgresFact]
        public void The_schema_version_table_can_never_hold_a_second_row()
        {
            // Two rows would make "which version is this" ambiguous, and the
            // migrator would then apply an arbitrary subset of the scripts.
            using TempDb db = new TempDb();

            Assert.Throws<PostgresException>(
                () => db.Execute("INSERT INTO schema_version (only_row, version) VALUES (TRUE, 2);"));

            Assert.Throws<PostgresException>(
                () => db.Execute("INSERT INTO schema_version (only_row, version) VALUES (FALSE, 2);"));

            Assert.Equal(1, db.Scalar<int>("SELECT COUNT(*) FROM schema_version;"));
        }
    }
}

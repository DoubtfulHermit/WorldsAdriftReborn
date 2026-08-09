using Npgsql;
using WorldsAdriftReborn.Storage.Policy;
using WorldsAdriftReborn.Storage.Records;

namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// The stored accounts. Thin glue: it owns SQL and nothing else, and
    /// delegates every decision about what a username or a password may be to
    /// <see cref="AccountPolicy"/>.
    /// </summary>
    public sealed class AccountRepository
    {
        private readonly Db db;

        public AccountRepository(Db db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        private const string Columns =
            "account_id, username_key, username, display_name, password_hash, "
            + "steam_user_key, created_at, last_login_at";

        /// <summary>
        /// Creates an account, returning null if the username is already taken.
        ///
        /// Null rather than an exception because "that name is gone" is an
        /// ordinary answer the sign-up page has to render, not a fault. A
        /// username or password the policy refuses IS a fault - the caller was
        /// supposed to check first and show the player why.
        ///
        /// The uniqueness check is the database's, not a SELECT followed by an
        /// INSERT: two sign-ups racing for one name would both see it free.
        /// </summary>
        public AccountRecord? Create(
            string username,
            string displayName,
            string password,
            string? steamUserKey,
            DateTimeOffset now)
        {
            if (!AccountPolicy.IsUsableUsername(username))
            {
                throw new ArgumentException(
                    "Refusing to store an unusable username.", nameof(username));
            }

            if (!AccountPolicy.IsUsablePassword(password))
            {
                throw new ArgumentException(
                    "Refusing to store an unusable password.", nameof(password));
            }

            string typed = AccountPolicy.TypedUsername(username);
            string key = AccountPolicy.NormalizeUsername(username);

            // An empty display name is a dead menu on the client, so it falls
            // back to the username rather than being allowed through as blank.
            string screenName = string.IsNullOrWhiteSpace(displayName)
                ? typed
                : displayName.Trim();

            string hash = AccountPolicy.HashPassword(password);
            string? steam = Normalize(steamUserKey);

            // Only a real SteamID is ever stored. A Steam-less client sends the
            // literal "steamUserId", and storing that would make every such
            // player one shared account - after which the partial unique index
            // rejects the second friend to sign up, and nobody else.
            if (steam != null && !AccountPolicy.IsRealSteamUserKey(steam))
            {
                steam = null;
            }

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "INSERT INTO accounts "
                + "(username_key, username, display_name, password_hash, steam_user_key, created_at) "
                + "VALUES (@username_key, @username, @display_name, @password_hash, "
                + "@steam_user_key, @created_at) "
                + "RETURNING " + Columns + ";";

            command.Parameters.AddWithValue("username_key", key);
            command.Parameters.AddWithValue("username", typed);
            command.Parameters.AddWithValue("display_name", screenName);
            command.Parameters.AddWithValue("password_hash", hash);
            command.Parameters.AddWithValue("steam_user_key", (object?)steam ?? DBNull.Value);
            command.Parameters.AddWithValue("created_at", Timestamps.ToDb(now));

            try
            {
                using NpgsqlDataReader reader = command.ExecuteReader();

                return reader.Read() ? Read(reader) : null;
            }
            catch (PostgresException e) when (IsUniqueViolation(e))
            {
                return null;
            }
        }

        public AccountRecord? FindById(long accountId)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "SELECT " + Columns + " FROM accounts WHERE account_id = @value;";
            command.Parameters.AddWithValue("value", accountId);

            using NpgsqlDataReader reader = command.ExecuteReader();

            return reader.Read() ? Read(reader) : null;
        }

        /// <summary>
        /// Looks an account up by whatever the player typed, matching on the
        /// normalized key so capitalisation and stray whitespace do not hide an
        /// account from its owner.
        /// </summary>
        public AccountRecord? FindByUsername(string? username)
        {
            string key = AccountPolicy.NormalizeUsername(username);

            if (key.Length == 0)
            {
                return null;
            }

            return QueryOne("username_key = @value", key);
        }

        /// <summary>
        /// Looks an account up by linked SteamID. This is how a player who once
        /// typed a password never sees the login form again, and it is also what
        /// the 28-minute Steam-only refresh resolves through - so it must return
        /// the same account every time or the player's roster identity flips
        /// mid-session.
        /// </summary>
        public AccountRecord? FindBySteamUserKey(string? steamUserKey)
        {
            string? key = Normalize(steamUserKey);

            if (key == null || !AccountPolicy.IsRealSteamUserKey(key))
            {
                return null;
            }

            return QueryOne("steam_user_key = @value", key);
        }

        /// <summary>
        /// Verifies a password and returns the account, or null.
        ///
        /// One null for both "no such account" and "wrong password", and PBKDF2
        /// is run against a dummy hash in the first case so the two take the same
        /// time. Otherwise the response time answers "does this username exist"
        /// for anyone who asks.
        /// </summary>
        public AccountRecord? Verify(string? username, string? password)
        {
            AccountRecord? account = FindByUsername(username);

            if (account == null)
            {
                AccountPolicy.VerifyPassword(password ?? string.Empty, AccountPolicy.DummyHash);
                return null;
            }

            return AccountPolicy.VerifyPassword(password, account.PasswordHash) ? account : null;
        }

        /// <summary>
        /// Attaches a SteamID to an account, opportunistically, on a successful
        /// password login. Returns false and changes nothing if another account
        /// already holds it - a shared machine is a normal situation and must not
        /// turn one friend's login into a failure for the other.
        ///
        /// A userKey that is not a real SteamID is ignored rather than stored.
        /// </summary>
        public bool LinkSteamUserKey(long accountId, string? steamUserKey)
        {
            string? key = Normalize(steamUserKey);

            if (key == null || !AccountPolicy.IsRealSteamUserKey(key))
            {
                return false;
            }

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "UPDATE accounts SET steam_user_key = @steam_user_key WHERE account_id = @id;";
            command.Parameters.AddWithValue("steam_user_key", key);
            command.Parameters.AddWithValue("id", accountId);

            try
            {
                return command.ExecuteNonQuery() == 1;
            }
            catch (PostgresException e) when (IsUniqueViolation(e))
            {
                return false;
            }
        }

        /// <summary>Stamps a successful login. Purely operational; nothing reads it.</summary>
        public bool TouchLastLogin(long accountId, DateTimeOffset now)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "UPDATE accounts SET last_login_at = @at WHERE account_id = @id;";
            command.Parameters.AddWithValue("at", Timestamps.ToDb(now));
            command.Parameters.AddWithValue("id", accountId);

            return command.ExecuteNonQuery() == 1;
        }

        /// <summary>
        /// How many accounts exist. For the hard account cap - a runaway guard on
        /// a public sign-up page, not a security control.
        /// </summary>
        public int Count()
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "SELECT COUNT(*) FROM accounts;";

            return Convert.ToInt32(command.ExecuteScalar() ?? 0);
        }

        /// <summary>
        /// How many accounts were created at or after <paramref name="since"/>.
        /// For the dashboard's "signups today vs total" line, where the caller
        /// passes the start of the current day.
        /// </summary>
        public int CountCreatedSince(DateTimeOffset since)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "SELECT COUNT(*) FROM accounts WHERE created_at >= @since;";
            command.Parameters.AddWithValue("since", Timestamps.ToDb(since));

            return Convert.ToInt32(command.ExecuteScalar() ?? 0);
        }

        /// <summary>
        /// The most recently created accounts, newest first, as dashboard
        /// summaries - username, signup time, and how many real characters each
        /// owns. Selects into <see cref="AccountSummary"/> rather than
        /// <see cref="AccountRecord"/> precisely so the password hash and steam
        /// key never leave the database on this path.
        ///
        /// The character count is a correlated subquery counting non-empty slots,
        /// so an account with only the trailing create-new slot reads as zero
        /// characters rather than one.
        /// </summary>
        public IReadOnlyList<AccountSummary> Recent(int limit)
        {
            if (limit <= 0)
            {
                return Array.Empty<AccountSummary>();
            }

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "SELECT a.username, a.created_at, "
                + "(SELECT COUNT(*) FROM characters c "
                + " WHERE c.account_id = a.account_id AND c.is_empty_slot = FALSE) AS character_count "
                + "FROM accounts a ORDER BY a.created_at DESC, a.account_id DESC LIMIT @limit;";
            command.Parameters.AddWithValue("limit", limit);

            List<AccountSummary> summaries = new List<AccountSummary>();

            using NpgsqlDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                summaries.Add(new AccountSummary(
                    reader.GetString(0),
                    Timestamps.FromDb(reader.GetDateTime(1)),
                    Convert.ToInt32(reader.GetInt64(2))));
            }

            return summaries;
        }

        /// <summary>
        /// Total real (non-empty-slot) characters across every account. The
        /// dashboard's world-population figure; distinct from account count
        /// because one account can own several.
        /// </summary>
        public int CountCharacters()
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "SELECT COUNT(*) FROM characters WHERE is_empty_slot = FALSE;";

            return Convert.ToInt32(command.ExecuteScalar() ?? 0);
        }

        private AccountRecord? QueryOne(string where, string value)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "SELECT " + Columns + " FROM accounts WHERE " + where + ";";
            command.Parameters.AddWithValue("value", value);

            using NpgsqlDataReader reader = command.ExecuteReader();

            return reader.Read() ? Read(reader) : null;
        }

        internal static AccountRecord Read(NpgsqlDataReader reader)
        {
            return new AccountRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                Timestamps.FromDb(reader.GetDateTime(6)),
                reader.IsDBNull(7) ? null : Timestamps.FromDb(reader.GetDateTime(7)));
        }

        private static string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value!.Trim();
        }

        /// <summary>
        /// SQLSTATE 23505, unique_violation. Narrower than "any constraint" on
        /// purpose: a CHECK failing means we wrote code that produces rows the
        /// schema forbids, and swallowing that as "name taken" would hide the bug
        /// behind a plausible message.
        /// </summary>
        internal static bool IsUniqueViolation(PostgresException e)
        {
            return e.SqlState == PostgresErrorCodes.UniqueViolation;
        }
    }
}

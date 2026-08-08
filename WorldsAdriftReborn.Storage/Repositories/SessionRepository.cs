using Npgsql;
using WorldsAdriftReborn.Storage.Policy;
using WorldsAdriftReborn.Storage.Records;

namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// The stored session tokens.
    ///
    /// The whole design of this table is a response to one client behaviour: a
    /// token refresh that fails is silent and terminal. The client re-auths every
    /// 1680 seconds, the no-linked-account callback is an empty delegate, and no
    /// further refresh is ever scheduled. So a session that expires while
    /// somebody is playing does not log them out with a message - it leaves them
    /// in a game where things quietly stop working. Hence 30 days, sliding on
    /// every use: a playing player can never reach the expiry.
    /// </summary>
    public sealed class SessionRepository
    {
        private readonly Db db;

        public SessionRepository(Db db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        private const string Columns = "token, account_id, issued_at, last_seen_at, expires_at";

        /// <summary>
        /// Mints a session for an account.
        ///
        /// Existing sessions are left alone: the same player may be signed in
        /// from the game and from the sign-up page at once, and revoking on issue
        /// would make the second login silently break the first.
        /// </summary>
        public SessionRecord Issue(long accountId, DateTimeOffset now)
        {
            SessionRecord session = new SessionRecord(
                AccountPolicy.NewSessionToken(),
                accountId,
                now,
                now,
                AccountPolicy.ExpiryFrom(now));

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "INSERT INTO sessions (" + Columns + ") VALUES "
                + "(@token, @account_id, @issued_at, @last_seen_at, @expires_at);";

            command.Parameters.AddWithValue("token", session.Token);
            command.Parameters.AddWithValue("account_id", session.AccountId);
            command.Parameters.AddWithValue("issued_at", Timestamps.ToDb(session.IssuedAt));
            command.Parameters.AddWithValue("last_seen_at", Timestamps.ToDb(session.LastSeenAt));
            command.Parameters.AddWithValue("expires_at", Timestamps.ToDb(session.ExpiresAt));

            command.ExecuteNonQuery();

            return session;
        }

        /// <summary>
        /// Resolves a token to its session and slides the expiry forward, or
        /// returns null if the token is unknown or spent.
        ///
        /// One statement, not a read followed by a write: two requests arriving
        /// together from the same player would otherwise race, and the answer
        /// this returns has to be the state it actually wrote.
        ///
        /// An expired row is deleted rather than left to a sweeper. There is no
        /// scheduled job in this deployment, and a table that only grows is a 2am
        /// problem nobody has been told about.
        /// </summary>
        public SessionRecord? Resolve(string? token, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            using NpgsqlConnection connection = db.Open();

            using (NpgsqlCommand slide = connection.CreateCommand())
            {
                slide.CommandText =
                    "UPDATE sessions SET last_seen_at = @now, expires_at = @expires "
                    + "WHERE token = @token AND expires_at >= @now "
                    + "RETURNING " + Columns + ";";
                slide.Parameters.AddWithValue("now", Timestamps.ToDb(now));
                slide.Parameters.AddWithValue(
                    "expires", Timestamps.ToDb(AccountPolicy.ExpiryFrom(now)));
                slide.Parameters.AddWithValue("token", token!);

                using NpgsqlDataReader reader = slide.ExecuteReader();

                if (reader.Read())
                {
                    return Read(reader);
                }
            }

            // Either the token is unknown or it is spent. Clearing it costs one
            // statement on a path that has already failed.
            using NpgsqlCommand purge = connection.CreateCommand();
            purge.CommandText = "DELETE FROM sessions WHERE token = @token AND expires_at < @now;";
            purge.Parameters.AddWithValue("token", token!);
            purge.Parameters.AddWithValue("now", Timestamps.ToDb(now));
            purge.ExecuteNonQuery();

            return null;
        }

        /// <summary>
        /// Reads a session without touching its expiry. For diagnostics and for
        /// tests that need to observe what <see cref="Resolve"/> did; the login
        /// path should use Resolve so the token keeps sliding.
        /// </summary>
        public SessionRecord? Peek(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "SELECT " + Columns + " FROM sessions WHERE token = @token;";
            command.Parameters.AddWithValue("token", token!);

            using NpgsqlDataReader reader = command.ExecuteReader();

            return reader.Read() ? Read(reader) : null;
        }

        /// <summary>Drops one session. Signing out, and the operator's big hammer.</summary>
        public bool Revoke(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "DELETE FROM sessions WHERE token = @token;";
            command.Parameters.AddWithValue("token", token!);

            return command.ExecuteNonQuery() == 1;
        }

        /// <summary>Drops every session an account holds.</summary>
        public int RevokeAllFor(long accountId)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "DELETE FROM sessions WHERE account_id = @account_id;";
            command.Parameters.AddWithValue("account_id", accountId);

            return command.ExecuteNonQuery();
        }

        /// <summary>
        /// Removes every spent session. Cheap enough to call at startup; not
        /// required for correctness, since <see cref="Resolve"/> refuses an
        /// expired token whether or not the row is still there.
        /// </summary>
        public int DeleteExpired(DateTimeOffset now)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "DELETE FROM sessions WHERE expires_at < @now;";
            command.Parameters.AddWithValue("now", Timestamps.ToDb(now));

            return command.ExecuteNonQuery();
        }

        private static SessionRecord Read(NpgsqlDataReader reader)
        {
            return new SessionRecord(
                reader.GetString(0),
                reader.GetInt64(1),
                Timestamps.FromDb(reader.GetDateTime(2)),
                Timestamps.FromDb(reader.GetDateTime(3)),
                Timestamps.FromDb(reader.GetDateTime(4)));
        }
    }
}

using Npgsql;
using WorldsAdriftReborn.Storage;
using WorldsAdriftReborn.Storage.Records;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// A throwaway PostgreSQL schema, migrated to the current version, dropped on
    /// dispose.
    ///
    /// A schema rather than a database: creating a database needs a privilege the
    /// role running the servers should not have, and it cannot be done inside a
    /// transaction on a connection that is already open. A schema is cheap, is
    /// fully isolated by search_path, and means these tests can run against the
    /// same database an operator already has without touching what is in it.
    ///
    /// Real Postgres rather than a fake: the constraints are the whole point of
    /// the schema, and a fake that accepted rows the real server rejects would be
    /// worse than no test at all.
    /// </summary>
    internal sealed class TempDb : IDisposable
    {
        private readonly string schema;

        internal TempDb()
        {
            schema = "wareborn_test_" + Guid.NewGuid().ToString("N");

            using (NpgsqlConnection setup = new NpgsqlConnection(Db.Configured))
            {
                setup.Open();

                using NpgsqlCommand create = setup.CreateCommand();
                create.CommandText = "CREATE SCHEMA " + Quote(schema) + ";";
                create.ExecuteNonQuery();
            }

            NpgsqlConnectionStringBuilder builder =
                new NpgsqlConnectionStringBuilder(Db.Configured)
                {
                    SearchPath = schema,
                };

            Db = new Db(builder.ToString());
            Db.EnsureSchema();

            Accounts = new Repositories.AccountRepository(Db);
            Sessions = new Repositories.SessionRepository(Db);
            Characters = new Repositories.CharacterRepository(Db);
            Inventories = new Repositories.InventoryRepository(Db);
            Progressions = new Repositories.ProgressionRepository(Db);
        }

        internal Db Db { get; }

        internal Repositories.AccountRepository Accounts { get; }

        internal Repositories.SessionRepository Sessions { get; }

        internal Repositories.CharacterRepository Characters { get; }

        internal Repositories.InventoryRepository Inventories { get; }

        internal Repositories.ProgressionRepository Progressions { get; }

        /// <summary>A fixed instant, so nothing here depends on the wall clock.</summary>
        internal static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// Runs raw SQL, so that a test can try to write a row the repositories
        /// would never build. That is the only way to prove a CHECK is doing
        /// anything: if every insert goes through code that already refuses the
        /// bad value, the constraint could be missing and nothing would notice.
        /// </summary>
        internal void Execute(string sql, params (string Name, object? Value)[] parameters)
        {
            using NpgsqlConnection connection = Db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = sql;

            foreach ((string name, object? value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            command.ExecuteNonQuery();
        }

        internal T Scalar<T>(string sql)
        {
            using NpgsqlConnection connection = Db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = sql;

            return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T));
        }

        /// <summary>An account to hang other rows off, with sane values throughout.</summary>
        internal AccountRecord AnAccount(string username = "timu", string? steamUserKey = null)
        {
            return Accounts.Create(username, username, "hunter22", steamUserKey, Now)!;
        }

        /// <summary>A character row that every constraint accepts, for tests to spoil one field of.</summary>
        internal static CharacterRecord ACharacter(
            long accountId,
            string name = "Billy Bones",
            int slot = 0,
            bool empty = false)
        {
            return new CharacterRecord(
                Guid.NewGuid(),
                accountId,
                name,
                slot,
                empty,
                "{\"Cosmetics\":{}}",
                Now,
                Now);
        }

        public void Dispose()
        {
            try
            {
                using NpgsqlConnection connection = new NpgsqlConnection(Db.Configured);
                connection.Open();

                using NpgsqlCommand drop = connection.CreateCommand();
                drop.CommandText = "DROP SCHEMA IF EXISTS " + Quote(schema) + " CASCADE;";
                drop.ExecuteNonQuery();
            }
            catch (NpgsqlException)
            {
                // A leaked test schema is untidy, not a reason to fail a green run.
            }
        }

        /// <summary>
        /// Schema names cannot be parameterised, so the one place a name is
        /// interpolated into SQL quotes it properly. The value is a Guid we just
        /// generated, but the habit is the point.
        /// </summary>
        private static string Quote(string identifier)
        {
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }
    }
}

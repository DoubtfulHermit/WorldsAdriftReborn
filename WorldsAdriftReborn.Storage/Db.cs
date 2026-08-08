using Npgsql;
using WorldsAdriftReborn.Storage.Schema;

namespace WorldsAdriftReborn.Storage
{
    /// <summary>
    /// Owns the connection string and hands out open connections. Nothing else in
    /// this library opens one.
    ///
    /// Two processes use this database: the login server owns accounts, sessions
    /// and characters; the game server only ever SELECTs characters, to resolve a
    /// characterUid. That is one writer per table, so no cross-process
    /// transaction is ever needed and there is no reverse write path to get
    /// wrong.
    /// </summary>
    public sealed class Db
    {
        /// <summary>
        /// Environment variable holding the connection string. Named and read the
        /// same way as WAREBORN_DATA_DIR in WorldsAdriftServer's JsonFileStore, so
        /// there is one place an operator looks to find out where state lives.
        /// </summary>
        public const string ConnectionStringVariable = "WAREBORN_DB";

        /// <summary>
        /// Used when <see cref="ConnectionStringVariable"/> is unset.
        ///
        /// Deliberately carries NO password. A default with a credential in it
        /// becomes the credential everybody ships, and this file is in a public
        /// repository. Loopback with a peer- or trust-authenticated role is the
        /// intended local setup; anything else must set the variable.
        /// </summary>
        public const string DefaultConnectionString =
            "Host=127.0.0.1;Port=5432;Database=wareborn;Username=wareborn";

        public Db(string? connectionString = null)
        {
            ConnectionString = string.IsNullOrWhiteSpace(connectionString)
                ? Configured
                : connectionString!;
        }

        /// <summary>The connection string this instance uses. Contains a password; do not log it.</summary>
        public string ConnectionString { get; }

        /// <summary>The connection string from the environment, or the local default.</summary>
        public static string Configured
        {
            get
            {
                string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);

                if (!string.IsNullOrWhiteSpace(configured))
                {
                    return configured!;
                }

                return DefaultConnectionString;
            }
        }

        /// <summary>
        /// Whether an operator has actually configured a database. Used by the
        /// test suite to skip rather than fail on a machine that has none.
        /// </summary>
        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ConnectionStringVariable));

        /// <summary>
        /// Opens a connection.
        ///
        /// There is no pragma dance here, which is the point of the move off an
        /// embedded database: foreign keys are always enforced, there is no WAL
        /// mode to set, and lock waiting is the server's business rather than a
        /// per-connection setting that a caller can forget.
        /// </summary>
        public NpgsqlConnection Open()
        {
            NpgsqlConnection connection = new NpgsqlConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        /// <summary>
        /// Brings the database up to the current schema version, doing nothing if
        /// it is already there. Safe to call on every start and from both
        /// processes at once: a transaction-scoped advisory lock serialises them,
        /// and each script commits together with the version stamp, so a
        /// half-applied schema is not representable.
        /// </summary>
        public int EnsureSchema()
        {
            using NpgsqlConnection connection = Open();

            Execute(connection, null, SchemaScripts.VersionTable);

            using NpgsqlTransaction transaction = connection.BeginTransaction();

            // Two servers starting together would otherwise both read version 0
            // and both try to CREATE TABLE. The loser's error is harmless but it
            // is also an error message in a log at boot, which is exactly the
            // kind of noise that trains an operator to ignore logs.
            Execute(connection, transaction,
                "SELECT pg_advisory_xact_lock(" + SchemaScripts.MigrationLockKey + ");");

            int version = ReadVersion(connection, transaction);

            foreach (string script in SchemaMigrator.ScriptsToApply(version, SchemaScripts.All))
            {
                version++;

                Execute(connection, transaction, script);

                using NpgsqlCommand stamp = connection.CreateCommand();
                stamp.Transaction = transaction;
                stamp.CommandText = "UPDATE schema_version SET version = @version;";
                stamp.Parameters.AddWithValue("version", version);
                stamp.ExecuteNonQuery();
            }

            transaction.Commit();

            return version;
        }

        /// <summary>The schema version stamped on the database, 0 for a fresh one.</summary>
        public int SchemaVersion()
        {
            using NpgsqlConnection connection = Open();

            Execute(connection, null, SchemaScripts.VersionTable);

            return ReadVersion(connection, null);
        }

        private static int ReadVersion(NpgsqlConnection connection, NpgsqlTransaction? transaction)
        {
            using NpgsqlCommand command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = "SELECT version FROM schema_version;";

            return Convert.ToInt32(command.ExecuteScalar() ?? 0);
        }

        private static void Execute(
            NpgsqlConnection connection,
            NpgsqlTransaction? transaction,
            string sql)
        {
            using NpgsqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}

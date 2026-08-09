using Npgsql;
using WorldsAdriftReborn.Storage.Policy;

namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// The operator-set server configuration. Thin glue: it owns SQL and nothing
    /// else, and delegates every decision about what a value may be to
    /// <see cref="ServerConfigPolicy"/>.
    ///
    /// One writer (the login server's admin panel) and one hot reader
    /// (/deploymentStatus). The KV table it sits on is deliberately generic, but
    /// the only key with a typed accessor today is the server name, because that
    /// is the only setting the panel exposes.
    /// </summary>
    public sealed class ServerConfigRepository
    {
        private readonly Db db;

        public ServerConfigRepository(Db db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// The server's display name, or <see cref="ServerConfigPolicy.DefaultServerName"/>
        /// if nobody has set one. Falls back to the default rather than throwing:
        /// /deploymentStatus is on the client's hot path and a missing row is the
        /// normal state of a fresh database, not a fault.
        /// </summary>
        public string GetServerName()
        {
            string? stored = Get(ServerConfigPolicy.ServerNameKey);
            return string.IsNullOrWhiteSpace(stored)
                ? ServerConfigPolicy.DefaultServerName
                : stored!;
        }

        /// <summary>
        /// Sets the server's display name. The caller is expected to have checked
        /// <see cref="ServerConfigPolicy.IsValid"/> and shown the operator why if
        /// not; storing an unusable value is a fault, so it throws rather than
        /// silently writing a name the browser cannot render.
        /// </summary>
        public void SetServerName(string? name, DateTimeOffset now)
        {
            if (!ServerConfigPolicy.IsValid(name))
            {
                throw new ArgumentException(
                    "Refusing to store an unusable server name.", nameof(name));
            }

            Set(ServerConfigPolicy.ServerNameKey, ServerConfigPolicy.Normalize(name), now);
        }

        /// <summary>One config value by key, or null if it is not set.</summary>
        public string? Get(string key)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "SELECT value FROM server_config WHERE key = @key;";
            command.Parameters.AddWithValue("key", key);

            object? value = command.ExecuteScalar();
            return value as string;
        }

        /// <summary>
        /// Upserts one config value. Keyed on the primary key so a second write
        /// of the same setting replaces rather than duplicates it.
        /// </summary>
        public void Set(string key, string value, DateTimeOffset now)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "INSERT INTO server_config (key, value, updated_at) "
                + "VALUES (@key, @value, @updated_at) "
                + "ON CONFLICT (key) DO UPDATE SET "
                + "value = excluded.value, updated_at = excluded.updated_at;";

            command.Parameters.AddWithValue("key", key);
            command.Parameters.AddWithValue("value", value);
            command.Parameters.AddWithValue("updated_at", Timestamps.ToDb(now));

            command.ExecuteNonQuery();
        }
    }
}

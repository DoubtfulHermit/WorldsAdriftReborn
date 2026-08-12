using Npgsql;
using WorldsAdriftReborn.Storage;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// A [Fact] that needs a real PostgreSQL server, and skips - loudly, with a
    /// reason - when there is not one.
    ///
    /// The policy tests need no database and must always run. These do, and a
    /// contributor who has not set WAREBORN_DB should get a suite that says
    /// "skipped, here is how to run them", not a suite that fails and teaches
    /// them that red is normal.
    ///
    /// The reachability check runs once and is cached, so a missing server costs
    /// one connection attempt for the whole assembly rather than one per test.
    /// </summary>
    public sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (Unavailable != null)
            {
                Skip = Unavailable;
            }
        }

        private static readonly Lazy<string?> unavailable = new Lazy<string?>(Check);

        internal static string? Unavailable => unavailable.Value;

        private static string? Check()
        {
            if (!Db.IsConfigured)
            {
                return Db.ConnectionStringVariable + " is not set, so there is no database to "
                    + "test against. Set it to a PostgreSQL connection string and re-run, e.g. "
                    + Db.ConnectionStringVariable
                    + "='Host=127.0.0.1;Port=5432;Database=wareborn;Username=wareborn'.";
            }

            try
            {
                using NpgsqlConnection connection = new NpgsqlConnection(Db.Configured);
                connection.Open();
                return null;
            }
            catch (Exception e)
            {
                // Configured but unreachable is a real problem worth naming; it
                // still skips rather than fails, because the alternative is a red
                // suite that says nothing about the code under test.
                return Db.ConnectionStringVariable + " is set but the server could not be "
                    + "reached: " + e.Message;
            }
        }
    }
}

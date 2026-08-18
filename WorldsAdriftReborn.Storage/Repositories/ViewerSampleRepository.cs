using Npgsql;

namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// The recorded audience of the public map: one row per minute, each one an
    /// instant and a count.
    ///
    /// The narrowest repository in this library, on purpose. Its whole surface is
    /// "write a number for a minute" and "read numbers between two instants" -
    /// there is no method here that takes or returns anything about a person,
    /// because the table it sits on has no column for one. If a later change wants
    /// to know WHO was watching, it cannot start here; it has to add a column, a
    /// migration and a parameter, and be seen doing it.
    ///
    /// One writer (the login server's per-minute sampler) and one reader (the map
    /// and the operator console, both in the same process), so there is no
    /// cross-process ordering to get wrong.
    /// </summary>
    public sealed class ViewerSampleRepository
    {
        private readonly Db db;

        public ViewerSampleRepository(Db db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// Rounds an instant down to the minute it falls in, in UTC.
        ///
        /// Exposed and pure so the sampler and the tests agree on where a minute
        /// starts, and so nothing has to remember to do it at the call site: the
        /// grain is the privacy property, so <see cref="Record"/> applies this
        /// itself rather than trusting a caller to.
        /// </summary>
        public static DateTimeOffset FloorToMinute(DateTimeOffset at)
        {
            long ticks = at.UtcTicks;
            return new DateTimeOffset(ticks - ticks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
        }

        /// <summary>
        /// Records the viewer count for the minute <paramref name="at"/> falls in.
        ///
        /// A second write for the same minute keeps the HIGHER count. That is the
        /// only sensible merge: the two obvious cases are the server being
        /// restarted mid-minute and a clock nudge, and in both the question the
        /// series answers is "how busy did it get", so losing the busier of two
        /// readings would be the wrong kind of wrong.
        /// </summary>
        public void Record(DateTimeOffset at, int viewerCount)
        {
            if (viewerCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(viewerCount), viewerCount, "A viewer count cannot be negative.");
            }

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "INSERT INTO map_viewer_samples (sampled_at, viewer_count) "
                + "VALUES (@sampled_at, @viewer_count) "
                + "ON CONFLICT (sampled_at) DO UPDATE "
                + "SET viewer_count = GREATEST(map_viewer_samples.viewer_count, EXCLUDED.viewer_count);";
            command.Parameters.AddWithValue("sampled_at", FloorToMinute(at));
            command.Parameters.AddWithValue("viewer_count", viewerCount);

            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Every sample in <c>[from, to)</c>, oldest first.
        ///
        /// Half-open so that two adjacent windows never double-count the sample on
        /// the boundary, which would put a phantom spike in a stitched series.
        /// </summary>
        public IReadOnlyList<(DateTimeOffset At, int Count)> Between(
            DateTimeOffset from, DateTimeOffset to)
        {
            List<(DateTimeOffset, int)> samples = new List<(DateTimeOffset, int)>();

            if (to <= from)
            {
                return samples;
            }

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "SELECT sampled_at, viewer_count FROM map_viewer_samples "
                + "WHERE sampled_at >= @from AND sampled_at < @to "
                + "ORDER BY sampled_at;";
            command.Parameters.AddWithValue("from", from.ToUniversalTime());
            command.Parameters.AddWithValue("to", to.ToUniversalTime());

            using NpgsqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                samples.Add((
                    new DateTimeOffset(reader.GetDateTime(0).ToUniversalTime(), TimeSpan.Zero),
                    reader.GetInt32(1)));
            }

            return samples;
        }

        /// <summary>
        /// The highest count ever recorded, or 0 on a database that has never
        /// sampled. Operator-side only: the public page shows a day's peak, which
        /// it derives from the day it already fetched.
        /// </summary>
        public int PeakAllTime()
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "SELECT COALESCE(MAX(viewer_count), 0) FROM map_viewer_samples;";

            return Convert.ToInt32(command.ExecuteScalar() ?? 0);
        }

        /// <summary>How many minutes have been recorded. Used by tests and the console's footer.</summary>
        public long Count()
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "SELECT COUNT(*) FROM map_viewer_samples;";

            return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
        }
    }
}

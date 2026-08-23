using Npgsql;
using WorldsAdriftReborn.Storage;
using WorldsAdriftReborn.Storage.Repositories;
using WorldsAdriftReborn.Storage.Schema;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// The recorded audience of the public map.
    ///
    /// Two kinds of test here, and the second kind is the point. The first is the
    /// ordinary repository contract - a written sample comes back, a window
    /// selects, a restart inside a minute does not duplicate. The second asserts
    /// against the REAL database that there is nowhere in this table for a visitor
    /// to be recorded, so "aggregate only" is something the schema enforces rather
    /// than something a comment claims.
    /// </summary>
    public class ViewerSampleRepositoryTests
    {
        private static readonly DateTimeOffset Noon =
            new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        // ---- the grain is a minute, and that is a privacy property -------------

        [Fact]
        public void An_instant_is_floored_to_its_minute_before_it_is_stored()
        {
            // Applied by the repository rather than trusted to the caller: the
            // grain is what stops the series' own density from being a visit log,
            // so it must not be a thing a call site can forget.
            Assert.Equal(Noon,
                ViewerSampleRepository.FloorToMinute(Noon + TimeSpan.FromSeconds(59.999)));
            Assert.Equal(Noon, ViewerSampleRepository.FloorToMinute(Noon));
            Assert.Equal(Noon + TimeSpan.FromMinutes(1),
                ViewerSampleRepository.FloorToMinute(Noon + TimeSpan.FromSeconds(60)));
        }

        [PostgresFact]
        public void Twenty_writes_inside_one_minute_are_one_row()
        {
            using TempDb db = new TempDb();

            for (int i = 0; i < 20; i++)
            {
                db.ViewerSamples.Record(Noon + TimeSpan.FromSeconds(i), 3);
            }

            Assert.Equal(1, db.ViewerSamples.Count());
        }

        [PostgresFact]
        public void A_second_write_for_the_same_minute_keeps_the_busier_reading()
        {
            // The restart-inside-a-minute case. The question the series answers is
            // "how busy did it get", so losing the busier of two readings would be
            // the wrong kind of wrong.
            using TempDb db = new TempDb();

            db.ViewerSamples.Record(Noon, 7);
            db.ViewerSamples.Record(Noon + TimeSpan.FromSeconds(30), 2);

            Assert.Equal(1, db.ViewerSamples.Count());
            Assert.Equal(7, Assert.Single(db.ViewerSamples.Between(Noon, Noon + TimeSpan.FromMinutes(1))).Count);
        }

        // ---- the ordinary contract ---------------------------------------------

        [PostgresFact]
        public void A_written_sample_comes_back_with_its_minute_and_its_count()
        {
            using TempDb db = new TempDb();

            db.ViewerSamples.Record(Noon + TimeSpan.FromSeconds(17), 5);

            (DateTimeOffset at, int count) =
                Assert.Single(db.ViewerSamples.Between(Noon, Noon + TimeSpan.FromHours(1)));

            Assert.Equal(Noon, at);
            Assert.Equal(5, count);
        }

        [PostgresFact]
        public void A_window_is_half_open_so_two_of_them_never_double_count_a_sample()
        {
            using TempDb db = new TempDb();

            db.ViewerSamples.Record(Noon, 1);
            db.ViewerSamples.Record(Noon + TimeSpan.FromMinutes(10), 2);
            db.ViewerSamples.Record(Noon + TimeSpan.FromMinutes(20), 3);

            Assert.Equal(2, db.ViewerSamples.Between(Noon, Noon + TimeSpan.FromMinutes(20)).Count);
            Assert.Single(db.ViewerSamples.Between(Noon + TimeSpan.FromMinutes(20), Noon + TimeSpan.FromMinutes(30)));
        }

        [PostgresFact]
        public void Samples_come_back_oldest_first_however_they_were_written()
        {
            using TempDb db = new TempDb();

            db.ViewerSamples.Record(Noon + TimeSpan.FromMinutes(5), 3);
            db.ViewerSamples.Record(Noon, 1);
            db.ViewerSamples.Record(Noon + TimeSpan.FromMinutes(2), 2);

            IReadOnlyList<(DateTimeOffset At, int Count)> series =
                db.ViewerSamples.Between(Noon, Noon + TimeSpan.FromHours(1));

            Assert.Equal(new[] { 1, 2, 3 }, series.Select(s => s.Count).ToArray());
        }

        [PostgresFact]
        public void An_empty_window_is_an_empty_list_rather_than_a_null()
        {
            using TempDb db = new TempDb();

            Assert.Empty(db.ViewerSamples.Between(Noon, Noon + TimeSpan.FromHours(1)));
            Assert.Empty(db.ViewerSamples.Between(Noon, Noon));
            Assert.Empty(db.ViewerSamples.Between(Noon + TimeSpan.FromHours(1), Noon));
            Assert.Equal(0, db.ViewerSamples.PeakAllTime());
            Assert.Equal(0, db.ViewerSamples.Count());
        }

        [PostgresFact]
        public void Zero_viewers_is_a_real_reading_and_is_recorded_as_one()
        {
            // The sampler runs whether or not anybody is there, which is what
            // breaks the link between "a row exists" and "somebody arrived".
            using TempDb db = new TempDb();

            db.ViewerSamples.Record(Noon, 0);

            Assert.Equal(0, Assert.Single(db.ViewerSamples.Between(Noon, Noon + TimeSpan.FromMinutes(1))).Count);
        }

        [PostgresFact]
        public void The_all_time_peak_is_the_busiest_minute_ever_recorded()
        {
            using TempDb db = new TempDb();

            db.ViewerSamples.Record(Noon, 2);
            db.ViewerSamples.Record(Noon + TimeSpan.FromDays(40), 11);
            db.ViewerSamples.Record(Noon + TimeSpan.FromDays(80), 4);

            Assert.Equal(11, db.ViewerSamples.PeakAllTime());
        }

        [PostgresFact]
        public void A_negative_count_is_refused_by_the_repository_and_by_the_database()
        {
            using TempDb db = new TempDb();

            Assert.Throws<ArgumentOutOfRangeException>(() => db.ViewerSamples.Record(Noon, -1));

            // And the CHECK is really there, not just the guard above: a call site
            // that reached past the repository still cannot write one.
            Assert.Throws<PostgresException>(() => db.Execute(
                "INSERT INTO map_viewer_samples (sampled_at, viewer_count) VALUES (@at, -1);",
                ("at", Noon)));
        }

        // ---- there is nowhere here to record a person ---------------------------

        /// <summary>
        /// The tripwire, asserted against the live database rather than the
        /// script: this table has exactly two columns, an instant and a count.
        ///
        /// Somebody who dumps it in five years learns how busy the map was and
        /// cannot learn that any particular person was ever there - not because we
        /// chose not to write it, but because there is no column to write it into.
        /// </summary>
        [PostgresFact]
        public void The_recorded_table_has_exactly_two_columns_and_neither_is_a_visitor()
        {
            using TempDb db = new TempDb();

            Assert.Equal(2, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.columns "
                + "WHERE table_schema = current_schema() AND table_name = 'map_viewer_samples';"));

            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.columns "
                + "WHERE table_schema = current_schema() AND table_name = 'map_viewer_samples' "
                + "AND column_name = 'sampled_at' AND data_type = 'timestamp with time zone';"));

            Assert.Equal(1, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.columns "
                + "WHERE table_schema = current_schema() AND table_name = 'map_viewer_samples' "
                + "AND column_name = 'viewer_count' AND data_type = 'integer';"));
        }

        [PostgresFact]
        public void The_recorded_table_is_joined_to_nothing_so_a_row_cannot_be_resolved_to_a_person()
        {
            // No foreign key out of it, and nothing points into it. Every other
            // table in this database hangs off accounts or characters; this one is
            // deliberately an island, so there is no join that turns a busy minute
            // into a list of who was there.
            using TempDb db = new TempDb();

            Assert.Equal(0, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.table_constraints "
                + "WHERE table_schema = current_schema() AND constraint_type = 'FOREIGN KEY' "
                + "AND table_name = 'map_viewer_samples';"));

            Assert.Equal(0, db.Scalar<int>(
                "SELECT COUNT(*) FROM information_schema.constraint_column_usage "
                + "WHERE table_schema = current_schema() AND table_name = 'map_viewer_samples' "
                + "AND constraint_name IN ("
                + "  SELECT constraint_name FROM information_schema.table_constraints "
                + "  WHERE table_schema = current_schema() AND constraint_type = 'FOREIGN KEY');"));
        }

        // ---- upgrading a database that already exists ---------------------------

        /// <summary>
        /// The migration a live server will actually run: a database stamped at
        /// v8, with data in it, brought forward to v9 without losing anything.
        ///
        /// Built by running the shipped scripts up to v8 by hand rather than by
        /// using <see cref="TempDb"/>, which always migrates to current - the
        /// whole point is to start from the version production is on today.
        /// </summary>
        [PostgresFact]
        public void An_older_database_upgrades_to_the_viewer_series_without_losing_anything()
        {
            string schema = "wareborn_v8_" + Guid.NewGuid().ToString("N");

            using (NpgsqlConnection setup = new NpgsqlConnection(Db.Configured))
            {
                setup.Open();
                using NpgsqlCommand create = setup.CreateCommand();
                create.CommandText = "CREATE SCHEMA \"" + schema + "\";";
                create.ExecuteNonQuery();
            }

            try
            {
                NpgsqlConnectionStringBuilder builder =
                    new NpgsqlConnectionStringBuilder(Db.Configured) { SearchPath = schema };
                Db db = new Db(builder.ToString());

                // Stand a v8 database up, script by script, exactly as a server
                // running today's build left it. The version table itself is laid
                // down by PRODUCTION code - SchemaVersion() bootstraps it - rather
                // than by a copy of its DDL in this file, so this test cannot pass
                // against a shape the real migrator does not use.
                Assert.Equal(0, db.SchemaVersion());

                using (NpgsqlConnection connection = db.Open())
                {
                    for (int i = 0; i < 8; i++)
                    {
                        Run(connection, SchemaScripts.All[i]);
                    }
                    Run(connection, "UPDATE schema_version SET version = 8;");
                }

                Assert.Equal(8, db.SchemaVersion());

                // Something in it worth not losing.
                AccountRepository accounts = new AccountRepository(db);
                accounts.Create("timu", "Timu", "hunter22", null, Noon);

                // The upgrade.
                Assert.Equal(10, db.EnsureSchema());
                Assert.Equal(10, db.SchemaVersion());

                // The account is still there...
                Assert.Equal(1, accounts.Count());
                Assert.NotNull(accounts.FindByUsername("timu"));

                // ...and the new table works.
                ViewerSampleRepository samples = new ViewerSampleRepository(db);
                samples.Record(Noon, 6);
                Assert.Equal(6, samples.PeakAllTime());

                // And running it again is a no-op, so it is safe on every start.
                Assert.Equal(10, db.EnsureSchema());
                Assert.Equal(1, samples.Count());
            }
            finally
            {
                using NpgsqlConnection cleanup = new NpgsqlConnection(Db.Configured);
                cleanup.Open();
                using NpgsqlCommand drop = cleanup.CreateCommand();
                drop.CommandText = "DROP SCHEMA IF EXISTS \"" + schema + "\" CASCADE;";
                drop.ExecuteNonQuery();
            }
        }

        private static void Run(NpgsqlConnection connection, string sql)
        {
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}

namespace WorldsAdriftServer.PublicMap
{
    /// <summary>
    /// Writes the live viewer count down once a minute, so the map's audience
    /// survives a restart.
    ///
    /// WHY A TIMER AND NOT A WRITE PER REQUEST. A row per poll would be a row
    /// every three seconds per viewer, which is both wasteful and - much more
    /// importantly - a different KIND of data. Even with no identifying column,
    /// a series that dense is a visit log: arrivals and departures are legible in
    /// where the rows start and stop. Sampling on a fixed cadence that runs whether
    /// or not anybody is there breaks that link, because the row exists because a
    /// minute passed, not because somebody arrived. A visitor who reads the map for
    /// half a minute may leave no row at all, and that is the intended behaviour
    /// rather than a rounding error.
    ///
    /// A minute is also small enough to be free (1,440 rows a day) and coarse
    /// enough that the recorded series is only ever a trend.
    ///
    /// The tick doubles as the census's reaper: <see cref="ViewerCensus.Count"/>
    /// sweeps expired entries on the way past, so even on a map nobody is looking
    /// at, no viewer's fingerprint sits in memory for more than the TTL plus the
    /// remainder of a minute.
    ///
    /// Thin glue. The counting is <see cref="ViewerCensus"/>'s and the storing is
    /// the repository's; what is here is a clock and a try/catch, and
    /// <see cref="Tick"/> is separated out so the failure behaviour can be tested
    /// without waiting a minute or opening a database.
    /// </summary>
    internal sealed class ViewerSampler : IDisposable
    {
        /// <summary>One sample per minute. See the class comment for why not per request.</summary>
        internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        private static ViewerSampler? running;

        private readonly Func<DateTimeOffset, int> _count;
        private readonly Action<DateTimeOffset, int> _record;
        private readonly Func<DateTimeOffset> _clock;
        private readonly Action<string> _log;
        private readonly Timer _timer;

        private int _consecutiveFailures;

        internal ViewerSampler(
            Func<DateTimeOffset, int> count,
            Action<DateTimeOffset, int> record,
            Func<DateTimeOffset>? clock = null,
            Action<string>? log = null,
            bool started = true)
        {
            _count = count ?? throw new ArgumentNullException(nameof(count));
            _record = record ?? throw new ArgumentNullException(nameof(record));
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _log = log ?? Console.WriteLine;

            _timer = new Timer(_ => Tick(), null,
                started ? Interval : Timeout.InfiniteTimeSpan,
                started ? Interval : Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// How many consecutive ticks have failed to store. Zero once one succeeds.
        /// Exposed so a test can prove the sampler keeps going rather than dying on
        /// the first failure.
        /// </summary>
        internal int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

        /// <summary>
        /// One sample. Never throws: this runs on a thread-pool timer, where an
        /// escaping exception takes the process down, and a database hiccup must
        /// not be able to kill a login server over a number on a map.
        ///
        /// A failure is logged the first time and then every hundredth time, so a
        /// database that is down for an hour does not write sixty lines an hour
        /// into the journal and train an operator to ignore it.
        /// </summary>
        internal void Tick()
        {
            try
            {
                DateTimeOffset now = _clock();
                _record(now, _count(now));
                Volatile.Write(ref _consecutiveFailures, 0);
            }
            catch (Exception e)
            {
                int failures = Interlocked.Increment(ref _consecutiveFailures);
                if (failures == 1 || failures % 100 == 0)
                {
                    _log("[warn] could not record the map viewer count (" + failures
                        + " in a row): " + e.Message);
                }
            }
        }

        /// <summary>
        /// Starts the one sampler this process runs, wired to the shared census and
        /// the account database. Idempotent, and safe to call before anybody has
        /// ever opened the map.
        /// </summary>
        internal static void Start()
        {
            if (running != null)
            {
                return;
            }

            running = new ViewerSampler(
                ViewerCensus.Shared.Count,
                (at, count) => Persistence.Accounts.ViewerSamples.Record(at, count));

            Console.WriteLine("[info] recording the public map's viewer count once a minute "
                + "(aggregate counts only; no addresses, no visitors, no rows per person).");
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}

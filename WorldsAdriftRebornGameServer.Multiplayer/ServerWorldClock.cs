using System.Diagnostics;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The thin, impure glue that owns the monotonic boot-relative clock feeding
    /// <see cref="WorldClock"/>. All the arithmetic lives in <see cref="WorldClock"/>
    /// (pure, tested); this only supplies "how many real seconds since boot" from a
    /// <see cref="Stopwatch"/> and hands it to that.
    ///
    /// One process-wide epoch: the stopwatch is the server's shared clock, started
    /// once at boot, so every 1131 checkout - whenever it happens - reads the SAME
    /// advancing timeline. That is what puts two clients that joined minutes apart
    /// in phase.
    /// </summary>
    public static class ServerWorldClock
    {
        private static readonly object Gate = new object();
        private static Stopwatch? _stopwatch;

        /// <summary>
        /// Start the shared clock. Called once from server start-up so the epoch is
        /// pinned to actual boot. Idempotent - a second call does not restart the
        /// clock - so a stray call cannot rewind the shared timeline.
        /// </summary>
        public static void Start()
        {
            lock (Gate)
            {
                if (_stopwatch == null)
                {
                    _stopwatch = Stopwatch.StartNew();
                }
            }
        }

        /// <summary>
        /// Real seconds elapsed since <see cref="Start"/>. Lazily starts the clock
        /// if start-up somehow never called <see cref="Start"/>, so a checkout can
        /// never divide by an unstarted clock; the first read then defines the
        /// epoch.
        /// </summary>
        public static double ElapsedSeconds
        {
            get
            {
                lock (Gate)
                {
                    if (_stopwatch == null)
                    {
                        _stopwatch = Stopwatch.StartNew();
                    }
                    return _stopwatch.Elapsed.TotalSeconds;
                }
            }
        }

        /// <summary>
        /// The CURRENT shared world time to seed into a 1131 WorldData checkout.
        /// One-time seed: the client free-runs the cycle after this.
        /// </summary>
        public static WorldTime Current()
        {
            return WorldClock.Current(ElapsedSeconds);
        }
    }
}

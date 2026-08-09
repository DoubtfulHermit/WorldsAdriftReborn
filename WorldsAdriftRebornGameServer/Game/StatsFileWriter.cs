using System.Diagnostics;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// Writes the live-session snapshot to a file every few seconds so the login
    /// server - a separate process that cannot see live peers but can serve the
    /// operator dashboard - can read it. This is the game-side wire of the
    /// file-drop bridge; the numbers themselves come from the pure, tested
    /// <see cref="ServerStats"/> and <see cref="StatsSnapshot"/>.
    ///
    /// It writes ATOMICALLY - a temp file plus a rename - so a reader never sees a
    /// half-written file, and it NEVER throws into the main loop: a full disk or a
    /// permissions change costs a skipped update and a throttled log line, not the
    /// server. The same reasoning and the same /tmp path as
    /// <see cref="TeleportService"/>, which Wine already maps to the host.
    ///
    /// Self-throttled to a few seconds because the main loop turns once per ENet
    /// EVENT, far faster than any sensible snapshot cadence - the same trap the
    /// teleport poll and the relay stats window each guard against.
    /// </summary>
    internal sealed class StatsFileWriter
    {
        private const string DefaultStatsFile = "/tmp/wareborn-stats.json";

        /// <summary>
        /// How often a snapshot is written. Inside the 3-5 s the login server's
        /// staleness threshold expects, and cheap enough that missing it costs
        /// nothing: an empty world serialises to a few hundred bytes.
        /// </summary>
        private static readonly TimeSpan WriteInterval = TimeSpan.FromSeconds(3);

        private readonly string _path;
        private readonly Func<StatsSnapshot> _build;
        private readonly Stopwatch _sinceLastWrite = Stopwatch.StartNew();
        private bool _wroteOnce;
        private long _faults;

        public StatsFileWriter(Func<StatsSnapshot> build)
        {
            _build = build ?? throw new ArgumentNullException(nameof(build));

            string? configured = Environment.GetEnvironmentVariable("WAREBORN_STATS_FILE");
            _path = string.IsNullOrWhiteSpace(configured) ? DefaultStatsFile : configured.Trim();
        }

        /// <summary>The file path, for the startup banner.</summary>
        public string Path => _path;

        /// <summary>
        /// Writes a fresh snapshot if the interval has elapsed (and always on the
        /// first call, so the dashboard has data the moment the server is up).
        /// Safe to call every loop iteration; cheap when not due (one Stopwatch
        /// compare).
        /// </summary>
        public void MaybeWrite()
        {
            if (_wroteOnce && _sinceLastWrite.Elapsed < WriteInterval)
            {
                return;
            }
            _sinceLastWrite.Restart();
            _wroteOnce = true;

            try
            {
                string json = _build().ToJson();

                // Temp-then-rename so a reader never catches a partial file. The
                // temp lives next to the target so the rename stays on one
                // filesystem (a cross-device rename would silently fall back to a
                // non-atomic copy).
                string temp = _path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception e)
            {
                // First fault in full, then sampled: a persistent failure (no
                // disk, bad path) must not turn the log into a fault-per-tick
                // firehose. The dashboard already shows "not reporting" when the
                // file goes stale, so this is diagnosis, not alarm.
                _faults++;
                if (_faults == 1 || _faults % 100 == 0)
                {
                    Console.WriteLine("[warning] stats: could not write " + _path
                        + " (fault #" + _faults + "): " + e.Message);
                }
            }
        }
    }
}

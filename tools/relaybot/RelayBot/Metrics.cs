using System.Globalization;

namespace RelayBot
{
    /// <summary>
    /// The measurement itself: every sample is TRUE end-to-end staleness -
    /// sender wall-clock at ENet_Send minus receiver wall-clock at decode, for
    /// one position/state packet, matched through the timestamp field both bots
    /// stamp with their publish clock. Both bots live in one process on one
    /// monotonic clock (Stopwatch), so the match is exact and needs no clock
    /// sync protocol.
    /// </summary>
    public sealed class Metrics
    {
        public readonly record struct GapEvent(int Bot, double AtSecond, double GapSeconds, string Stream);
        public readonly record struct DisconnectEvent(int Bot, double AtSecond);

        private readonly object _gate = new();
        private readonly Dictionary<(int bot, long second), List<double>> _staleness = new();
        private readonly Dictionary<(int bot, long second), int> _receives = new();
        private readonly Dictionary<(int bot, long second), int> _sends = new();
        private readonly List<GapEvent> _gaps = new();
        private readonly List<DisconnectEvent> _disconnects = new();
        private readonly List<(int Bot, string Reason)> _botDeaths = new();
        private long _unmatched;
        private long _matched;
        private long _heartbeats;
        private long _decodeErrors;
        private long _timelineViolations;

        /// <summary>Set once, when both bots hold authority and publishing begins.</summary>
        public long MeasurementStartNs { get; private set; } = -1;

        public void StartMeasurement(long nowNs)
        {
            lock (_gate)
            {
                if (MeasurementStartNs < 0)
                {
                    MeasurementStartNs = nowNs;
                }
            }
        }

        private long SecondOf(long nowNs) => (nowNs - MeasurementStartNs) / 1_000_000_000L;

        public void RecordSend(int bot, long nowNs)
        {
            lock (_gate)
            {
                if (MeasurementStartNs < 0) return;
                var key = (bot, SecondOf(nowNs));
                _sends.TryGetValue(key, out int n);
                _sends[key] = n + 1;
            }
        }

        /// <summary>A relayed update arrived and matched a recorded send.</summary>
        public void RecordStaleness(int receiverBot, long nowNs, double stalenessMs)
        {
            lock (_gate)
            {
                _matched++;
                if (MeasurementStartNs < 0) return;
                var key = (receiverBot, SecondOf(nowNs));
                if (!_staleness.TryGetValue(key, out List<double> list))
                {
                    _staleness[key] = list = new List<double>();
                }
                list.Add(stalenessMs);
            }
        }

        /// <summary>Any relayed 190602/1073 arrival, matched or not.</summary>
        public void RecordReceive(int receiverBot, long nowNs)
        {
            lock (_gate)
            {
                if (MeasurementStartNs < 0) return;
                var key = (receiverBot, SecondOf(nowNs));
                _receives.TryGetValue(key, out int n);
                _receives[key] = n + 1;
            }
        }

        public void RecordUnmatched()
        {
            lock (_gate) { _unmatched++; }
        }

        /// <summary>
        /// A relayed movement update with NO timestamp field: relay v2's
        /// heartbeat (the emitter re-sends the last position, timestampless by
        /// design, when the source published nothing inside one emit tick).
        /// Counted apart from unmatched: nothing was sent, so nothing could
        /// match, and lumping them together would make delivery look lossy.
        /// </summary>
        public void RecordHeartbeat()
        {
            lock (_gate) { _heartbeats++; }
        }

        /// <summary>A received packet the bot's handling code threw on. Any nonzero is an alarm.</summary>
        public void RecordDecodeError()
        {
            lock (_gate) { _decodeErrors++; }
        }

        /// <summary>
        /// A relayed 1073 whose server-issued synthetic stamp failed to
        /// increase. This is the receiver-side twin of the server's badTsPairs
        /// counter: the client pairs positions with the latest 1073 stamp and
        /// collapses equal stamps, so any nonzero here means the rewrite is
        /// wrong AS DELIVERED, whatever the server thinks it emitted.
        /// </summary>
        public void RecordTimelineViolation()
        {
            lock (_gate) { _timelineViolations++; }
        }

        /// <summary>
        /// A bot's thread ended with a failure while the soak was running. The
        /// 2026-08-09 v2 gate aborted at t=0 with "disconnects: 0" and no
        /// reason printed anywhere - the death that ended the run was invisible
        /// to the summary. Never again: deaths are first-class events.
        /// </summary>
        public void RecordBotDeath(int bot, string reason)
        {
            lock (_gate) { _botDeaths.Add((bot, reason)); }
        }

        public void RecordGap(int bot, long nowNs, double gapSeconds, string stream)
        {
            lock (_gate)
            {
                _gaps.Add(new GapEvent(bot, MeasurementStartNs < 0 ? -1 : (nowNs - MeasurementStartNs) / 1e9, gapSeconds, stream));
            }
        }

        public void RecordDisconnect(int bot, long nowNs)
        {
            lock (_gate)
            {
                _disconnects.Add(new DisconnectEvent(bot, MeasurementStartNs < 0 ? -1 : (nowNs - MeasurementStartNs) / 1e9));
            }
        }

        private static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return double.NaN;
            double rank = p * (sorted.Count - 1);
            int lo = (int)Math.Floor(rank);
            int hi = (int)Math.Ceiling(rank);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
        }

        /// <summary>
        /// CSV: second,bot,staleness_ms_p50,staleness_ms_p95,staleness_ms_max,recv_rate,send_rate.
        /// A second with no receives still gets a row (empty percentiles,
        /// recv_rate 0) - a silent hole in this table IS the observed failure
        /// mode, so it must be visible, not absent.
        /// </summary>
        public void WriteCsv(TextWriter w, long totalSeconds, IReadOnlyList<string> botNames)
        {
            lock (_gate)
            {
                w.WriteLine("second,bot,staleness_ms_p50,staleness_ms_p95,staleness_ms_max,recv_rate,send_rate");
                for (long s = 0; s < totalSeconds; s++)
                {
                    for (int bot = 0; bot < botNames.Count; bot++)
                    {
                        _receives.TryGetValue((bot, s), out int recv);
                        _sends.TryGetValue((bot, s), out int sent);
                        string p50 = "", p95 = "", max = "";
                        if (_staleness.TryGetValue((bot, s), out List<double> list) && list.Count > 0)
                        {
                            List<double> sorted = new(list);
                            sorted.Sort();
                            p50 = Percentile(sorted, 0.50).ToString("0.###", CultureInfo.InvariantCulture);
                            p95 = Percentile(sorted, 0.95).ToString("0.###", CultureInfo.InvariantCulture);
                            max = sorted[^1].ToString("0.###", CultureInfo.InvariantCulture);
                        }
                        w.WriteLine(string.Join(",", s, botNames[bot], p50, p95, max, recv, sent));
                    }
                }
            }
        }

        public sealed record Verdict(bool Flat, double DriftMs, double SlopeMsOverSoak,
            double OverallP50, double OverallP95, double OverallMax,
            long Matched, long Unmatched, long TotalSends, int GapCount, int DisconnectCount,
            long Heartbeats, long DecodeErrors, long TimelineViolations, int BotDeaths, string Detail,
            double OverstaleShare, double OverstaleThresholdMs);

        /// <summary>
        /// How stale a delivered sample may be before it has demonstrably MISSED
        /// an emit tick: one emit interval, plus a tenth for transport and
        /// serialisation on the way out.
        ///
        /// WHY THIS AND NOT A PERCENTILE CEILING. Where inside [0, interval) a
        /// sample lands is decided by the phase between the bots' publish grid
        /// and the emitter's grid, and that phase is fixed at join time and
        /// arbitrary: measured run-to-run on ONE unchanged tree, the same code
        /// puts one bot's median at 0.24 ms and the other's anywhere from 40 to
        /// 46 ms. A p50 ceiling therefore measures luck. Whether a sample waited
        /// LONGER THAN A WHOLE PERIOD does not: no phase can produce that under
        /// a working cadence, so the share that does is a contract violation
        /// however the run's phase fell.
        /// </summary>
        public static double OverstaleThresholdMsFor(double relayHz) =>
            1.1 * WorldsAdriftRebornGameServer.Multiplayer.RelayCadencePolicy
                .IntervalFor(relayHz).TotalMilliseconds;

        /// <summary>
        /// Flat = the staleness level at the end of the soak is what it was at
        /// the start, within 20 ms, measured two ways so a step and a ramp are
        /// both caught: end-window minus start-window medians (drift), and a
        /// least-squares slope over the per-second p50 series scaled to the whole
        /// soak. Growing = either says the level rose by 20 ms or more.
        /// </summary>
        public Verdict ComputeVerdict(long totalSeconds, int botCount, double relayHz = 20.0)
        {
            double overstaleThresholdMs = OverstaleThresholdMsFor(relayHz);
            lock (_gate)
            {
                // Per-second p50 across both bots combined - the curve the CSV plots.
                var seconds = new List<long>();
                var p50s = new List<double>();
                var all = new List<double>();
                for (long s = 0; s < totalSeconds; s++)
                {
                    var merged = new List<double>();
                    for (int bot = 0; bot < botCount; bot++)
                    {
                        if (_staleness.TryGetValue((bot, s), out List<double> list))
                        {
                            merged.AddRange(list);
                        }
                    }
                    if (merged.Count > 0)
                    {
                        merged.Sort();
                        seconds.Add(s);
                        p50s.Add(Percentile(merged, 0.50));
                        all.AddRange(merged);
                    }
                }

                long totalSends = 0;
                foreach (int n in _sends.Values) { totalSends += n; }

                if (all.Count == 0)
                {
                    return new Verdict(false, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN,
                        _matched, _unmatched, totalSends, _gaps.Count, _disconnects.Count,
                        _heartbeats, _decodeErrors, _timelineViolations, _botDeaths.Count,
                        "NO SAMPLES - nothing was relayed; the soak did not measure anything.",
                        0.0, overstaleThresholdMs);
                }

                all.Sort();

                long overstale = 0;
                foreach (double sample in all)
                {
                    if (sample > overstaleThresholdMs) { overstale++; }
                }
                double overstaleShare = (double)overstale / all.Count;

                // Window = a fifth of the soak, capped at 60 s, floored at 5 s.
                int window = (int)Math.Clamp(totalSeconds / 5, 5, 60);
                double startMedian = MedianOfSecondsWindow(seconds, p50s, 0, window);
                double endMedian = MedianOfSecondsWindow(seconds, p50s, totalSeconds - window, totalSeconds);
                double drift = endMedian - startMedian;

                // Least-squares slope of p50 vs second, scaled to the full soak.
                double slopePerSecond = Slope(seconds, p50s);
                double slopeOverSoak = slopePerSecond * totalSeconds;

                bool flat = drift < 20.0 && slopeOverSoak < 20.0;

                string detail = string.Format(CultureInfo.InvariantCulture,
                    "start-window p50 {0:0.##} ms, end-window p50 {1:0.##} ms, drift {2:+0.##;-0.##;0} ms; "
                    + "trend {3:+0.##;-0.##;0} ms over the whole soak",
                    startMedian, endMedian, drift, slopeOverSoak);

                return new Verdict(flat, drift, slopeOverSoak,
                    Percentile(all, 0.50), Percentile(all, 0.95), all[^1],
                    _matched, _unmatched, totalSends, _gaps.Count, _disconnects.Count,
                    _heartbeats, _decodeErrors, _timelineViolations, _botDeaths.Count, detail,
                    overstaleShare, overstaleThresholdMs);
            }
        }

        public IReadOnlyList<GapEvent> Gaps { get { lock (_gate) { return _gaps.ToArray(); } } }
        public IReadOnlyList<DisconnectEvent> Disconnects { get { lock (_gate) { return _disconnects.ToArray(); } } }
        public IReadOnlyList<(int Bot, string Reason)> BotDeaths { get { lock (_gate) { return _botDeaths.ToArray(); } } }

        private static double MedianOfSecondsWindow(List<long> seconds, List<double> p50s, long from, long to)
        {
            var window = new List<double>();
            for (int i = 0; i < seconds.Count; i++)
            {
                if (seconds[i] >= from && seconds[i] < to)
                {
                    window.Add(p50s[i]);
                }
            }
            if (window.Count == 0) return double.NaN;
            window.Sort();
            return Percentile(window, 0.50);
        }

        private static double Slope(List<long> xs, List<double> ys)
        {
            if (xs.Count < 2) return 0;
            double mx = 0, my = 0;
            for (int i = 0; i < xs.Count; i++) { mx += xs[i]; my += ys[i]; }
            mx /= xs.Count; my /= xs.Count;
            double num = 0, den = 0;
            for (int i = 0; i < xs.Count; i++)
            {
                num += (xs[i] - mx) * (ys[i] - my);
                den += (xs[i] - mx) * (xs[i] - mx);
            }
            return den == 0 ? 0 : num / den;
        }
    }
}

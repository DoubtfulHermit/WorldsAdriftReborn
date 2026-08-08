using System.Text;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Per-peer wire traffic accumulator: what was received from and sent to
    /// each peer, counted per component id, reported every five seconds.
    ///
    /// WHY. A live two-player session degraded progressively and ended with the
    /// server silently dropping a peer after 73 seconds, and the log offered no
    /// way to tell WHAT was on the wire: how many updates each client was
    /// pushing, of what, and - the suspected killer - how much reliable relay
    /// traffic the server was pushing at a peer that had stopped keeping up.
    /// This class is that visibility. It is pure bookkeeping (no I/O, no
    /// statics, clock injected) so every counting and reporting rule is
    /// unit-tested; the main loop's glue is one Record call per packet and one
    /// DueReports drain per iteration.
    ///
    /// A report is emitted for every tracked peer each interval EVEN IF its
    /// counts are zero: a peer we keep sending to but never hear from is
    /// exactly the peer that is about to time out, and a report that only
    /// appears for talkative peers would hide it.
    ///
    /// Single-threaded by design, like everything else in the main loop.
    /// </summary>
    public sealed class PeerRates
    {
        /// <summary>How often each peer's line is due.</summary>
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

        /// <summary>How many per-component counters a report lists per direction.</summary>
        public const int TopCount = 5;

        /// <summary>
        /// Key namespace bit for non-component traffic. Real component ids in
        /// this game are small (1036..190607); the high bit marks a key as an
        /// ENet CHANNEL number instead, so op traffic (asset acks, interest
        /// requests) shares the table without colliding.
        /// </summary>
        private const uint ChannelFlag = 0x80000000u;

        private sealed class Window
        {
            public TimeSpan StartedAt;
            public long ReceiveTotal;
            public long SendTotal;
            public readonly Dictionary<uint, long> Receives = new();
            public readonly Dictionary<uint, long> Sends = new();
        }

        private readonly IClock _clock;
        private readonly TimeSpan _interval;
        private readonly Dictionary<ulong, Window> _peers = new();

        public PeerRates(IClock clock, TimeSpan? interval = null)
        {
            _clock = clock;
            _interval = interval ?? DefaultInterval;
        }

        /// <summary>Key for a packet counted by ENet channel rather than component id.</summary>
        public static uint ChannelKey(int channel)
        {
            return ChannelFlag | (uint)channel;
        }

        /// <summary>"190602" for a component id, "ch0" for a channel key.</summary>
        public static string DescribeKey(uint key)
        {
            return (key & ChannelFlag) != 0 ? "ch" + (key & ~ChannelFlag) : key.ToString();
        }

        /// <summary>One inbound component update (or, via ChannelKey, one inbound op packet).</summary>
        public void RecordReceive(ulong peerId, uint key)
        {
            Window w = WindowOf(peerId);
            w.ReceiveTotal++;
            w.Receives.TryGetValue(key, out long n);
            w.Receives[key] = n + 1;
        }

        /// <summary>One outbound component update (or, via ChannelKey, one outbound op packet).</summary>
        public void RecordSend(ulong peerId, uint key)
        {
            Window w = WindowOf(peerId);
            w.SendTotal++;
            w.Sends.TryGetValue(key, out long n);
            w.Sends[key] = n + 1;
        }

        /// <summary>Drops a departed peer's window so nothing accumulates for ghosts.</summary>
        public void Forget(ulong peerId)
        {
            _peers.Remove(peerId);
        }

        /// <summary>
        /// Every peer whose interval has elapsed, as one report each; emitting a
        /// report resets that peer's window. Call once per loop iteration.
        /// </summary>
        public IReadOnlyList<PeerRateReport> DueReports()
        {
            TimeSpan now = _clock.Elapsed;
            List<PeerRateReport> due = new();

            foreach (KeyValuePair<ulong, Window> entry in _peers)
            {
                Window w = entry.Value;
                TimeSpan elapsed = now - w.StartedAt;
                if (elapsed < _interval)
                {
                    continue;
                }

                due.Add(new PeerRateReport(
                    entry.Key, elapsed,
                    w.ReceiveTotal, Top(w.Receives),
                    w.SendTotal, Top(w.Sends)));

                w.StartedAt = now;
                w.ReceiveTotal = 0;
                w.SendTotal = 0;
                w.Receives.Clear();
                w.Sends.Clear();
            }

            return due;
        }

        private Window WindowOf(ulong peerId)
        {
            if (!_peers.TryGetValue(peerId, out Window? w))
            {
                w = new Window { StartedAt = _clock.Elapsed };
                _peers[peerId] = w;
            }
            return w;
        }

        /// <summary>Top counters, largest first; ties broken by key so output is deterministic.</summary>
        private static IReadOnlyList<KeyValuePair<uint, long>> Top(Dictionary<uint, long> counts)
        {
            return counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Take(TopCount)
                .ToList();
        }
    }

    /// <summary>One peer's five-second traffic summary. See <see cref="PeerRates"/>.</summary>
    public readonly struct PeerRateReport
    {
        public ulong PeerId { get; }

        /// <summary>Actual window length - can exceed the interval on a busy loop.</summary>
        public TimeSpan Window { get; }

        public long ReceiveTotal { get; }
        public IReadOnlyList<KeyValuePair<uint, long>> TopReceives { get; }
        public long SendTotal { get; }
        public IReadOnlyList<KeyValuePair<uint, long>> TopSends { get; }

        public PeerRateReport(
            ulong peerId, TimeSpan window,
            long receiveTotal, IReadOnlyList<KeyValuePair<uint, long>> topReceives,
            long sendTotal, IReadOnlyList<KeyValuePair<uint, long>> topSends)
        {
            PeerId = peerId;
            Window = window;
            ReceiveTotal = receiveTotal;
            TopReceives = topReceives;
            SendTotal = sendTotal;
            TopSends = topSends;
        }

        /// <summary>
        /// One greppable line, e.g.
        /// <c>peer 0x2f00: rx 612 (190602:305 1073:301 ch0:6), tx 1224 (190602:610 1073:602 ch2:12) in 5.0s</c>.
        /// </summary>
        public string Describe()
        {
            StringBuilder line = new StringBuilder("peer 0x").Append(PeerId.ToString("x"))
                .Append(": rx ").Append(ReceiveTotal);
            AppendTop(line, TopReceives);
            line.Append(", tx ").Append(SendTotal);
            AppendTop(line, TopSends);
            // Invariant culture: this line is grepped, and "5,0s" on a German
            // locale would quietly split every awk field.
            return line.Append(" in ")
                .Append(Window.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                .Append('s').ToString();
        }

        private static void AppendTop(StringBuilder line, IReadOnlyList<KeyValuePair<uint, long>> top)
        {
            if (top.Count == 0)
            {
                return;
            }

            line.Append(" (");
            for (int i = 0; i < top.Count; i++)
            {
                if (i > 0)
                {
                    line.Append(' ');
                }
                line.Append(PeerRates.DescribeKey(top[i].Key)).Append(':').Append(top[i].Value);
            }
            line.Append(')');
        }
    }
}

using System.Diagnostics;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// A monotonic source of elapsed time.
    ///
    /// It exists so that deadlines can be asserted on without sleeping, and - far
    /// more importantly - so that "two seconds" is measured in seconds rather than
    /// in main-loop iterations. See <see cref="MirrorSchedule"/>.
    /// </summary>
    public interface IClock
    {
        /// <summary>Time since a fixed point in the past. Never goes backwards.</summary>
        TimeSpan Elapsed { get; }
    }

    /// <summary>
    /// The production clock. Monotonic, so it is immune to the host's wall clock
    /// being stepped by NTP under the server.
    /// </summary>
    public sealed class MonotonicClock : IClock
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public TimeSpan Elapsed => _stopwatch.Elapsed;
    }

    /// <summary>One due batch of mirror ops to resend to a peer.</summary>
    public readonly struct MirrorResend
    {
        /// <summary>Peer that should receive the resend.</summary>
        public ulong PeerId { get; }

        /// <summary>The ops originally flushed to that peer.</summary>
        public IReadOnlyList<MirrorIntent> Ops { get; }

        /// <summary>Attempts remaining after this one.</summary>
        public int AttemptsLeft { get; }

        public MirrorResend(ulong peerId, IReadOnlyList<MirrorIntent> ops, int attemptsLeft)
        {
            PeerId = peerId;
            Ops = ops;
            AttemptsLeft = attemptsLeft;
        }
    }

    /// <summary>
    /// Owns WHEN parked mirror ops are force-flushed and WHEN they are resent.
    ///
    /// This used to live in the main loop and count loop iterations: a counter was
    /// bumped once per <c>ENet_Poll</c> and 40 of those were documented as "~2s",
    /// 60 as "~3s". That arithmetic assumed every iteration blocks for its full
    /// 50 ms timeout, but <c>enet_host_service</c> returns the moment an event is
    /// already queued (enetLayer.cpp:141), so the loop spins once per EVENT.
    /// A second player publishing transform and skeleton updates every frame -
    /// especially over the internet, where packets arrive in bursts - drives that
    /// to hundreds of iterations a second, and the two-second grace period for a
    /// client to load the Traveller prefab collapsed to a fraction of a second.
    /// The load never finished, the client dropped the AddEntity, and the remote
    /// avatar never appeared. Timeouts are now measured against a real clock.
    ///
    /// It is also the single owner of all per-peer mirror bookkeeping. Five
    /// parallel dictionaries keyed by peer became two records, so <see cref="Forget"/>
    /// cannot half-clean a departed peer the way five separate removals could.
    ///
    /// Pure policy: it decides and remembers, it never sends.
    /// </summary>
    public sealed class MirrorSchedule
    {
        /// <summary>
        /// How long a peer's parked ops wait for its asset-load ack before being
        /// flushed anyway. An already-in-world, idle player never sends another
        /// ack, so without the fallback its mirror of a newcomer never fires.
        /// </summary>
        public static readonly TimeSpan DefaultFlushTimeout = TimeSpan.FromSeconds(2);

        /// <summary>Gap between resends of the flushed ops.</summary>
        public static readonly TimeSpan DefaultResendInterval = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Live default: never duplicate-create a mirrored entity. The resend
        /// machine remains injectable only for historical pure-policy tests.
        /// </summary>
        public const int DefaultResendAttempts = 0;

        private sealed class ParkedBatch
        {
            public readonly List<MirrorIntent> Ops = new();
            public TimeSpan ParkedAt;
        }

        private sealed class ResendBatch
        {
            public List<MirrorIntent> Ops = new();
            public TimeSpan LastSentAt;
            public int AttemptsLeft;
        }

        private readonly IClock _clock;
        private readonly Dictionary<ulong, ParkedBatch> _parked = new();
        private readonly Dictionary<ulong, ResendBatch> _resending = new();

        public TimeSpan FlushTimeout { get; }
        public TimeSpan ResendInterval { get; }
        public int ResendAttempts { get; }

        public MirrorSchedule(IClock clock)
            : this(clock, DefaultFlushTimeout, DefaultResendInterval, DefaultResendAttempts)
        {
        }

        public MirrorSchedule(IClock clock, TimeSpan flushTimeout, TimeSpan resendInterval, int resendAttempts)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            FlushTimeout = flushTimeout;
            ResendInterval = resendInterval;
            ResendAttempts = resendAttempts;
        }

        /// <summary>Peers with ops parked waiting for an asset-load ack.</summary>
        public int ParkedPeerCount => _parked.Count;

        /// <summary>Peers with ops still scheduled for resending.</summary>
        public int ResendingPeerCount => _resending.Count;

        /// <summary>Whether this peer holds any state at all in the schedule.</summary>
        public bool IsTracking(ulong peerId)
        {
            return _parked.ContainsKey(peerId) || _resending.ContainsKey(peerId);
        }

        /// <summary>
        /// Parks one op for a peer until that peer acks an asset load (or the
        /// fallback fires).
        ///
        /// Returns true when this is the FIRST op parked for the peer, which is
        /// the caller's signal to send the asset-load request that the flush is
        /// waiting on. Returning it rather than having the caller test a
        /// dictionary keeps "request the asset exactly once per batch" a property
        /// of this type instead of an invariant the loop has to remember.
        /// </summary>
        public bool Park(ulong peerId, MirrorIntent intent)
        {
            bool isFirst = !_parked.TryGetValue(peerId, out ParkedBatch? batch);
            if (isFirst)
            {
                batch = new ParkedBatch { ParkedAt = _clock.Elapsed };
                _parked[peerId] = batch;
            }

            batch!.Ops.Add(intent);
            return isFirst;
        }

        /// <summary>
        /// Peers whose parked ops have waited longer than <see cref="FlushTimeout"/>.
        /// Non-consuming: the caller flushes them with <see cref="TakeParked"/>.
        /// </summary>
        public IReadOnlyList<ulong> DueForFlush()
        {
            if (_parked.Count == 0)
            {
                return Array.Empty<ulong>();
            }

            TimeSpan now = _clock.Elapsed;
            List<ulong>? due = null;
            foreach (KeyValuePair<ulong, ParkedBatch> entry in _parked)
            {
                if (now - entry.Value.ParkedAt >= FlushTimeout)
                {
                    (due ??= new List<ulong>()).Add(entry.Key);
                }
            }

            return due ?? (IReadOnlyList<ulong>)Array.Empty<ulong>();
        }

        /// <summary>
        /// Removes and returns a peer's parked ops, arming the resends.
        ///
        /// Empty for a peer with nothing parked, which is the common case: this is
        /// called on every asset-load ack and most acks belong to a peer that is
        /// not mirroring anyone.
        /// </summary>
        public IReadOnlyList<MirrorIntent> TakeParked(ulong peerId)
        {
            if (!_parked.TryGetValue(peerId, out ParkedBatch? batch))
            {
                return Array.Empty<MirrorIntent>();
            }

            _parked.Remove(peerId);

            if (batch.Ops.Count > 0 && ResendAttempts > 0)
            {
                _resending[peerId] = new ResendBatch
                {
                    Ops = new List<MirrorIntent>(batch.Ops),
                    LastSentAt = _clock.Elapsed,
                    AttemptsLeft = ResendAttempts,
                };
            }

            return batch.Ops;
        }

        /// <summary>
        /// Resend batches whose interval has elapsed.
        ///
        /// Consuming: each returned batch spends one attempt and re-arms its
        /// timer, and a peer that has spent its last attempt is dropped. That way
        /// a caller cannot resend forever by forgetting to decrement anything.
        /// </summary>
        public IReadOnlyList<MirrorResend> DueForResend()
        {
            if (_resending.Count == 0)
            {
                return Array.Empty<MirrorResend>();
            }

            TimeSpan now = _clock.Elapsed;
            List<MirrorResend>? due = null;
            List<ulong>? exhausted = null;

            foreach (KeyValuePair<ulong, ResendBatch> entry in _resending)
            {
                ResendBatch batch = entry.Value;
                if (now - batch.LastSentAt < ResendInterval)
                {
                    continue;
                }

                if (batch.AttemptsLeft <= 0)
                {
                    (exhausted ??= new List<ulong>()).Add(entry.Key);
                    continue;
                }

                batch.AttemptsLeft--;
                batch.LastSentAt = now;
                (due ??= new List<MirrorResend>()).Add(new MirrorResend(entry.Key, batch.Ops, batch.AttemptsLeft));

                if (batch.AttemptsLeft <= 0)
                {
                    (exhausted ??= new List<ulong>()).Add(entry.Key);
                }
            }

            if (exhausted != null)
            {
                foreach (ulong peerId in exhausted)
                {
                    _resending.Remove(peerId);
                }
            }

            return due ?? (IReadOnlyList<MirrorResend>)Array.Empty<MirrorResend>();
        }

        /// <summary>
        /// Drops every trace of a peer. Called when it disconnects: keeping ops
        /// parked for a gone peer leaks, and ENet reuses peer slots, so a stale
        /// batch can be misattributed to whoever lands in that slot next.
        ///
        /// Returns whether anything was actually held.
        /// </summary>
        public bool Forget(ulong peerId)
        {
            bool hadParked = _parked.Remove(peerId);
            bool hadResends = _resending.Remove(peerId);
            return hadParked || hadResends;
        }
    }
}

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The game server's live-session bookkeeping, accumulated since boot: how
    /// many peers have connected and left, how many are on right now, and the
    /// high-water mark. Pure - no I/O, no clock of its own, wall-clock instants
    /// handed in as arguments - so every counting rule is unit-tested without a
    /// running ENet host.
    ///
    /// WHY IT EXISTS. The game server knows things the login server cannot see
    /// (it is the only process that talks to live peers) and the login server
    /// owns the operator dashboard (it is the only process that can reach
    /// Postgres and serve HTTP). This is the game-side half of the bridge: it
    /// accumulates the numbers, a thin writer snapshots them to a file every few
    /// seconds, and the login server reads that file. See
    /// <see cref="StatsSnapshot"/> for the wire shape.
    ///
    /// WALL CLOCK, not the monotonic <see cref="IClock"/> everything else here
    /// uses. Connect times and boot time are written to a file that a DIFFERENT
    /// process reads and compares against ITS wall clock to decide how stale the
    /// snapshot is. A monotonic "seconds since this process started" would be
    /// meaningless across that boundary. The two processes share the host clock,
    /// so wall-clock instants are the only currency that survives the trip.
    ///
    /// Single-threaded by design, like everything else in the main loop.
    /// </summary>
    public sealed class ServerStats
    {
        private readonly Dictionary<ulong, DateTimeOffset> _connectedAt = new();

        /// <summary>When this server booted. Fixed for the process lifetime.</summary>
        public DateTimeOffset BootTime { get; }

        public ServerStats(DateTimeOffset bootTime)
        {
            BootTime = bootTime;
        }

        /// <summary>Peers that have connected since boot, ever.</summary>
        public long TotalConnects { get; private set; }

        /// <summary>Peers that have disconnected since boot, ever.</summary>
        public long TotalDisconnects { get; private set; }

        /// <summary>How many peers are connected right now.</summary>
        public int CurrentOnline => _connectedAt.Count;

        /// <summary>The most peers ever connected at once since boot.</summary>
        public int PeakOnline { get; private set; }

        /// <summary>
        /// Records a peer connecting at <paramref name="now"/>. Idempotent per
        /// peer id: a duplicate connect for an id already tracked is ignored
        /// rather than double-counted, because ENet can hand the same pointer to
        /// a reconnect and the count must follow reality, not events.
        /// </summary>
        public void OnConnect(ulong peerId, DateTimeOffset now)
        {
            if (_connectedAt.ContainsKey(peerId))
            {
                return;
            }

            _connectedAt[peerId] = now;
            TotalConnects++;

            if (_connectedAt.Count > PeakOnline)
            {
                PeakOnline = _connectedAt.Count;
            }
        }

        /// <summary>
        /// Records a peer disconnecting. A disconnect for an id we were not
        /// tracking is ignored - the disconnect total counts sessions that were
        /// actually established, so a teardown race that never reached OnConnect
        /// does not inflate it.
        /// </summary>
        public void OnDisconnect(ulong peerId)
        {
            if (_connectedAt.Remove(peerId))
            {
                TotalDisconnects++;
            }
        }

        /// <summary>
        /// When a currently-connected peer connected, or null if it is not
        /// tracked. The snapshot writer uses this to stamp each live player with
        /// how long they have been on.
        /// </summary>
        public DateTimeOffset? ConnectedAt(ulong peerId)
        {
            return _connectedAt.TryGetValue(peerId, out DateTimeOffset at) ? at : null;
        }
    }
}

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// WHICH joining peers are still holding the loading screen waiting for their
    /// initial world set to become ready, and WHEN each one's patience runs out -
    /// the pure book-keeping behind the <c>190000/190001/190002</c> loading barrier.
    ///
    /// The barrier itself is a client protocol (see
    /// <see cref="LoadBarrierPolicy"/>): the server seeds <c>190000 Requested</c>
    /// with an entity list and holds <c>190002 Activated=false</c>; the client
    /// publishes <c>190001 Loaded=true</c> when that list is ready; the server then
    /// pushes <c>Activated=true</c>. This type is the server's side of the handshake
    /// reduced to its state machine: a peer is ARMED when the barrier components go
    /// out, COMPLETED exactly once when its readiness signal (or its timeout)
    /// arrives, and never completed twice.
    ///
    /// TWO THINGS IT GUARANTEES, both load-bearing:
    /// <list type="number">
    /// <item><b>Exactly-once activation.</b> <see cref="Complete"/> and each id
    ///   returned by <see cref="DueTimeouts"/> report a peer as ready ONE time and
    ///   then forget it, so a client that signals ready AND then times out (or
    ///   signals twice) is activated once. Activation pushes a component update and
    ///   releases the player; doing it twice is at best wasted wire and at worst a
    ///   double state transition.</item>
    /// <item><b>No immortal loading screen.</b> A client that never signals ready -
    ///   an old mod build with no checker, a prefab that never instantiates - is
    ///   released by <see cref="DueTimeouts"/> once its deadline passes, so the
    ///   worst case is a slightly long load, never a stuck one.</item>
    /// </list>
    ///
    /// Keyed by the ulong peer id (not the ENet handle) so it stays in the pure
    /// Multiplayer assembly with no DLLCommunication dependency, exactly like the
    /// rest of the policy layer. The clock is passed in as a monotonic
    /// <see cref="TimeSpan"/> (the server's <c>ServerClock.Elapsed</c>), never read
    /// here, so the timeout behaviour is deterministically testable.
    ///
    /// NOT THREAD-SAFE, deliberately: the server is a single poll loop, and every
    /// caller is on it.
    /// </summary>
    public sealed class LoadBarrierTracker
    {
        private readonly Dictionary<ulong, TimeSpan> _deadlineByPeer = new Dictionary<ulong, TimeSpan>();

        /// <summary>
        /// Records that a peer's barrier components have gone out and it is now
        /// holding the loading screen; <paramref name="deadline"/> is the monotonic
        /// clock value past which it must be activated anyway. Re-arming an
        /// already-armed peer replaces its deadline (a re-sent setup, say), rather
        /// than stacking two.
        /// </summary>
        public void Arm(ulong peerId, TimeSpan deadline)
        {
            _deadlineByPeer[peerId] = deadline;
        }

        /// <summary>Whether a peer is currently holding the barrier.</summary>
        public bool IsPending(ulong peerId) => _deadlineByPeer.ContainsKey(peerId);

        /// <summary>How many peers are currently holding the barrier. Cheap; for the loop's fast idle check.</summary>
        public int PendingCount => _deadlineByPeer.Count;

        /// <summary>
        /// Completes a peer's barrier in response to its readiness signal, returning
        /// TRUE only if it was still pending. A false return means the peer was
        /// already activated (by an earlier signal or a timeout) and the caller must
        /// NOT push activation again - this is the exactly-once guard for the
        /// readiness path.
        /// </summary>
        public bool Complete(ulong peerId) => _deadlineByPeer.Remove(peerId);

        /// <summary>
        /// The peers whose deadline has passed as of <paramref name="now"/>, removed
        /// and returned so each times out exactly once. The caller activates each in
        /// degraded/fallback mode. Empty (and allocation-light) in the common case
        /// where nothing is overdue.
        /// </summary>
        public IReadOnlyList<ulong> DueTimeouts(TimeSpan now)
        {
            if (_deadlineByPeer.Count == 0)
            {
                return Array.Empty<ulong>();
            }

            List<ulong>? due = null;
            foreach (KeyValuePair<ulong, TimeSpan> entry in _deadlineByPeer)
            {
                if (entry.Value <= now)
                {
                    (due ??= new List<ulong>()).Add(entry.Key);
                }
            }

            if (due == null)
            {
                return Array.Empty<ulong>();
            }

            foreach (ulong peerId in due)
            {
                _deadlineByPeer.Remove(peerId);
            }
            return due;
        }

        /// <summary>
        /// Drops a peer from the tracker unconditionally - used when it disconnects,
        /// so a departed peer's id can never be reported as timing out. Silent if the
        /// peer was not pending.
        /// </summary>
        public void Forget(ulong peerId) => _deadlineByPeer.Remove(peerId);
    }
}

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Decides whether one packet-processing fault should have its detail line
    /// printed, so that a single malformed peer throwing on EVERY packet cannot
    /// drown the log while still surfacing the fault loudly the first time.
    ///
    /// This is the error path, not the hot path, so the concern here is spam, not
    /// throughput: the first <see cref="_first"/> faults all print verbatim (a
    /// genuine one-off bad packet is never hidden), and after that only every
    /// <see cref="_every"/>-th does, each carrying the running total so the log
    /// still shows the true scale of a flood.
    ///
    /// Pure and single-threaded by contract: the server drains packets on one
    /// thread, so no locking. Kept as its own module purely so the count-first
    /// policy is unit-testable away from the unsafe packet loop.
    /// </summary>
    public sealed class PacketFaultThrottle
    {
        private readonly long _first;
        private readonly long _every;
        private long _count;

        public PacketFaultThrottle(long first = 20, long every = 1000)
        {
            _first = first < 0 ? 0 : first;
            _every = every < 1 ? 1 : every;
        }

        /// <summary>
        /// Records one fault and reports whether its detail line should be printed
        /// now. <paramref name="total"/> is the number of faults seen so far,
        /// including this one, so an emitted line can say which fault it is.
        /// </summary>
        public bool ShouldLog(out long total)
        {
            _count++;
            total = _count;

            if (_count <= _first)
            {
                return true;
            }

            return (_count % _every) == 0;
        }

        /// <summary>Faults recorded so far, whether printed or suppressed.</summary>
        public long Count => _count;
    }
}

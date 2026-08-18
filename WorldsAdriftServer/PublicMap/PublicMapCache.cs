namespace WorldsAdriftServer.PublicMap
{
    /// <summary>
    /// A short-TTL, single-entry cache for the public live payload.
    ///
    /// The stats file is rewritten every ~3 seconds, but the public endpoint
    /// is unauthenticated and may be polled by arbitrarily many browsers: this
    /// cache is what turns "N viewers" into "at most one file read and one
    /// projection every <see cref="Ttl"/>", so a popular map cannot become a
    /// disk-hammering loop. Two seconds is comfortably under the writer's
    /// cadence, so no viewer ever sees data older than roughly one write.
    ///
    /// Deliberately NOT keyed: there is exactly one public payload. Thread
    /// safe under a lock because the HTTP sessions run on socket threads.
    /// </summary>
    internal sealed class PublicMapCache
    {
        internal static readonly TimeSpan Ttl = TimeSpan.FromSeconds(2);

        private readonly object _gate = new object();
        private string? _payload;
        private DateTimeOffset _builtAt;

        /// <summary>Pure freshness rule, testable in isolation.</summary>
        internal static bool IsFresh(DateTimeOffset builtAt, DateTimeOffset now) =>
            now >= builtAt && now - builtAt < Ttl;

        internal bool TryGet(DateTimeOffset now, out string payload)
        {
            lock (_gate)
            {
                if (_payload != null && IsFresh(_builtAt, now))
                {
                    payload = _payload;
                    return true;
                }
            }

            payload = string.Empty;
            return false;
        }

        internal void Store(string payload, DateTimeOffset now)
        {
            lock (_gate)
            {
                _payload = payload;
                _builtAt = now;
            }
        }
    }
}

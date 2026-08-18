using System.Security.Cryptography;
using System.Text;

namespace WorldsAdriftServer.PublicMap
{
    /// <summary>
    /// How many browser tabs have the public map open right now.
    ///
    /// WHAT THE NUMBER ACTUALLY IS, stated here because a count on a page invites
    /// people to read more into it than it holds: it is the number of distinct
    /// ephemeral tokens that have polled within <see cref="Ttl"/>. That is a count
    /// of TABS, not of people. One person with the map open twice counts twice.
    /// A closed tab does not vanish; it stops beating and falls out of the count
    /// when its last beat expires. A tab that the browser has pushed into the
    /// background gets its timers throttled to roughly once a minute, so it
    /// expires too - which is the right answer for a readout that claims to say
    /// who is LOOKING at the map, and is why the TTL is deliberately shorter than
    /// that throttle rather than longer.
    ///
    /// WHY THIS CANNOT ANSWER "WHO" OR "WHERE". There is exactly one piece of
    /// state per viewer and it is a pair: an opaque fingerprint and the instant it
    /// was last seen. Nothing else is offered to this class and nothing else can
    /// be stored in it - not an address, not a user agent, not a referrer, not a
    /// country, not a request path. The fingerprint is SHA-256 over a salt that is
    /// generated in memory at boot, never written anywhere, and thrown away when
    /// the process stops, so:
    ///
    ///   - what the client sent is not what is held, and cannot be recovered from
    ///     what is held without a secret that exists only in this process's heap;
    ///   - nothing here survives a restart, so no viewer can be recognised across
    ///     one, let alone across days;
    ///   - and the entries expire on their own, so the structure does not
    ///     accumulate a history even within one run.
    ///
    /// This is the same discipline the marker ids already use in
    /// <see cref="PublicMapProjection"/> - a hash over a per-process random salt,
    /// rotated on restart - applied to the one new thing this feature learns.
    ///
    /// WHAT IT IS NOT. It is not attestable. Anyone can hold up any number of
    /// fresh tokens and inflate the figure, bounded by <see cref="MaxTracked"/>.
    /// That bound exists so the inflation costs memory that we have already agreed
    /// to spend rather than memory we have not; there is no defence against a
    /// determined inflater that does not involve identifying the caller, and
    /// identifying the caller is the thing this feature is built not to do. A
    /// slightly wrong number on a fan server's map is the cheaper mistake.
    /// </summary>
    internal sealed class ViewerCensus
    {
        /// <summary>
        /// How long a beat keeps a viewer counted.
        ///
        /// The page polls every three seconds, so this is ten missed polls - a
        /// dropped request, a tunnel, a slow phone all ride through it. It is also
        /// comfortably under the ~60 s a browser throttles a background tab to, so
        /// a tab nobody is looking at leaves the count on its own, and comfortably
        /// over the poll so the number does not flicker.
        /// </summary>
        internal static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The most viewers tracked at once. Far above any audience this server
        /// will ever have, and the point is not the ceiling but that there IS one:
        /// an unauthenticated endpoint that allocates per distinct token is
        /// otherwise a way to spend the server's memory from the internet.
        /// </summary>
        internal const int MaxTracked = 4096;

        /// <summary>The shared census, salted from the same per-process secret the marker ids use.</summary>
        internal static readonly ViewerCensus Shared = new ViewerCensus(PublicMapProjection.ProcessSalt);

        private readonly object _gate = new object();

        /// <summary>
        /// Fingerprint to last-seen instant, and nothing else. The shape of this
        /// dictionary IS the privacy argument: there is no third column for a
        /// future change to quietly put an address in.
        /// </summary>
        private readonly Dictionary<string, DateTimeOffset> _lastSeen =
            new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        private readonly byte[] _salt;

        internal ViewerCensus(byte[] salt)
        {
            _salt = salt ?? throw new ArgumentNullException(nameof(salt));
        }

        /// <summary>Pure expiry rule, testable without a census.</summary>
        internal static bool HasExpired(DateTimeOffset lastSeen, DateTimeOffset now) =>
            now - lastSeen >= Ttl;

        /// <summary>
        /// Records that a viewer is still there. Returns true when the beat was
        /// taken; false when the token was unusable or the census is full.
        ///
        /// Expired entries are dropped on the way past, so a beat is also a sweep:
        /// the structure cannot hold a viewer for longer than the TTL merely
        /// because nobody asked for the count.
        /// </summary>
        internal bool Beat(string? rawToken, DateTimeOffset now)
        {
            if (!ViewerToken.IsWellFormed(rawToken))
            {
                return false;
            }

            string fingerprint = Fingerprint(rawToken!, _salt);

            lock (_gate)
            {
                PruneLocked(now);

                if (!_lastSeen.ContainsKey(fingerprint) && _lastSeen.Count >= MaxTracked)
                {
                    // Saturated rather than growing. The count stops rising; the
                    // server does not stop working.
                    return false;
                }

                _lastSeen[fingerprint] = now;
                return true;
            }
        }

        /// <summary>
        /// How many viewers are currently counted. Sweeps first, so the answer is
        /// never inflated by tabs that have already gone.
        /// </summary>
        internal int Count(DateTimeOffset now)
        {
            lock (_gate)
            {
                PruneLocked(now);
                return _lastSeen.Count;
            }
        }

        /// <summary>
        /// Drops everything past its TTL. Called on EVERY read and every write
        /// rather than on a timer, so "nothing outlives the TTL" is a property of
        /// the structure rather than of a schedule somebody could turn off. The
        /// per-minute sampler is what guarantees this runs even when nobody is
        /// looking at the map at all.
        /// </summary>
        private void PruneLocked(DateTimeOffset now)
        {
            List<string>? expired = null;

            foreach (KeyValuePair<string, DateTimeOffset> entry in _lastSeen)
            {
                if (HasExpired(entry.Value, now))
                {
                    (expired ??= new List<string>()).Add(entry.Key);
                }
            }

            if (expired == null)
            {
                return;
            }

            foreach (string key in expired)
            {
                _lastSeen.Remove(key);
            }
        }

        /// <summary>
        /// The opaque per-viewer key: SHA-256 over salt || "viewer " || token, in
        /// full rather than truncated, because a truncated digest could collide
        /// and quietly under-count.
        ///
        /// The "viewer " prefix is the same kind-separation the marker tokens use,
        /// so a viewer token and an entity id that happened to be the same string
        /// still land on unrelated keys and the two anonymised populations cannot
        /// be joined to each other.
        /// </summary>
        internal static string Fingerprint(string rawToken, byte[] salt)
        {
            byte[] material = Encoding.UTF8.GetBytes("viewer " + rawToken);
            byte[] payload = new byte[salt.Length + material.Length];
            Buffer.BlockCopy(salt, 0, payload, 0, salt.Length);
            Buffer.BlockCopy(material, 0, payload, salt.Length, material.Length);
            return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        }
    }
}

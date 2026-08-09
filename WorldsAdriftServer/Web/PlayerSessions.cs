using WorldsAdriftReborn.Storage.Policy;

namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The live browser sessions behind the player-facing /login and /download
    /// pages, held in memory. Deliberately NOT the Postgres <c>sessions</c> table:
    /// that table's tokens are the game client's auth tokens, carried in the
    /// Security header and read as identity by the whole character-roster path
    /// (see <see cref="WorldsAdriftServer.Persistence.Accounts"/>). A browser
    /// cookie that could be replayed as a game token is a strictly larger blast
    /// radius than a web login needs, so these live in their own set and map a
    /// cookie straight to an account id and nothing else. A player whose web
    /// session lapses simply signs in on the page again; the download it gates is
    /// a public build, not a secret.
    ///
    /// A token is 32 bytes of CSPRNG (<see cref="AccountPolicy.NewSessionToken"/>),
    /// so it is a bearer credential that cannot be forged or guessed - which is
    /// why no signature is needed on top. Expiry is sliding: a token stays valid
    /// as long as it is used inside the lifetime window.
    ///
    /// This mirrors <see cref="WorldsAdriftServer.Admin.AdminSessions"/> almost
    /// exactly; the one difference is that a value here is the account id the
    /// cookie stands for, not just an expiry, because unlike the single operator
    /// there are many players and the page has to know WHICH one is signed in.
    ///
    /// Thread-safe because NetCoreServer dispatches requests on a pool: two tabs,
    /// or a click racing a background fetch, touch this at once.
    /// </summary>
    internal sealed class PlayerSessions
    {
        /// <summary>
        /// How long a session lives without use. A week: long enough that a player
        /// who grabbed the patcher last weekend is still signed in this one, short
        /// compared to the game client's own 30-day token because this only guards
        /// a download page and re-entering a password costs nothing here.
        /// </summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

        private readonly object _gate = new object();
        private readonly Dictionary<string, Entry> _sessions = new();
        private readonly TimeSpan _lifetime;

        public PlayerSessions(TimeSpan? lifetime = null)
        {
            _lifetime = lifetime ?? Lifetime;
        }

        /// <summary>The session lifetime in whole seconds, for the cookie's Max-Age.</summary>
        public int LifetimeSeconds => (int)_lifetime.TotalSeconds;

        /// <summary>
        /// Mints a session for <paramref name="accountId"/>, valid until
        /// <paramref name="now"/> + lifetime.
        /// </summary>
        public string Issue(long accountId, DateTimeOffset now)
        {
            string token = AccountPolicy.NewSessionToken();

            lock (_gate)
            {
                _sessions[token] = new Entry(accountId, now + _lifetime);
            }

            return token;
        }

        /// <summary>
        /// The account a token names, or null if it names no live session. Slides
        /// the expiry forward on a hit. A spent token is removed on the way out
        /// rather than left to a sweeper - there is no scheduled job here, and the
        /// check cleans up after itself.
        /// </summary>
        public long? Resolve(string? token, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            lock (_gate)
            {
                if (!_sessions.TryGetValue(token!, out Entry entry))
                {
                    return null;
                }

                if (now > entry.ExpiresAt)
                {
                    _sessions.Remove(token!);
                    return null;
                }

                _sessions[token!] = new Entry(entry.AccountId, now + _lifetime);
                return entry.AccountId;
            }
        }

        /// <summary>Drops a session. Sign-out.</summary>
        public void Revoke(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            lock (_gate)
            {
                _sessions.Remove(token!);
            }
        }

        /// <summary>How many sessions are live, for diagnostics and tests.</summary>
        public int Count
        {
            get { lock (_gate) { return _sessions.Count; } }
        }

        private readonly struct Entry
        {
            public Entry(long accountId, DateTimeOffset expiresAt)
            {
                AccountId = accountId;
                ExpiresAt = expiresAt;
            }

            public long AccountId { get; }
            public DateTimeOffset ExpiresAt { get; }
        }
    }
}

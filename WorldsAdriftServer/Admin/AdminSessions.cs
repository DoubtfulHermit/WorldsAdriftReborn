using WorldsAdriftReborn.Storage.Policy;

namespace WorldsAdriftServer.Admin
{
    /// <summary>
    /// The live admin sessions, held in memory. Deliberately NOT the Postgres
    /// <c>sessions</c> table: that table's rows reference an <c>accounts</c> row
    /// by foreign key, and the admin is not a player account - there is no row to
    /// point at, and inventing one would put a privileged credential in the same
    /// table as the players'. An operator dashboard that forgets its logins when
    /// the login server restarts is the right trade: the admin simply signs in
    /// again, and there is exactly one of them.
    ///
    /// A token is 32 bytes of CSPRNG (<see cref="AccountPolicy.NewSessionToken"/>),
    /// so it is a bearer credential that cannot be forged or guessed - which is
    /// why no signature is needed on top. Expiry is sliding: a token stays valid
    /// as long as it is used inside the lifetime window, and lapses a while after
    /// the operator walks away.
    ///
    /// Thread-safe because NetCoreServer dispatches requests on a pool: two admin
    /// tabs, or the dashboard's auto-refresh racing a click, touch this at once.
    /// </summary>
    internal sealed class AdminSessions
    {
        /// <summary>
        /// How long a session lives without use. Short compared to a player's
        /// 30-day token because this one guards mutating actions and there is a
        /// human at the keyboard who can re-enter a password.
        /// </summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

        private readonly object _gate = new object();
        private readonly Dictionary<string, DateTimeOffset> _expiry = new();
        private readonly TimeSpan _lifetime;

        public AdminSessions(TimeSpan? lifetime = null)
        {
            _lifetime = lifetime ?? Lifetime;
        }

        /// <summary>The session lifetime in whole seconds, for the cookie's Max-Age.</summary>
        public int LifetimeSeconds => (int)_lifetime.TotalSeconds;

        /// <summary>Mints a session valid until <paramref name="now"/> + lifetime.</summary>
        public string Issue(DateTimeOffset now)
        {
            string token = AccountPolicy.NewSessionToken();

            lock (_gate)
            {
                _expiry[token] = now + _lifetime;
            }

            return token;
        }

        /// <summary>
        /// Whether a token names a live session, sliding its expiry forward if so.
        /// A spent token is removed on the way out rather than left to a sweeper -
        /// there is no scheduled job here, and the set is tiny, so the check
        /// cleans up after itself.
        /// </summary>
        public bool IsValid(string? token, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            lock (_gate)
            {
                if (!_expiry.TryGetValue(token!, out DateTimeOffset expiresAt))
                {
                    return false;
                }

                if (now > expiresAt)
                {
                    _expiry.Remove(token!);
                    return false;
                }

                _expiry[token!] = now + _lifetime;
                return true;
            }
        }

        /// <summary>Drops a session. Sign-out, and the operator's own hammer.</summary>
        public void Revoke(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            lock (_gate)
            {
                _expiry.Remove(token!);
            }
        }

        /// <summary>How many sessions are live, for diagnostics and tests.</summary>
        public int Count
        {
            get { lock (_gate) { return _expiry.Count; } }
        }
    }
}

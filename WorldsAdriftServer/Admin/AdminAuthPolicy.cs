using WorldsAdriftReborn.Storage.Policy;

namespace WorldsAdriftServer.Admin
{
    /// <summary>
    /// Pure rules for the single-operator admin login. No I/O, no clock, no
    /// stored state - it decides how the one admin credential is CONFIGURED, how
    /// a login attempt is CHECKED, and how the session cookie is parsed and
    /// written. The stateful parts (the env read, the live session set) live in
    /// <see cref="AdminConfig"/> and <see cref="AdminSessions"/>; this is what
    /// they delegate every decision to, so a test can hold each rule without a
    /// socket.
    ///
    /// There is exactly one admin, and its password is NEVER in source. The
    /// operator installs it out of band (see <see cref="ConfigVariable"/>); this
    /// file only knows how to read what they installed and how to verify against
    /// it with the same PBKDF2 the player accounts use.
    /// </summary>
    internal static class AdminAuthPolicy
    {
        /// <summary>
        /// The environment variable the admin credential is read from, mirroring
        /// how <c>WAREBORN_DB</c> carries the connection string: one place an
        /// operator looks, nothing secret in the repo. The value is
        /// <c>username:credential</c>, where credential is EITHER a PBKDF2 hash
        /// string (the recommended, install-once form) OR a plaintext password
        /// that is hashed in memory on first read (convenience; the plaintext
        /// then lives only in the root-owned env file, never in source).
        /// </summary>
        public const string ConfigVariable = "WAREBORN_ADMIN";

        /// <summary>The session cookie name.</summary>
        public const string CookieName = "wa_admin";

        /// <summary>
        /// The path the cookie is scoped to. The whole panel lives under /admin,
        /// so the browser sends the cookie there and NOWHERE else - not on the
        /// public /signup or /register, which share this host.
        /// </summary>
        public const string CookiePath = "/admin";
        public const string CsrfHeader = "X-Wareborn-CSRF";

        /// <summary>
        /// A session-bound double-submit value for authenticated POSTs. The
        /// bearer token remains HttpOnly; only its one-way CSRF derivative is
        /// embedded in the authenticated page.
        /// </summary>
        public static string CsrfTokenForSession(string sessionToken)
        {
            if (string.IsNullOrWhiteSpace(sessionToken)) return string.Empty;
            byte[] bytes = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("wareborn-admin-csrf-v1:" + sessionToken));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public static bool VerifyCsrf(string? sessionToken, string? presented)
        {
            if (string.IsNullOrWhiteSpace(sessionToken) || string.IsNullOrWhiteSpace(presented))
                return false;
            string expected = CsrfTokenForSession(sessionToken!);
            byte[] left = System.Text.Encoding.ASCII.GetBytes(expected);
            byte[] right = System.Text.Encoding.ASCII.GetBytes(presented!);
            return left.Length == right.Length
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
        }

        /// <summary>
        /// Splits a configured <c>username:credential</c> value. The split is on
        /// the FIRST colon only: a PBKDF2 hash is <c>pbkdf2$sha256$...</c> in the
        /// base64 alphabet (which has no colon), and the admin username has none,
        /// so one colon unambiguously separates the two. Returns false for a
        /// value with no colon or an empty half - a misconfigured credential must
        /// fail closed, not half-parse.
        /// </summary>
        public static bool TrySplitConfig(string? configured, out string username, out string credential)
        {
            username = string.Empty;
            credential = string.Empty;

            if (string.IsNullOrWhiteSpace(configured))
            {
                return false;
            }

            string trimmed = configured.Trim();
            int colon = trimmed.IndexOf(':');
            if (colon <= 0 || colon >= trimmed.Length - 1)
            {
                return false;
            }

            username = trimmed.Substring(0, colon).Trim();
            credential = trimmed.Substring(colon + 1).Trim();

            return username.Length > 0 && credential.Length > 0;
        }

        /// <summary>
        /// Whether a credential is already a stored PBKDF2 hash (as opposed to a
        /// plaintext password the operator typed for convenience). Same shape
        /// check <see cref="AccountPolicy.VerifyPassword"/> parses:
        /// <c>pbkdf2$sha256$iterations$salt$hash</c>, five dollar-separated parts
        /// with the algorithm and PRF this build produces.
        /// </summary>
        public static bool LooksLikeStoredHash(string? credential)
        {
            if (string.IsNullOrEmpty(credential))
            {
                return false;
            }

            string[] parts = credential.Split('$');
            return parts.Length == 5
                && string.Equals(parts[0], AccountPolicy.HashAlgorithm, StringComparison.Ordinal)
                && string.Equals(parts[1], AccountPolicy.HashPrf, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies a login attempt against the configured admin. Runs PBKDF2 on
        /// EVERY call, against the real hash when the username matches and against
        /// <see cref="AccountPolicy.DummyHash"/> when it does not, so that a wrong
        /// username and a wrong password take the same time. With one admin the
        /// enumeration surface is tiny, but the cost is one hash either way and
        /// the alternative is a login that silently answers "no such user" faster
        /// than "wrong password".
        /// </summary>
        public static bool Verify(
            string? attemptUsername,
            string? attemptPassword,
            string configuredUsername,
            string storedHash)
        {
            bool userMatches = string.Equals(
                attemptUsername?.Trim(),
                configuredUsername,
                StringComparison.Ordinal);

            // Always hash. When the user is wrong, burn the time on the dummy and
            // discard the result.
            bool passwordMatches = AccountPolicy.VerifyPassword(
                attemptPassword,
                userMatches ? storedHash : AccountPolicy.DummyHash);

            return userMatches && passwordMatches;
        }

        /// <summary>
        /// Pulls the admin session token out of a Cookie header value, or null if
        /// it is not there. Header form is <c>name=value; name2=value2</c>; only
        /// the <see cref="CookieName"/> pair is returned.
        /// </summary>
        public static string? TokenFromCookieHeader(string? cookieHeader)
        {
            if (string.IsNullOrEmpty(cookieHeader))
            {
                return null;
            }

            foreach (string part in cookieHeader.Split(';'))
            {
                string pair = part.Trim();
                int eq = pair.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                if (string.Equals(pair.Substring(0, eq), CookieName, StringComparison.Ordinal))
                {
                    string value = pair.Substring(eq + 1).Trim();
                    return value.Length > 0 ? value : null;
                }
            }

            return null;
        }

        /// <summary>
        /// The Set-Cookie value that arms a session. HttpOnly so page script
        /// cannot read the token, SameSite=Strict so it never rides a
        /// cross-site request (the only mutating actions are here), Path scoped to
        /// the panel. No Secure attribute is set BY THIS STRING because TLS is
        /// terminated at Caddy and the app speaks plain HTTP to it on loopback;
        /// the cookie is nonetheless only ever transmitted over the HTTPS front.
        /// </summary>
        public static string BuildSessionCookie(string token, int maxAgeSeconds)
        {
            return CookieName + "=" + token
                + "; Path=" + CookiePath
                + "; HttpOnly; SameSite=Strict"
                + "; Max-Age=" + maxAgeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The Set-Cookie value that clears a session (logout).</summary>
        public static string BuildClearCookie()
        {
            return CookieName + "=; Path=" + CookiePath + "; HttpOnly; SameSite=Strict; Max-Age=0";
        }
    }
}

namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// Pure rules for the player-facing web session cookie: its name, its scope,
    /// how it is parsed out of an incoming Cookie header and how the Set-Cookie
    /// value is written. No I/O, no clock, no stored state - the live session set
    /// lives in <see cref="PlayerSessions"/>, and this is only the string-shaped
    /// half, kept separate so a test can hold each rule without a socket.
    ///
    /// It is the sibling of <see cref="WorldsAdriftServer.Admin.AdminAuthPolicy"/>
    /// and follows it deliberately, with two differences that matter:
    /// <list type="bullet">
    ///   <item>Path is <c>/</c>, not <c>/admin</c>: the cookie has to ride both
    ///   <c>/download</c> (the page) and <c>/download/WAPatch.exe</c> (the file),
    ///   so it is scoped to the whole site rather than one subtree.</item>
    ///   <item>SameSite is <c>Lax</c>, not <c>Strict</c>: nothing gated by this
    ///   cookie mutates state, and Lax keeps the session attached when a player
    ///   follows a link to the download page from elsewhere.</item>
    /// </list>
    /// </summary>
    internal static class PlayerAuthPolicy
    {
        /// <summary>The session cookie name.</summary>
        public const string CookieName = "wa_player";

        /// <summary>
        /// The path the cookie is scoped to. Site-wide, because the login gate
        /// covers both the download PAGE and the exe served under /download/.
        /// </summary>
        public const string CookiePath = "/";

        /// <summary>
        /// Pulls the player session token out of a Cookie header value, or null if
        /// it is not there. Header form is <c>name=value; name2=value2</c>; only
        /// the <see cref="CookieName"/> pair is returned. Mirrors
        /// <see cref="WorldsAdriftServer.Admin.AdminAuthPolicy.TokenFromCookieHeader"/>
        /// so the two cookies parse identically.
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
        /// The Set-Cookie value that arms a session. HttpOnly so page script cannot
        /// read the token, SameSite=Lax so it rides a top-level navigation to the
        /// download page but not a cross-site sub-request, Path scoped to the whole
        /// site. No Secure attribute is set BY THIS STRING because TLS is terminated
        /// at Caddy and the app speaks plain HTTP to it on loopback; the cookie is
        /// nonetheless only ever transmitted over the HTTPS front.
        /// </summary>
        public static string BuildSessionCookie(string token, int maxAgeSeconds)
        {
            return CookieName + "=" + token
                + "; Path=" + CookiePath
                + "; HttpOnly; SameSite=Lax"
                + "; Max-Age=" + maxAgeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The Set-Cookie value that clears a session (logout).</summary>
        public static string BuildClearCookie()
        {
            return CookieName + "=; Path=" + CookiePath + "; HttpOnly; SameSite=Lax; Max-Age=0";
        }

        /// <summary>The hidden form field the CSRF token is posted back in.</summary>
        public const string CsrfField = "csrf";

        /// <summary>
        /// A CSRF token bound to one player session.
        ///
        /// It arrived with the account page, which is the first thing behind this
        /// cookie that CHANGES something - the download gate only ever read. The
        /// SameSite=Lax cookie already blocks a cross-site POST on its own, so
        /// this is the second lock rather than the only one, and it is worth
        /// having because "Lax blocks it" is a browser behaviour and not something
        /// this server can assert.
        ///
        /// Derived rather than stored, exactly as
        /// <see cref="WorldsAdriftServer.Admin.AdminAuthPolicy.CsrfTokenForSession"/>
        /// is: no second table to expire in step with the session, and a token
        /// that dies with the session by construction. The domain string differs
        /// from the admin one ON PURPOSE - the two must not be interchangeable, or
        /// a token minted for a player page would be accepted by an operator
        /// endpoint that shared a session token format.
        /// </summary>
        public static string CsrfTokenForSession(string? sessionToken)
        {
            if (string.IsNullOrWhiteSpace(sessionToken)) return string.Empty;

            byte[] bytes = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("wareborn-player-csrf-v1:" + sessionToken));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// <summary>
        /// Whether a presented token belongs to this session. Compared in fixed
        /// time, and false for either half missing - an absent token must never
        /// compare equal to an absent expectation.
        /// </summary>
        public static bool VerifyCsrf(string? sessionToken, string? presented)
        {
            if (string.IsNullOrWhiteSpace(sessionToken) || string.IsNullOrWhiteSpace(presented))
            {
                return false;
            }

            string expected = CsrfTokenForSession(sessionToken);
            byte[] left = System.Text.Encoding.ASCII.GetBytes(expected);
            byte[] right = System.Text.Encoding.ASCII.GetBytes(presented!);

            return left.Length == right.Length
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
        }
    }
}

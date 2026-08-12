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
    }
}

namespace WorldsAdriftServer.PublicMap
{
    /// <summary>
    /// The one thing a browser is allowed to tell us about itself: an ephemeral
    /// per-page-load token, so that "how many people have the map open" can be
    /// counted at all.
    ///
    /// WHY A TOKEN AND NOT THE OBVIOUS THING. The obvious way to count concurrent
    /// viewers is to count distinct source addresses. That is exactly the thing
    /// this feature must be incapable of doing: an address answers "where from",
    /// and once it is in memory somebody will one day log it. So the server never
    /// reads the socket's address for this purpose at all - there is no code path
    /// here that can - and instead the PAGE mints a random value at load time and
    /// echoes it on each poll. The server's whole knowledge of a viewer is that
    /// random value and the time it last appeared.
    ///
    /// WHAT THE SHAPE RULE IS FOR. A token arrives as untrusted text on a public,
    /// unauthenticated URL, so without a rule the census would be a free-text
    /// store any client could write a nickname, an e-mail address or its own IP
    /// into. <see cref="IsWellFormed"/> accepts nothing but 8 to 64 characters of
    /// ASCII letters and digits, and the value is NEVER percent-decoded: a token
    /// that needs decoding to look acceptable is refused as it stands. That kills
    /// casual smuggling outright, and <see cref="ViewerCensus"/> then hashes what
    /// survives under a salt that only exists in memory, so even a client that
    /// deliberately picks an identifying string leaves nothing readable behind.
    ///
    /// Pure string handling, no clock and no state, so the parsing rules are
    /// testable on their own - the same reason <see cref="PublicMapRoutes"/> is
    /// split out of its handler.
    /// </summary>
    internal static class ViewerToken
    {
        /// <summary>The query parameter the page echoes its token in: <c>?v=</c>.</summary>
        internal const string QueryKey = "v";

        /// <summary>
        /// Short enough that a browser can mint one cheaply, long enough that two
        /// tabs picking the same value by accident is not a thing that happens.
        /// The page mints 128 bits as 32 hex characters.
        /// </summary>
        internal const int MinLength = 8;

        /// <summary>
        /// An upper bound so a hostile client cannot make the server hash a
        /// megabyte per request. Well above what any honest page sends.
        /// </summary>
        internal const int MaxLength = 64;

        /// <summary>
        /// The token on a request URL, or null when there is not a well-formed
        /// one. Null is the normal case for anything that is not our own page -
        /// a third party embedding the open feed, a crawler, curl - and those are
        /// correctly not counted as somebody watching the map.
        /// </summary>
        internal static string? FromUrl(string? url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            int q = url!.IndexOf('?');
            if (q < 0 || q + 1 >= url.Length)
            {
                return null;
            }

            string query = url.Substring(q + 1);
            int fragment = query.IndexOf('#');
            if (fragment >= 0)
            {
                query = query.Substring(0, fragment);
            }

            foreach (string pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }

                if (!string.Equals(pair.Substring(0, eq), QueryKey, StringComparison.Ordinal))
                {
                    continue;
                }

                string value = pair.Substring(eq + 1);
                return IsWellFormed(value) ? value : null;
            }

            return null;
        }

        /// <summary>
        /// Whether a value is acceptable as a viewer token: 8-64 characters, each
        /// one an ASCII letter or digit, and nothing else.
        ///
        /// Deliberately narrow rather than "sanitise it into shape". Trimming a
        /// bad value into an acceptable one would mean the census could still be
        /// steered by what a client sent; refusing it means the only values that
        /// ever reach the census are ones that carry no punctuation, no
        /// separators and no encodings - so no address, no e-mail and no path
        /// survives the door in a readable form.
        /// </summary>
        internal static bool IsWellFormed(string? value)
        {
            if (value == null || value.Length < MinLength || value.Length > MaxLength)
            {
                return false;
            }

            foreach (char c in value)
            {
                bool ok = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z');
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

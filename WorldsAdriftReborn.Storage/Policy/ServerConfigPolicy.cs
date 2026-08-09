namespace WorldsAdriftReborn.Storage.Policy
{
    /// <summary>
    /// Pure rules for operator-set server configuration. No I/O, no clock, no
    /// connection - everything is a function of its argument, so the same
    /// normalisation the admin panel applies before a write can be unit tested
    /// without a database.
    ///
    /// Today it holds exactly one setting: the server's display name, the string
    /// the in-game server browser shows for this deployment. It lived as a
    /// hardcoded literal at the /deploymentStatus call site; moving it here is
    /// what lets the panel change it and a test pin what a legal value is.
    /// </summary>
    public static class ServerConfigPolicy
    {
        /// <summary>
        /// The key the server name is stored under in the config table. A named
        /// constant rather than a scattered string literal so the repository and
        /// any future reader cannot disagree about it.
        /// </summary>
        public const string ServerNameKey = "server_name";

        /// <summary>
        /// What the browser shows before anybody has set anything. This is the
        /// exact literal that used to be hardcoded at the /deploymentStatus call
        /// site, kept identical so an un-configured server reads the same as it
        /// always did.
        /// </summary>
        public const string DefaultServerName = "awesome community server";

        /// <summary>Shortest name we will store. One visible character.</summary>
        public const int MinServerNameLength = 1;

        /// <summary>
        /// Longest name we will store. The client renders this in a fixed-width
        /// browser row; a runaway string is a layout problem there and a way to
        /// smuggle a megabyte through a POST here, so it is capped rather than
        /// trusted.
        /// </summary>
        public const int MaxServerNameLength = 64;

        /// <summary>
        /// The stored form of a raw operator input: outer whitespace removed,
        /// internal whitespace runs (including tabs and newlines a paste can
        /// carry) collapsed to single spaces, and the result capped at
        /// <see cref="MaxServerNameLength"/>.
        ///
        /// Collapsing rather than rejecting internal whitespace is deliberate: a
        /// name with a double space is a typo, not an attack, and the operator
        /// should see it fixed rather than refused. The cap is applied AFTER the
        /// collapse so the limit counts visible characters, not the whitespace a
        /// paste happened to include.
        /// </summary>
        public static string Normalize(string? raw)
        {
            if (raw == null)
            {
                return string.Empty;
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder(raw.Length);
            bool pendingSpace = false;

            foreach (char c in raw)
            {
                if (char.IsWhiteSpace(c))
                {
                    // Only emit a space once we know a non-space follows, so
                    // leading and trailing runs never reach the output.
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(c);

                if (builder.Length >= MaxServerNameLength)
                {
                    break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Whether a raw input normalises to something we will store. A value
        /// that is only whitespace, or empty, is refused - an empty server name
        /// renders a nameless row the operator cannot tell from a missing server.
        /// </summary>
        public static bool IsValid(string? raw)
        {
            string normalized = Normalize(raw);
            return normalized.Length >= MinServerNameLength
                && normalized.Length <= MaxServerNameLength;
        }
    }
}

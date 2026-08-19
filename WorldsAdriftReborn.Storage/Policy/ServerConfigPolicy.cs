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

        // ---- welcome message ----------------------------------------------

        /// <summary>
        /// The key the client's welcome message is stored under. Same generic KV
        /// table as the server name, and for the same reason: an operator-set
        /// string is a ROW, not a schema change. Production runs at schema 9 and
        /// shipping a migration to hold one string would take persistence off for
        /// the duration of the deploy - a cost this setting cannot possibly earn.
        /// </summary>
        public const string WelcomeMessageKey = "welcome_message";

        /// <summary>
        /// What a fresh, un-configured server greets a player with. Written out
        /// here rather than in the client so the words can be changed from the
        /// panel; the client only renders whatever /welcomeMessage hands it.
        ///
        /// Built by concatenating literals with explicit <c>\n</c> rather than
        /// written as a verbatim string, so the constant is the same bytes
        /// whatever line endings this file happens to be checked out with. A
        /// CRLF checkout of a verbatim literal would silently ship a different
        /// default than a LF one - and the difference would only show as ragged
        /// spacing in the client, far from here.
        /// </summary>
        public const string DefaultWelcomeMessage =
            "Greetings Traveller,\n"
            + "\n"
            + "Worlds Adrift closed in 2019. Wareborn is a fan-run server that puts it back online.\n"
            + "\n"
            + "Much of the game is here. Islands, ships, mining, crafting, and the sky between them. "
            + "Some of it is not, and some of it breaks. We fix things as we find them.\n"
            + "\n"
            + "Nothing here is for sale. There is no studio behind it, just people who missed the game.\n"
            + "\n"
            + "See you in the skies.\n"
            + "\n"
            + "- The Wareborn crew";

        /// <summary>Shortest message we will store. One visible character.</summary>
        public const int MinWelcomeMessageLength = 1;

        /// <summary>
        /// Longest message we will store. Generous - this is prose, not a name -
        /// but bounded, because the value goes into a KV row read on a client
        /// path and an unbounded POST body is a way to make both expensive.
        /// </summary>
        public const int MaxWelcomeMessageLength = 4000;

        /// <summary>
        /// How many blank lines in a row survive normalisation. Paragraph breaks
        /// are the point of this field, so unlike the server name its newlines
        /// are kept; only a run longer than this - the signature of a paste, not
        /// of an intention - is shortened.
        /// </summary>
        public const int MaxConsecutiveBlankLines = 2;

        /// <summary>
        /// The stored form of a raw operator message: line endings normalised to
        /// <c>\n</c> (a browser textarea POSTs CRLF, and the client must not have
        /// to know that), trailing whitespace stripped from every line, outer
        /// whitespace trimmed, and runs of more than
        /// <see cref="MaxConsecutiveBlankLines"/> blank lines shortened to that
        /// many.
        ///
        /// A null or blank input returns <see cref="DefaultWelcomeMessage"/>
        /// rather than the empty string. That is not politeness: the
        /// <c>server_config</c> CHECK refuses a blank value outright, so an empty
        /// normalisation could only ever reach the database as an exception, and
        /// a blank welcome message is not a state any caller wants anyway.
        ///
        /// Deliberately does NOT truncate at <see cref="MaxWelcomeMessageLength"/>.
        /// The server name truncates because a too-long name is a layout problem
        /// with an obvious fix; a too-long message is an operator mistake, and
        /// silently storing the first 4000 characters of it would cut a sentence
        /// in half in front of every player. <see cref="IsValidWelcomeMessage"/>
        /// refuses it instead, and the panel says so.
        /// </summary>
        public static string NormalizeWelcomeMessage(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return DefaultWelcomeMessage;
            }

            string unified = raw!.Replace("\r\n", "\n").Replace('\r', '\n');

            string[] lines = unified.Split('\n');
            System.Text.StringBuilder builder = new System.Text.StringBuilder(unified.Length);
            int blankRun = 0;
            bool wroteAnything = false;

            foreach (string line in lines)
            {
                string trimmed = line.TrimEnd();

                if (trimmed.Length == 0)
                {
                    // Leading blank lines are dropped outright; interior ones are
                    // counted and only emitted once a non-blank line follows, so
                    // a trailing run never reaches the output either.
                    if (wroteAnything)
                    {
                        blankRun++;
                    }
                    continue;
                }

                if (wroteAnything)
                {
                    // One newline ends the previous line; each blank line kept is
                    // one more on top of it.
                    builder.Append('\n');
                    for (int i = 0; i < Math.Min(blankRun, MaxConsecutiveBlankLines); i++)
                    {
                        builder.Append('\n');
                    }
                }

                blankRun = 0;
                builder.Append(trimmed);
                wroteAnything = true;
            }

            string normalized = builder.ToString().Trim();
            return normalized.Length == 0 ? DefaultWelcomeMessage : normalized;
        }

        /// <summary>
        /// Whether a raw message is one we will store. Blank and whitespace-only
        /// are refused before normalisation, because normalisation would answer
        /// them with the default and a caller checking validity would then be
        /// told "yes" about a value it never supplied. Over-length is refused on
        /// the NORMALISED form, so the cap counts the text that will actually be
        /// stored rather than the line endings a paste carried.
        /// </summary>
        public static bool IsValidWelcomeMessage(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string normalized = NormalizeWelcomeMessage(raw);
            return normalized.Length >= MinWelcomeMessageLength
                && normalized.Length <= MaxWelcomeMessageLength;
        }
    }
}

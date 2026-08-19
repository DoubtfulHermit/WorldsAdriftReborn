using System.Globalization;

namespace WorldsAdriftServer.PatchNotes
{
    /// <summary>What a parsed block of a release is.</summary>
    internal enum PatchNotesBlockKind
    {
        /// <summary>A heading inside a release ("### The world is fuller").</summary>
        Heading,

        /// <summary>A run of prose lines, joined into one paragraph.</summary>
        Paragraph,

        /// <summary>A run of "- " lines, kept as one list.</summary>
        Bullets,

        /// <summary>
        /// A run of "* &lt;sha&gt; &lt;subject&gt;" lines - the commit log.
        ///
        /// Its own kind rather than a bullet because a commit is two fields, not
        /// a sentence: the sha wants a fixed column and the subject wants the
        /// reading width, and a list that renders them as one string cannot line
        /// the shas up.
        /// </summary>
        Commits,
    }

    /// <summary>One commit: an abbreviated sha and the subject line.</summary>
    internal readonly struct PatchNotesCommit
    {
        internal PatchNotesCommit(string sha, string subject)
        {
            Sha = sha;
            Subject = subject;
        }

        internal string Sha { get; }
        internal string Subject { get; }

        /// <summary>
        /// Splits "153728a Record the release" into its two parts, or returns
        /// false when the line is not a commit after all.
        ///
        /// The sha must be hex and 6-40 long. That test is what lets the same
        /// "* " marker stay usable for anything else without this silently
        /// eating it: a line the generator did not write falls through to being
        /// an ordinary bullet rather than rendering as a commit with a nonsense
        /// sha in the column.
        /// </summary>
        internal static bool TryParse(string? line, out PatchNotesCommit commit)
        {
            commit = default;
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string text = line!.Trim();
            int space = text.IndexOf(' ');
            if (space < 6 || space > 40)
            {
                return false;
            }

            string sha = text.Substring(0, space);
            foreach (char c in sha)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            string subject = text.Substring(space + 1).Trim();
            if (subject.Length == 0)
            {
                return false;
            }

            commit = new PatchNotesCommit(sha, subject);
            return true;
        }
    }

    /// <summary>One heading, paragraph or list inside a release.</summary>
    internal sealed class PatchNotesBlock
    {
        internal PatchNotesBlock(PatchNotesBlockKind kind, string text, IReadOnlyList<string> items)
        {
            Kind = kind;
            Text = text;
            Items = items;
        }

        internal PatchNotesBlockKind Kind { get; }

        /// <summary>The heading or paragraph text. Empty for a list.</summary>
        internal string Text { get; }

        /// <summary>The list's items. Empty for anything else.</summary>
        internal IReadOnlyList<string> Items { get; }
    }

    /// <summary>One dated release.</summary>
    internal sealed class PatchNotesRelease
    {
        internal PatchNotesRelease(string date, string displayDate, string title, string badge,
            string anchor, IReadOnlyList<PatchNotesBlock> blocks)
        {
            Date = date;
            DisplayDate = displayDate;
            Title = title;
            Badge = badge;
            Anchor = anchor;
            Blocks = blocks;
        }

        /// <summary>The machine date as written, or empty when it was not a date.</summary>
        internal string Date { get; }

        /// <summary>The date as a reader sees it ("18 August 2026").</summary>
        internal string DisplayDate { get; }

        internal string Title { get; }

        /// <summary>An optional short tag - a patcher version, "server-side only".</summary>
        internal string Badge { get; }

        /// <summary>The id this release is linkable by.</summary>
        internal string Anchor { get; }

        internal IReadOnlyList<PatchNotesBlock> Blocks { get; }
    }

    /// <summary>
    /// The notes source, parsed.
    ///
    /// The source is a small, fixed line format rather than Markdown - see
    /// <see cref="PatchNotesMarkup"/> for why the vocabulary is short - and this
    /// module is the whole of its grammar:
    ///
    /// <code>
    /// lines before the first release          the page's standfirst
    /// ## 2026-08-18 | Title | optional badge  starts a release
    /// ### Heading                             a heading inside it
    /// - item                                  a bullet; a run of them is one list
    /// anything else                           prose; a run of lines is one paragraph
    /// </code>
    ///
    /// Pure: text in, objects out. It never throws on bad input, because the
    /// input can be edited by an operator at three in the morning and a page that
    /// 500s is a worse answer than a page that renders the odd line as prose.
    /// A source that is empty, missing or nothing but blank lines parses to a
    /// document with no releases, which the page has a proper state for.
    /// </summary>
    internal sealed class PatchNotesDocument
    {
        private PatchNotesDocument(IReadOnlyList<string> intro, IReadOnlyList<PatchNotesRelease> releases)
        {
            Intro = intro;
            Releases = releases;
        }

        /// <summary>The paragraphs above the first release.</summary>
        internal IReadOnlyList<string> Intro { get; }

        internal IReadOnlyList<PatchNotesRelease> Releases { get; }

        internal bool IsEmpty => Releases.Count == 0;

        internal static PatchNotesDocument Empty { get; } =
            new PatchNotesDocument(Array.Empty<string>(), Array.Empty<PatchNotesRelease>());

        internal static PatchNotesDocument Parse(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return Empty;
            }

            string[] lines = source!.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');

            List<string> intro = new List<string>();
            List<PatchNotesRelease> releases = new List<PatchNotesRelease>();

            // The release being filled, and the run of like lines inside it.
            string? date = null, title = null, badge = null;
            List<PatchNotesBlock> blocks = new List<PatchNotesBlock>();
            List<string> bullets = new List<string>();
            List<string> commits = new List<string>();
            List<string> prose = new List<string>();
            HashSet<string> anchors = new HashSet<string>(StringComparer.Ordinal);

            void FlushRuns()
            {
                if (bullets.Count > 0)
                {
                    Add(new PatchNotesBlock(PatchNotesBlockKind.Bullets, string.Empty,
                        bullets.ToArray()));
                    bullets.Clear();
                }

                if (commits.Count > 0)
                {
                    Add(new PatchNotesBlock(PatchNotesBlockKind.Commits, string.Empty,
                        commits.ToArray()));
                    commits.Clear();
                }

                if (prose.Count > 0)
                {
                    Add(new PatchNotesBlock(PatchNotesBlockKind.Paragraph,
                        string.Join(" ", prose), Array.Empty<string>()));
                    prose.Clear();
                }
            }

            void Add(PatchNotesBlock block)
            {
                if (title == null && block.Kind == PatchNotesBlockKind.Paragraph)
                {
                    // Above the first release: the page's own standfirst.
                    intro.Add(block.Text);
                    return;
                }

                if (title == null)
                {
                    // A heading or list before any release has nowhere to live.
                    return;
                }

                blocks.Add(block);
            }

            void CloseRelease()
            {
                FlushRuns();
                if (title == null)
                {
                    return;
                }

                string anchor = UniqueAnchor(anchors, date, title);
                releases.Add(new PatchNotesRelease(
                    Machine(date), Display(date), title!, badge ?? string.Empty, anchor,
                    blocks.ToArray()));
                blocks.Clear();
            }

            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                string trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    FlushRuns();
                    continue;
                }

                if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                {
                    CloseRelease();

                    string[] parts = trimmed.Substring(3).Split('|');
                    date = parts.Length > 0 ? parts[0].Trim() : string.Empty;
                    title = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                    badge = parts.Length > 2 ? parts[2].Trim() : string.Empty;

                    if (title.Length == 0)
                    {
                        // "## Something" with no pipe: it is a title, not a date.
                        title = date;
                        date = string.Empty;
                    }

                    continue;
                }

                if (trimmed.StartsWith("### ", StringComparison.Ordinal))
                {
                    FlushRuns();
                    Add(new PatchNotesBlock(PatchNotesBlockKind.Heading,
                        trimmed.Substring(4).Trim(), Array.Empty<string>()));
                    continue;
                }

                // A commit line. Checked BEFORE the bullet marker so the sha test
                // gets first refusal: "* not-a-sha ..." then falls through and is
                // treated as prose rather than being lost.
                if (trimmed.StartsWith("* ", StringComparison.Ordinal)
                    && PatchNotesCommit.TryParse(trimmed.Substring(2), out _))
                {
                    if (prose.Count > 0 || bullets.Count > 0)
                    {
                        FlushRuns();
                    }

                    commits.Add(trimmed.Substring(2).Trim());
                    continue;
                }

                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    if (prose.Count > 0 || commits.Count > 0)
                    {
                        FlushRuns();
                    }

                    bullets.Add(trimmed.Substring(2).Trim());
                    continue;
                }

                if (bullets.Count > 0 || commits.Count > 0)
                {
                    FlushRuns();
                }

                prose.Add(trimmed);
            }

            CloseRelease();

            return releases.Count == 0 && intro.Count == 0
                ? Empty
                : new PatchNotesDocument(intro.ToArray(), releases.ToArray());
        }

        /// <summary>The date as an ISO string when it is one, else empty.</summary>
        private static string Machine(string? date) =>
            IsIso(date) ? date! : string.Empty;

        /// <summary>
        /// The date as a reader reads it. Invariant culture on purpose: the page
        /// is English and the server's locale is not the reader's.
        /// </summary>
        private static string Display(string? date)
        {
            if (string.IsNullOrWhiteSpace(date))
            {
                return string.Empty;
            }

            return IsIso(date)
                ? DateTime.ParseExact(date!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                    .ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
                : date!.Trim();
        }

        private static bool IsIso(string? date) =>
            !string.IsNullOrWhiteSpace(date)
            && DateTime.TryParseExact(date!.Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        /// <summary>
        /// A stable, readable id for a release, so a note can be linked to. Two
        /// releases that would collide get a counter rather than one silently
        /// stealing the other's anchor.
        /// </summary>
        private static string UniqueAnchor(HashSet<string> taken, string? date, string? title)
        {
            string seed = Slug(!string.IsNullOrWhiteSpace(date) ? date! : title);
            if (seed.Length == 0)
            {
                seed = "release";
            }

            string anchor = seed;
            int n = 2;
            while (!taken.Add(anchor))
            {
                anchor = seed + "-" + n.ToString(CultureInfo.InvariantCulture);
                n++;
            }

            return anchor;
        }

        private static string Slug(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            System.Text.StringBuilder slug = new System.Text.StringBuilder();
            bool dash = false;
            foreach (char c in text!.Trim().ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    slug.Append(c);
                    dash = false;
                }
                else if (!dash && slug.Length > 0)
                {
                    slug.Append('-');
                    dash = true;
                }
            }

            return slug.ToString().Trim('-');
        }
    }
}

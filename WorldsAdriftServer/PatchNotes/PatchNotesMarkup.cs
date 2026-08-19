using System.Text;

namespace WorldsAdriftServer.PatchNotes
{
    /// <summary>
    /// The inline markup the notes source may use, and the escaping around it.
    ///
    /// This is deliberately a SHORT list - bold, a code span, and a link that can
    /// only point back at this site - rather than a Markdown library. Two reasons.
    ///
    /// The source is operator-editable (see <see cref="PatchNotesSource"/>), so
    /// whatever this module accepts is, in effect, what somebody with the admin
    /// password can put on a public page. Everything is HTML-escaped FIRST and the
    /// three markers are the only things that ever become a tag, so there is no
    /// path from the stored text to raw HTML in a browser.
    ///
    /// And the page must reach for nothing off this host. An external link is not
    /// a fetch, but a page that can grow one can grow an image tag next; the href
    /// policy here refuses anything with a scheme, so "no third-party requests"
    /// holds by construction instead of by review.
    /// </summary>
    internal static class PatchNotesMarkup
    {
        /// <summary>
        /// HTML-escapes a string. Quotes as well as angle brackets, because the
        /// escaped text is also used inside attribute values.
        /// </summary>
        internal static string Escape(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder escaped = new StringBuilder(text!.Length + 16);
            foreach (char c in text)
            {
                Append(escaped, c);
            }

            return escaped.ToString();
        }

        /// <summary>
        /// True when a link target is somewhere on this site.
        ///
        /// A colon is refused outright, which is what makes this safe rather than
        /// merely tidy: no colon means no <c>javascript:</c>, no <c>data:</c> and
        /// no scheme of any kind. A leading double slash is refused for the same
        /// reason - <c>//evil.example</c> is protocol-relative and is a different
        /// host. What is left is a path on this server, or a fragment on this page.
        /// </summary>
        internal static bool IsInternalHref(string? href)
        {
            if (string.IsNullOrEmpty(href))
            {
                return false;
            }

            string target = href!;
            if (target[0] != '/' && target[0] != '#')
            {
                return false;
            }

            if (target.StartsWith("//", StringComparison.Ordinal))
            {
                return false;
            }

            foreach (char c in target)
            {
                bool allowed = IsAsciiLetterOrDigit(c)
                    || c == '/' || c == '-' || c == '_' || c == '.'
                    || c == '#' || c == '?' || c == '=' || c == '&';
                if (!allowed)
                {
                    return false;
                }
            }

            // "/../" would escape the path this page means to point at. Nothing
            // here needs it.
            return !target.Contains("..", StringComparison.Ordinal);
        }

        /// <summary>
        /// One line of source text as safe HTML.
        ///
        /// A marker that is never closed is not markup - it is a literal asterisk
        /// or backtick somebody typed, and it comes out as one. A link whose
        /// target fails <see cref="IsInternalHref"/> keeps its label and loses its
        /// link: the sentence still reads, and the page still reaches nowhere.
        /// </summary>
        internal static string Inline(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string source = text!;
            StringBuilder html = new StringBuilder(source.Length + 32);

            int at = 0;
            while (at < source.Length)
            {
                char c = source[at];

                if (c == '`')
                {
                    int close = source.IndexOf('`', at + 1);
                    if (close > at + 1)
                    {
                        html.Append("<code>")
                            .Append(Escape(source.Substring(at + 1, close - at - 1)))
                            .Append("</code>");
                        at = close + 1;
                        continue;
                    }
                }
                else if (c == '*' && at + 1 < source.Length && source[at + 1] == '*')
                {
                    int close = source.IndexOf("**", at + 2, StringComparison.Ordinal);
                    if (close > at + 2)
                    {
                        html.Append("<strong>")
                            .Append(Inline(source.Substring(at + 2, close - at - 2)))
                            .Append("</strong>");
                        at = close + 2;
                        continue;
                    }
                }
                else if (c == '[')
                {
                    int mid = source.IndexOf("](", at + 1, StringComparison.Ordinal);
                    int end = mid > at ? source.IndexOf(')', mid + 2) : -1;
                    if (mid > at && end > mid + 1)
                    {
                        string label = source.Substring(at + 1, mid - at - 1);
                        string href = source.Substring(mid + 2, end - mid - 2);
                        if (IsInternalHref(href))
                        {
                            html.Append("<a href=\"").Append(Escape(href)).Append("\">")
                                .Append(Inline(label))
                                .Append("</a>");
                        }
                        else
                        {
                            html.Append(Inline(label));
                        }

                        at = end + 1;
                        continue;
                    }
                }

                Append(html, c);
                at++;
            }

            return html.ToString();
        }

        /// <summary>
        /// ASCII letters and digits, spelled out because this project targets
        /// net6.0 and <c>char.IsAsciiLetterOrDigit</c> arrived in net7. It must
        /// be ASCII rather than <c>char.IsLetterOrDigit</c>: the latter is true
        /// for characters that normalise to a slash or a dot in some contexts,
        /// which is not a decision an href allowlist should be delegating.
        /// </summary>
        private static bool IsAsciiLetterOrDigit(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');

        private static void Append(StringBuilder into, char c)
        {
            switch (c)
            {
                case '&': into.Append("&amp;"); break;
                case '<': into.Append("&lt;"); break;
                case '>': into.Append("&gt;"); break;
                case '"': into.Append("&quot;"); break;
                case '\'': into.Append("&#39;"); break;
                default: into.Append(c); break;
            }
        }
    }
}

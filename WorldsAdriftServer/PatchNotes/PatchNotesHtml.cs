using System.Globalization;
using System.Text;

namespace WorldsAdriftServer.PatchNotes
{
    /// <summary>
    /// A parsed document as page markup. Pure: document in, HTML string out, no
    /// request, no database, no clock.
    ///
    /// It is separate from <see cref="Web.PatchNotesPage"/> because the page is
    /// chrome - head, stylesheet, footer - and this is the part with the
    /// decisions in it: which element a block becomes, what a release without a
    /// date looks like, and what the page says when there is nothing to say.
    /// </summary>
    internal static class PatchNotesHtml
    {
        /// <summary>
        /// The empty state. A server can be freshly stood up, or an operator can
        /// clear the box, and neither is an error - so this is a sentence, not a
        /// blank page and not a stack trace.
        /// </summary>
        internal const string EmptyState =
            "<div class=\"card pn-empty\"><p>No notes have been published yet. "
            + "When something changes on this server, it gets written down here.</p></div>";

        /// <summary>Every release, newest first as the source lists them.</summary>
        internal static string Releases(PatchNotesDocument document)
        {
            if (document.IsEmpty)
            {
                return EmptyState;
            }

            StringBuilder html = new StringBuilder();
            foreach (PatchNotesRelease release in document.Releases)
            {
                html.Append(Release(release));
            }

            return html.ToString();
        }

        /// <summary>
        /// The standfirst above the releases - the source's lines before the
        /// first <c>##</c>. Empty when the source opens straight into a release.
        /// </summary>
        internal static string Intro(PatchNotesDocument document)
        {
            StringBuilder html = new StringBuilder();
            foreach (string paragraph in document.Intro)
            {
                html.Append("<p class=\"lede pn-lede\">")
                    .Append(PatchNotesMarkup.Inline(paragraph))
                    .Append("</p>\n");
            }

            return html.ToString();
        }

        /// <summary>
        /// The jump list beside the notes. Dropped entirely below one release -
        /// a contents list with a single entry is furniture, not navigation.
        /// </summary>
        internal static string Index(PatchNotesDocument document)
        {
            if (document.Releases.Count < 2)
            {
                return string.Empty;
            }

            StringBuilder html = new StringBuilder(
                "<nav class=\"pn-index\" aria-label=\"Releases\"><h2>Releases</h2><ol>\n");
            foreach (PatchNotesRelease release in document.Releases)
            {
                html.Append("<li><a href=\"#").Append(PatchNotesMarkup.Escape(release.Anchor))
                    .Append("\"><span class=\"pn-index-title\">")
                    .Append(PatchNotesMarkup.Inline(release.Title))
                    .Append("</span><span class=\"pn-index-date\">")
                    .Append(PatchNotesMarkup.Escape(release.DisplayDate))
                    .Append("</span></a></li>\n");
            }

            return html.Append("</ol></nav>\n").ToString();
        }

        /// <summary>How many releases, as a phrase for the header strip.</summary>
        internal static string Count(PatchNotesDocument document)
        {
            int n = document.Releases.Count;
            return n == 1
                ? "1 release"
                : n.ToString(CultureInfo.InvariantCulture) + " releases";
        }

        /// <summary>The date of the newest release, or empty if there is none.</summary>
        internal static string Latest(PatchNotesDocument document) =>
            document.Releases.Count == 0
                ? string.Empty
                : PatchNotesMarkup.Escape(document.Releases[0].DisplayDate);

        private static string Release(PatchNotesRelease release)
        {
            StringBuilder html = new StringBuilder();
            html.Append("<article class=\"card pn-release\" id=\"")
                .Append(PatchNotesMarkup.Escape(release.Anchor)).Append("\">\n");

            html.Append("<header class=\"pn-head\">\n");
            if (release.DisplayDate.Length > 0)
            {
                html.Append(release.Date.Length > 0
                        ? "<time class=\"pn-date\" datetime=\"" + PatchNotesMarkup.Escape(release.Date) + "\">"
                        : "<span class=\"pn-date\">")
                    .Append(PatchNotesMarkup.Escape(release.DisplayDate))
                    .Append(release.Date.Length > 0 ? "</time>\n" : "</span>\n");
            }

            html.Append("<h2 class=\"pn-title\">")
                .Append(PatchNotesMarkup.Inline(release.Title))
                .Append("</h2>\n");

            if (release.Badge.Length > 0)
            {
                html.Append("<span class=\"pill pn-badge\">")
                    .Append(PatchNotesMarkup.Inline(release.Badge))
                    .Append("</span>\n");
            }

            html.Append("</header>\n");

            foreach (PatchNotesBlock block in release.Blocks)
            {
                html.Append(Block(block));
            }

            return html.Append("</article>\n").ToString();
        }

        private static string Block(PatchNotesBlock block)
        {
            switch (block.Kind)
            {
                case PatchNotesBlockKind.Heading:
                    return "<h3 class=\"pn-section\">"
                        + PatchNotesMarkup.Inline(block.Text) + "</h3>\n";

                case PatchNotesBlockKind.Bullets:
                {
                    StringBuilder list = new StringBuilder("<ul class=\"pn-list\">\n");
                    foreach (string item in block.Items)
                    {
                        list.Append("<li>").Append(PatchNotesMarkup.Inline(item)).Append("</li>\n");
                    }

                    return list.Append("</ul>\n").ToString();
                }

                default:
                    return "<p>" + PatchNotesMarkup.Inline(block.Text) + "</p>\n";
            }
        }
    }
}

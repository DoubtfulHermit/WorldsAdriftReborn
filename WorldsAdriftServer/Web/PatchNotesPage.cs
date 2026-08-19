using WorldsAdriftServer.PatchNotes;

namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The public patch notes at <c>/patchnotes</c>.
    ///
    /// This is the page the in-game PATCH NOTES button lands on, so for a lot of
    /// people it is the first thing they read about the server. It is therefore
    /// styled as the console and the public map are, from the same
    /// <c>console.css</c> - one site, not a changelog bolted on beside one - with
    /// a second stylesheet for the things a page of prose needs and a dashboard
    /// does not.
    ///
    /// Self-contained like every page here: no CDN, no web font, no script at
    /// all. There is nothing on this page that needs one - it is text the server
    /// already has - and a public page that fetches from a third party tells that
    /// third party who read it.
    ///
    /// The words come from <see cref="PatchNotesSource"/>; the grammar from
    /// <see cref="PatchNotesDocument"/>; the markup from
    /// <see cref="PatchNotesHtml"/>. This file is the frame around them.
    /// </summary>
    internal static class PatchNotesPage
    {
        internal const string ContentType = "text/html; charset=utf-8";

        internal static readonly string Style =
            "<style>" + WebAssets.Read("console.css") + WebAssets.Read("patchnotes.css") + "</style>";

        internal static string Html(string? source)
        {
            PatchNotesDocument document = PatchNotesDocument.Parse(source);

            string latest = PatchNotesHtml.Latest(document);
            string body = WebAssets.Fill(WebAssets.Read("patchnotes-body.html"),
                ("count", document.IsEmpty ? "Nothing published yet" : PatchNotesHtml.Count(document)),
                ("latestSuffix", latest.Length > 0 ? " &middot; latest " + latest : string.Empty),
                ("intro", PatchNotesHtml.Intro(document)),
                ("releases", PatchNotesHtml.Releases(document)),
                ("index", PatchNotesHtml.Index(document)));

            return @"<!DOCTYPE html><html lang=""en""><head>
<meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""dark"">
<meta name=""description"" content=""What has changed on the Wareborn server: a fan-run revival of Worlds Adrift, written down release by release."">
<title>Patch notes - Worlds Adrift Reborn</title>" + Style + @"</head>
<body><div class=""wrap pn-wrap"">
" + body + @"</div></body></html>";
        }
    }
}

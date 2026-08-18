using System.Text;
using WorldsAdriftServer.Admin;

namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The operator dashboard's HTML, served verbatim like <see cref="SignupPage"/>
    /// as a restrained simulation operations console. Its typography, neutral
    /// controls, telemetry surfaces and danger treatment are all self-contained:
    /// no external CSS, fonts, scripts or images.
    ///
    /// Two pages live here. The login page is what an unauthenticated visitor
    /// sees at /admin; the dashboard is what a signed-in operator sees. The
    /// dashboard renders entirely from a JSON payload (embedded once for the
    /// first paint, then re-fetched from /admin/api/stats) so a single code path
    /// - the API - is the source of truth and the auto-refresh is just that path
    /// on a timer.
    /// </summary>
    internal static class AdminPage
    {
        internal const string ContentType = "text/html; charset=utf-8";

        /// <summary>
        /// Self-contained, high-density simulation-console design system, read
        /// from <c>Web/Assets/console.css</c>.
        ///
        /// Neither the tier colours nor the weather-wall colours are written
        /// there. They are appended from <see cref="MapTierPalette"/> and
        /// <see cref="MapWallPalette"/>, so the drawn surface and the legend key
        /// beside it are always the same value - including the ocean the
        /// translucent tier cells are composited over, which those modules emit
        /// too rather than assume.
        /// </summary>
        internal static readonly string Style =
            "<style>" + WebAssets.Read("console.css")
            + MapTierPalette.Css() + MapWallPalette.Css() + "</style>";

        // ---- login ---------------------------------------------------------

        internal static string Login(string? error)
        {
            string errBlock = string.IsNullOrEmpty(error)
                ? string.Empty
                : "<span class=\"err\">" + HtmlEncode(error!) + "</span>";

            return @"<!DOCTYPE html><html lang=""en""><head>
<meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark""><meta name=""robots"" content=""noindex"">
<title>Operator sign-in - Worlds Adrift Reborn</title>" + Style + @"</head>
<body><div class=""wrap"" style=""max-width:26rem"">
<p class=""mark"">Worlds Adrift Reborn</p>
<h1>Operator sign-in</h1>
<p class=""lede"">This console is for the server operator. Player accounts do not sign in here.</p>
<div class=""card"">
<form method=""post"" action=""/admin/login"">
  <div class=""field"">
    <label for=""username"">Operator</label>
    <input id=""username"" name=""username"" type=""text"" autocomplete=""username"" spellcheck=""false"" autocapitalize=""none"">
  </div>
  <div class=""field"">
    <label for=""password"">Passphrase</label>
    <input id=""password"" name=""password"" type=""password"" autocomplete=""current-password"">
  </div>
  " + errBlock + @"
  <button type=""submit"">Sign in</button>
</form>
</div>
<footer>An unofficial, fan-run community server. Not affiliated with Bossa Studios.</footer>
</div></body></html>";
        }

        // ---- disabled ------------------------------------------------------

        internal static readonly string Disabled = @"<!DOCTYPE html><html lang=""en""><head>
<meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark""><meta name=""robots"" content=""noindex"">
<title>Admin panel disabled</title>" + Style + @"</head>
<body><div class=""wrap"" style=""max-width:30rem"">
<p class=""mark"">Worlds Adrift Reborn</p>
<h1>Admin panel is off</h1>
<p class=""lede"">No operator credential is installed. Set the <code>WAREBORN_ADMIN</code> environment
variable to <code>username:hash</code> and restart the login server to enable this console.</p>
</div></body></html>";

        // ---- dashboard -----------------------------------------------------

        /// <summary>
        /// The dashboard shell. <paramref name="bootstrapJson"/> is the same
        /// payload /admin/api/stats returns, embedded for the first paint;
        /// everything below the header is rendered by the composed script from
        /// it and then re-rendered every few seconds from a fresh fetch.
        ///
        /// The body markup and every line of script now come from
        /// <c>Web/Assets</c> (see <see cref="WebAssets"/>). The dashboard takes
        /// ALL the fragments - the shared renderer plus the operator-only ones;
        /// the public map at <see cref="PublicMapPage"/> takes the shared ones
        /// and none of the operator ones, which is what keeps identity-bearing
        /// UI off the public page by construction.
        /// </summary>
        internal static string Dashboard(string bootstrapJson, string csrfToken,
            string worldMapJson = "{}")
        {
            // The map body is the SHARED fragment the public map draws into
            // too; only the copy around it is the operator's. See
            // PublicMapPage.MapBody for the other composition of the same file.
            string mapBody = WebAssets.Fill(WebAssets.ReadTrimmed("map-body.html"),
                ("mapTitle", "Preserved release-world map "
                    + "<span class=\"provenance-tag\">map evidence</span>"),
                ("mapProvenance", WebAssets.ReadTrimmed("admin-map-provenance.html")),
                ("mapLegend", WebAssets.Fill(WebAssets.ReadTrimmed("admin-map-legend.html"),
                    ("tierFillOpacity", MapTierPalette.FillOpacityCss),
                    ("wallLegend", MapWallPalette.LegendHtml()))),
                ("mapAuthenticity", WebAssets.ReadTrimmed("admin-map-authenticity.html")),
                ("mapLedger", WebAssets.ReadTrimmed("admin-map-ledger.html")));

            string body = WebAssets.Fill(WebAssets.Read("admin-body.html"),
                ("csrfTokenAttr", HtmlEncode(csrfToken)),
                ("mapBody", mapBody));

            // 1.5 s: the game server rewrites its snapshot every three, so a
            // reader on four was guaranteed to sometimes miss a generation
            // outright. One operator reads this; the public map, which may
            // have many readers, deliberately asks far less often.
            string script = WebAssets.Fill(WebAssets.Script(AdminScriptFragments),
                ("refreshMs", "1500"), ("csrfToken", csrfToken));

            return @"<!DOCTYPE html><html lang=""en""><head>
<meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark""><meta name=""robots"" content=""noindex"">
<title>Operator console - Worlds Adrift Reborn</title>" + Style + @"</head>
<body><div class=""wrap"">
" + body + @"<script id=""bootstrap"" type=""application/json"">" + bootstrapJson + @"</script>
<script id=""releaseWorldMap"" type=""application/json"">" + worldMapJson + @"</script>
<script>
(function(){
  'use strict';
" + script + @"})();
</script>
</body></html>";
        }

        /// <summary>
        /// The dashboard's script, in load order. The first two and the last
        /// two are the operator's; the middle three are the SHARED map renderer
        /// the public map draws from too, so a fix to a coastline or a creature
        /// reaches both pages from one edit.
        /// </summary>
        internal static readonly string[] AdminScriptFragments =
        {
            "console-core.js",
            "admin-domains.js",
            "map-render.js",
            "map-fauna.js",
            "map-ships.js",
            "map-interaction.js",
            "admin-console.js",
            "admin-wiring.js",
        };

        /// <summary>Minimal HTML entity escaping for text interpolated into markup.</summary>
        internal static string HtmlEncode(string value)
        {
            StringBuilder b = new StringBuilder(value.Length + 16);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '&': b.Append("&amp;"); break;
                    case '<': b.Append("&lt;"); break;
                    case '>': b.Append("&gt;"); break;
                    case '"': b.Append("&quot;"); break;
                    case '\'': b.Append("&#39;"); break;
                    default: b.Append(c); break;
                }
            }
            return b.ToString();
        }
    }
}

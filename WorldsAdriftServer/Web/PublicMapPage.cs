using WorldsAdriftServer.Admin;

namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The public live world map at /map.
    ///
    /// It is the SAME map as the operator console's: the same coastlines, the
    /// same zones, the same wildlife evaluated from the same closed form, the
    /// same hulls. It is composed from the same shared asset files
    /// (<see cref="WebAssets"/>), so a fix to the renderer reaches both pages
    /// from one edit and neither can drift into being a second implementation.
    ///
    /// What makes it public is what it does NOT compose. The operator
    /// fragments - the command UI, the player table, the terrain matrix, the
    /// domain workbench - are simply not in
    /// <see cref="ScriptFragments"/>, and the data it renders has already been
    /// through the anonymizing whitelist in
    /// <see cref="PublicMap.PublicMapProjection"/>. The privacy boundary is
    /// therefore structural at BOTH ends: there is no identity in the payload,
    /// and no UI that could show identity if there were.
    ///
    /// Per the current console decision, provenance is not displayed: no
    /// INFERRED badges, no authenticity disclaimers, no "map evidence" tags.
    /// The catalogue still carries its provenance labelling - the page just
    /// shows the data plainly.
    /// </summary>
    internal static class PublicMapPage
    {
        internal const string ContentType = "text/html; charset=utf-8";

        /// <summary>
        /// The renderer fragments this page shares with the operator console.
        /// <see cref="WebAssetCompositionTests"/> asserts every one of these is
        /// also in the console's load order, so "shared" is a checked fact.
        /// </summary>
        internal static readonly string[] SharedRendererFragments =
        {
            "console-core.js",
            "map-render.js",
            "map-fauna.js",
            "map-ships.js",
            "map-interaction.js",
            // The viewer sparkline, shared for the same reason the map is: the
            // console draws the same series over a longer window, and a second
            // copy of the drawing would drift away from this one.
            "map-viewers.js",
        };

        /// <summary>
        /// This page's script, in load order: the shared renderer, then the
        /// public page's own bootstrap and wiring. No operator fragment
        /// appears here, and a test asserts none ever does.
        ///
        /// public-map-viewers.js sits BEFORE public-map.js rather than after it,
        /// which matters: the fragments are one closure, so a function declared
        /// anywhere in it is visible everywhere, but a <c>var</c> is only
        /// INITIALISED when its fragment's top-level code runs. The viewer token
        /// is such a var and public-map.js's last lines fire the first poll, so a
        /// later fragment would mean the first poll of every page load went out
        /// with an undefined token and was not counted.
        /// </summary>
        internal static readonly string[] ScriptFragments =
            SharedRendererFragments
                .Concat(new[] { "public-map-viewers.js", "public-map.js" }).ToArray();

        /// <summary>
        /// How often a viewer's browser asks for a fresh snapshot.
        ///
        /// Slower than the console's 1.5 s, because the operator is ONE reader
        /// diagnosing a live world and needs every generation the game server
        /// writes, while the public map may have many readers who are simply
        /// watching it. But NOT arbitrarily slower, and this number was
        /// measured rather than guessed.
        ///
        /// Five seconds was tried first and was wrong: a moving hull is drawn
        /// by carrying its last measurement forward, and the server's own
        /// dead-reckoning window is 3 s - past that the browser correctly
        /// stops guessing and holds position. At a 5 s poll that left every
        /// ship visibly frozen for the last two seconds of each cycle, which
        /// headless capture caught as a hull whose transform did not change
        /// between frames.
        ///
        /// Three seconds is the game server's OWN write cadence, so a viewer
        /// gets each generation about once, and the reckoning window covers
        /// the whole gap - ships move continuously. It costs nothing extra:
        /// the endpoint's 2 s cache means any number of viewers share one
        /// rebuild, so the poll is a cached string per request either way.
        /// </summary>
        internal const string RefreshMs = "3000";

        /// <summary>
        /// The shared map body, composed with the public page's copy: no
        /// provenance strip, no authenticity note, a legend that names the
        /// anonymous marks for what they are, and the island ledger kept -
        /// what an island holds is preserved catalogue data, not anybody's
        /// business but the world's.
        /// </summary>
        private static string MapBody() => WebAssets.Fill(
            WebAssets.ReadTrimmed("map-body.html"),
            ("mapTitle", "The world of Worlds Adrift"),
            ("mapProvenance", string.Empty),
            ("mapLegend", WebAssets.Fill(WebAssets.ReadTrimmed("public-map-legend.html"),
                ("wallLegend", MapWallPalette.LegendHtml()))),
            ("mapAuthenticity", string.Empty),
            ("mapLedger", WebAssets.ReadTrimmed("public-map-ledger.html")));

        /// <summary>
        /// The page. <paramref name="bootstrapJson"/> is the anonymized live
        /// payload /map/data returns, embedded for the first paint exactly as
        /// the console embeds its own - so the map is drawn before the first
        /// poll rather than flashing empty.
        /// </summary>
        internal static string Html(string bootstrapJson, string worldMapJson) =>
            @"<!DOCTYPE html><html lang=""en""><head>
<meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""dark"">
<meta name=""description"" content=""A live map of the Worlds Adrift Reborn world: islands, zones, wildlife, and the ships and travellers currently aloft."">
<title>Live world map - Worlds Adrift Reborn</title>" + AdminPage.Style + PublicSiteChrome.Style + @"</head>
<body class=""wa-public wa-map"">" + PublicSiteChrome.Header("map", false) + @"<div class=""wrap"">
" + WebAssets.Fill(WebAssets.Read("public-map-body.html"),
        ("mapBody", MapBody()),
        // The viewer count's explanation, in the About panel with the rest of
        // the prose. Nothing about it goes on the page itself beyond the chip:
        // this page shows the world rather than describing its own methods.
        ("viewersAbout", WebAssets.ReadTrimmed("public-map-about-viewers.html")))
   + @"<script id=""bootstrap"" type=""application/json"">" + bootstrapJson + @"</script>
<script id=""releaseWorldMap"" type=""application/json"">" + worldMapJson + @"</script>
<script>
(function(){
  'use strict';
" + WebAssets.Fill(WebAssets.Script(ScriptFragments), ("refreshMs", RefreshMs)) + @"})();
</script>
</body></html>";
    }
}

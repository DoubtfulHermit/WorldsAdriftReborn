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
        };

        /// <summary>
        /// This page's script, in load order: the shared renderer, then the
        /// public page's own bootstrap and wiring. No operator fragment
        /// appears here, and a test asserts none ever does.
        /// </summary>
        internal static readonly string[] ScriptFragments =
            SharedRendererFragments.Concat(new[] { "public-map.js" }).ToArray();

        /// <summary>
        /// How often a viewer's browser asks for a fresh snapshot.
        ///
        /// Deliberately far slower than the console's 1.5 s. The operator is
        /// ONE reader who is diagnosing a live world and needs every
        /// generation the game server writes; the public map may have many
        /// readers who are watching it, and for watching, five seconds is
        /// indistinguishable - the wildlife and the ships are both animated
        /// in the browser from a closed form and a dead-reckoned pose, so
        /// motion stays smooth between polls no matter how far apart they
        /// are. The endpoint's own 2 s cache means extra viewers cost a
        /// cached string rather than a file read either way; this keeps the
        /// bandwidth honest as well.
        /// </summary>
        internal const string RefreshMs = "5000";

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
                ("tierFillOpacity", MapTierPalette.FillOpacityCss),
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
<title>Live world map - Worlds Adrift Reborn</title>" + AdminPage.Style + @"</head>
<body><div class=""wrap"">
" + WebAssets.Fill(WebAssets.Read("public-map-body.html"), ("mapBody", MapBody()))
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

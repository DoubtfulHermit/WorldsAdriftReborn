using System.Globalization;

namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// How an emblem gets from the <c>alliances.emblem_url</c> column onto the
    /// wire, and how a request for the PNG gets back to a spec.
    ///
    /// NO SCHEMA MIGRATION. The alliances table has carried an
    /// <c>emblem_url TEXT NOT NULL DEFAULT ''</c> column since the alliance work
    /// landed, and this feature reuses it - the whole emblem is a twelve-character
    /// code that fits in the string that was already there. That is worth stating
    /// loudly: the game server and the login server share ONE database, so a
    /// migration shipped in one binary alone turns the other's persistence off and
    /// loses player progression. This change needs neither binary's schema to move.
    ///
    /// WHAT IS STORED IS NOT WHAT IS SERVED. The column holds
    /// <c>wareborn:emblem:1-0-6-3-1-7-13</c>; the wire gets
    /// <c>https://host/alliance-emblem/&lt;uid&gt;.png?e=1-0-6-3-1-7-13</c>. Storing
    /// the marker rather than the finished URL means the public host name lives in
    /// exactly one place (configuration) instead of being baked into every row the
    /// day it was saved - so moving the server behind a different name does not
    /// silently break every crest, which is precisely the failure a stored
    /// absolute URL guarantees and nobody notices until a player mentions it.
    ///
    /// ANYTHING THAT IS NOT THE MARKER IS PASSED THROUGH VERBATIM. An operator
    /// who wants an alliance to wear an externally hosted image can still put a
    /// plain URL in the column and it is served as-is. That is the pre-existing
    /// behaviour and this must not remove it.
    ///
    /// Pure: strings in, strings out.
    /// </summary>
    internal static class EmblemUrlPolicy
    {
        /// <summary>The prefix that marks a stored value as a built emblem.</summary>
        internal const string Marker = "wareborn:emblem:";

        /// <summary>The path the crest is served from.</summary>
        internal const string RoutePrefix = "/alliance-emblem/";

        /// <summary>
        /// The two things a crest can be asked for as.
        ///
        /// The GAME is only ever given <see cref="Png"/>, and that is not a
        /// preference: the client decodes with <c>Texture2D.LoadImage</c>, which
        /// handles PNG and JPEG and nothing else, and does not check whether it
        /// worked - an SVG body would be displayed as a garbage texture rather
        /// than refused. <see cref="Svg"/> exists for PEOPLE: a leader can
        /// download their alliance's crest as a vector and scale, print or
        /// recolour it. Nothing this server puts in an alliance payload ever names
        /// it.
        /// </summary>
        internal enum Format
        {
            Png = 0,
            Svg = 1,
        }

        private const string PngExtension = ".png";
        private const string SvgExtension = ".svg";

        /// <summary>The query parameter carrying the code.</summary>
        internal const string CodeParameter = "e";

        /// <summary>The query parameter carrying the edge length, in pixels.</summary>
        internal const string SizeParameter = "s";

        /// <summary>
        /// The edge lengths this route will render, and the ONLY ones.
        ///
        /// AN ALLOWLIST RATHER THAN A CLAMP, because the route is
        /// unauthenticated and the cost of a render is the square of the edge
        /// length: a clamp invites <c>s=4095</c> and answers it with the most
        /// expensive picture allowed, over and over, from anybody. Three values
        /// means three answers exist and every other string in that parameter is
        /// the crest size - so the worst thing a stranger can ask for is a number
        /// this file wrote down, and it was measured before it was written down
        /// (EmblemStackRenderTests).
        ///
        /// WHY THESE THREE. 256 is what the game downloads, and it stays the
        /// default so that no address the client already holds changes meaning.
        /// 512 and 1024 exist because nobody downloads a crest to put it back in
        /// the game - they put it on a Discord server, a banner or a forum post,
        /// and 256 is small for all three. 2048 is not offered: it is four times
        /// the samples of 1024 for a picture of twenty flat silhouettes, and the
        /// vector download is the honest answer to "I want it bigger than this".
        /// </summary>
        internal static readonly int[] DownloadSizes = { 256, 512, 1024 };

        /// <summary>
        /// The edge length a request that names none gets - the crest size, so
        /// that <c>?e=CODE</c> and <c>?e=CODE&amp;s=256</c> are the same picture
        /// and the game's own URL is unchanged by this parameter existing.
        /// </summary>
        internal const int DefaultSize = EmblemPainter.Size;

        /// <summary>Whether this is one of the three sizes we render.</summary>
        internal static bool IsOfferedSize(int size)
        {
            foreach (int offered in DownloadSizes)
            {
                if (offered == size) return true;
            }
            return false;
        }

        /// <summary>
        /// The path segment the builder's preview asks for, in place of an
        /// alliance uid. It exists so a leader can look at a crest they have not
        /// saved: the renderer is stateless, so previewing is the same request
        /// with a name that belongs to no alliance.
        /// </summary>
        internal const string PreviewId = "preview";

        /// <summary>
        /// The path segment the editor's object catalogue is served from.
        ///
        /// Inside the emblem namespace rather than beside it because this route
        /// already claims its whole prefix and always answers (see
        /// <see cref="IsEmblemPath"/>); a catalogue served from a path nothing
        /// claimed would hang the socket instead of 404ing.
        /// </summary>
        internal const string CatalogueSegment = EmblemEditorData.CatalogueName;

        /// <summary>The value to write into <c>emblem_url</c> for a built emblem.</summary>
        internal static string Store(EmblemArtwork artwork) => Marker + artwork.ToCode();

        /// <summary>
        /// Reads a stored column value back as a spec, or false if it is not one
        /// of ours (empty, an external URL, or a marker whose code no longer
        /// parses because the vocabulary version moved).
        /// </summary>
        internal static bool TryReadStored(string? stored, out EmblemArtwork artwork)
        {
            artwork = default;

            if (string.IsNullOrEmpty(stored)) return false;
            if (!stored!.StartsWith(Marker, StringComparison.Ordinal)) return false;

            return EmblemArtwork.TryParse(stored.Substring(Marker.Length), out artwork);
        }

        /// <summary>
        /// The absolute URL to put in the alliance payload.
        ///
        /// Absolute because it has to be: the client hands the string to
        /// <c>new Uri(url)</c> inside <c>HttpHelper.GenerateRequest</c>, which
        /// throws a UriFormatException on a relative one - and that throw happens
        /// inside the emblem promise, where nothing catches it.
        /// </summary>
        internal static string PublicUrl(string baseUrl, Guid allianceId, EmblemArtwork artwork) =>
            TrimBase(baseUrl) + RoutePrefix
            + allianceId.ToString("D", CultureInfo.InvariantCulture)
            + PngExtension + "?" + CodeParameter + "=" + artwork.ToCode();

        /// <summary>The preview URL the builder page fetches. Relative, because the
        /// page fetching it is served by this same server and a browser resolves
        /// it against the origin the operator actually reached us on.</summary>
        internal static string PreviewUrl(EmblemArtwork artwork) =>
            RoutePrefix + PreviewId + PngExtension + "?" + CodeParameter + "=" + artwork.ToCode();

        /// <summary>
        /// The vector of the same crest, for a player to download. Relative for
        /// the same reason <see cref="PreviewUrl"/> is: the page offering the link
        /// is served by this server, so the browser resolves it against whatever
        /// origin the operator actually reached us on.
        /// </summary>
        internal static string VectorUrl(Guid allianceId, EmblemArtwork artwork) =>
            RoutePrefix + (allianceId == Guid.Empty
                ? PreviewId
                : allianceId.ToString("D", CultureInfo.InvariantCulture))
            + SvgExtension + "?" + CodeParameter + "=" + artwork.ToCode();

        /// <summary>
        /// The same crest as a PNG a player can keep, at one of
        /// <see cref="DownloadSizes"/>.
        ///
        /// THE SAME ROUTE THE GAME USES, with a size on it, rather than a second
        /// download-only path. There is one rasteriser and one cache; a parallel
        /// path would be a second address for the same picture that could drift
        /// from it, which is exactly what the SVG route does NOT do. The size is
        /// always written out, including 256 - it makes the request
        /// self-describing in a log, and it costs nothing because the parser
        /// canonicalises 256 back to the address the client already holds.
        /// </summary>
        internal static string RasterUrl(Guid allianceId, EmblemArtwork artwork, int size) =>
            RoutePrefix + (allianceId == Guid.Empty
                ? PreviewId
                : allianceId.ToString("D", CultureInfo.InvariantCulture))
            + PngExtension + "?" + CodeParameter + "=" + artwork.ToCode()
            + "&" + SizeParameter + "="
            + (IsOfferedSize(size) ? size : DefaultSize).ToString(CultureInfo.InvariantCulture);

        /// <summary>The filename a downloaded vector crest is offered under.</summary>
        internal static string VectorFileName(EmblemArtwork artwork) =>
            FileName(artwork, SvgExtension);

        /// <summary>
        /// The filename a downloaded PNG is offered under. The SIZE is in it
        /// because three of them exist: a player who fetches two of the same crest
        /// otherwise gets "alliance-crest-....png" and
        /// "alliance-crest-....png (1)" and has to open both to find out which is
        /// which.
        /// </summary>
        internal static string RasterFileName(EmblemArtwork artwork, int size) =>
            FileName(artwork, "-" + (IsOfferedSize(size) ? size : DefaultSize)
                .ToString(CultureInfo.InvariantCulture) + PngExtension);

        /// <summary>
        /// How long a code may be before it is left out of the filename.
        ///
        /// A DESIGN CODE IS NOT A FILENAME. Twenty layers is 262 characters, and
        /// "alliance-crest-" plus that plus an extension is past the 255 BYTES
        /// every mainstream filesystem stops at - so the browser would silently
        /// truncate it into a name that still looks like a code but no longer is
        /// one, which is worse than not having it. Under the limit the code stays,
        /// because for the designs people actually build it is the one thing that
        /// tells two downloads apart.
        /// </summary>
        private const int MaxCodeInFileName = 96;

        private static string FileName(EmblemArtwork artwork, string suffix)
        {
            string code = artwork.ToCode();

            return code.Length <= MaxCodeInFileName
                ? "alliance-crest-" + code + suffix
                : "alliance-crest" + suffix;
        }

        /// <summary>
        /// Resolves what the alliance payload's <c>emblemUrl</c> should say, given
        /// what is in the column.
        ///
        /// The fallback is the point: an alliance that has never opened the builder
        /// gets the crest <see cref="EmblemSpec.DefaultFor"/> derives from its own
        /// uid, rather than an empty string. Empty was correct while there was
        /// nothing to serve - it left the client's grey placeholder alone - but now
        /// that there IS something to serve, every alliance having a crest of its
        /// own is strictly better than every alliance sharing one placeholder.
        /// </summary>
        internal static string Resolve(string baseUrl, Guid allianceId, string? stored)
        {
            if (TryReadStored(stored, out EmblemArtwork artwork))
            {
                return PublicUrl(baseUrl, allianceId, artwork);
            }

            // An operator's hand-set external URL wins over the generated default:
            // somebody typed it on purpose.
            if (!string.IsNullOrWhiteSpace(stored)) return stored!;

            return PublicUrl(baseUrl, allianceId, EmblemSpec.DefaultFor(allianceId));
        }

        /// <summary>
        /// Whether this URL asks for the editor's object catalogue.
        ///
        /// Answered before <see cref="TryParseRequest"/> gets a look, because the
        /// catalogue is not a picture and shares nothing with the crest routes but
        /// its prefix.
        /// </summary>
        internal static bool IsCatalogueRequest(string? url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            int q = url!.IndexOf('?');
            string path = q >= 0 ? url.Substring(0, q) : url;

            return string.Equals(path, RoutePrefix + CatalogueSegment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether an <c>Accept-Encoding</c> header offers gzip.
        ///
        /// Deliberately strict about two things a naive <c>Contains("gzip")</c> gets
        /// wrong, because the failure mode is a body the caller cannot read at an
        /// address it will then cache forever:
        /// <list type="bullet">
        /// <item><c>x-gzip</c>, or any other token that merely ENDS in "gzip", is
        ///   not gzip - so tokens are matched whole;</item>
        /// <item><c>gzip;q=0</c> is a client saying it does NOT want gzip, which is
        ///   the spelling browsers and proxies use to opt out.</item>
        /// </list>
        /// No header at all is no, which is the safe direction: an uncompressed body
        /// is readable by everything.
        /// </summary>
        internal static bool AcceptsGzip(string? acceptEncoding)
        {
            if (string.IsNullOrEmpty(acceptEncoding)) return false;

            foreach (string part in acceptEncoding!.Split(','))
            {
                string[] pieces = part.Split(';');
                string token = pieces[0].Trim();

                if (!string.Equals(token, "gzip", StringComparison.OrdinalIgnoreCase)) continue;

                for (int i = 1; i < pieces.Length; i++)
                {
                    string parameters = pieces[i].Trim();

                    if (!parameters.StartsWith("q=", StringComparison.OrdinalIgnoreCase)) continue;

                    if (double.TryParse(parameters.Substring(2),
                            System.Globalization.NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double weight)
                        && weight <= 0.0)
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Whether this URL is in the emblem namespace at all, whether or not it
        /// names something renderable.
        ///
        /// The distinction matters here more than it usually would. This server's
        /// router answers a request only if some handler claims it - an unmatched
        /// URL gets NO response at all and the socket sits open until the caller
        /// times out. That is pre-existing and true of every path on it. So a
        /// handler that claimed only the URLs it could render would leave
        /// <c>/alliance-emblem/anything-else</c> hanging, which is a worse failure
        /// than a 404 and a much more confusing one to diagnose. This route claims
        /// its whole prefix and always answers.
        /// </summary>
        internal static bool IsEmblemPath(string? url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            int q = url!.IndexOf('?');
            string path = q >= 0 ? url.Substring(0, q) : url;

            return path.StartsWith(RoutePrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Parses a request for the PNG route.
        ///
        /// <paramref name="allianceId"/> comes back as <see cref="Guid.Empty"/> for
        /// the preview path and for a path segment that is not a guid. The caller
        /// does NOT need it to be one: the code in the query string is the whole
        /// input to the renderer, and the uid in the path is there so the URL is
        /// self-describing in a log and so each alliance gets its own cache entry.
        ///
        /// <paramref name="size"/> is ALWAYS one of <see cref="DownloadSizes"/>.
        /// Junk, a number we do not offer, and no parameter at all all come back
        /// as <see cref="DefaultSize"/> - the same rule the code itself gets, and
        /// for the same reason: this route must answer with a picture, so there is
        /// nothing for a bad size to refuse with. It is meaningful only for
        /// <see cref="Format.Png"/>; a vector has no pixels, so the size on an
        /// .svg request is dropped rather than carried into its ETag, where it
        /// would mint two tags for one document.
        /// </summary>
        internal static bool TryParseRequest(
            string? url, out Guid allianceId, out EmblemArtwork artwork, out bool hasCode,
            out Format format, out int size)
        {
            allianceId = Guid.Empty;
            artwork = default;
            hasCode = false;
            format = Format.Png;
            size = DefaultSize;

            if (string.IsNullOrEmpty(url)) return false;

            string path = url!;
            string query = string.Empty;

            int q = path.IndexOf('?');
            if (q >= 0)
            {
                query = path.Substring(q + 1);
                path = path.Substring(0, q);
            }

            if (!path.StartsWith(RoutePrefix, StringComparison.Ordinal)) return false;

            string name = path.Substring(RoutePrefix.Length);

            // One segment, and it must be the PNG. No traversal check is needed
            // because nothing here ever becomes a file path - the response is
            // rendered from the code, and the name is only ever parsed as a guid -
            // but a second segment still means the caller asked for something this
            // route does not have, and answering it anyway would be answering a
            // URL we never published.
            if (name.Contains('/') || name.Contains('\\')) return false;

            if (name.EndsWith(PngExtension, StringComparison.Ordinal))
            {
                format = Format.Png;
            }
            else if (name.EndsWith(SvgExtension, StringComparison.Ordinal))
            {
                format = Format.Svg;
            }
            else
            {
                return false;
            }

            string id = name.Substring(0, name.Length - 4);

            // The name must be the preview or a real alliance uid, and NOT merely
            // "something that is not a path separator". Nothing here is ever used
            // to build a file path, so this is not a traversal guard - it is a
            // claim boundary: a URL this server never published is left to fall
            // through to the router rather than being answered with a picture.
            if (!string.Equals(id, PreviewId, StringComparison.Ordinal)
                && !Guid.TryParse(id, out allianceId))
            {
                return false;
            }

            if (format == Format.Png)
            {
                string? asked = QueryValue(query, SizeParameter);

                if (asked != null
                    && int.TryParse(asked, NumberStyles.None, CultureInfo.InvariantCulture, out int wanted)
                    && IsOfferedSize(wanted))
                {
                    size = wanted;
                }
            }

            string? code = QueryValue(query, CodeParameter);
            hasCode = !string.IsNullOrEmpty(code);

            if (hasCode && !EmblemArtwork.TryParse(code, out artwork))
            {
                // A code that does not parse is NOT a 404 and not a 400 with an
                // empty body. See EmblemHandler: the client's decoder turns any
                // non-image response into a garbage texture it then displays, so
                // this route must always answer with a picture. The caller renders
                // the alliance's default instead.
                hasCode = false;
            }

            return true;
        }

        /// <summary>
        /// One value out of a query string. Hand-rolled rather than
        /// <c>HttpUtility.ParseQueryString</c> because that lives in
        /// System.Web.HttpUtility and this is the only query string the login
        /// server reads; percent-decoding is applied because a browser will encode
        /// nothing in a code of digits and hyphens but a hand-typed URL might.
        /// </summary>
        private static string? QueryValue(string query, string key)
        {
            foreach (string pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;

                if (!string.Equals(pair.Substring(0, eq), key, StringComparison.Ordinal)) continue;

                return Uri.UnescapeDataString(pair.Substring(eq + 1));
            }

            return null;
        }

        private static string TrimBase(string? baseUrl)
        {
            string text = (baseUrl ?? string.Empty).Trim();
            while (text.EndsWith("/", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1);
            }
            return text;
        }
    }
}

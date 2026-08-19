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

        /// <summary>The filename a downloaded crest is offered under.</summary>
        internal static string VectorFileName(EmblemArtwork artwork) =>
            "alliance-crest-" + artwork.ToCode() + SvgExtension;

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
        /// </summary>
        internal static bool TryParseRequest(
            string? url, out Guid allianceId, out EmblemArtwork artwork, out bool hasCode, out Format format)
        {
            allianceId = Guid.Empty;
            artwork = default;
            hasCode = false;
            format = Format.Png;

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

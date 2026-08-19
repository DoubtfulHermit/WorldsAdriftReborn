using NetCoreServer;
using WorldsAdriftServer.Emblems;

namespace WorldsAdriftServer.Handlers.Emblem
{
    /// <summary>
    /// Serves the alliance crest at
    /// <c>/alliance-emblem/&lt;uid&gt;.png?e=&lt;code&gt;</c>, and the same crest as
    /// downloadable vector art at <c>/alliance-emblem/&lt;uid&gt;.svg?e=&lt;code&gt;</c>.
    ///
    /// THE GAME ONLY EVER GETS THE PNG. The .svg route is for players, and no URL
    /// this server puts in an alliance payload names it - see the note on
    /// <see cref="EmblemUrlPolicy.Format"/> for why handing the client vector art
    /// would be worse than handing it nothing.
    ///
    /// DELIBERATELY UNAUTHENTICATED, because the consumer cannot authenticate.
    /// <c>SpriteDownloader.GetSpriteFromUrl</c> builds a bare
    /// <c>new HTTPRequest(new Uri(url), GET)</c> through
    /// <c>HttpHelper.GenerateRequest</c> - it bypasses
    /// <c>SocialRequest.DecorateRequest</c> entirely and carries neither the
    /// <c>Security</c> header nor a character uid. That is RECOVERED retail
    /// behaviour, not a choice made here, and it is safe because the response
    /// discloses nothing: the picture is a pure function of the code in the URL
    /// the caller already had.
    ///
    /// IT MUST NEVER 404 A URL WE PUBLISHED, and that is the sharpest constraint
    /// on this file. The client's decode path is
    /// <c>HTTPResponse.DataAsTexture2D</c>, which calls
    /// <c>Texture2D.LoadImage</c> and does NOT check its return value; on a
    /// non-image body LoadImage fails silently and leaves a tiny placeholder
    /// texture behind, which <c>Sprite.Create</c> happily wraps. The promise then
    /// resolves a NON-NULL sprite, so the UI's <c>if (sprite != null)</c> guard
    /// passes and the alliance panel replaces its own grey placeholder with
    /// garbage. An error page is therefore worse than any picture: a bad or
    /// missing code renders the alliance's default crest instead.
    /// </summary>
    internal static class EmblemHandler
    {
        /// <summary>
        /// Takes any request in the emblem namespace. Returns true when it
        /// answered, so the router stops.
        /// </summary>
        internal static bool TryHandle(HttpSession session, HttpRequest request)
        {
            // The WHOLE prefix is claimed, not just the URLs that render. This
            // server's router sends no response at all for a path nothing claims -
            // the socket just hangs - so leaving the odd corners of our own
            // namespace unanswered would be worse than any status code.
            if (!EmblemUrlPolicy.IsEmblemPath(request.Url)) return false;

            if (request.Method != "GET" && request.Method != "HEAD")
            {
                Refuse(session, 405, "Emblems are served on GET.");
                return true;
            }

            if (EmblemUrlPolicy.IsCatalogueRequest(request.Url))
            {
                Catalogue(session);
                return true;
            }

            if (!EmblemUrlPolicy.TryParseRequest(
                    request.Url, out Guid allianceId, out EmblemArtwork artwork, out bool hasCode,
                    out EmblemUrlPolicy.Format format))
            {
                // In the namespace but not a crest we could ever have published:
                // a name that is neither an alliance uid nor "preview", a second
                // path segment, a non-.png extension. Answered honestly, and
                // safely - no client can be looking at this URL, because nothing
                // ever put one in an alliance payload.
                Refuse(session, 404, "No such emblem.");
                return true;
            }

            if (!hasCode)
            {
                // No code, or a code the vocabulary no longer parses. The uid in
                // the path is enough to produce the alliance's own default crest;
                // a preview request with no code (uid is empty) gets the one
                // Guid.Empty yields, which is a real emblem and not a blank.
                artwork = EmblemSpec.DefaultFor(allianceId);
            }

            string etag = EmblemImages.ETag(artwork, format);

            if (string.Equals(HeaderValue(request, "If-None-Match"), etag, StringComparison.Ordinal))
            {
                NotModified(session, etag);
                return true;
            }

            HttpResponse resp = new HttpResponse();
            resp.SetBegin(200);

            // A year, immutable. Safe because the code is IN the url: changing an
            // emblem changes the url, so a cached copy can never be the wrong
            // picture. This is the half of the design that lets the client's
            // always-on disk cache work for us instead of against us.
            resp.SetHeader("Cache-Control", "public, max-age=31536000, immutable");
            resp.SetHeader("ETag", etag);
            resp.SetHeader("X-Content-Type-Options", "nosniff");

            if (format == EmblemUrlPolicy.Format.Svg)
            {
                resp.SetHeader("Content-Type", EmblemSvg.ContentType);

                // An SVG is script-capable and this one is served from the same
                // origin as the account page, so it gets a second lock even though
                // it carries no player-supplied byte at all: the document is built
                // entirely from a closed vocabulary of integers.
                resp.SetHeader("Content-Security-Policy", "default-src 'none'; sandbox");

                // inline, not attachment: a leader clicking the link should SEE
                // their crest, and every browser's save-as picks the name up from
                // here anyway.
                resp.SetHeader("Content-Disposition",
                    "inline; filename=\"" + EmblemUrlPolicy.VectorFileName(artwork) + "\"");

                resp.SetBody(artwork.ToSvg());
            }
            else
            {
                resp.SetHeader("Content-Type", "image/png");
                resp.SetBody(EmblemImages.Png(artwork));
            }

            session.SendResponseAsync(resp);
            return true;
        }

        /// <summary>
        /// The editor's object catalogue.
        ///
        /// Cached the same way and for the same reason the pictures are: the
        /// catalogue's own revision is in the URL, so the body at a given address
        /// can never change and a browser may keep it forever. A shape added or
        /// retouched mints a different URL.
        ///
        /// Unauthenticated like the rest of this route, and safe for the same
        /// reason: it is a table of shapes this server drew, identical for every
        /// caller, and it names no player, alliance or account.
        /// </summary>
        private static void Catalogue(HttpSession session)
        {
            HttpResponse resp = new HttpResponse();
            resp.SetBegin(200);
            resp.SetHeader("Content-Type", EmblemEditorData.ContentType);
            resp.SetHeader("Cache-Control", "public, max-age=31536000, immutable");
            resp.SetHeader("ETag", "\"cat-" + EmblemEditorData.Revision + "\"");
            resp.SetHeader("X-Content-Type-Options", "nosniff");
            resp.SetBody(EmblemEditorData.Catalogue);
            session.SendResponseAsync(resp);
        }

        private static void NotModified(HttpSession session, string etag)
        {
            HttpResponse resp = new HttpResponse();
            resp.SetBegin(304);
            resp.SetHeader("ETag", etag);
            resp.SetHeader("Cache-Control", "public, max-age=31536000, immutable");
            resp.SetBody(string.Empty);
            session.SendResponseAsync(resp);
        }

        private static void Refuse(HttpSession session, int status, string message)
        {
            HttpResponse resp = new HttpResponse();
            resp.SetBegin(status);
            resp.SetHeader("Content-Type", "text/plain; charset=utf-8");
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetHeader("Allow", "GET, HEAD");
            resp.SetBody(message);
            session.SendResponseAsync(resp);
        }

        private static string? HeaderValue(HttpRequest request, string name)
        {
            for (int i = 0; i < request.Headers; i++)
            {
                (string header, string value) = request.Header(i);
                if (string.Equals(header, name, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
            return null;
        }
    }
}

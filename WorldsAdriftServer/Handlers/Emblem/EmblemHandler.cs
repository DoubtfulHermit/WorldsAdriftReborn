using NetCoreServer;
using WorldsAdriftServer.Emblems;

namespace WorldsAdriftServer.Handlers.Emblem
{
    /// <summary>
    /// Serves the alliance crest PNG at
    /// <c>/alliance-emblem/&lt;uid&gt;.png?e=&lt;code&gt;</c>.
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

            if (!EmblemUrlPolicy.TryParseRequest(
                    request.Url, out Guid allianceId, out EmblemSpec spec, out bool hasCode))
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
                spec = EmblemSpec.DefaultFor(allianceId);
            }

            string etag = EmblemImages.ETag(spec);

            if (string.Equals(HeaderValue(request, "If-None-Match"), etag, StringComparison.Ordinal))
            {
                NotModified(session, etag);
                return true;
            }

            byte[] png = EmblemImages.Png(spec);

            HttpResponse resp = new HttpResponse();
            resp.SetBegin(200);
            resp.SetHeader("Content-Type", "image/png");

            // A year, immutable. Safe because the code is IN the url: changing an
            // emblem changes the url, so a cached copy can never be the wrong
            // picture. This is the half of the design that lets the client's
            // always-on disk cache work for us instead of against us.
            resp.SetHeader("Cache-Control", "public, max-age=31536000, immutable");
            resp.SetHeader("ETag", etag);
            resp.SetHeader("X-Content-Type-Options", "nosniff");
            resp.SetBody(png);

            session.SendResponseAsync(resp);
            return true;
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

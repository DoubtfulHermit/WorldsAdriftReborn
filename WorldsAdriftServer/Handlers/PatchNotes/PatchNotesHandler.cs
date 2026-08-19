using NetCoreServer;
using WorldsAdriftServer.PatchNotes;
using WorldsAdriftServer.Web;

namespace WorldsAdriftServer.Handlers.PatchNotes
{
    /// <summary>
    /// The public patch notes' HTTP surface: <c>/patchnotes</c> and
    /// <c>/patchnotes/source</c>, both unauthenticated by design.
    ///
    /// It CLAIMS THE WHOLE PREFIX, the way the public map's handler does, and for
    /// the reason spelled out in <see cref="PatchNotesRoutes"/>: this server
    /// simply does not reply to a URL nothing recognises, so a mistyped path
    /// under a route we advertise would hang the browser instead of 404ing. Every
    /// URL beneath <c>/patchnotes</c> is answered here.
    ///
    /// Nothing on this surface reads a cookie, issues a session, or can reach the
    /// admin command bridge. It serves one document that the operator can see in
    /// the panel before anyone else sees it here.
    ///
    /// Glue only: routing in <see cref="PatchNotesRoutes"/>, the text in
    /// <see cref="PatchNotesSource"/>, the grammar in
    /// <see cref="PatchNotesDocument"/>, the markup in
    /// <see cref="PatchNotesHtml"/> - each pure and separately tested.
    /// </summary>
    internal static class PatchNotesHandler
    {
        internal static bool TryHandle(HttpSession session, HttpRequest request)
        {
            PatchNotesRoute route = PatchNotesRoutes.Match(request.Method, request.Url);
            bool headOnly = request.Method == "HEAD";

            switch (route)
            {
                case PatchNotesRoute.None:
                    return false;

                case PatchNotesRoute.Page:
                    // Not cached. The whole point of the operator override is that
                    // a correction is live on the next load; a proxy holding the
                    // previous text for an hour would undo that. The page is a few
                    // kilobytes of already-composed string.
                    Send(session, 200, PatchNotesPage.ContentType,
                        PatchNotesPage.Html(PatchNotesSource.Current()), "no-cache", headOnly);
                    return true;

                case PatchNotesRoute.Source:
                    // The same notes as plain text, for anyone who would rather
                    // read them in a terminal or diff two days of them.
                    Send(session, 200, "text/plain; charset=utf-8",
                        PatchNotesSource.Current(), "no-cache", headOnly);
                    return true;

                default:
                    Send(session, 404, "text/plain; charset=utf-8",
                        "Not found. The patch notes are at /patchnotes.", "no-store", headOnly);
                    return true;
            }
        }

        private static void Send(HttpSession session, int status, string contentType,
            string body, string cacheControl, bool headOnly)
        {
            HttpResponse resp = new HttpResponse();
            resp.SetBegin(status);
            resp.SetHeader("Content-Type", contentType);
            resp.SetHeader("Cache-Control", cacheControl);
            resp.SetHeader("X-Content-Type-Options", "nosniff");
            resp.SetHeader("Referrer-Policy", "no-referrer");
            resp.SetBody(headOnly ? string.Empty : body);
            session.SendResponseAsync(resp);
        }
    }
}

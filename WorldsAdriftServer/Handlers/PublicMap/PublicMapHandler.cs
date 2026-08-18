using NetCoreServer;
using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Web;
using WorldsAdriftServer.PublicMap;

namespace WorldsAdriftServer.Handlers.PublicMap
{
    /// <summary>
    /// The public map's HTTP surface: /map, /map/data and /map/world, all
    /// unauthenticated by design.
    ///
    /// This is a SEPARATE class from <see cref="Admin.AdminHandler"/> on
    /// purpose, so the auth boundary is structural rather than an if-statement:
    /// nothing in this file reads a cookie, issues a session, or can reach the
    /// admin command bridge, and everything it serves has already passed the
    /// whitelist in <see cref="PublicMapProjection"/>. An accidental route
    /// added here can expose at most what the projection carries.
    ///
    /// Glue only. The routing decision lives in <see cref="PublicMapRoutes"/>,
    /// the data boundary in <see cref="PublicMapProjection"/>, the load
    /// shedding in <see cref="PublicMapCache"/> - each pure and separately
    /// tested. The raw stats file is never served: every /map/data response is
    /// the projected payload, freshly built at most once per cache TTL.
    /// </summary>
    internal static class PublicMapHandler
    {
        private static readonly PublicMapCache Cache = new PublicMapCache();

        /// <summary>
        /// Handles any /map* request; returns true when it took the request.
        /// Every path under /map is answered here - unknown ones with a 404 -
        /// so nothing can fall through this namespace to another handler.
        /// </summary>
        internal static bool TryHandle(HttpSession session, HttpRequest request)
        {
            PublicMapRoute route = PublicMapRoutes.Match(request.Method, request.Url);
            bool headOnly = request.Method == "HEAD";
            switch (route)
            {
                case PublicMapRoute.None:
                    return false;

                case PublicMapRoute.Page:
                    // Short cache: the page shell is static per build, but a
                    // deploy should propagate within a minute.
                    Send(session, 200, PublicMapPage.ContentType, PublicMapPage.Html,
                        "public, max-age=60", headOnly);
                    return true;

                case PublicMapRoute.LiveData:
                    Send(session, 200, "application/json", LivePayload(DateTimeOffset.UtcNow),
                        "public, max-age=2", headOnly);
                    return true;

                case PublicMapRoute.WorldData:
                    // The preserved-release catalogue: static for the lifetime
                    // of a build, and by far the heavier payload, so browsers
                    // and the fronting proxy may keep it for an hour.
                    Send(session, 200, "application/json", ReleaseWorldMap.Json,
                        "public, max-age=3600", headOnly);
                    return true;

                default:
                    Send(session, 404, "text/plain; charset=utf-8", "Not found",
                        "no-store", headOnly);
                    return true;
            }
        }

        /// <summary>
        /// The anonymized live payload, rebuilt at most once per
        /// <see cref="PublicMapCache.Ttl"/> no matter how many viewers poll.
        /// </summary>
        private static string LivePayload(DateTimeOffset now)
        {
            if (Cache.TryGet(now, out string cached))
            {
                return cached;
            }

            GameStatsResult stats = GameStats.Read(now);
            JObject projected = PublicMapProjection.Project(stats, PublicMapProjection.ProcessSalt);
            string payload = PublicMapProjection.Serialize(projected);
            Cache.Store(payload, now);
            return payload;
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
            // Deliberately open CORS: this surface is credential-free and
            // read-only, and the projection is the privacy boundary. Anyone
            // may embed the public feed; there is no session to ride.
            resp.SetHeader("Access-Control-Allow-Origin", "*");
            resp.SetBody(headOnly ? string.Empty : body);
            session.SendResponseAsync(resp);
        }
    }
}

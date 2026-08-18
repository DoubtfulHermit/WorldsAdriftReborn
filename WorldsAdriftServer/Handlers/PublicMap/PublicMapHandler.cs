using NetCoreServer;
using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Web;
using WorldsAdriftServer.PublicMap;

namespace WorldsAdriftServer.Handlers.PublicMap
{
    /// <summary>
    /// The public map's HTTP surface: /map, /map/data, /map/world and
    /// /map/viewers, all unauthenticated by design.
    ///
    /// Note what this file does NOT do with a request, which is the whole of the
    /// viewer count's privacy argument at the HTTP layer: it never asks the
    /// session for its remote endpoint, never reads a User-Agent, a Referer, a
    /// Cookie or an X-Forwarded-For, and never writes a line about a request
    /// anywhere. The only thing a request contributes to the count is an
    /// ephemeral token the PAGE minted for itself (see
    /// <see cref="PublicMap.ViewerToken"/>), and the only place it goes is a
    /// salted-hash census that forgets it after thirty seconds.
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
        /// The viewer trend, cached for the sampling interval: the rows behind it
        /// only change once a minute, so a shorter window would buy nothing and
        /// spend a database query per unauthenticated request.
        /// </summary>
        private static readonly PublicMapCache ViewersCache =
            new PublicMapCache(ViewerSampler.Interval);

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
                    // The live payload is embedded for the first paint, the
                    // same way the console bootstraps itself, so the map is
                    // drawn on arrival rather than flashing empty. That makes
                    // the page as fresh as the snapshot inside it, so it is
                    // NOT cached - the world catalogue beside it, which is the
                    // heavy part, is cached for an hour instead.
                    Send(session, 200, PublicMapPage.ContentType,
                        PublicMapPage.Html(LivePayload(DateTimeOffset.UtcNow), ReleaseWorldMap.Json),
                        "no-cache", headOnly);
                    return true;

                case PublicMapRoute.LiveData:
                {
                    // The poll doubles as the viewer heartbeat. The page appends
                    // its ephemeral per-load token as ?v=...; anything else - a
                    // third party embedding the open feed, a crawler, curl - has
                    // no token, is simply not counted, and still gets the data.
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    string? token = ViewerToken.FromUrl(request.Url);
                    if (token != null)
                    {
                        ViewerCensus.Shared.Beat(token, now);
                    }

                    // A heartbeat that a cache can answer is not a heartbeat, so a
                    // tokened request is never stored. The token-free form keeps
                    // its two-second cache, which is what the open feed is for.
                    Send(session, 200, "application/json", LivePayload(now),
                        token != null ? "no-store" : "public, max-age=2", headOnly);
                    return true;
                }

                case PublicMapRoute.Viewers:
                    // The trend readout behind the About panel. Aggregate counts
                    // only - see ViewerHistory - and cached for a minute because
                    // that is the sampling interval: asking more often cannot
                    // produce a different answer.
                    Send(session, 200, "application/json", ViewersPayload(DateTimeOffset.UtcNow),
                        "public, max-age=60", headOnly);
                    return true;

                case PublicMapRoute.ShipGeometry:
                    // CONTENT-ADDRESSED, so it may be cached properly. The page
                    // puts the drawing's revision in the query, and a revision
                    // is a hash of the drawing - so this URL names a SHAPE, not
                    // a ship, and the answer for it cannot go stale. Mount a
                    // lamp and the page asks a different URL.
                    //
                    // Keyed on the SHIP instead, any cache at all would be a
                    // lie for as long as it lasted. That was the first cut, and
                    // a headless run that moved a helm on the server caught the
                    // browser going on drawing the old one out of its own HTTP
                    // cache - which is what put the revision in the query.
                    //
                    // Not cached server-side: it is one small object built from
                    // a snapshot already in hand, and it is asked for when a
                    // reader opens a ship rather than on a timer.
                    Send(session, 200, "application/json",
                        PublicMapProjection.Serialize(ShipGeometryEndpoint.ForPublic(
                            GameStats.Read(DateTimeOffset.UtcNow),
                            ShipGeometryEndpoint.Selector(request.Url, ShipGeometryEndpoint.TokenKey),
                            PublicMapProjection.ProcessSalt)),
                        "public, max-age=300", headOnly);
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
            JObject projected = PublicMapProjection.Project(
                stats, PublicMapProjection.ProcessSalt, ViewerCensus.Shared.Count(now));
            string payload = PublicMapProjection.Serialize(projected);
            Cache.Store(payload, now);
            return payload;
        }

        /// <summary>
        /// The public trend payload: the live count, the day's peak, and a day of
        /// ten-minute buckets.
        ///
        /// Cached for the sampling interval, because a fresher answer does not
        /// exist - the underlying rows only change once a minute - and because
        /// this is the one public route that touches the database, so it must not
        /// be a way to make an unauthenticated poll into a query per request.
        ///
        /// A database that is unreachable degrades to "the live count and a flat
        /// line", not to a 500: the number in the strip comes from memory and is
        /// still true, and a broken sparkline is not a reason to fail the page.
        /// </summary>
        private static string ViewersPayload(DateTimeOffset now)
        {
            if (ViewersCache.TryGet(now, out string cached))
            {
                return cached;
            }

            int live = ViewerCensus.Shared.Count(now);
            DateTimeOffset to = ViewerHistory.FloorTo(now, ViewerHistory.PublicStep)
                + ViewerHistory.PublicStep;
            DateTimeOffset from = to - ViewerHistory.PublicStep * ViewerHistory.PublicBuckets;

            IReadOnlyList<(DateTimeOffset At, int Count)> samples;
            try
            {
                samples = Persistence.Accounts.ViewerSamples.Between(from, to);
            }
            catch (Exception)
            {
                samples = Array.Empty<(DateTimeOffset, int)>();
            }

            string payload = PublicMapProjection.Serialize(ViewerHistory.Payload(
                live, samples, from, ViewerHistory.PublicStep, ViewerHistory.PublicBuckets));
            ViewersCache.Store(payload, now);
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

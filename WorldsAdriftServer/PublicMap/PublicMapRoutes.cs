namespace WorldsAdriftServer.PublicMap
{
    /// <summary>
    /// Which public-map route, if any, a request is. Pure string policy split
    /// out of the handler so the routing table - the thing that decides what
    /// is reachable WITHOUT authentication - is unit-testable on its own.
    ///
    /// The whole /map namespace is claimed, including unknown paths and
    /// non-GET methods: nothing under /map may ever fall through to another
    /// handler, so a future route added elsewhere cannot accidentally answer
    /// underneath the public prefix.
    /// </summary>
    internal enum PublicMapRoute
    {
        /// <summary>Not a /map URL - not ours, let the router continue.</summary>
        None,

        /// <summary>GET /map - the public page.</summary>
        Page,

        /// <summary>GET /map/data - the anonymized live payload.</summary>
        LiveData,

        /// <summary>GET /map/world - the static preserved-world catalogue.</summary>
        WorldData,

        /// <summary>
        /// GET /map/viewers - how many people have the map open, now and over the
        /// last day, as counts and nothing else.
        /// </summary>
        Viewers,

        /// <summary>Anything else under /map - answered 404, never forwarded.</summary>
        NotFound,
    }

    internal static class PublicMapRoutes
    {
        internal static PublicMapRoute Match(string method, string url)
        {
            string path = url;
            int q = path.IndexOf('?');
            if (q >= 0)
            {
                path = path.Substring(0, q);
            }

            bool isRoot = path == "/map" || path == "/map/";
            if (!isRoot && !path.StartsWith("/map/", StringComparison.Ordinal))
            {
                return PublicMapRoute.None;
            }

            if (method != "GET" && method != "HEAD")
            {
                return PublicMapRoute.NotFound;
            }

            if (isRoot)
            {
                return PublicMapRoute.Page;
            }

            return path switch
            {
                "/map/data" => PublicMapRoute.LiveData,
                "/map/world" => PublicMapRoute.WorldData,
                "/map/viewers" => PublicMapRoute.Viewers,
                _ => PublicMapRoute.NotFound,
            };
        }
    }
}

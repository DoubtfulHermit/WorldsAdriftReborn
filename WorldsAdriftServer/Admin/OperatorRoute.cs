namespace WorldsAdriftServer.Admin
{
    /// <summary>The operator command endpoints this server serves.</summary>
    internal enum OperatorRouteKind
    {
        /// <summary>Not an operator route at all.</summary>
        None = 0,

        /// <summary>GET /admin/api/operator/targets - the roster and vocabulary a GUI needs.</summary>
        Targets,

        /// <summary>POST /admin/api/operator/teleport</summary>
        Teleport,

        /// <summary>POST /admin/api/operator/summon-ship</summary>
        SummonShip,
    }

    /// <summary>
    /// A parsed operator request: which endpoint, and whether the URL is in the
    /// operator namespace at all.
    ///
    /// The second question is not the same as the first and is why this is a type
    /// rather than a switch inline in the handler. An unknown path under
    /// <c>/admin/</c> is somebody probing and is answered with the login page; an
    /// unknown path under <c>/admin/api/operator/</c> is a GUI calling an endpoint
    /// that does not exist, and it has to get a JSON refusal naming the endpoints
    /// that DO exist - a login page arriving where JSON was expected is the least
    /// debuggable failure this surface can produce.
    ///
    /// Pure: a method and a URL in, a route out. No session, no request object.
    /// </summary>
    internal sealed class OperatorRoute
    {
        /// <summary>The path prefix every operator endpoint lives under.</summary>
        internal const string Prefix = "/admin/api/operator/";

        internal OperatorRouteKind Kind { get; }

        private OperatorRoute(OperatorRouteKind kind) => Kind = kind;

        private static readonly OperatorRoute NoMatch = new OperatorRoute(OperatorRouteKind.None);

        /// <summary>
        /// Whether this path is in the operator namespace, whether or not it names
        /// a route that exists.
        /// </summary>
        internal static bool IsOperatorPath(string? path) =>
            (path ?? string.Empty).StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>
        /// Resolves a method and a query-stripped path to a route.
        ///
        /// The METHOD is part of the match, not checked afterwards: a GET to
        /// /teleport must not resolve to the teleport route and then be refused
        /// for its verb, because the two produce different refusals and only one of
        /// them tells a GUI author what they actually did wrong.
        /// </summary>
        internal static OperatorRoute Parse(string? method, string? path)
        {
            string verb = (method ?? string.Empty).ToUpperInvariant();
            string text = path ?? string.Empty;

            if (!IsOperatorPath(text)) return NoMatch;

            string tail = text.Substring(Prefix.Length).Trim('/');

            return (verb, tail) switch
            {
                ("GET", "targets") => new OperatorRoute(OperatorRouteKind.Targets),
                ("POST", "teleport") => new OperatorRoute(OperatorRouteKind.Teleport),
                ("POST", "summon-ship") => new OperatorRoute(OperatorRouteKind.SummonShip),
                _ => NoMatch,
            };
        }

        /// <summary>
        /// The verb+path of every route, for the refusal that lists them. Kept next
        /// to the matcher so the two cannot disagree.
        /// </summary>
        internal static IReadOnlyList<string> Catalogue { get; } = new[]
        {
            "GET " + Prefix + "targets",
            "POST " + Prefix + "teleport",
            "POST " + Prefix + "summon-ship",
        };

        /// <summary>The action label this route's journal entries and results carry.</summary>
        internal static string ActionOf(OperatorRouteKind kind) => kind switch
        {
            OperatorRouteKind.Targets => "operator-targets",
            OperatorRouteKind.Teleport => "operator-teleport",
            OperatorRouteKind.SummonShip => "operator-summon-ship",
            _ => "operator",
        };
    }
}

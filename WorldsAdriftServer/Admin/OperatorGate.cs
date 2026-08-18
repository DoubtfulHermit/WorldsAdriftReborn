using Newtonsoft.Json.Linq;

namespace WorldsAdriftServer.Admin
{
    /// <summary>
    /// Everything an operator endpoint decides BEFORE it touches the game server:
    /// which route this is, whether the caller may have it, and - if not - a
    /// refusal already labelled with the route it refused.
    ///
    /// It is a separate type for the reason <see cref="Social.SocialGate"/> spells
    /// out at length: the handler needs an HttpSession and the live admin session
    /// set to run at all, so anything decided inside it is decided behind a seam no
    /// test can reach. Here the decision is pure - flags in, an envelope or a route
    /// out - and every combination of "no session", "no confirmation header", "bad
    /// CSRF" and "unknown path" can be asserted with no socket.
    ///
    /// ORDER MATTERS and it is the same order for the same reason: the ROUTE is
    /// parsed first, so an auth failure knows which endpoint it is refusing and can
    /// say so. A refusal labelled "operator" when the caller asked for
    /// "operator-teleport" is a refusal a dashboard cannot attribute to the button
    /// that produced it.
    /// </summary>
    internal static class OperatorGate
    {
        internal readonly struct Decision
        {
            /// <summary>The parsed route. Always meaningful, including on a refusal.</summary>
            internal OperatorRouteKind Kind { get; }

            /// <summary>The ready-to-send refusal, or null when the request may proceed.</summary>
            internal JObject? Refusal { get; }

            /// <summary>The HTTP status the refusal is sent with. 0 when serving.</summary>
            internal int Status { get; }

            internal bool Serve => Refusal == null;

            private Decision(OperatorRouteKind kind, JObject? refusal, int status)
            {
                Kind = kind;
                Refusal = refusal;
                Status = status;
            }

            internal static Decision Allow(OperatorRouteKind kind) =>
                new Decision(kind, null, 0);

            /// <summary>A refusal whose envelope was built elsewhere.</summary>
            internal static Decision RefuseWith(OperatorRouteKind kind, JObject refusal, int status) =>
                new Decision(kind, refusal, status);

            internal static Decision Refuse(OperatorRouteKind kind, string code, string reason) =>
                new Decision(
                    kind,
                    OperatorRefusal.Refuse(OperatorRoute.ActionOf(kind), code, reason),
                    OperatorRefusal.StatusFor(code));
        }

        /// <summary>
        /// Decides one operator request.
        ///
        /// <paramref name="hasConfirmationHeader"/> is the <c>X-Wareborn-Admin: 1</c>
        /// header the existing command endpoint already requires. It is not a
        /// secret and is not pretending to be one: it is a NON-SIMPLE header, which
        /// forces any cross-origin caller to preflight, and this server exposes no
        /// CORS permission - so another site cannot ride an operator's cookie to
        /// teleport a player. The CSRF token is the actual defence; this is the
        /// belt to its braces, and it is required on the read route too because a
        /// roster of who is online and where they are standing is not something
        /// another origin should be able to pull either.
        /// </summary>
        internal static Decision Evaluate(
            string? method,
            string? path,
            bool authenticated,
            bool hasConfirmationHeader,
            bool csrfValid)
        {
            OperatorRoute route = OperatorRoute.Parse(method, path);

            if (route.Kind == OperatorRouteKind.None)
            {
                // Answered in band and as JSON. A caller here is a program, and the
                // /admin fallback (an HTML login page on a 200) would reach it as a
                // parse error with no clue in it.
                return Decision.RefuseWith(
                    OperatorRouteKind.None,
                    OperatorRefusal.UnknownRoute(),
                    OperatorRefusal.StatusFor(OperatorErrorCodes.UnknownRoute));
            }

            if (!authenticated)
            {
                return Decision.Refuse(
                    route.Kind, OperatorErrorCodes.Unauthenticated,
                    "Sign in to the operator panel first.");
            }

            if (!hasConfirmationHeader)
            {
                return Decision.Refuse(
                    route.Kind, OperatorErrorCodes.Forbidden,
                    "The X-Wareborn-Admin confirmation header is missing.");
            }

            if (!csrfValid)
            {
                return Decision.Refuse(
                    route.Kind, OperatorErrorCodes.Forbidden,
                    "The session-bound CSRF token is missing or invalid.");
            }

            return Decision.Allow(route.Kind);
        }
    }
}

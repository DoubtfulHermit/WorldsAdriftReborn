using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Policy;

namespace WorldsAdriftServer.Admin
{
    /// <summary>
    /// Everything the welcome-message admin routes decide BEFORE they touch the
    /// database: may this caller read the greeting, may this caller replace it,
    /// and is the replacement a string we will store.
    ///
    /// It is a separate pure type for the same reason <see cref="OperatorGate"/>
    /// is: the handler needs an HttpSession and the live admin session set to run
    /// at all, so a guard written inline there is a guard no test can reach. Here
    /// the decision is flags in, an answer out, and every combination of "no
    /// session", "no confirmation header" and "bad CSRF" is assertable with no
    /// socket.
    ///
    /// ORDER MATTERS and it is the same order the existing command endpoint uses
    /// (AdminHandler.HandleAdminCommand): session first, then the confirmation
    /// header, then the CSRF token. A caller who is not signed in should be told
    /// that, not told its CSRF token is wrong.
    /// </summary>
    internal static class WelcomeMessageGate
    {
        internal readonly struct Decision
        {
            /// <summary>The ready-to-send refusal body, or null when the request may proceed.</summary>
            internal string? Refusal { get; }

            /// <summary>The HTTP status the refusal is sent with. 0 when serving.</summary>
            internal int Status { get; }

            internal bool Serve => Refusal == null;

            private Decision(string? refusal, int status)
            {
                Refusal = refusal;
                Status = status;
            }

            internal static Decision Allow() => new Decision(null, 0);

            internal static Decision Refuse(int status, string error, string message) =>
                new Decision(
                    new JObject
                    {
                        ["error"] = error,
                        ["message"] = message,
                    }.ToString(Formatting.None),
                    status);
        }

        /// <summary>
        /// Reading the greeting from the panel. Gated on the operator session
        /// only - no confirmation header, no CSRF - because it changes nothing
        /// and the very same string is served unauthenticated at
        /// /welcomeMessage. Requiring more here would be theatre.
        /// </summary>
        internal static Decision EvaluateRead(bool authenticated)
        {
            return authenticated
                ? Decision.Allow()
                : Decision.Refuse(401, "unauthenticated", "Sign in to the operator panel first.");
        }

        /// <summary>
        /// Replacing the greeting. All three checks, in the command endpoint's
        /// order.
        ///
        /// <paramref name="hasConfirmationHeader"/> is <c>X-Wareborn-Admin: 1</c>.
        /// It is not a secret and is not pretending to be one: it is a NON-SIMPLE
        /// header, so a cross-origin caller must preflight, and this server
        /// exposes no CORS permission - another site therefore cannot ride an
        /// operator's cookie to rewrite what every player reads on arrival. The
        /// CSRF token is the actual defence; this is the belt to its braces.
        /// </summary>
        internal static Decision EvaluateWrite(bool authenticated, bool hasConfirmationHeader,
            bool csrfValid)
        {
            if (!authenticated)
            {
                return Decision.Refuse(401, "unauthenticated",
                    "Sign in to the operator panel first.");
            }

            if (!hasConfirmationHeader)
            {
                return Decision.Refuse(403, "forbidden",
                    "The X-Wareborn-Admin confirmation header is missing.");
            }

            if (!csrfValid)
            {
                return Decision.Refuse(403, "forbidden",
                    "The session-bound CSRF token is missing or invalid.");
            }

            return Decision.Allow();
        }

        /// <summary>
        /// Whether a submitted body is a message we will store, and - when it is
        /// not - the refusal that names the problem rather than a bare 400. The
        /// operator is typing prose into a textarea; "invalid" alone would leave
        /// them guessing between "I left it empty" and "I pasted a novel".
        ///
        /// Blankness is refused here as well as by the table's CHECK constraint,
        /// so a blank never reaches the database as an exception.
        /// </summary>
        internal static Decision EvaluateBody(string? message)
        {
            if (message == null)
            {
                return Decision.Refuse(400, "invalid",
                    "The request body must be JSON with a \"message\" string.");
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return Decision.Refuse(400, "invalid",
                    "The welcome message cannot be empty.");
            }

            if (!ServerConfigPolicy.IsValidWelcomeMessage(message))
            {
                return Decision.Refuse(400, "invalid",
                    "The welcome message is longer than "
                    + ServerConfigPolicy.MaxWelcomeMessageLength + " characters.");
            }

            return Decision.Allow();
        }
    }
}

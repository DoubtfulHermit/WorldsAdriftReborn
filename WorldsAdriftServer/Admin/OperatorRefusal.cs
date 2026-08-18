using Newtonsoft.Json.Linq;

namespace WorldsAdriftServer.Admin
{
    /// <summary>
    /// The machine-readable reasons an operator command is refused. A GUI switches
    /// on these; a human reads the sentence next to them.
    ///
    /// They exist as constants and not as HTTP statuses because the two answer
    /// different questions. 409 covers "the world says no" for six unrelated
    /// reasons, and a GUI that wants to grey out a button, re-fetch a roster, or
    /// pop a confirmation needs to tell them apart.
    /// </summary>
    internal static class OperatorErrorCodes
    {
        /// <summary>No admin session.</summary>
        public const string Unauthenticated = "unauthenticated";

        /// <summary>Session present, but the CSRF or confirmation header is not.</summary>
        public const string Forbidden = "forbidden";

        /// <summary>The URL is under /admin/api/operator/ but names nothing.</summary>
        public const string UnknownRoute = "unknown_route";

        /// <summary>The body was not readable as this endpoint's parameters.</summary>
        public const string BadRequest = "bad_request";

        /// <summary>A selector, destination or hull could not be parsed.</summary>
        public const string BadTarget = "bad_target";

        /// <summary>A well-formed selector that matches nobody.</summary>
        public const string TargetNotFound = "target_not_found";

        /// <summary>A well-formed selector that matches more than one player.</summary>
        public const string TargetAmbiguous = "target_ambiguous";

        /// <summary>The game server's status file is missing, unreadable or stale.</summary>
        public const string GameUnavailable = "game_unavailable";

        /// <summary>The bridge is still holding a command the game server has not taken.</summary>
        public const string Busy = "busy";
    }

    /// <summary>
    /// The one shape every operator response takes, refusal or not.
    ///
    /// A single envelope on BOTH outcomes is a deliberate choice and the same one
    /// <see cref="Social.SocialRefusal"/> reasons about from the other direction:
    /// there, the client had two parsers and the refusal had to be built for
    /// whichever one would read it. Here the reader is a dashboard we also write,
    /// so the leverage is the opposite - make every answer identical in shape so
    /// the GUI needs exactly one code path, and make <c>ok</c> the only field it
    /// has to branch on.
    ///
    /// <code>
    ///   { "ok": true,  "action": "...", "message": "...",
    ///     "target": "...", "warnings": [ "..." ] }
    ///   { "ok": false, "action": "...", "code": "...", "reason": "...",
    ///     "target": "..." }
    /// </code>
    ///
    /// <c>reason</c> is always a sentence an operator can act on, never a code and
    /// never an exception's ToString. That is not politeness: the operator is the
    /// only person who can fix any of these, and the thing they need is the NEXT
    /// STEP - refresh the list, name the hull, widen the rollout - which a code
    /// cannot carry.
    /// </summary>
    internal static class OperatorRefusal
    {
        internal static JObject Refuse(string action, string code, string reason, string? target = null)
        {
            JObject refusal = new JObject
            {
                ["ok"] = false,
                ["action"] = action,
                ["code"] = code,
                ["reason"] = reason,
            };
            if (!string.IsNullOrEmpty(target)) refusal["target"] = target;
            return refusal;
        }

        internal static JObject Accept(
            string action, string message, string? target = null,
            IReadOnlyList<string>? warnings = null)
        {
            JObject accepted = new JObject
            {
                ["ok"] = true,
                ["action"] = action,
                ["message"] = message,
            };
            if (!string.IsNullOrEmpty(target)) accepted["target"] = target;
            accepted["warnings"] = new JArray(warnings ?? Array.Empty<string>());
            return accepted;
        }

        /// <summary>
        /// The HTTP status a code is answered with. Kept here rather than at each
        /// call site so a code cannot be a 400 on one route and a 409 on another.
        /// </summary>
        internal static int StatusFor(string code) => code switch
        {
            OperatorErrorCodes.Unauthenticated => 401,
            OperatorErrorCodes.Forbidden => 403,
            OperatorErrorCodes.UnknownRoute => 404,
            OperatorErrorCodes.BadRequest => 400,
            OperatorErrorCodes.BadTarget => 400,
            OperatorErrorCodes.TargetNotFound => 409,
            OperatorErrorCodes.TargetAmbiguous => 409,
            OperatorErrorCodes.GameUnavailable => 503,
            OperatorErrorCodes.Busy => 409,
            _ => 400,
        };

        /// <summary>
        /// The refusal for a path in the operator namespace that names no route. It
        /// LISTS the endpoints, because the caller is a program and the author of
        /// that program is the person who needs this answer.
        /// </summary>
        internal static JObject UnknownRoute() => Refuse(
            "operator",
            OperatorErrorCodes.UnknownRoute,
            "No such operator endpoint. This server serves: "
            + string.Join(", ", OperatorRoute.Catalogue) + ".");
    }
}

using Newtonsoft.Json.Linq;

namespace WorldsAdriftServer.Social
{
    /// <summary>
    /// The shape a refusal has to take for the route that will read it.
    ///
    /// This client does not have one response parser, it has two, and they
    /// disagree about where a failure's text lives:
    ///
    ///  - the general one (SocialRequest.CheckResponseModelForErrors) reads
    ///    <c>errorCode</c> and looks it up in the client's closed
    ///    ServerErrorCodesTable;
    ///  - the character-search one (SocialRequest.CheckSearchResponseModelForErrors,
    ///    :114-119) ignores <c>errorCode</c> entirely and throws
    ///    <c>SocialServerResponseErrorException(model.desc)</c>.
    ///
    /// So a refusal carrying only <c>errorCode</c> reaches a searching player as a
    /// dialog with a NULL message. SocialService already got this right for the
    /// search endpoint's own "no such name" answer; what it could not fix is the
    /// handler's SHARED refusal path - auth failures, unimplemented routes, and
    /// the catch-all around a database fault - which refused identically for
    /// every route because it ran before the route was even parsed.
    ///
    /// Hence this type: the refusal is built for the reader, not for the server's
    /// convenience. Kept pure - no session, no request, no clock - so every route
    /// and code pair can be asserted directly.
    /// </summary>
    internal static class SocialRefusal
    {
        /// <summary>
        /// Whether this route's client parser reads <c>desc</c> rather than
        /// <c>errorCode</c>.
        ///
        /// Exactly one route does, and it is enumerated rather than defaulted:
        /// a new route added later must be a deliberate decision here, not a
        /// silent inheritance of whichever shape happened to be first.
        /// </summary>
        internal static bool ReadsDescription(SocialRouteKind kind)
        {
            return kind == SocialRouteKind.CharacterSearch;
        }

        /// <summary>
        /// A refusal in the shape the given route can actually read.
        /// </summary>
        internal static JObject For(SocialRouteKind kind, string errorCode)
        {
            return ReadsDescription(kind)
                ? SocialWire.CharacterNotFound(Sentence(errorCode))
                : SocialEnvelope.Error(errorCode);
        }

        /// <summary>
        /// One sentence per error code, for the readers that print the text
        /// instead of looking the code up.
        ///
        /// Deliberately plain and actionable: this is shown to the player
        /// verbatim, with no table to translate it, so a code leaking through
        /// here would read as debug output.
        /// </summary>
        internal static string Sentence(string errorCode)
        {
            switch (errorCode)
            {
                case SocialErrorCodes.NoAuthToken:
                case SocialErrorCodes.AuthFailed:
                    return "Your session is no longer valid. Return to the character screen and back to search again.";

                case SocialErrorCodes.StoreUnavailable:
                    return "Could not reach the player directory. Please try again in a moment.";

                case SocialErrorCodes.InvalidName:
                    return "That is not a name this server can look up.";

                case SocialErrorCodes.InvalidEntityId:
                    return "That player could not be found.";

                default:
                    return "The search could not be completed. Please try again in a moment.";
            }
        }
    }
}

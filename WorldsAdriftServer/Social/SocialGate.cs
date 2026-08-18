using Newtonsoft.Json.Linq;

namespace WorldsAdriftServer.Social
{
    /// <summary>
    /// Everything the social endpoint decides BEFORE it touches the database:
    /// which route this is, whether the caller may have it, and - if not - what
    /// shape the refusal has to take for that particular route to be able to read
    /// it.
    ///
    /// It is a separate type because that is precisely the seam two shipped bugs
    /// lived in and no test could reach. SocialHandler needed an HttpSession and
    /// the static Postgres-backed repositories to run at all, so the only social
    /// tests that existed called SocialService directly with the actor already
    /// resolved - behind this logic, not through it. The route-blind refusal that
    /// sent a null message to searching players was invisible from there.
    ///
    /// So the decision is pure: strings and flags in, an envelope or a route out.
    /// No session, no request object, no repositories, no clock. The handler
    /// keeps only the part that genuinely needs I/O - reading two headers and
    /// looking up the account - and this can be asserted exhaustively with no
    /// database at all.
    /// </summary>
    internal static class SocialGate
    {
        internal readonly struct Decision
        {
            /// <summary>
            /// The parsed route. Always set, including on a refusal - the caller
            /// logs off it, and the refusal's own shape was chosen from it.
            /// SocialRoute is a class, so a default here would be null and the
            /// first Kind read on any refusal would throw.
            /// </summary>
            internal SocialRoute Route { get; }

            /// <summary>The authenticated character. Meaningful only when <see cref="Serve"/>.</summary>
            internal Guid Character { get; }

            /// <summary>The ready-to-send refusal, or null when the request may proceed.</summary>
            internal JObject? Refusal { get; }

            internal bool Serve => Refusal == null;

            private Decision(SocialRoute route, Guid character, JObject? refusal)
            {
                Route = route;
                Character = character;
                Refusal = refusal;
            }

            internal static Decision Allow(SocialRoute route, Guid character) =>
                new Decision(route, character, null);

            internal static Decision Refuse(SocialRoute route, JObject refusal) =>
                new Decision(route, Guid.Empty, refusal);
        }

        /// <summary>
        /// Parses the route, then authorises, then refuses in the route's own
        /// dialect if either fails.
        ///
        /// Order matters and is the fix for the second defect: this used to
        /// authorise FIRST and parse the route afterwards, so an auth failure had
        /// no idea which endpoint it was refusing and always answered with
        /// <c>errorCode</c>. The character-search parser does not read
        /// <c>errorCode</c>.
        /// </summary>
        internal static Decision Evaluate(
            string method,
            string url,
            bool hasSecurityHeader,
            bool hasLiveSession,
            string? claimedCharacterUid,
            IReadOnlyList<Guid> charactersOnAccount)
        {
            SocialRoute route = SocialRoute.Parse(method ?? "GET", url ?? string.Empty);

            SocialIdentityPolicy.Outcome identity = SocialIdentityPolicy.Authorize(
                hasSecurityHeader: hasSecurityHeader,
                hasLiveSession: hasLiveSession,
                claimedCharacterUid: claimedCharacterUid,
                charactersOnAccount: charactersOnAccount);

            if (!identity.Authorized)
            {
                return Decision.Refuse(route, SocialRefusal.For(route.Kind, identity.ErrorCode!));
            }

            if (route.Kind == SocialRouteKind.None)
            {
                // A social URL we do not implement - every alliance endpoint past
                // listing and searching. Refused deliberately and in band rather
                // than faked: a plausible-looking alliance the UI half-accepts is
                // worse for a player than a clear refusal.
                return Decision.Refuse(
                    route, SocialRefusal.For(route.Kind, SocialErrorCodes.StoreUnavailable));
            }

            return Decision.Allow(route, identity.Character);
        }
    }
}

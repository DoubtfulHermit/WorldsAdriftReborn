using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Social;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The social endpoint's HTTP BOUNDARY: route parsing, authorisation, and the
    /// shape a refusal takes on the way back out.
    ///
    /// This file exists because two shipped defects lived in exactly this seam and
    /// nothing could see them. The social tests that existed called
    /// SocialService.Handle with the actor already resolved - behind the boundary -
    /// and were [PostgresFact], so they were skipped on any machine without a
    /// database. Everything here is a plain [Fact] over pure decisions: no
    /// database, no HttpSession, no network.
    /// </summary>
    public class SocialGateTests
    {
        private static readonly Guid Mine = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly IReadOnlyList<Guid> Owned = new[] { Mine };

        private static SocialGate.Decision Anonymous(string method, string url) =>
            SocialGate.Evaluate(method, url,
                hasSecurityHeader: false, hasLiveSession: false,
                claimedCharacterUid: null, charactersOnAccount: Array.Empty<Guid>());

        private static SocialGate.Decision Authenticated(string method, string url) =>
            SocialGate.Evaluate(method, url,
                hasSecurityHeader: true, hasLiveSession: true,
                claimedCharacterUid: Mine.ToString("D"), charactersOnAccount: Owned);

        // ---- the defect: a refusal the reader cannot read ---------------------

        /// <summary>
        /// REGRESSION. The search parser
        /// (SocialRequest.CheckSearchResponseModelForErrors, :114-124) never reads
        /// errorCode - it throws SocialServerResponseErrorException(model.desc).
        /// Refusing the search route with an errorCode-only envelope therefore put
        /// a dialog in front of the player whose text was the .NET default message,
        /// naming the exception class.
        /// </summary>
        [Fact]
        public void AnUnauthenticatedSearchIsRefusedWithASentence()
        {
            SocialGate.Decision decision = Anonymous("GET", "/screenname/find/Bob");

            Assert.False(decision.Serve);
            JObject refusal = decision.Refusal!;
            Assert.False(refusal.Value<bool>("success"));

            string? desc = refusal.Value<string>("desc");
            Assert.False(string.IsNullOrWhiteSpace(desc));
        }

        /// <summary>
        /// The other side of the same rule: every OTHER route is read by the
        /// generic parser, which looks errorCode up in the client's closed table.
        /// A sentence there would print as nothing.
        /// </summary>
        [Theory]
        [InlineData("GET", "/memberships/character/11111111-1111-1111-1111-111111111111")]
        [InlineData("GET", "/memberships/crew/crew:1")]
        [InlineData("POST", "/memberships/invite")]
        [InlineData("POST", "/crews")]
        public void AnUnauthenticatedNonSearchRouteIsRefusedWithAnErrorCode(string method, string url)
        {
            SocialGate.Decision decision = Anonymous(method, url);

            Assert.False(decision.Serve);
            Assert.Equal(SocialErrorCodes.NoAuthToken, decision.Refusal!.Value<string>("errorCode"));
            Assert.Null(decision.Refusal!["desc"]);
        }

        /// <summary>
        /// The ordering that caused the defect. Authorisation used to run before
        /// the route was parsed, so a refusal could not know which dialect to
        /// speak. Parsing first is what makes the test above possible.
        /// </summary>
        [Fact]
        public void TheRouteIsKnownEvenWhenAuthorisationFails()
        {
            Assert.Equal(SocialRouteKind.CharacterSearch,
                SocialRoute.Parse("GET", "/screenname/find/Bob").Kind);

            JObject refusal = Anonymous("GET", "/screenname/find/Bob").Refusal!;
            Assert.NotNull(refusal["desc"]);
        }

        // ---- the second trigger of the same defect ---------------------------

        /// <summary>
        /// REGRESSION. CrewScreen guards the invite field with IsNullOrEmpty and
        /// trims afterwards (CrewScreen.cs:308-310), so a field of spaces sends an
        /// EMPTY term and the client builds "screenname/find/". That used to miss
        /// the route entirely, refuse with an errorCode, and hit the same null
        /// dialog - by a completely different path than the auth one.
        /// </summary>
        [Fact]
        public void AnEmptySearchTermStillRoutesToSearch()
        {
            SocialRoute route = SocialRoute.Parse("GET", "/screenname/find/");

            Assert.Equal(SocialRouteKind.CharacterSearch, route.Kind);
        }

        [Fact]
        public void AnEmptySearchTermIsServedRatherThanRefusedAsUnknown()
        {
            // Served means it reaches SocialService, whose own branch answers with
            // "No name was given to search for." in the desc field. That branch was
            // unreachable before this: the one place a refusal correctly carried
            // desc could never be entered.
            SocialGate.Decision decision = Authenticated("GET", "/screenname/find/");

            Assert.True(decision.Serve);
            Assert.Equal(SocialRouteKind.CharacterSearch, decision.Route.Kind);
        }

        // ---- unimplemented routes and identity -------------------------------

        [Fact]
        public void AnUnimplementedSocialRouteIsRefusedInBand()
        {
            SocialGate.Decision decision = Authenticated("POST", "/alliance/community_server/ranks");

            Assert.False(decision.Serve);
            Assert.Equal(SocialRouteKind.None, decision.Route.Kind);
            Assert.Equal(SocialErrorCodes.StoreUnavailable, decision.Refusal!.Value<string>("errorCode"));
        }

        /// <summary>
        /// Every alliance endpoint has to pass the SAME identity gate as the crew
        /// ones. They were unimplemented when this file was written, so the gate
        /// had never been asked about them - and "the route now resolves" and "the
        /// route is now authorised" are two different changes.
        /// </summary>
        [Theory]
        [InlineData("POST", "/alliance")]
        [InlineData("GET", "/alliance/community_server/a1")]
        [InlineData("PATCH", "/alliance/community_server/a1")]
        [InlineData("DELETE", "/alliance/community_server/a1")]
        [InlineData("GET", "/alliance/find/community_server/11111111-1111-1111-1111-111111111111")]
        [InlineData("POST", "/alliance/community_server/batch")]
        [InlineData("GET", "/memberships/alliance/a1")]
        [InlineData("DELETE", "/memberships/alliance/a1/c1")]
        [InlineData("GET", "/memberships/invites/alliance/a1")]
        [InlineData("POST", "/memberships/join")]
        [InlineData("PATCH", "/memberships/character/c1/a1")]
        [InlineData("GET", "/ranks/a1")]
        [InlineData("POST", "/rank")]
        [InlineData("PUT", "/rank/r1")]
        [InlineData("DELETE", "/rank/r1")]
        public void AnUnauthenticatedAllianceRequestIsRefusedWithAnErrorCode(string method, string url)
        {
            SocialGate.Decision decision = Anonymous(method, url);

            Assert.False(decision.Serve);
            Assert.Equal(SocialErrorCodes.NoAuthToken, decision.Refusal!.Value<string>("errorCode"));

            // The route is still known on a refusal - that ordering is what lets
            // the refusal be written in the dialect its reader parses.
            Assert.NotEqual(SocialRouteKind.None, decision.Route.Kind);
        }

        [Theory]
        [InlineData("POST", "/alliance")]
        [InlineData("GET", "/alliances/community_server")]
        [InlineData("GET", "/ranks/a1")]
        [InlineData("POST", "/memberships/join")]
        public void AnAuthenticatedOwnerReachesTheAllianceEndpoints(string method, string url)
        {
            SocialGate.Decision decision = Authenticated(method, url);

            Assert.True(decision.Serve);
            Assert.Equal(Mine, decision.Character);
        }

        [Fact]
        public void ALiveSessionClaimingSomeoneElsesCharacterIsRefused()
        {
            SocialGate.Decision decision = SocialGate.Evaluate(
                "GET", "/memberships/crew/crew:1",
                hasSecurityHeader: true, hasLiveSession: true,
                claimedCharacterUid: Guid.NewGuid().ToString("D"),
                charactersOnAccount: Owned);

            Assert.False(decision.Serve);
            Assert.Equal(SocialErrorCodes.AuthFailed, decision.Refusal!.Value<string>("errorCode"));
        }

        [Fact]
        public void AnAuthenticatedOwnerIsServedAndCarriesItsCharacter()
        {
            SocialGate.Decision decision = Authenticated("GET", "/memberships/crew/crew:1");

            Assert.True(decision.Serve);
            Assert.Null(decision.Refusal);
            Assert.Equal(Mine, decision.Character);
        }

        // ---- invariants that hold for every refusal --------------------------

        /// <summary>
        /// The transport rules from SocialEnvelope, asserted at the boundary
        /// rather than one layer in: an object root, and success:false. The client
        /// parses the body before it looks at anything, and an array root or an
        /// empty body throws before the error is ever read.
        /// </summary>
        [Theory]
        [InlineData("GET", "/screenname/find/Bob")]
        [InlineData("GET", "/memberships/crew/crew:1")]
        [InlineData("GET", "/memberships/invites/character/11111111-1111-1111-1111-111111111111")]
        [InlineData("PUT", "/memberships/invite/accept/invite:1/11111111-1111-1111-1111-111111111111/community_server")]
        [InlineData("POST", "/alliance/community_server/ranks")]
        public void EveryRefusalIsAJsonObjectSayingSuccessFalse(string method, string url)
        {
            JObject refusal = Anonymous(method, url).Refusal!;

            Assert.Equal(JTokenType.Object, refusal.Type);
            Assert.False(refusal.Value<bool>("success"));
        }

        /// <summary>
        /// Neither shape may carry the client-injected fields. HttpHelper writes
        /// statusCode itself after parsing and SocialRequest writes
        /// originalResponseData; emitting them would be inventing wire fields.
        /// </summary>
        [Theory]
        [InlineData("GET", "/screenname/find/Bob")]
        [InlineData("GET", "/memberships/crew/crew:1")]
        public void ARefusalNeverCarriesClientInjectedFields(string method, string url)
        {
            JObject refusal = Anonymous(method, url).Refusal!;

            Assert.Null(refusal["statusCode"]);
            Assert.Null(refusal["originalResponseData"]);
        }
    }
}

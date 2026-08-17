using WorldsAdriftServer.Social;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The reconstructed URL contract, pinned.
    ///
    /// These paths were recovered by reading a decompiler's output
    /// (docs/research/findings-social-api.md), and a transcription slip in one of
    /// them is invisible: the endpoint simply never matches, the client sees a
    /// refusal, and it looks like the feature is unimplemented rather than
    /// misspelled. Writing the shape down twice - once as a matcher, once here -
    /// is the only defence.
    /// </summary>
    public class SocialRouteTests
    {
        [Theory]
        // The expected kind travels as a STRING because xUnit needs the test
        // class public and SocialRouteKind is internal to the server assembly. A
        // typo in one of these names would fail loudly on the Enum.Parse rather
        // than silently comparing two wrong things.
        [InlineData("GET", "/memberships/character/abc", "CharacterMemberships")]
        [InlineData("GET", "/memberships/invites/character/abc", "InvitesForCharacter")]
        [InlineData("GET", "/memberships/crew/crew:1", "CrewMembers")]
        [InlineData("GET", "/memberships/invites/crew/crew:1", "CrewInvites")]
        [InlineData("POST", "/memberships/invite", "SendInvite")]
        [InlineData("DELETE", "/memberships/crew/crew:1/abc", "RemoveCrewMember")]
        [InlineData("POST", "/crews", "CreateCrew")]
        [InlineData("GET", "/crew/community_server/crew:1", "GetCrew")]
        [InlineData("DELETE", "/crew/community_server/crew:1", "DisbandCrew")]
        [InlineData("GET", "/screenname/find/Billy", "CharacterSearch")]
        [InlineData("GET", "/alliances/community_server", "ListAlliances")]
        [InlineData("GET", "/alliance/search/community_server?term=x", "SearchAlliances")]
        public void RecognisesTheEndpointsTheClientSends(string method, string url, string expected)
        {
            SocialRouteKind kind = SocialRoute.Parse(method, url).Kind;
            Assert.Equal(Enum.Parse<SocialRouteKind>(expected), kind);
        }

        /// <summary>
        /// Accept and cancel carry a trailing region segment; reject does not.
        /// That asymmetry is in the client (SocialServerImpl.cs:85/93/101) and a
        /// tidier server that required a region on all three would break exactly
        /// one button.
        /// </summary>
        [Fact]
        public void RejectHasNoRegionSegmentWhileAcceptAndCancelDo()
        {
            SocialRoute accept = SocialRoute.Parse("PUT", "/memberships/invite/accept/inv1/char1/community_server");
            SocialRoute reject = SocialRoute.Parse("PUT", "/memberships/invite/reject/inv1/char1");
            SocialRoute cancel = SocialRoute.Parse("PUT", "/memberships/invite/cancel/inv1/char1/community_server");

            Assert.Equal(SocialRouteKind.AcceptInvite, accept.Kind);
            Assert.Equal(SocialRouteKind.RejectInvite, reject.Kind);
            Assert.Equal(SocialRouteKind.CancelInvite, cancel.Kind);

            // All three capture the same two things in the same order, whatever
            // trails them.
            Assert.Equal(new[] { "inv1", "char1" }, accept.Segments);
            Assert.Equal(new[] { "inv1", "char1" }, reject.Segments);
            Assert.Equal(new[] { "inv1", "char1" }, cancel.Segments);
        }

        /// <summary>
        /// The client escapes the WHOLE search endpoint, separators included -
        /// Uri.EscapeDataString("screenname/find/NAME") - and whether those %2F
        /// survive Unity's Mono System.Uri canonicalisation cannot be determined
        /// by reading the decompile. So both forms have to arrive somewhere.
        /// </summary>
        [Fact]
        public void CharacterSearchRoutesWhetherOrNotItsSlashesArrivedEscaped()
        {
            SocialRoute plain = SocialRoute.Parse("GET", "/screenname/find/Billy%20Bones");
            SocialRoute escaped = SocialRoute.Parse("GET", "/screenname%2Ffind%2FBilly%20Bones");

            Assert.Equal(SocialRouteKind.CharacterSearch, plain.Kind);
            Assert.Equal(SocialRouteKind.CharacterSearch, escaped.Kind);
            Assert.Equal("Billy Bones", plain.Segments[0]);
            Assert.Equal("Billy Bones", escaped.Segments[0]);
        }

        [Fact]
        public void MethodDecidesBetweenTwoEndpointsOnOneUrl()
        {
            Assert.Equal(SocialRouteKind.GetCrew,
                SocialRoute.Parse("GET", "/crew/r/c1").Kind);
            Assert.Equal(SocialRouteKind.DisbandCrew,
                SocialRoute.Parse("DELETE", "/crew/r/c1").Kind);
        }

        [Fact]
        public void SegmentCountDecidesBetweenListingAndRemoving()
        {
            Assert.Equal(SocialRouteKind.CrewMembers,
                SocialRoute.Parse("GET", "/memberships/crew/c1").Kind);
            Assert.Equal(SocialRouteKind.RemoveCrewMember,
                SocialRoute.Parse("DELETE", "/memberships/crew/c1/u1").Kind);
        }

        /// <summary>
        /// Every alliance URL is ours to answer even though almost none of them
        /// are implemented. Falling through to the router's 404 would reach the
        /// player as "Issue connecting to server", which reads as a broken server
        /// rather than an absent feature.
        /// </summary>
        [Theory]
        [InlineData("/alliance")]
        [InlineData("/alliance/community_server/a1")]
        [InlineData("/alliances/community_server")]
        [InlineData("/rank")]
        [InlineData("/ranks/a1")]
        [InlineData("/memberships/join")]
        [InlineData("/screenname/find/x")]
        [InlineData("/crews")]
        public void ClaimsTheWholeSocialNamespace(string url)
        {
            Assert.True(SocialRoute.IsSocialUrl(url));
        }

        [Theory]
        [InlineData("/authenticate")]
        [InlineData("/deploymentStatus")]
        [InlineData("/characterList/steam/1234")]
        [InlineData("/patch/manifest.json")]
        [InlineData("/admin")]
        [InlineData("/")]
        [InlineData("")]
        public void LeavesEveryOtherRouteAlone(string url)
        {
            Assert.False(SocialRoute.IsSocialUrl(url));
        }

        /// <summary>
        /// An unimplemented alliance endpoint must NOT resolve to a route. It is
        /// still ours (IsSocialUrl above), so the handler refuses it in band -
        /// but silently matching it to something adjacent would be worse than a
        /// refusal, which is the whole "do not fake it" rule.
        /// </summary>
        [Theory]
        [InlineData("POST", "/alliance")]
        [InlineData("PATCH", "/alliance/community_server/a1")]
        [InlineData("POST", "/rank")]
        [InlineData("PUT", "/rank/r1")]
        [InlineData("GET", "/ranks/a1")]
        [InlineData("POST", "/memberships/join")]
        [InlineData("GET", "/memberships/alliance/a1")]
        [InlineData("GET", "/memberships/invites/alliance/a1")]
        public void DoesNotInventARouteForUnimplementedAllianceEndpoints(string method, string url)
        {
            Assert.Equal(SocialRouteKind.None, SocialRoute.Parse(method, url).Kind);
        }

        [Fact]
        public void ReadsTheSearchTermOffTheQueryString()
        {
            Assert.Equal("dread pirate",
                SocialRoute.QueryValue("/alliance/search/r?term=dread%20pirate", "term"));
            Assert.Null(SocialRoute.QueryValue("/alliance/search/r", "term"));
        }

        /// <summary>
        /// The region is a path segment the client fills from whatever
        /// /deploymentStatus advertised. It must be ACCEPTED, not matched against
        /// a literal - an operator who renames the server would otherwise silently
        /// lose the crew panel.
        /// </summary>
        [Theory]
        [InlineData("community_server")]
        [InlineData("eu-west")]
        [InlineData("whatever-the-operator-called-it")]
        public void AcceptsAnyRegion(string region)
        {
            Assert.Equal(SocialRouteKind.GetCrew,
                SocialRoute.Parse("GET", "/crew/" + region + "/crew:1").Kind);
        }
    }
}

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
        // The next two differ only in SEGMENT COUNT: listing a crew's members and
        // removing one of them are the same path with a uid appended.
        [InlineData("GET", "/memberships/crew/crew:1", "CrewMembers")]
        [InlineData("GET", "/memberships/invites/crew/crew:1", "CrewInvites")]
        [InlineData("POST", "/memberships/invite", "SendInvite")]
        [InlineData("DELETE", "/memberships/crew/crew:1/abc", "RemoveCrewMember")]
        [InlineData("POST", "/crews", "CreateCrew")]
        // The next two differ only in METHOD: one URL, two endpoints.
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
        /// All seventeen alliance endpoints, pinned to the kind they must resolve
        /// to.
        ///
        /// This list USED to assert the opposite - that none of them resolved -
        /// because alliances were unimplemented and refused in band. POST /alliance
        /// in particular parsed to nothing, was answered "not implemented", and
        /// reached the player as the client's generic E00001 dialog. Writing the
        /// shape down twice, once as a matcher and once here, is the only defence
        /// against a transcription slip in a contract recovered from a decompiler.
        /// </summary>
        [Theory]
        [InlineData("POST", "/alliance", SocialRouteKind.CreateAlliance)]
        [InlineData("GET", "/alliance/find/community_server/c1", SocialRouteKind.FindAllianceForCharacter)]
        [InlineData("GET", "/alliance/community_server/a1", SocialRouteKind.GetAlliance)]
        [InlineData("PATCH", "/alliance/community_server/a1", SocialRouteKind.UpdateAlliance)]
        [InlineData("DELETE", "/alliance/community_server/a1", SocialRouteKind.DisbandAlliance)]
        [InlineData("POST", "/alliance/community_server/batch", SocialRouteKind.AllianceBatch)]
        [InlineData("GET", "/alliances/community_server", SocialRouteKind.ListAlliances)]
        [InlineData("GET", "/alliance/search/community_server?term=x", SocialRouteKind.SearchAlliances)]
        [InlineData("GET", "/memberships/alliance/a1", SocialRouteKind.AllianceMembers)]
        [InlineData("DELETE", "/memberships/alliance/a1/c1", SocialRouteKind.RemoveAllianceMember)]
        [InlineData("GET", "/memberships/invites/alliance/a1", SocialRouteKind.AllianceInvites)]
        [InlineData("POST", "/memberships/join", SocialRouteKind.ApplyToAlliance)]
        [InlineData("PATCH", "/memberships/character/c1/a1", SocialRouteKind.UpdateAllianceMembership)]
        [InlineData("GET", "/ranks/a1", SocialRouteKind.AllianceRanks)]
        [InlineData("POST", "/rank", SocialRouteKind.CreateAllianceRank)]
        [InlineData("PUT", "/rank/r1", SocialRouteKind.UpdateAllianceRank)]
        [InlineData("DELETE", "/rank/r1", SocialRouteKind.DeleteAllianceRank)]
        internal void EveryAllianceEndpointResolves(string method, string url, SocialRouteKind expected)
        {
            Assert.Equal(expected, SocialRoute.Parse(method, url).Kind);
        }

        /// <summary>
        /// Three pairs of URLs that are the same shape and mean different things.
        /// Each of these is a place where matching the wrong one swaps two ids
        /// silently rather than failing.
        /// </summary>
        [Fact]
        public void TheAmbiguousAlliancePathsCaptureTheRightSegments()
        {
            // find takes a CHARACTER; the bare form takes an ALLIANCE. Same prefix.
            SocialRoute find = SocialRoute.Parse("GET", "/alliance/community_server/find/x");
            Assert.NotEqual(SocialRouteKind.FindAllianceForCharacter, find.Kind);

            SocialRoute byCharacter = SocialRoute.Parse("GET", "/alliance/find/community_server/c1");
            Assert.Equal(SocialRouteKind.FindAllianceForCharacter, byCharacter.Kind);
            Assert.Equal("c1", byCharacter.Segments[1]);

            SocialRoute byAlliance = SocialRoute.Parse("GET", "/alliance/community_server/a1");
            Assert.Equal("a1", byAlliance.Segments[1]);

            // The membership pair is written CHARACTER-first here and
            // ALLIANCE-first one line below, in the same client file.
            SocialRoute patch = SocialRoute.Parse("PATCH", "/memberships/character/c1/a1");
            Assert.Equal("c1", patch.Segments[0]);
            Assert.Equal("a1", patch.Segments[1]);

            SocialRoute remove = SocialRoute.Parse("DELETE", "/memberships/alliance/a1/c1");
            Assert.Equal("a1", remove.Segments[0]);
            Assert.Equal("c1", remove.Segments[1]);

            // "batch" is a literal last segment on a POST; an alliance id is not.
            Assert.Equal(SocialRouteKind.AllianceBatch,
                SocialRoute.Parse("POST", "/alliance/community_server/batch").Kind);
        }

        /// <summary>
        /// Verbs the client never sends must still not resolve to something
        /// adjacent. Silently matching is worse than a refusal.
        /// </summary>
        [Theory]
        [InlineData("PUT", "/alliance/community_server/a1")]
        [InlineData("DELETE", "/alliances/community_server")]
        [InlineData("GET", "/rank/r1")]
        [InlineData("POST", "/ranks/a1")]
        [InlineData("GET", "/alliance")]
        [InlineData("PATCH", "/memberships/character/c1")]
        public void UnknownAllianceVerbCombinationsDoNotResolve(string method, string url)
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

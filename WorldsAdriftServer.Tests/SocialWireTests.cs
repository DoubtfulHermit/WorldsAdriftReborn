using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Social;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The payload field names.
    ///
    /// The client's DTOs carry no [JsonProperty] attributes, so these names are
    /// matched by plain field-name deserialisation and a single typo produces a
    /// null field rather than an error - which surfaces three screens later as a
    /// blank name or an NRE. Every assertion here is a name read out of the
    /// decompile.
    /// </summary>
    public class SocialWireTests
    {
        private static readonly DateTimeOffset When =
            new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void NameRefIsUidAndName()
        {
            JObject reference = SocialWire.NameRef("u", "Billy");

            Assert.Equal("u", reference.Value<string>("uid"));
            Assert.Equal("Billy", reference.Value<string>("name"));
        }

        [Fact]
        public void CrewDataCarriesEveryFieldTheClientReads()
        {
            JObject crew = SocialWire.CrewData(
                "crew:1", "community_server", "A crew", "desc", "leader-uid", "Bones", When, When);

            Assert.Equal("crew:1", crew.Value<string>("uid"));
            Assert.Equal("community_server", crew.Value<string>("region"));
            Assert.Equal("A crew", crew.Value<string>("name"));
            Assert.Equal("desc", crew.Value<string>("description"));
            Assert.Equal("leader-uid", crew.Value<string>("leaderCharacterUid"));
            Assert.Equal("leader-uid", crew["leaderCharacter"]!.Value<string>("uid"));
            Assert.Equal("Bones", crew["leaderCharacter"]!.Value<string>("name"));
            Assert.Equal(When.ToUnixTimeMilliseconds(), crew.Value<long>("created"));
            Assert.Equal(When.ToUnixTimeMilliseconds(), crew.Value<long>("lastUpdated"));
        }

        /// <summary>
        /// The client reads member.uid when member is present and memberId when it
        /// is not, so both are filled rather than relying on which branch it takes.
        /// </summary>
        [Fact]
        public void CrewMembershipFillsBothTheFlatIdAndTheEmbeddedOne()
        {
            JObject membership = SocialWire.CrewMembership("u", "Billy", "crew:1", When, When);

            Assert.Equal("u", membership.Value<string>("memberId"));
            Assert.Equal("u", membership["member"]!.Value<string>("uid"));
            Assert.Equal("Billy", membership["member"]!.Value<string>("name"));
            Assert.Equal("crew:1", membership.Value<string>("targetId"));
        }

        /// <summary>
        /// The single most important shape on this wire. GetYourBasicAllianceInfo
        /// tests alliance != null and, when it is null, resolves null WITHOUT
        /// issuing a second request - so an ABSENT KEY is how a player is told
        /// they have no alliance. A 404 or success:false here instead puts
        /// "Can't retrieve alliance or crew data" over the whole sheet, crew tab
        /// included.
        /// </summary>
        [Fact]
        public void NoAllianceAndNoCrewAreAbsentKeysNotEmptyObjects()
        {
            JObject memberships = SocialWire.PlayerMemberships("u", "Billy", crew: null, alliance: null);

            Assert.Equal("u", memberships.Value<string>("character"));
            Assert.Equal("Billy", memberships["member"]!.Value<string>("name"));
            Assert.Null(memberships["alliance"]);
            Assert.Null(memberships["crew"]);
        }

        [Fact]
        public void ACrewMembershipAppearsUnderTheCrewKey()
        {
            JObject crew = SocialWire.CrewMembership("u", "Billy", "crew:1", When, When);
            JObject memberships = SocialWire.PlayerMemberships("u", "Billy", crew, alliance: null);

            Assert.NotNull(memberships["crew"]);
            Assert.Equal("crew:1", memberships["crew"]!.Value<string>("targetId"));
        }

        /// <summary>
        /// inviter == null is the client's own discriminator between an INVITE and
        /// an APPLICATION (CheckMembershipRequestType). It has to be an explicit
        /// JSON null rather than an absent key OR an empty object - an object
        /// would file a player's own application in the invitations list.
        /// </summary>
        [Fact]
        public void AnApplicationHasAnExplicitlyNullInviter()
        {
            JObject application = SocialWire.ChangeRequest(
                "i1", "crew:1", "Crew", "crew_member", "u", "Billy",
                inviterUid: null, inviterName: null, message: "", status: "new", When, When);

            Assert.NotNull(application["inviter"]);
            Assert.Equal(JTokenType.Null, application["inviter"]!.Type);
        }

        [Fact]
        public void AnInviteCarriesItsInviterAsAnObject()
        {
            JObject invite = SocialWire.ChangeRequest(
                "i1", "crew:1", "Crew", "crew_member", "u", "Billy",
                inviterUid: "l", inviterName: "Bones", message: "", status: "new", When, When);

            Assert.Equal("l", invite["inviter"]!.Value<string>("uid"));
            Assert.Equal("Bones", invite["inviter"]!.Value<string>("name"));
            Assert.Equal("i1", invite.Value<string>("id"));
            Assert.Equal("crew_member", invite.Value<string>("targetType"));
            Assert.Equal("new", invite.Value<string>("status"));

            // The invitee rides in `character`, and that is what the crew panel
            // renders on a pending-invite bar.
            Assert.Equal("Billy", invite["character"]!.Value<string>("name"));
        }

        /// <summary>
        /// The search response does NOT use the standard envelope:
        /// CharacterSearchResponseModel extends ResponseSchema, so `screenname` is
        /// a sibling of `success`, not a child of `data`. Nesting it would leave
        /// characterSearchResponse.screenname null and NRE the invite flow one
        /// call later.
        /// </summary>
        [Fact]
        public void CharacterSearchPutsScreennameBesideSuccessNotUnderData()
        {
            JObject found = SocialWire.CharacterFound("u", "Billy", 2, When);

            Assert.True(found.Value<bool>("success"));
            Assert.Null(found["data"]);
            Assert.NotNull(found["screenname"]);
            Assert.Equal("u", found["screenname"]!.Value<string>("characterUid"));
            Assert.Equal("Billy", found["screenname"]!.Value<string>("name"));
        }

        /// <summary>
        /// And its failures are reported through `desc`, shown to the player
        /// verbatim, rather than through an errorCode lookup
        /// (SocialRequest.CheckSearchResponseModelForErrors).
        /// </summary>
        [Fact]
        public void AFailedSearchExplainsItselfInDesc()
        {
            JObject missing = SocialWire.CharacterNotFound("No player called Nobody was found.");

            Assert.False(missing.Value<bool>("success"));
            Assert.Equal("No player called Nobody was found.", missing.Value<string>("desc"));
            Assert.Null(missing["errorCode"]);
        }

        /// <summary>
        /// The client decides leadership by ordinal string comparison across three
        /// separate responses of ours, so every uid must be formatted identically.
        /// Lowercase hyphenated "D" is what CharacterAdapter already puts in the
        /// character list, which is where the client got the uid it compares
        /// against.
        /// </summary>
        [Fact]
        public void UidsAreLowercaseHyphenatedAndMatchTheCharacterList()
        {
            Guid uid = Guid.Parse("A1B2C3D4-0000-0000-0000-000000000001");

            Assert.Equal("a1b2c3d4-0000-0000-0000-000000000001", SocialWire.Uid(uid));
            Assert.Equal(uid.ToString(), SocialWire.Uid(uid));
        }
    }
}

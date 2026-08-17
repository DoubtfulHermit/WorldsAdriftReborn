using WorldsAdriftServer.Social;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The social API's whole security surface.
    ///
    /// Every crew endpoint names its target in the URL, so a server that trusted
    /// the URL would let anyone disband anyone's crew by typing a different uid.
    /// The rule that stops that is ownership - the claimed character must belong
    /// to the account the session token resolves to - and it is worth testing the
    /// refusals harder than the acceptance.
    /// </summary>
    public class SocialIdentityPolicyTests
    {
        private static readonly Guid Mine = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid AlsoMine = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid Theirs = Guid.Parse("33333333-3333-3333-3333-333333333333");

        [Fact]
        public void AllowsACharacterOnTheCallersOwnAccount()
        {
            SocialIdentityPolicy.Outcome outcome = SocialIdentityPolicy.Authorize(
                hasSecurityHeader: true,
                hasLiveSession: true,
                claimedCharacterUid: Mine.ToString(),
                charactersOnAccount: new[] { Mine, AlsoMine });

            Assert.True(outcome.Authorized);
            Assert.Equal(Mine, outcome.Character);
        }

        /// <summary>
        /// The case a uid-shaped check would wave through: a real, well-formed,
        /// existing character uid that simply is not the caller's.
        /// </summary>
        [Fact]
        public void RefusesAPerfectlyValidUidThatBelongsToSomebodyElse()
        {
            SocialIdentityPolicy.Outcome outcome = SocialIdentityPolicy.Authorize(
                hasSecurityHeader: true,
                hasLiveSession: true,
                claimedCharacterUid: Theirs.ToString(),
                charactersOnAccount: new[] { Mine, AlsoMine });

            Assert.False(outcome.Authorized);
            Assert.Equal("auth_failed", outcome.ErrorCode);
        }

        /// <summary>
        /// The client OMITS the Security header rather than sending an empty one
        /// when it has no token (SocialRequest.DecorateRequest guards on null), so
        /// its absence is a distinguishable state and gets the client's own word
        /// for it.
        /// </summary>
        [Fact]
        public void AMissingSecurityHeaderIsNoAuthTokenNotAuthFailed()
        {
            SocialIdentityPolicy.Outcome outcome = SocialIdentityPolicy.Authorize(
                hasSecurityHeader: false,
                hasLiveSession: false,
                claimedCharacterUid: Mine.ToString(),
                charactersOnAccount: new[] { Mine });

            Assert.Equal("no_auth_token", outcome.ErrorCode);
        }

        [Fact]
        public void AnExpiredOrUnknownTokenIsAuthFailed()
        {
            SocialIdentityPolicy.Outcome outcome = SocialIdentityPolicy.Authorize(
                hasSecurityHeader: true,
                hasLiveSession: false,
                claimedCharacterUid: Mine.ToString(),
                charactersOnAccount: Array.Empty<Guid>());

            Assert.Equal("auth_failed", outcome.ErrorCode);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-guid")]
        [InlineData("valid-UIDs-have-at-least-one-")]
        public void RefusesAClaimThatIsNotAUid(string? claimed)
        {
            SocialIdentityPolicy.Outcome outcome = SocialIdentityPolicy.Authorize(
                hasSecurityHeader: true,
                hasLiveSession: true,
                claimedCharacterUid: claimed,
                charactersOnAccount: new[] { Mine });

            Assert.False(outcome.Authorized);
            Assert.Equal("invalid_entity_id", outcome.ErrorCode);
        }

        /// <summary>
        /// A live session whose account owns nothing cannot act as anyone. Worth
        /// stating: an empty roster is the state right after a reset, and a
        /// "no characters means no check" shortcut would be a hole open exactly
        /// then.
        /// </summary>
        [Fact]
        public void AnAccountWithNoCharactersCanActAsNobody()
        {
            SocialIdentityPolicy.Outcome outcome = SocialIdentityPolicy.Authorize(
                hasSecurityHeader: true,
                hasLiveSession: true,
                claimedCharacterUid: Mine.ToString(),
                charactersOnAccount: Array.Empty<Guid>());

            Assert.False(outcome.Authorized);
        }

        /// <summary>
        /// Uid casing must not decide authorisation. The client echoes back
        /// whatever we sent it, and Guid.TryParse is case-insensitive, so the
        /// comparison happens on parsed Guids rather than on strings.
        /// </summary>
        [Fact]
        public void UidCasingDoesNotChangeTheAnswer()
        {
            SocialIdentityPolicy.Outcome outcome = SocialIdentityPolicy.Authorize(
                hasSecurityHeader: true,
                hasLiveSession: true,
                claimedCharacterUid: Mine.ToString().ToUpperInvariant(),
                charactersOnAccount: new[] { Mine });

            Assert.True(outcome.Authorized);
        }
    }
}

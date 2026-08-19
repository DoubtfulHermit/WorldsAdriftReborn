using WorldsAdriftReborn.Storage.Policy;
using WorldsAdriftServer.Portal;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The portal's forms, its password rule and its notice vocabulary - the three
    /// pure things between a browser POST and a database write.
    /// </summary>
    public class PortalFormPolicyTests
    {
        private const string Alliance = "11111111-1111-1111-1111-111111111111";
        private const string Character = "22222222-2222-2222-2222-222222222222";
        private const string Target = "33333333-3333-3333-3333-333333333333";
        private const string Rank = "44444444-4444-4444-4444-444444444444";

        private static Dictionary<string, string> Form(params (string Key, string Value)[] pairs)
        {
            Dictionary<string, string> form = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string key, string value) in pairs) form[key] = value;
            return form;
        }

        // ---------------------------------------------------------- the details

        [Fact]
        public void ADetailsPostCarriesTheAllianceTheCharacterAndOneField()
        {
            DetailsForm form = PortalFormPolicy.ReadDetails(Form(
                (PortalFormPolicy.AllianceField, Alliance),
                (PortalFormPolicy.CharacterField, Character),
                (PortalFormPolicy.DescriptionField, "We fly at dawn.")));

            Assert.True(form.Ok);
            Assert.Equal(Guid.Parse(Alliance), form.AllianceId);
            Assert.Equal(Guid.Parse(Character), form.CharacterUid);
            Assert.Equal("We fly at dawn.", form.Description);
        }

        /// <summary>
        /// ABSENT is not EMPTY, and the difference is a permission. The page posts
        /// one field per form because the two carry different permissions; a
        /// missing key must therefore mean "leave it alone", not "set it to
        /// nothing" - otherwise somebody holding edit_group would blank the MOTD
        /// every time they saved a description.
        /// </summary>
        [Fact]
        public void AFieldTheFormDidNotSendIsDistinguishableFromAnEmptyOne()
        {
            Dictionary<string, string> onlyDescription = Form(
                (PortalFormPolicy.AllianceField, Alliance),
                (PortalFormPolicy.CharacterField, Character),
                (PortalFormPolicy.DescriptionField, string.Empty));

            Assert.True(PortalFormPolicy.Sent(onlyDescription, PortalFormPolicy.DescriptionField));
            Assert.False(PortalFormPolicy.Sent(onlyDescription, PortalFormPolicy.MotdField));
            Assert.True(PortalFormPolicy.ReadDetails(onlyDescription).Ok);
        }

        [Fact]
        public void ADetailsPostWithNeitherFieldIsRefused()
        {
            DetailsForm form = PortalFormPolicy.ReadDetails(Form(
                (PortalFormPolicy.AllianceField, Alliance),
                (PortalFormPolicy.CharacterField, Character)));

            Assert.False(form.Ok);
            Assert.Equal(PortalFormFault.MissingField, form.Fault);
        }

        [Fact]
        public void OverlongTextIsRefusedRatherThanTruncated()
        {
            DetailsForm form = PortalFormPolicy.ReadDetails(Form(
                (PortalFormPolicy.AllianceField, Alliance),
                (PortalFormPolicy.CharacterField, Character),
                (PortalFormPolicy.MotdField, new string('x', PortalFormPolicy.MaxTextLength + 1))));

            Assert.Equal(PortalFormFault.TooLong, form.Fault);
        }

        [Theory]
        [InlineData("not-a-guid")]
        [InlineData("")]
        [InlineData("11111111111111111111111111111111")]
        public void AnIdThatIsNotAHyphenatedGuidIsRefused(string bad)
        {
            DetailsForm form = PortalFormPolicy.ReadDetails(Form(
                (PortalFormPolicy.AllianceField, bad),
                (PortalFormPolicy.CharacterField, Character),
                (PortalFormPolicy.DescriptionField, "x")));

            Assert.Equal(PortalFormFault.NotAnId, form.Fault);
        }

        // ----------------------------------------------------------- the member

        [Fact]
        public void ARankPostCarriesTheRankAndABootPostDoesNotNeedOne()
        {
            MemberForm rank = PortalFormPolicy.ReadMember(Form(
                (PortalFormPolicy.ActionField, "rank"),
                (PortalFormPolicy.AllianceField, Alliance),
                (PortalFormPolicy.CharacterField, Character),
                (PortalFormPolicy.TargetField, Target),
                (PortalFormPolicy.RankField, Rank)));

            Assert.True(rank.Ok);
            Assert.Equal(MemberVerb.SetRank, rank.Verb);
            Assert.Equal(Guid.Parse(Rank), rank.RankId);

            MemberForm boot = PortalFormPolicy.ReadMember(Form(
                (PortalFormPolicy.ActionField, "boot"),
                (PortalFormPolicy.AllianceField, Alliance),
                (PortalFormPolicy.CharacterField, Character),
                (PortalFormPolicy.TargetField, Target)));

            Assert.True(boot.Ok);
            Assert.Equal(MemberVerb.Boot, boot.Verb);
        }

        [Fact]
        public void ARankPostWithoutARankIsRefused()
        {
            MemberForm form = PortalFormPolicy.ReadMember(Form(
                (PortalFormPolicy.ActionField, "rank"),
                (PortalFormPolicy.AllianceField, Alliance),
                (PortalFormPolicy.CharacterField, Character),
                (PortalFormPolicy.TargetField, Target)));

            Assert.Equal(PortalFormFault.NotAnId, form.Fault);
        }

        [Theory]
        [InlineData("promote")]
        [InlineData("")]
        [InlineData("Rank")]
        public void AVerbTheFormDoesNotHaveIsRefused(string verb)
        {
            MemberForm form = PortalFormPolicy.ReadMember(Form(
                (PortalFormPolicy.ActionField, verb),
                (PortalFormPolicy.AllianceField, Alliance),
                (PortalFormPolicy.CharacterField, Character),
                (PortalFormPolicy.TargetField, Target)));

            Assert.Equal(PortalFormFault.UnknownAction, form.Fault);
        }

        // ---------------------------------------------------------- the request

        [Fact]
        public void ARequestPostCarriesAVerbAndAnInviteId()
        {
            (string Word, RequestVerb Expected)[] cases =
            {
                ("accept", RequestVerb.Accept),
                ("reject", RequestVerb.Reject),
                ("rescind", RequestVerb.Rescind),
            };

            foreach ((string word, RequestVerb expected) in cases)
            {
                RequestForm form = PortalFormPolicy.ReadRequest(Form(
                    (PortalFormPolicy.ActionField, word),
                    (PortalFormPolicy.AllianceField, Alliance),
                    (PortalFormPolicy.CharacterField, Character),
                    (PortalFormPolicy.InviteField, "invite:abc")));

                Assert.True(form.Ok);
                Assert.Equal(expected, form.Verb);
                Assert.Equal("invite:abc", form.InviteId);
            }
        }

        /// <summary>
        /// An invite id is <c>invite:{guid}</c>, not a bare guid - it is the shape
        /// the store mints and the only shape it can be looked up by, so parsing it
        /// as a GUID here would refuse every real one.
        /// </summary>
        [Fact]
        public void AnInviteIdIsNotParsedAsAGuid()
        {
            RequestForm form = PortalFormPolicy.ReadRequest(Form(
                (PortalFormPolicy.ActionField, "accept"),
                (PortalFormPolicy.AllianceField, Alliance),
                (PortalFormPolicy.CharacterField, Character),
                (PortalFormPolicy.InviteField, "invite:" + Guid.NewGuid().ToString("D"))));

            Assert.True(form.Ok);
        }

        [Fact]
        public void ARequestPostWithNoInviteIsRefused()
        {
            RequestForm form = PortalFormPolicy.ReadRequest(Form(
                (PortalFormPolicy.ActionField, "accept"),
                (PortalFormPolicy.AllianceField, Alliance),
                (PortalFormPolicy.CharacterField, Character),
                (PortalFormPolicy.InviteField, "  ")));

            Assert.Equal(PortalFormFault.MissingField, form.Fault);
        }

        // --------------------------------------------------------- the password

        [Fact]
        public void AGoodPasswordChangePasses()
        {
            Assert.Equal(
                PasswordChangeFault.None,
                PasswordChangePolicy.Check("old-one-here", "a-much-better-one", "a-much-better-one"));
        }

        [Fact]
        public void EachRefusalIsItsOwn()
        {
            (string? Current, string Next, string Confirm, PasswordChangeFault Expected)[] cases =
            {
                (null, "newpassword", "newpassword", PasswordChangeFault.Missing),
                ("old", "", "", PasswordChangeFault.Missing),
                ("old", "newpassword", "newpassvvord", PasswordChangeFault.Mismatch),
                ("samesame", "samesame", "samesame", PasswordChangeFault.Unchanged),
                ("oldpassword", "abc", "abc", PasswordChangeFault.TooWeak),
            };

            foreach ((string? current, string next, string confirm, PasswordChangeFault expected) in cases)
            {
                Assert.Equal(expected, PasswordChangePolicy.Check(current, next, confirm));
            }
        }

        /// <summary>
        /// The weakness rule has to be sign-up's, or a password the portal accepts
        /// is one the sign-up page would have refused.
        /// </summary>
        [Fact]
        public void TheWeaknessRuleIsTheOneSignUpApplies()
        {
            string shortest = new string('p', AccountPolicy.MinPasswordLength);
            string tooShort = new string('p', AccountPolicy.MinPasswordLength - 1);

            Assert.Equal(PasswordChangeFault.None,
                PasswordChangePolicy.Check("something-else", shortest, shortest));
            Assert.Equal(PasswordChangeFault.TooWeak,
                PasswordChangePolicy.Check("something-else", tooShort, tooShort));
        }

        [Fact]
        public void EveryFaultHasASentence()
        {
            foreach (PasswordChangeFault fault in Enum.GetValues<PasswordChangeFault>())
            {
                Assert.False(string.IsNullOrWhiteSpace(PasswordChangePolicy.Explain(fault)));
            }
        }

        // ---------------------------------------------------------- the notices

        [Fact]
        public void EveryPasswordFaultMapsToACodeTheNoticeTableKnows()
        {
            foreach (PasswordChangeFault fault in Enum.GetValues<PasswordChangeFault>())
            {
                string code = PortalNotices.CodeFor(fault);
                (string? text, _) = PortalNotices.For(code);
                Assert.False(string.IsNullOrWhiteSpace(text), "no sentence for " + code);
            }
        }

        /// <summary>
        /// THE ONE THAT MATTERS. A code this table does not know must say nothing -
        /// a fallthrough to a success sentence would let a link hand a player
        /// "Password changed." after nothing happened.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("nope")]
        [InlineData("<script>alert(1)</script>")]
        [InlineData(null)]
        public void AnUnknownCodeSaysNothing(string? code)
        {
            (string? text, bool error) = PortalNotices.For(code);

            Assert.Null(text);
            Assert.False(error);
        }

        [Theory]
        [InlineData(PortalNotices.Expired, true)]
        [InlineData(PortalNotices.Denied, true)]
        [InlineData(PortalNotices.Failed, true)]
        [InlineData(PortalNotices.Gone, true)]
        [InlineData(PortalNotices.CrestSaved, false)]
        [InlineData(PortalNotices.RankSet, false)]
        [InlineData(PortalNotices.PasswordChanged, false)]
        public void RefusalsAreMarkedAsRefusalsAndSuccessesAreNot(string code, bool isError)
        {
            (string? text, bool error) = PortalNotices.For(code);

            Assert.NotNull(text);
            Assert.Equal(isError, error);
        }

        [Theory]
        [InlineData("/account", null)]
        [InlineData("/account?m=denied", PortalNotices.Denied)]
        [InlineData("/account?x=1&m=rank-set", PortalNotices.RankSet)]
        [InlineData("/account?m=", "")]
        public void TheCodeIsReadOutOfTheQueryString(string url, string? expected)
        {
            Assert.Equal(expected, PortalNotices.CodeFrom(url));
        }

        [Fact]
        public void AnAbsurdlyLongCodeIsNotEvenConsidered()
        {
            Assert.Null(PortalNotices.CodeFrom("/account?m=" + new string('z', 500)));
        }
    }
}

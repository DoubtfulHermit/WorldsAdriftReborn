using WorldsAdriftRebornGameServer.Multiplayer.Alliance;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Alliance
{
    /// <summary>
    /// The name rules, checked against the client's own pre-flight regexes
    /// (StringFormatHelper.CheckRules). Every case below is one the retail UI
    /// would have shown the player a reason for.
    /// </summary>
    public sealed class AllianceNamePolicyTests
    {
        /// <summary>The name from the report that started this work. It must pass -
        /// the client accepted it and sent the request.</summary>
        [Fact]
        public void The_name_the_player_actually_typed_is_accepted()
        {
            Assert.True(AllianceNamePolicy.IsAcceptable("Rat Corp"));
        }

        [Theory]
        [InlineData("Rats")]
        [InlineData("The Sky Rats")]
        [InlineData("O'Malley")]
        [InlineData("A")]
        public void Letters_spaces_and_apostrophes_are_allowed(string name)
        {
            Assert.True(AllianceNamePolicy.IsAcceptable(name));
        }

        [Theory]
        [InlineData("Rat Corp 2")]        // digits
        [InlineData("Rat-Corp")]          // hyphen
        [InlineData("Rat_Corp")]          // underscore
        [InlineData("Rat.Corp")]          // punctuation
        [InlineData("Räuber")]            // outside A-Za-z, and this machine is a German locale
        public void Anything_but_letters_spaces_and_apostrophes_is_refused(string name)
        {
            Assert.False(AllianceNamePolicy.IsAcceptable(name));
        }

        /// <summary>
        /// The client's oddest rule, and the easiest to drop by accident: a
        /// capital may not directly follow another letter, so CamelCase is out but
        /// "Rat Corp" is fine because a space intervenes.
        /// </summary>
        [Theory]
        [InlineData("RatCorp")]
        [InlineData("McDonald")]
        public void A_capital_may_not_follow_a_letter(string name)
        {
            Assert.False(AllianceNamePolicy.IsAcceptable(name));
        }

        [Theory]
        [InlineData(" Rats")]
        [InlineData("Rats ")]
        [InlineData("'Rats")]
        [InlineData("Rats'")]
        public void No_leading_or_trailing_space_or_apostrophe(string name)
        {
            Assert.False(AllianceNamePolicy.IsAcceptable(name));
        }

        [Theory]
        [InlineData("Rat  Corp")]
        [InlineData("O''Malley")]
        public void Doubled_spaces_and_apostrophes_are_refused(string name)
        {
            Assert.False(AllianceNamePolicy.IsAcceptable(name));
        }

        [Fact]
        public void Null_and_empty_are_refused_without_throwing()
        {
            Assert.False(AllianceNamePolicy.IsAcceptable(null));
            Assert.False(AllianceNamePolicy.IsAcceptable(string.Empty));
        }

        [Fact]
        public void A_name_longer_than_the_server_will_store_is_refused()
        {
            Assert.False(AllianceNamePolicy.IsAcceptable(new string('a', AllianceNamePolicy.MaxLength + 1)));
            Assert.True(AllianceNamePolicy.IsAcceptable(new string('a', AllianceNamePolicy.MaxLength)));
        }

        /// <summary>
        /// Culture-invariant. The machine this runs on has a German locale, and
        /// whether two players may share a name must not depend on that.
        /// </summary>
        [Fact]
        public void Uniqueness_folds_case_invariantly()
        {
            Assert.Equal(
                AllianceNamePolicy.UniquenessKey("RAT CORP"),
                AllianceNamePolicy.UniquenessKey("rat corp"));
        }
    }
}

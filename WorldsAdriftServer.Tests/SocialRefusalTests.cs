using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Social;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// Which dialect a refusal is written in, per route.
    ///
    /// The client has two response checkers that disagree about where a failure's
    /// text lives, and using the wrong one does not degrade the message - it
    /// replaces it with the name of an exception class.
    /// </summary>
    public class SocialRefusalTests
    {
        /// <summary>
        /// The whole rule, stated once over the WHOLE enum rather than sampled.
        /// A route added later shows up here as a failure and has to be a
        /// deliberate decision, instead of silently inheriting errorCode - which
        /// is exactly how the search route came to be refused in a dialect it
        /// cannot read.
        /// </summary>
        [Fact]
        public void ExactlyOneRouteReadsTheDescriptionField()
        {
            List<SocialRouteKind> readers = new List<SocialRouteKind>();
            foreach (SocialRouteKind kind in Enum.GetValues<SocialRouteKind>())
            {
                if (SocialRefusal.ReadsDescription(kind)) readers.Add(kind);
            }

            Assert.Equal(new[] { SocialRouteKind.CharacterSearch }, readers);
        }

        [Fact]
        public void TheSearchRouteIsRefusedWithADescriptionAndNoErrorCode()
        {
            JObject refusal = SocialRefusal.For(
                SocialRouteKind.CharacterSearch, SocialErrorCodes.NoAuthToken);

            Assert.False(refusal.Value<bool>("success"));
            Assert.False(string.IsNullOrWhiteSpace(refusal.Value<string>("desc")));

            // The search parser never looks at errorCode, so putting one there
            // would be noise that reads as a contract we do not have.
            Assert.Null(refusal["errorCode"]);
        }

        [Fact]
        public void EveryOtherRouteIsRefusedWithItsErrorCodeVerbatim()
        {
            foreach (SocialRouteKind kind in Enum.GetValues<SocialRouteKind>())
            {
                if (kind == SocialRouteKind.CharacterSearch) continue;

                JObject refusal = SocialRefusal.For(kind, SocialErrorCodes.InviteLimitMet);
                Assert.Equal(SocialErrorCodes.InviteLimitMet, refusal.Value<string>("errorCode"));
            }
        }

        /// <summary>
        /// Every code that can reach a description reader has to become a
        /// sentence. A code leaking through as its own text would print to the
        /// player as debug output, since there is no table to translate it.
        /// </summary>
        [Theory]
        [InlineData(SocialErrorCodes.NoAuthToken)]
        [InlineData(SocialErrorCodes.AuthFailed)]
        [InlineData(SocialErrorCodes.StoreUnavailable)]
        [InlineData(SocialErrorCodes.InvalidName)]
        [InlineData(SocialErrorCodes.InvalidEntityId)]
        [InlineData("some_code_we_never_defined")]
        public void ASentenceIsNeverTheRawCode(string code)
        {
            string sentence = SocialRefusal.Sentence(code);

            Assert.False(string.IsNullOrWhiteSpace(sentence));
            Assert.DoesNotContain(code, sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("_", sentence, StringComparison.Ordinal);
            Assert.EndsWith(".", sentence.Trim(), StringComparison.Ordinal);
        }

        [Fact]
        public void AnUnknownCodeStillGetsUsableText()
        {
            Assert.False(string.IsNullOrWhiteSpace(SocialRefusal.Sentence("not_a_real_code")));
        }
    }
}

using WorldsAdriftServer.Handlers.Authentication;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    public class GameSessionResumeCredentialTests
    {
        private const string Token = "abcdefghijklmnopqrstuvwxyzABCDEFGH123456789";

        [Fact]
        public void Exact_versioned_base64url_session_is_accepted()
        {
            Assert.True(GameSessionResumeCredential.TryParse(
                GameSessionResumeCredential.Prefix + Token, out string parsed));
            Assert.Equal(Token, parsed);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("ordinary-password")]
        [InlineData("wareborn-session-v2:abcdefghijklmnopqrstuvwxyzABCDEFGH123456789")]
        [InlineData("wareborn-session-v1:short")]
        [InlineData("wareborn-session-v1:abcdefghijklmnopqrstuvwxyzABCDEFGH12345678/")]
        [InlineData("wareborn-session-v1:abcdefghijklmnopqrstuvwxyzABCDEFGH1234567890")]
        public void Passwords_malformed_tokens_and_unknown_versions_are_not_resume_credentials(
            string? secret)
        {
            Assert.False(GameSessionResumeCredential.TryParse(secret, out string parsed));
            Assert.Equal(string.Empty, parsed);
        }
    }
}

using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Handlers.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The bits of the admin HTTP glue that are worth pinning without a socket:
    /// the form-body parser the login and server-name POSTs depend on, and the
    /// staleness threshold that decides "game server not reporting".
    /// </summary>
    public class AdminHandlerFormTests
    {
        [Fact]
        public void Parses_a_simple_login_body()
        {
            var form = AdminHandler.ParseForm("username=hermit&password=secret");

            Assert.Equal("hermit", form["username"]);
            Assert.Equal("secret", form["password"]);
        }

        [Fact]
        public void Decodes_percent_and_plus_encoding()
        {
            // '+' is a space, %2B is a literal '+', %40 an '@'.
            var form = AdminHandler.ParseForm("username=a%40b.com&password=one+two%2Bthree");

            Assert.Equal("a@b.com", form["username"]);
            Assert.Equal("one two+three", form["password"]);
        }

        [Fact]
        public void Handles_a_server_name_with_spaces()
        {
            var form = AdminHandler.ParseForm("serverName=The+Anchor+Tavern");
            Assert.Equal("The Anchor Tavern", form["serverName"]);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void An_empty_body_is_an_empty_map(string? body)
        {
            Assert.Empty(AdminHandler.ParseForm(body));
        }

        [Fact]
        public void A_malformed_pair_is_skipped_not_thrown()
        {
            var form = AdminHandler.ParseForm("good=1&=novalue&bare&also=2");

            Assert.Equal("1", form["good"]);
            Assert.Equal("2", form["also"]);
            Assert.Equal("", form["bare"]);
            Assert.False(form.ContainsKey(""));
        }

        [Fact]
        public void Staleness_flips_at_the_threshold()
        {
            Assert.False(GameStats.IsStale(GameStats.StaleAfter - TimeSpan.FromMilliseconds(1)));
            Assert.True(GameStats.IsStale(GameStats.StaleAfter));
            Assert.True(GameStats.IsStale(GameStats.StaleAfter + TimeSpan.FromSeconds(30)));
        }
    }
}

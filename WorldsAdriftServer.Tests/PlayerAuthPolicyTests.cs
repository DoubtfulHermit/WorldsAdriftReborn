using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The string-shaped half of the player web session: how the wa_player cookie
    /// is parsed out of an incoming header and how the Set-Cookie value is written.
    /// The two attributes that differ from the admin cookie on purpose - Path=/ and
    /// SameSite=Lax - are pinned here so a refactor cannot quietly narrow the scope
    /// (which would drop the cookie on /download/WAPatch.exe) or tighten SameSite.
    /// </summary>
    public class PlayerAuthPolicyTests
    {
        // ---- cookie parsing ------------------------------------------------

        [Fact]
        public void The_token_is_pulled_from_a_lone_cookie()
        {
            Assert.Equal("abc123",
                PlayerAuthPolicy.TokenFromCookieHeader("wa_player=abc123"));
        }

        [Fact]
        public void The_token_is_pulled_from_among_other_cookies()
        {
            Assert.Equal("tok",
                PlayerAuthPolicy.TokenFromCookieHeader("theme=dark; wa_player=tok; other=1"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("theme=dark; other=1")]   // no wa_player present
        [InlineData("wa_player=")]             // present but empty
        [InlineData("wa_player")]              // no '=' at all
        public void A_missing_or_empty_token_is_null(string? header)
        {
            Assert.Null(PlayerAuthPolicy.TokenFromCookieHeader(header));
        }

        [Fact]
        public void A_prefix_named_cookie_is_not_mistaken_for_ours()
        {
            // "notwa_player" must not satisfy an exact-name match.
            Assert.Null(PlayerAuthPolicy.TokenFromCookieHeader("notwa_player=nope"));
        }

        // ---- cookie building -----------------------------------------------

        [Fact]
        public void A_session_cookie_is_site_wide_httponly_and_lax()
        {
            string cookie = PlayerAuthPolicy.BuildSessionCookie("TOKEN", 604800);

            Assert.Equal(
                "wa_player=TOKEN; Path=/; HttpOnly; SameSite=Lax; Max-Age=604800",
                cookie);
        }

        [Fact]
        public void The_clear_cookie_expires_immediately_and_keeps_the_scope()
        {
            string cookie = PlayerAuthPolicy.BuildClearCookie();

            Assert.Equal(
                "wa_player=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0",
                cookie);
        }
    }
}

using WorldsAdriftReborn.Storage.Policy;
using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The pure rules behind the single-operator login. Each assertion is a way
    /// the panel could be misconfigured or attacked: a credential that half-parses,
    /// a plaintext where a hash was expected, a wrong username slipping past on
    /// timing, a cookie header the browser actually sends.
    /// </summary>
    public class AdminAuthPolicyTests
    {
        private const string User = "hermit";
        private static readonly string Hash = AccountPolicy.HashPassword("swab-the-deck+9");

        // ---- config parsing ------------------------------------------------

        [Fact]
        public void Split_separates_username_from_credential_on_the_first_colon()
        {
            Assert.True(AdminAuthPolicy.TrySplitConfig("hermit:" + Hash, out string u, out string c));
            Assert.Equal("hermit", u);
            Assert.Equal(Hash, c);
        }

        [Fact]
        public void Split_keeps_a_hash_with_its_own_dollars_intact()
        {
            // The hash contains '$' and base64 '+/=', none of which is a colon,
            // so first-colon splitting recovers it byte-for-byte.
            Assert.True(AdminAuthPolicy.TrySplitConfig("hermit:" + Hash, out _, out string c));
            Assert.True(AccountPolicy.VerifyPassword("swab-the-deck+9", c));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("nocolon")]
        [InlineData(":onlycredential")]
        [InlineData("onlyuser:")]
        public void Split_fails_closed_on_a_malformed_value(string? configured)
        {
            Assert.False(AdminAuthPolicy.TrySplitConfig(configured, out _, out _));
        }

        [Fact]
        public void LooksLikeStoredHash_recognises_a_real_pbkdf2_string()
        {
            Assert.True(AdminAuthPolicy.LooksLikeStoredHash(Hash));
        }

        [Theory]
        [InlineData("swab-the-deck+9")]
        [InlineData("plaintext")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("bcrypt$sha256$1$a$b")]
        public void LooksLikeStoredHash_rejects_anything_that_is_not_our_hash(string? credential)
        {
            Assert.False(AdminAuthPolicy.LooksLikeStoredHash(credential));
        }

        // ---- verification --------------------------------------------------

        [Fact]
        public void Verify_accepts_the_right_username_and_password()
        {
            Assert.True(AdminAuthPolicy.Verify("hermit", "swab-the-deck+9", User, Hash));
        }

        [Fact]
        public void Verify_trims_the_attempted_username()
        {
            Assert.True(AdminAuthPolicy.Verify("  hermit  ", "swab-the-deck+9", User, Hash));
        }

        [Fact]
        public void Verify_rejects_a_wrong_password()
        {
            Assert.False(AdminAuthPolicy.Verify("hermit", "wrong", User, Hash));
        }

        [Fact]
        public void Verify_rejects_a_wrong_username_even_with_a_password_that_would_match()
        {
            Assert.False(AdminAuthPolicy.Verify("intruder", "swab-the-deck+9", User, Hash));
        }

        [Fact]
        public void Verify_rejects_null_credentials()
        {
            Assert.False(AdminAuthPolicy.Verify(null, null, User, Hash));
        }

        // ---- cookie handling ----------------------------------------------

        [Fact]
        public void Token_is_pulled_out_of_a_multi_cookie_header()
        {
            string? token = AdminAuthPolicy.TokenFromCookieHeader(
                "theme=dark; " + AdminAuthPolicy.CookieName + "=abc123; other=1");
            Assert.Equal("abc123", token);
        }

        [Fact]
        public void Token_is_found_as_the_only_cookie()
        {
            Assert.Equal("solo", AdminAuthPolicy.TokenFromCookieHeader(
                AdminAuthPolicy.CookieName + "=solo"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("theme=dark; other=1")]
        [InlineData("wa_admin=")]
        public void Token_is_null_when_the_cookie_is_absent_or_empty(string? header)
        {
            Assert.Null(AdminAuthPolicy.TokenFromCookieHeader(header));
        }

        [Fact]
        public void Session_cookie_is_scoped_hardened_and_carries_the_token()
        {
            string cookie = AdminAuthPolicy.BuildSessionCookie("tok", 3600);

            Assert.Contains(AdminAuthPolicy.CookieName + "=tok", cookie);
            Assert.Contains("Path=" + AdminAuthPolicy.CookiePath, cookie);
            Assert.Contains("HttpOnly", cookie);
            Assert.Contains("SameSite=Strict", cookie);
            Assert.Contains("Max-Age=3600", cookie);
        }

        [Fact]
        public void Clear_cookie_expires_immediately()
        {
            Assert.Contains("Max-Age=0", AdminAuthPolicy.BuildClearCookie());
        }
    }
}

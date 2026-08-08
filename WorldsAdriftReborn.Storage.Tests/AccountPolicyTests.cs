using WorldsAdriftReborn.Storage.Policy;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// Every assertion here stands for something the client or the player does
    /// that breaks when the rule is broken; the comments in AccountPolicy name
    /// them.
    /// </summary>
    public class AccountPolicyTests
    {
        /// <summary>
        /// A fixed instant. Nothing in this file touches a database or the wall
        /// clock, so these run on any machine with no setup at all.
        /// </summary>
        private static readonly DateTimeOffset Instant =
            new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

        // ---- usernames -----------------------------------------------------

        [Fact]
        public void Two_capitalisations_of_one_name_are_the_same_account()
        {
            Assert.Equal(
                AccountPolicy.NormalizeUsername("Timu"),
                AccountPolicy.NormalizeUsername("TIMU"));

            Assert.Equal("timu", AccountPolicy.NormalizeUsername("Timu"));
        }

        [Fact]
        public void The_key_is_lowercased_invariantly_so_the_servers_locale_cannot_change_it()
        {
            // ToLower under a Turkish locale maps 'I' to a dotless 'i'. The
            // server runs under Wine, where the locale is whatever the prefix
            // says, so a culture-sensitive fold would key the same typed name
            // differently on two machines.
            Assert.Equal("ilan", AccountPolicy.NormalizeUsername("Ilan"));
        }

        [Fact]
        public void Whitespace_a_player_cannot_see_does_not_create_a_second_account()
        {
            Assert.Equal("timu", AccountPolicy.NormalizeUsername("  Timu  "));
            Assert.Equal("Timu", AccountPolicy.TypedUsername("  Timu  "));
        }

        [Fact]
        public void The_typed_form_keeps_the_capitalisation_the_player_chose()
        {
            Assert.Equal("TimuTheBold", AccountPolicy.TypedUsername("TimuTheBold"));
        }

        [Fact]
        public void An_email_address_is_a_usable_username_because_the_form_asks_for_one()
        {
            // The shipped login form labels its first field "Email Address". A
            // player who types an email into a box that says Email Address is
            // behaving correctly and must not be refused.
            Assert.True(AccountPolicy.IsUsableUsername("tim.james+wa@example.com"));
        }

        [Fact]
        public void An_ordinary_username_is_usable()
        {
            Assert.True(AccountPolicy.IsUsableUsername("timu"));
            Assert.True(AccountPolicy.IsUsableUsername("Long_John-Silver"));
            Assert.True(AccountPolicy.IsUsableUsername("abc"));
        }

        [Fact]
        public void A_username_that_would_be_rendered_as_markup_is_refused()
        {
            // The username comes back as screenName and is drawn by the client.
            Assert.False(AccountPolicy.IsUsableUsername("<b>timu</b>"));
            Assert.False(AccountPolicy.IsUsableUsername("timu/../etc"));
            Assert.False(AccountPolicy.IsUsableUsername("timu\nadmin"));
            Assert.False(AccountPolicy.IsUsableUsername("timu bones"));
        }

        [Fact]
        public void An_empty_or_missing_username_is_refused_before_it_can_be_stored()
        {
            Assert.False(AccountPolicy.IsUsableUsername(null));
            Assert.False(AccountPolicy.IsUsableUsername(""));
            Assert.False(AccountPolicy.IsUsableUsername("   "));
            Assert.Equal(string.Empty, AccountPolicy.NormalizeUsername(null));
        }

        [Fact]
        public void A_username_of_nothing_but_punctuation_is_refused()
        {
            Assert.False(AccountPolicy.IsUsableUsername("..."));
            Assert.False(AccountPolicy.IsUsableUsername("---"));
        }

        [Fact]
        public void Usernames_outside_the_length_bounds_are_refused()
        {
            Assert.False(AccountPolicy.IsUsableUsername(new string('a', AccountPolicy.MinUsernameLength - 1)));
            Assert.True(AccountPolicy.IsUsableUsername(new string('a', AccountPolicy.MinUsernameLength)));
            Assert.True(AccountPolicy.IsUsableUsername(new string('a', AccountPolicy.MaxUsernameLength)));
            Assert.False(AccountPolicy.IsUsableUsername(new string('a', AccountPolicy.MaxUsernameLength + 1)));
        }

        // ---- passwords -----------------------------------------------------

        [Fact]
        public void A_stored_hash_carries_its_algorithm_so_moving_to_argon2_is_a_migration()
        {
            string hash = AccountPolicy.HashPassword("correct horse battery staple");
            string[] parts = hash.Split('$');

            Assert.Equal(5, parts.Length);
            Assert.Equal("pbkdf2", parts[0]);
            Assert.Equal("sha256", parts[1]);
            Assert.Equal("210000", parts[2]);
        }

        [Fact]
        public void A_stored_hash_uses_the_documented_salt_and_output_sizes()
        {
            string[] parts = AccountPolicy.HashPassword("hunter22").Split('$');

            Assert.Equal(AccountPolicy.SaltBytes, Convert.FromBase64String(parts[3]).Length);
            Assert.Equal(AccountPolicy.HashBytes, Convert.FromBase64String(parts[4]).Length);
            Assert.Equal(210_000, AccountPolicy.HashIterations);
        }

        [Fact]
        public void The_same_password_hashes_differently_every_time()
        {
            // A shared salt would mean one precomputation breaks every account
            // that chose the same password.
            Assert.NotEqual(
                AccountPolicy.HashPassword("hunter22"),
                AccountPolicy.HashPassword("hunter22"));
        }

        [Fact]
        public void The_password_the_player_typed_verifies_against_its_hash()
        {
            string hash = AccountPolicy.HashPassword("hunter22");

            Assert.True(AccountPolicy.VerifyPassword("hunter22", hash));
        }

        [Fact]
        public void A_wrong_password_does_not_verify()
        {
            string hash = AccountPolicy.HashPassword("hunter22");

            Assert.False(AccountPolicy.VerifyPassword("hunter23", hash));
            Assert.False(AccountPolicy.VerifyPassword("HUNTER22", hash));
            Assert.False(AccountPolicy.VerifyPassword("", hash));
            Assert.False(AccountPolicy.VerifyPassword(null, hash));
        }

        [Fact]
        public void A_hash_stored_with_a_different_iteration_count_still_verifies()
        {
            // The point of putting the cost in the string: raising it later must
            // not lock out everyone who signed up before the change.
            string cheap = string.Join(
                "$", "pbkdf2", "sha256", "1000",
                Convert.ToBase64String(new byte[16]),
                Convert.ToBase64String(System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
                    System.Text.Encoding.UTF8.GetBytes("hunter22"),
                    new byte[16],
                    1000,
                    System.Security.Cryptography.HashAlgorithmName.SHA256,
                    32)));

            Assert.True(AccountPolicy.VerifyPassword("hunter22", cheap));
        }

        [Fact]
        public void A_corrupt_hash_fails_one_login_rather_than_throwing()
        {
            // A row a human edited at 2am must not take the login server down.
            Assert.False(AccountPolicy.VerifyPassword("hunter22", null));
            Assert.False(AccountPolicy.VerifyPassword("hunter22", ""));
            Assert.False(AccountPolicy.VerifyPassword("hunter22", "hunter22"));
            Assert.False(AccountPolicy.VerifyPassword("hunter22", "pbkdf2$sha256$210000$"));
            Assert.False(AccountPolicy.VerifyPassword("hunter22", "pbkdf2$sha256$abc$AAAA$AAAA"));
            Assert.False(AccountPolicy.VerifyPassword("hunter22", "pbkdf2$sha256$210000$!!!$!!!"));
            Assert.False(AccountPolicy.VerifyPassword("hunter22", "argon2$x$1$AAAA$AAAA"));
        }

        [Fact]
        public void An_empty_password_is_never_hashed_into_a_working_credential()
        {
            Assert.Throws<ArgumentException>(() => AccountPolicy.HashPassword(""));
            Assert.Throws<ArgumentException>(() => AccountPolicy.HashPassword(null!));
            Assert.False(AccountPolicy.IsUsablePassword(""));
            Assert.False(AccountPolicy.IsUsablePassword(null));
        }

        [Fact]
        public void The_password_rule_is_length_only_so_it_cannot_lock_a_friend_out()
        {
            Assert.True(AccountPolicy.IsUsablePassword("a b c d"));
            Assert.True(AccountPolicy.IsUsablePassword("!!!!!!!!"));
            Assert.False(AccountPolicy.IsUsablePassword(new string('a', AccountPolicy.MinPasswordLength - 1)));
            Assert.True(AccountPolicy.IsUsablePassword(new string('a', AccountPolicy.MinPasswordLength)));
            Assert.False(AccountPolicy.IsUsablePassword(new string('a', AccountPolicy.MaxPasswordLength + 1)));
        }

        [Fact]
        public void The_dummy_hash_is_a_real_hash_that_no_password_matches()
        {
            Assert.StartsWith("pbkdf2$sha256$", AccountPolicy.DummyHash, StringComparison.Ordinal);
            Assert.False(AccountPolicy.VerifyPassword("hunter22", AccountPolicy.DummyHash));
        }

        // ---- session tokens -------------------------------------------------

        [Fact]
        public void A_session_token_is_url_and_config_file_safe()
        {
            // It travels in a header and, in the pairing flow, through a config
            // file a human copies. '+' and '/' survive neither reliably.
            for (int i = 0; i < 200; i++)
            {
                string token = AccountPolicy.NewSessionToken();

                Assert.DoesNotContain('+', token);
                Assert.DoesNotContain('/', token);
                Assert.DoesNotContain('=', token);
                Assert.All(token, c => Assert.True(
                    char.IsLetterOrDigit(c) || c == '-' || c == '_',
                    "unexpected character '" + c + "' in a session token"));
            }
        }

        [Fact]
        public void A_session_token_carries_the_full_32_bytes_of_entropy()
        {
            // 32 bytes unpadded base64 is 43 characters. Shorter would mean the
            // token was truncated somewhere, and it is a bearer credential.
            Assert.Equal(43, AccountPolicy.NewSessionToken().Length);
            Assert.Equal(32, AccountPolicy.SessionTokenBytes);
        }

        [Fact]
        public void Session_tokens_do_not_repeat()
        {
            HashSet<string> seen = new HashSet<string>();

            for (int i = 0; i < 1000; i++)
            {
                Assert.True(seen.Add(AccountPolicy.NewSessionToken()));
            }
        }

        [Fact]
        public void A_token_lives_thirty_days_so_it_cannot_expire_inside_a_session()
        {
            // The client's 28-minute refresh re-authenticates Steam-only, and a
            // failed refresh calls an empty delegate and schedules nothing
            // further. There is no recovery path, so expiry must be unreachable
            // for someone who is playing.
            Assert.Equal(TimeSpan.FromDays(30), AccountPolicy.SessionLifetime);
            Assert.Equal(
                Instant.AddDays(30),
                AccountPolicy.ExpiryFrom(Instant));
        }

        [Fact]
        public void A_token_is_still_valid_at_the_exact_instant_it_expires()
        {
            DateTimeOffset expires = AccountPolicy.ExpiryFrom(Instant);

            Assert.False(AccountPolicy.IsExpired(expires, expires));
            Assert.False(AccountPolicy.IsExpired(expires, expires.AddTicks(-1)));
            Assert.True(AccountPolicy.IsExpired(expires, expires.AddTicks(1)));
        }

        // ---- steam ids -------------------------------------------------------

        [Fact]
        public void The_placeholder_a_steamless_client_sends_is_not_a_steam_id()
        {
            // A client with no Steam sends the literal "steamUserId". Linking on
            // it would make every Steam-less player one shared account, and the
            // partial unique index would then reject the second of them.
            Assert.False(AccountPolicy.IsRealSteamUserKey("steamUserId"));
            Assert.False(AccountPolicy.IsRealSteamUserKey(null));
            Assert.False(AccountPolicy.IsRealSteamUserKey(""));
            Assert.False(AccountPolicy.IsRealSteamUserKey("1234"));
            Assert.False(AccountPolicy.IsRealSteamUserKey("7656119801234567x"));
        }

        [Fact]
        public void A_real_seventeen_digit_steam_id_is_accepted()
        {
            Assert.True(AccountPolicy.IsRealSteamUserKey("76561198012345678"));
            Assert.True(AccountPolicy.IsRealSteamUserKey(" 76561198012345678 "));
        }
    }
}

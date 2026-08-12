using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The in-memory player web-session set behind /login and /download. The rules
    /// that gate the download page: a fresh token resolves to its account, an
    /// unknown one never resolves, expiry is real and sliding, revocation is
    /// immediate, and two accounts never collide. The sibling of
    /// <see cref="AdminSessionsTests"/>, with the one difference that a session
    /// here carries WHICH account it stands for.
    /// </summary>
    public class PlayerSessionsTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.Zero);

        private const long Account = 4242;

        private static PlayerSessions Fresh(TimeSpan? life = null) =>
            new PlayerSessions(life ?? TimeSpan.FromDays(7));

        [Fact]
        public void An_issued_token_resolves_to_its_account_immediately()
        {
            PlayerSessions s = Fresh();
            string token = s.Issue(Account, T0);

            Assert.Equal(Account, s.Resolve(token, T0));
        }

        [Fact]
        public void An_unknown_token_never_resolves()
        {
            PlayerSessions s = Fresh();
            s.Issue(Account, T0);

            Assert.Null(s.Resolve("not-a-real-token", T0));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_blank_token_never_resolves(string? token)
        {
            PlayerSessions s = Fresh();
            Assert.Null(s.Resolve(token, T0));
        }

        [Fact]
        public void A_token_expires_after_the_lifetime_without_use()
        {
            PlayerSessions s = Fresh(TimeSpan.FromHours(1));
            string token = s.Issue(Account, T0);

            Assert.Null(s.Resolve(token, T0.AddHours(1).AddSeconds(1)));
        }

        [Fact]
        public void Use_slides_the_expiry_forward()
        {
            PlayerSessions s = Fresh(TimeSpan.FromHours(1));
            string token = s.Issue(Account, T0);

            // Used at 50 minutes: still valid, and now good for another hour.
            Assert.Equal(Account, s.Resolve(token, T0.AddMinutes(50)));
            Assert.Equal(Account, s.Resolve(token, T0.AddMinutes(50).AddMinutes(59)));
        }

        [Fact]
        public void An_expired_token_is_forgotten_so_it_cannot_come_back()
        {
            PlayerSessions s = Fresh(TimeSpan.FromHours(1));
            string token = s.Issue(Account, T0);

            Assert.Null(s.Resolve(token, T0.AddHours(2)));
            Assert.Equal(0, s.Count);
            // Even winding the clock back does not resurrect it.
            Assert.Null(s.Resolve(token, T0));
        }

        [Fact]
        public void Revoke_invalidates_immediately()
        {
            PlayerSessions s = Fresh();
            string token = s.Issue(Account, T0);

            s.Revoke(token);

            Assert.Null(s.Resolve(token, T0));
            Assert.Equal(0, s.Count);
        }

        [Fact]
        public void Distinct_tokens_are_issued_each_time()
        {
            PlayerSessions s = Fresh();
            Assert.NotEqual(s.Issue(Account, T0), s.Issue(Account, T0));
            Assert.Equal(2, s.Count);
        }

        [Fact]
        public void Two_accounts_get_their_own_sessions()
        {
            PlayerSessions s = Fresh();
            string a = s.Issue(1, T0);
            string b = s.Issue(2, T0);

            // Each token resolves to its own account, never the other's.
            Assert.Equal(1, s.Resolve(a, T0));
            Assert.Equal(2, s.Resolve(b, T0));
        }

        [Fact]
        public void The_default_lifetime_is_seven_days()
        {
            PlayerSessions s = new PlayerSessions();
            Assert.Equal((int)TimeSpan.FromDays(7).TotalSeconds, s.LifetimeSeconds);
        }
    }
}

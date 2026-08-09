using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The in-memory admin session set. The rules that gate every /admin route:
    /// a fresh token is valid, an unknown one never is, expiry is real and
    /// sliding, and revocation is immediate.
    /// </summary>
    public class AdminSessionsTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.Zero);

        private static AdminSessions Fresh(TimeSpan? life = null) =>
            new AdminSessions(life ?? TimeSpan.FromHours(12));

        [Fact]
        public void An_issued_token_is_valid_immediately()
        {
            AdminSessions s = Fresh();
            string token = s.Issue(T0);

            Assert.True(s.IsValid(token, T0));
        }

        [Fact]
        public void An_unknown_token_is_never_valid()
        {
            AdminSessions s = Fresh();
            s.Issue(T0);

            Assert.False(s.IsValid("not-a-real-token", T0));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_blank_token_is_never_valid(string? token)
        {
            AdminSessions s = Fresh();
            Assert.False(s.IsValid(token, T0));
        }

        [Fact]
        public void A_token_expires_after_the_lifetime_without_use()
        {
            AdminSessions s = Fresh(TimeSpan.FromHours(1));
            string token = s.Issue(T0);

            Assert.False(s.IsValid(token, T0.AddHours(1).AddSeconds(1)));
        }

        [Fact]
        public void Use_slides_the_expiry_forward()
        {
            AdminSessions s = Fresh(TimeSpan.FromHours(1));
            string token = s.Issue(T0);

            // Used at 50 minutes: still valid, and now good for another hour.
            Assert.True(s.IsValid(token, T0.AddMinutes(50)));
            Assert.True(s.IsValid(token, T0.AddMinutes(50).AddMinutes(59)));
        }

        [Fact]
        public void An_expired_token_is_forgotten_so_it_cannot_come_back()
        {
            AdminSessions s = Fresh(TimeSpan.FromHours(1));
            string token = s.Issue(T0);

            Assert.False(s.IsValid(token, T0.AddHours(2)));
            Assert.Equal(0, s.Count);
            // Even winding the clock back does not resurrect it.
            Assert.False(s.IsValid(token, T0));
        }

        [Fact]
        public void Revoke_invalidates_immediately()
        {
            AdminSessions s = Fresh();
            string token = s.Issue(T0);

            s.Revoke(token);

            Assert.False(s.IsValid(token, T0));
            Assert.Equal(0, s.Count);
        }

        [Fact]
        public void Distinct_tokens_are_issued_each_time()
        {
            AdminSessions s = Fresh();
            Assert.NotEqual(s.Issue(T0), s.Issue(T0));
            Assert.Equal(2, s.Count);
        }
    }
}

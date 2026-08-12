using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public class IslandResourceHandshakeTests
    {
        [Fact]
        public void MetalCount_unset_is_the_default()
        {
            Assert.Equal(IslandResourceHandshake.DefaultMetalCount, IslandResourceHandshake.MetalCount(null));
            Assert.Equal(IslandResourceHandshake.DefaultMetalCount, IslandResourceHandshake.MetalCount("   "));
        }

        [Fact]
        public void MetalCount_unparseable_is_the_default()
        {
            Assert.Equal(IslandResourceHandshake.DefaultMetalCount, IslandResourceHandshake.MetalCount("forty"));
            Assert.Equal(IslandResourceHandshake.DefaultMetalCount, IslandResourceHandshake.MetalCount("40.5"));
        }

        [Fact]
        public void MetalCount_parses_and_trims()
        {
            Assert.Equal(12, IslandResourceHandshake.MetalCount("12"));
            Assert.Equal(12, IslandResourceHandshake.MetalCount("  12 "));
        }

        [Fact]
        public void MetalCount_is_clamped_both_ends()
        {
            Assert.Equal(IslandResourceHandshake.MaxMetalCount, IslandResourceHandshake.MetalCount("100000"));
            Assert.Equal(0, IslandResourceHandshake.MetalCount("-5"));
        }

        [Fact]
        public void ClampCount_bounds()
        {
            Assert.Equal(0, IslandResourceHandshake.ClampCount(-1));
            Assert.Equal(0, IslandResourceHandshake.ClampCount(0));
            Assert.Equal(40, IslandResourceHandshake.ClampCount(40));
            Assert.Equal(IslandResourceHandshake.MaxMetalCount, IslandResourceHandshake.ClampCount(IslandResourceHandshake.MaxMetalCount + 1));
        }

        [Fact]
        public void Enabled_defaults_on()
        {
            Assert.True(IslandResourceHandshake.Enabled(null));
            Assert.True(IslandResourceHandshake.Enabled(""));
            Assert.True(IslandResourceHandshake.Enabled("1"));
            Assert.True(IslandResourceHandshake.Enabled("yes"));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("false")]
        [InlineData("FALSE")]
        [InlineData("off")]
        [InlineData("No")]
        public void Enabled_explicit_off(string v)
        {
            Assert.False(IslandResourceHandshake.Enabled(v));
        }

        [Fact]
        public void The_retry_schedule_is_non_empty_positive_and_increasing()
        {
            Assert.NotEmpty(IslandResourceHandshake.RequestRetrySeconds);
            double previous = 0;
            foreach (double s in IslandResourceHandshake.RequestRetrySeconds)
            {
                Assert.True(s > previous, "retry schedule must strictly increase from zero");
                previous = s;
            }
        }

        [Fact]
        public void Every_retry_lands_before_the_default_fallback_deadline()
        {
            // Otherwise the static table would take the island while requests are still
            // in flight, and the handshake would never get the chance it was given.
            Assert.True(IslandResourceHandshake.LastRetrySecond() < IslandResourceFallback.DefaultSeconds);
        }

        [Fact]
        public void The_retry_schedule_fits_inside_the_request_send_cap()
        {
            // The scheduled re-sends plus the initial send must not exhaust the cap on
            // their own, or the interest-re-declaration retry would be dead on arrival.
            Assert.True(IslandResourceHandshake.RequestRetrySeconds.Length + 1
                < IslandResourceHandshake.MaxRequestSends);
        }
    }
}

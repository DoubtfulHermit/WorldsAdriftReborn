using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The safe failure mode's policy: when the deadline fires, when it stands down, and
    /// - because the operator VERIFIES this deploy by grepping the log - that the two
    /// marker lines are exactly the strings the verification checklist greps for.
    /// </summary>
    public class IslandResourceFallbackTests
    {
        [Fact]
        public void Seconds_unset_is_the_default()
        {
            Assert.Equal(IslandResourceFallback.DefaultSeconds, IslandResourceFallback.Seconds(null));
            Assert.Equal(IslandResourceFallback.DefaultSeconds, IslandResourceFallback.Seconds("  "));
        }

        [Fact]
        public void Seconds_unparseable_is_the_default()
        {
            Assert.Equal(IslandResourceFallback.DefaultSeconds, IslandResourceFallback.Seconds("soon"));
        }

        [Fact]
        public void Seconds_parses_invariantly_and_trims()
        {
            Assert.Equal(45.5, IslandResourceFallback.Seconds(" 45.5 "));
        }

        [Fact]
        public void Seconds_is_clamped_both_ends()
        {
            Assert.Equal(IslandResourceFallback.MinSeconds, IslandResourceFallback.Seconds("0"));
            Assert.Equal(IslandResourceFallback.MinSeconds, IslandResourceFallback.Seconds("-5"));
            Assert.Equal(IslandResourceFallback.MaxSeconds, IslandResourceFallback.Seconds("999999"));
        }

        [Fact]
        public void Enabled_defaults_on()
        {
            Assert.True(IslandResourceFallback.Enabled(null));
            Assert.True(IslandResourceFallback.Enabled(""));
            Assert.True(IslandResourceFallback.Enabled("1"));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("false")]
        [InlineData("OFF")]
        [InlineData("No")]
        public void Enabled_explicit_off(string v)
        {
            Assert.False(IslandResourceFallback.Enabled(v));
        }

        [Fact]
        public void Falls_back_only_when_nothing_was_spawned()
        {
            Assert.True(IslandResourceFallback.ShouldFallBack(0, alreadyFiredOnce: false));
        }

        [Fact]
        public void Does_not_fall_back_when_the_handshake_produced_even_one_deposit()
        {
            // A partial reply is proof the mechanic works - the client replies in batches,
            // so "some but not all yet" is the normal mid-flight state.
            Assert.False(IslandResourceFallback.ShouldFallBack(1, alreadyFiredOnce: false));
            Assert.False(IslandResourceFallback.ShouldFallBack(40, alreadyFiredOnce: false));
        }

        [Fact]
        public void Never_falls_back_twice()
        {
            Assert.False(IslandResourceFallback.ShouldFallBack(0, alreadyFiredOnce: true));
        }

        [Fact]
        public void The_success_line_carries_the_handshake_marker_and_the_count()
        {
            string line = IslandResourceFallback.HandshakeLine(1234, 40, 40);
            Assert.Contains(IslandResourceFallback.HandshakeMarker, line);
            Assert.Contains("reply received, spawned 40", line);
            Assert.DoesNotContain(IslandResourceFallback.FallbackMarker, line);
        }

        [Fact]
        public void The_fallback_line_carries_the_fallback_marker_and_the_deadline()
        {
            string line = IslandResourceFallback.FallbackLine(1234, 90.0, 23);
            Assert.Contains(IslandResourceFallback.FallbackMarker, line);
            Assert.Contains("NO reply after 90s, falling back to static placements", line);
            Assert.DoesNotContain(IslandResourceFallback.HandshakeMarker, line);
        }

        [Fact]
        public void The_stood_down_line_counts_as_the_handshake_path()
        {
            string line = IslandResourceFallback.StoodDownLine(1234, 40, 40);
            Assert.Contains(IslandResourceFallback.HandshakeMarker, line);
            Assert.DoesNotContain(IslandResourceFallback.FallbackMarker, line);
        }

        [Fact]
        public void The_two_markers_are_distinguishable_by_a_plain_substring_grep()
        {
            // The whole verification checklist rests on this: neither marker may be a
            // substring of the other, or one grep would match both paths.
            Assert.DoesNotContain(IslandResourceFallback.HandshakeMarker, IslandResourceFallback.FallbackMarker);
            Assert.DoesNotContain(IslandResourceFallback.FallbackMarker, IslandResourceFallback.HandshakeMarker);
        }
    }
}

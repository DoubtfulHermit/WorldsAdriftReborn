using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The step-3 carry-test trigger grammar. It exists so a human can fire ONE
    /// ship move and watch whether a player standing on the hull travels with it -
    /// the single unverified assumption the whole ferry rests on.
    /// </summary>
    public class ShipNudgePolicyTests
    {
        [Fact]
        public void A_blank_line_is_the_default_five_metre_north_nudge()
        {
            Assert.True(ShipNudgePolicy.TryParseCommand("", out ShipNudge n, out string error));
            Assert.Equal(string.Empty, error);
            Assert.Equal(ShipNudgePolicy.Default, n);
            Assert.Equal(0.0, n.Dx);
            Assert.Equal(0.0, n.Dy);
            Assert.Equal(5.0, n.Dz);
        }

        [Fact]
        public void Whitespace_only_is_also_the_default_nudge_because_echo_should_just_work()
        {
            Assert.True(ShipNudgePolicy.TryParseCommand("   ", out ShipNudge n, out _));
            Assert.Equal(ShipNudgePolicy.Default, n);
        }

        [Fact]
        public void The_bare_keyword_is_the_default_nudge()
        {
            Assert.True(ShipNudgePolicy.TryParseCommand("nudge", out ShipNudge n, out _));
            Assert.Equal(ShipNudgePolicy.Default, n);

            Assert.True(ShipNudgePolicy.TryParseCommand("NUDGE", out ShipNudge upper, out _));
            Assert.Equal(ShipNudgePolicy.Default, upper);
        }

        [Fact]
        public void Nudge_with_a_distance_moves_that_far_north()
        {
            Assert.True(ShipNudgePolicy.TryParseCommand("nudge 12", out ShipNudge n, out _));
            Assert.Equal(new ShipNudge(0.0, 0.0, 12.0), n);

            Assert.True(ShipNudgePolicy.TryParseCommand("nudge -8.5", out ShipNudge south, out _));
            Assert.Equal(new ShipNudge(0.0, 0.0, -8.5), south);
        }

        [Fact]
        public void Three_numbers_are_an_explicit_translation()
        {
            Assert.True(ShipNudgePolicy.TryParseCommand("1 2 3", out ShipNudge n, out _));
            Assert.Equal(new ShipNudge(1.0, 2.0, 3.0), n);
        }

        [Fact]
        public void A_comment_is_nothing_to_do_and_says_so_silently()
        {
            Assert.False(ShipNudgePolicy.TryParseCommand("# move the ship later", out _, out string error));
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        public void A_null_line_is_nothing_to_do()
        {
            Assert.False(ShipNudgePolicy.TryParseCommand(null, out _, out string error));
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        public void Garbage_is_a_reported_error_not_a_silent_noop()
        {
            Assert.False(ShipNudgePolicy.TryParseCommand("nudge sideways", out _, out string one));
            Assert.NotEqual(string.Empty, one);

            Assert.False(ShipNudgePolicy.TryParseCommand("1 2 banana", out _, out string two));
            Assert.NotEqual(string.Empty, two);

            Assert.False(ShipNudgePolicy.TryParseCommand("1 2 3 4", out _, out string three));
            Assert.NotEqual(string.Empty, three);
        }

        [Fact]
        public void The_default_nudge_is_about_five_metres()
        {
            Assert.Equal(5.0, ShipNudgePolicy.Default.Magnitude, 6);
        }
    }
}

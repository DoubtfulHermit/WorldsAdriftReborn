using WorldsAdriftReborn.Storage.Policy;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// The pure rules the admin panel applies to a server name before it is
    /// stored. Each assertion stands for a way a raw operator input could reach
    /// the in-game browser and misbehave if the rule were absent.
    /// </summary>
    public class ServerConfigPolicyTests
    {
        [Fact]
        public void Normalize_trims_outer_whitespace()
        {
            Assert.Equal("Skyport", ServerConfigPolicy.Normalize("   Skyport   "));
        }

        [Fact]
        public void Normalize_collapses_internal_whitespace_runs()
        {
            Assert.Equal("The Anchor Tavern",
                ServerConfigPolicy.Normalize("The   Anchor\t\tTavern"));
        }

        [Fact]
        public void Normalize_collapses_pasted_newlines_to_a_single_space()
        {
            Assert.Equal("Line One Line Two",
                ServerConfigPolicy.Normalize("Line One\nLine Two"));
        }

        [Fact]
        public void Normalize_of_null_is_empty()
        {
            Assert.Equal(string.Empty, ServerConfigPolicy.Normalize(null));
        }

        [Fact]
        public void Normalize_of_only_whitespace_is_empty()
        {
            Assert.Equal(string.Empty, ServerConfigPolicy.Normalize("   \t\n  "));
        }

        [Fact]
        public void Normalize_caps_length_after_collapsing()
        {
            string raw = new string('a', ServerConfigPolicy.MaxServerNameLength + 50);
            string normalized = ServerConfigPolicy.Normalize(raw);

            Assert.Equal(ServerConfigPolicy.MaxServerNameLength, normalized.Length);
        }

        [Fact]
        public void Normalize_counts_visible_characters_not_the_whitespace_a_paste_carried()
        {
            // Leading whitespace must not eat into the length budget.
            string raw = "          " + new string('b', ServerConfigPolicy.MaxServerNameLength);
            string normalized = ServerConfigPolicy.Normalize(raw);

            Assert.Equal(ServerConfigPolicy.MaxServerNameLength, normalized.Length);
            Assert.StartsWith("b", normalized);
        }

        [Fact]
        public void IsValid_accepts_an_ordinary_name()
        {
            Assert.True(ServerConfigPolicy.IsValid("The Anchor Tavern"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("     ")]
        [InlineData("\t\n")]
        public void IsValid_rejects_blank_input(string? raw)
        {
            Assert.False(ServerConfigPolicy.IsValid(raw));
        }

        [Fact]
        public void IsValid_accepts_a_name_that_only_needs_trimming()
        {
            Assert.True(ServerConfigPolicy.IsValid("  x  "));
        }

        [Fact]
        public void Default_name_is_the_historic_literal()
        {
            // The value that used to be hardcoded at the /deploymentStatus call
            // site. Pinned so an un-configured server reads exactly as before.
            Assert.Equal("awesome community server", ServerConfigPolicy.DefaultServerName);
        }
    }
}

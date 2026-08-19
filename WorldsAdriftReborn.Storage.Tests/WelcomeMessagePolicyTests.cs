using WorldsAdriftReborn.Storage.Policy;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// The pure rules the admin panel applies to the client's welcome message
    /// before it is stored. Unlike the server name, this field is PROSE: its
    /// newlines are the point, so the rules here keep paragraph structure and
    /// only tidy what a browser textarea and a paste add on top of it.
    ///
    /// Each assertion stands for a way a raw operator input could reach the
    /// game client, or the server_config CHECK constraint, and misbehave if the
    /// rule were absent.
    /// </summary>
    public class WelcomeMessagePolicyTests
    {
        [Fact]
        public void The_key_is_a_row_in_the_existing_config_table()
        {
            // Pinned because the whole point of this setting is that it is a KV
            // ROW. Production runs at schema 9; a migration to hold one string
            // would take persistence off for the length of a deploy.
            Assert.Equal("welcome_message", ServerConfigPolicy.WelcomeMessageKey);
        }

        [Fact]
        public void Normalize_of_null_returns_the_default_message()
        {
            Assert.Equal(ServerConfigPolicy.DefaultWelcomeMessage,
                ServerConfigPolicy.NormalizeWelcomeMessage(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\r\n")]
        [InlineData(" \t \n \t ")]
        public void Normalize_of_a_blank_input_returns_the_default_message(string raw)
        {
            // The server_config CHECK refuses a blank value outright, so an empty
            // normalisation could only ever reach the database as an exception.
            Assert.Equal(ServerConfigPolicy.DefaultWelcomeMessage,
                ServerConfigPolicy.NormalizeWelcomeMessage(raw));
        }

        [Fact]
        public void Normalize_turns_the_CRLF_a_browser_textarea_posts_into_newlines()
        {
            // A <textarea> POSTs CRLF per the HTML spec. The client must not have
            // to know that, and a stray \r renders as a box glyph in some fonts.
            Assert.Equal("first\n\nsecond",
                ServerConfigPolicy.NormalizeWelcomeMessage("first\r\n\r\nsecond"));
        }

        [Fact]
        public void Normalize_turns_a_lone_carriage_return_into_a_newline()
        {
            Assert.Equal("first\nsecond",
                ServerConfigPolicy.NormalizeWelcomeMessage("first\rsecond"));
        }

        [Fact]
        public void Normalize_keeps_the_single_blank_line_that_separates_paragraphs()
        {
            // The difference from the server name: newlines here are structure,
            // not whitespace to be collapsed away.
            Assert.Equal("one\n\ntwo\n\nthree",
                ServerConfigPolicy.NormalizeWelcomeMessage("one\n\ntwo\n\nthree"));
        }

        [Fact]
        public void Normalize_shortens_a_long_run_of_blank_lines()
        {
            // Six blank lines is a paste artefact, not an intention. Kept at the
            // documented maximum rather than removed, so deliberate spacing
            // survives.
            Assert.Equal("one\n\n\ntwo",
                ServerConfigPolicy.NormalizeWelcomeMessage("one\n\n\n\n\n\n\ntwo"));
        }

        [Fact]
        public void Normalize_trims_outer_whitespace_and_leading_blank_lines()
        {
            Assert.Equal("body",
                ServerConfigPolicy.NormalizeWelcomeMessage("\n\n   \n  body  \n\n\n"));
        }

        [Fact]
        public void Normalize_strips_trailing_whitespace_from_every_line()
        {
            // Invisible in the textarea, visible as ragged spacing anywhere the
            // client renders the text in a proportional or wrapped layout.
            Assert.Equal("one\ntwo",
                ServerConfigPolicy.NormalizeWelcomeMessage("one   \ntwo\t"));
        }

        [Fact]
        public void Normalize_does_not_truncate_an_over_long_message()
        {
            // Deliberate: silently storing the first 4000 characters would cut a
            // sentence in half in front of every player. IsValid refuses instead.
            string raw = new string('a', ServerConfigPolicy.MaxWelcomeMessageLength + 25);

            Assert.Equal(raw.Length, ServerConfigPolicy.NormalizeWelcomeMessage(raw).Length);
        }

        [Fact]
        public void Normalize_is_idempotent()
        {
            string once = ServerConfigPolicy.NormalizeWelcomeMessage(
                "  Greetings\r\n\r\n\r\n\r\n  Traveller  \r\n ");

            Assert.Equal(once, ServerConfigPolicy.NormalizeWelcomeMessage(once));
        }

        [Fact]
        public void The_shipped_default_survives_its_own_normalisation_unchanged()
        {
            // If it did not, a fresh server and a server whose operator pressed
            // Save without editing would greet players differently.
            Assert.Equal(ServerConfigPolicy.DefaultWelcomeMessage,
                ServerConfigPolicy.NormalizeWelcomeMessage(
                    ServerConfigPolicy.DefaultWelcomeMessage));
        }

        [Fact]
        public void The_shipped_default_is_a_message_the_panel_would_accept()
        {
            Assert.True(ServerConfigPolicy.IsValidWelcomeMessage(
                ServerConfigPolicy.DefaultWelcomeMessage));
        }

        [Fact]
        public void The_shipped_default_carries_the_operators_own_copy()
        {
            // Pinned line by line: this is the text players read, and a reflow
            // that quietly dropped a paragraph would be invisible from here.
            Assert.StartsWith("Greetings Traveller,\n\n",
                ServerConfigPolicy.DefaultWelcomeMessage, System.StringComparison.Ordinal);
            Assert.Contains("Worlds Adrift closed in 2019.",
                ServerConfigPolicy.DefaultWelcomeMessage, System.StringComparison.Ordinal);
            Assert.Contains("Nothing here is for sale.",
                ServerConfigPolicy.DefaultWelcomeMessage, System.StringComparison.Ordinal);
            Assert.EndsWith("See you in the skies.\n\n- The Wareborn crew",
                ServerConfigPolicy.DefaultWelcomeMessage, System.StringComparison.Ordinal);

            // Built from "\n" literals rather than a verbatim string, so a CRLF
            // checkout of the policy file cannot change what ships.
            Assert.DoesNotContain("\r", ServerConfigPolicy.DefaultWelcomeMessage,
                System.StringComparison.Ordinal);
        }

        [Fact]
        public void IsValid_accepts_an_ordinary_multi_paragraph_message()
        {
            Assert.True(ServerConfigPolicy.IsValidWelcomeMessage(
                "Welcome aboard.\r\n\r\nMind the rigging."));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("     ")]
        [InlineData("\t\n")]
        [InlineData("\r\n\r\n\r\n")]
        public void IsValid_rejects_blank_input(string? raw)
        {
            // Not merely cosmetic: the server_config CHECK rejects a blank value,
            // so letting one through would surface as a database exception on a
            // panel button rather than as a message the operator can read.
            Assert.False(ServerConfigPolicy.IsValidWelcomeMessage(raw));
        }

        [Fact]
        public void IsValid_rejects_a_message_longer_than_the_maximum()
        {
            string raw = new string('a', ServerConfigPolicy.MaxWelcomeMessageLength + 1);

            Assert.False(ServerConfigPolicy.IsValidWelcomeMessage(raw));
        }

        [Fact]
        public void IsValid_accepts_a_message_exactly_at_the_maximum()
        {
            string raw = new string('a', ServerConfigPolicy.MaxWelcomeMessageLength);

            Assert.True(ServerConfigPolicy.IsValidWelcomeMessage(raw));
        }

        [Fact]
        public void IsValid_measures_the_stored_form_not_the_line_endings_a_paste_carried()
        {
            // MaxWelcomeMessageLength characters of text, padded with CRLF pairs
            // that normalisation halves. Measuring the raw input would refuse a
            // message that fits.
            string body = new string('a', ServerConfigPolicy.MaxWelcomeMessageLength - 2);
            string raw = "   \r\n\r\n" + body + "\r\n\r\n   ";

            Assert.True(ServerConfigPolicy.IsValidWelcomeMessage(raw));
            Assert.Equal(body, ServerConfigPolicy.NormalizeWelcomeMessage(raw));
        }

        [Fact]
        public void The_bounds_are_the_documented_ones()
        {
            Assert.Equal(1, ServerConfigPolicy.MinWelcomeMessageLength);
            Assert.Equal(4000, ServerConfigPolicy.MaxWelcomeMessageLength);
        }
    }
}

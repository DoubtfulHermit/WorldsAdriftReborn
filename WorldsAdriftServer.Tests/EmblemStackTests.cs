using WorldsAdriftServer.Emblems;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The layered design and its code.
    ///
    /// THE CODE IS THE CACHE KEY, so the two properties that matter most here are
    /// that it ROUND-TRIPS exactly and that it is CANONICAL - one design, one
    /// string. The emblem route serves a year of immutable caching off the
    /// strength of that: if two strings could mean one design, or one string two,
    /// a cached crest could be the wrong picture and nothing would ever expire it.
    /// </summary>
    public class EmblemStackTests
    {
        private static EmblemLayer Layer(
            int obj, int x = 0, int y = 0, int size = 500, int rotation = 0,
            int colour = 0, int opacity = EmblemLayer.OpacitySteps,
            bool flipX = false, bool flipY = false, bool locked = false)
        {
            Assert.True(EmblemLayer.TryCreate(
                obj, x, y, size, rotation, colour, opacity, flipX, flipY, locked,
                out EmblemLayer layer));
            return layer;
        }

        private static EmblemStack Stack(params EmblemLayer[] layers)
        {
            Assert.True(EmblemStack.TryCreate(layers, out EmblemStack stack));
            return stack;
        }

        // ------------------------------------------------------------ the shape

        [Fact]
        public void An_empty_stack_is_the_version_and_nothing_else()
        {
            Assert.Equal("3-", EmblemStack.Empty.ToCode());
            Assert.Equal(0, EmblemStack.Empty.Count);
        }

        [Fact]
        public void One_layer_is_thirteen_characters()
        {
            string code = Stack(Layer(0)).ToCode();

            Assert.StartsWith("3-", code, StringComparison.Ordinal);
            Assert.Equal(2 + EmblemLayerCode.Width, code.Length);
        }

        /// <summary>
        /// The longest code this vocabulary can express, measured rather than
        /// asserted from memory. This is the number the whole storage decision
        /// rests on - see the note on <see cref="EmblemStack"/> - so it is worth a
        /// test rather than a comment.
        /// </summary>
        [Fact]
        public void A_full_twenty_layer_design_is_262_characters()
        {
            List<EmblemLayer> layers = new List<EmblemLayer>();
            for (int i = 0; i < EmblemStack.MaxLayers; i++) layers.Add(Layer(i % EmblemObjects.Count));

            string code = Stack(layers.ToArray()).ToCode();

            Assert.Equal(262, code.Length);
            Assert.Equal(EmblemArtwork.MaxCodeLength, code.Length);

            // And the whole URL, which is the thing that actually has to fit
            // somewhere, stays comfortably short.
            string url = EmblemUrlPolicy.PublicUrl(
                "http://host:8085", Guid.NewGuid(), EmblemArtwork.Of(Stack(layers.ToArray())));

            Assert.True(url.Length < 400, "the crest URL is " + url.Length + " characters");
        }

        // ------------------------------------------------------- the round trip

        [Fact]
        public void Every_field_survives_the_code_exactly()
        {
            EmblemStack stack = Stack(
                Layer(EmblemObjects.Count - 1, x: -2000, y: 2000, size: 10, rotation: 359,
                    colour: EmblemVocabulary.ColourCount - 1, opacity: 0,
                    flipX: true, flipY: true, locked: true),
                Layer(0, x: 2000, y: -2000, size: 2000, rotation: 0, colour: 0,
                    opacity: EmblemLayer.OpacitySteps));

            string code = stack.ToCode();

            Assert.True(EmblemArtwork.TryParse(code, out EmblemArtwork read));
            Assert.True(read.IsLayered);
            Assert.Equal(stack, read.Stack);
            Assert.Equal(code, read.ToCode());
        }

        [Fact]
        public void The_layer_order_is_kept_and_is_not_a_set()
        {
            EmblemStack ab = Stack(Layer(0), Layer(1));
            EmblemStack ba = Stack(Layer(1), Layer(0));

            Assert.NotEqual(ab.ToCode(), ba.ToCode());
            Assert.NotEqual(ab, ba);
        }

        /// <summary>
        /// CANONICAL: one design has exactly one string. Round-tripping any code
        /// this vocabulary produces must give back the identical bytes, or the
        /// route's immutable caching is unsound.
        /// </summary>
        [Fact]
        public void Re_encoding_a_parsed_code_gives_the_identical_string()
        {
            Random random = new Random(20260819);

            for (int trial = 0; trial < 500; trial++)
            {
                int count = random.Next(0, EmblemStack.MaxLayers + 1);
                List<EmblemLayer> layers = new List<EmblemLayer>();

                for (int i = 0; i < count; i++)
                {
                    layers.Add(Layer(
                        random.Next(0, EmblemObjects.Count),
                        random.Next(-EmblemLayer.MaxOffset, EmblemLayer.MaxOffset + 1),
                        random.Next(-EmblemLayer.MaxOffset, EmblemLayer.MaxOffset + 1),
                        random.Next(EmblemLayer.MinSize, EmblemLayer.MaxSize + 1),
                        random.Next(0, EmblemLayer.RotationSteps),
                        random.Next(0, EmblemVocabulary.ColourCount),
                        random.Next(0, EmblemLayer.OpacitySteps + 1),
                        random.Next(2) == 0, random.Next(2) == 0, random.Next(2) == 0));
                }

                string code = Stack(layers.ToArray()).ToCode();

                Assert.True(EmblemArtwork.TryParse(code, out EmblemArtwork read));
                Assert.Equal(code, read.ToCode());
            }
        }

        // --------------------------------------------------------- the refusals

        [Theory]
        [InlineData("3-0")]                    // a partial layer
        [InlineData("3-000000000000")]         // twelve, not thirteen
        [InlineData("3-00000000000000")]       // fourteen
        [InlineData("3-00000000000-0")]        // a hyphen is not in the alphabet
        [InlineData("3-000000000000 ")]        // nor is a space
        [InlineData("3-00000000000éé")] // nor is anything unicode
        public void A_payload_that_is_not_a_whole_number_of_layers_is_refused(string code)
        {
            Assert.False(EmblemArtwork.TryParse(code, out _));
        }

        [Fact]
        public void More_than_twenty_layers_is_refused_rather_than_truncated()
        {
            List<EmblemLayer> twenty = new List<EmblemLayer>();
            for (int i = 0; i < EmblemStack.MaxLayers; i++) twenty.Add(Layer(0));

            Assert.True(EmblemStack.TryCreate(twenty, out _));

            twenty.Add(Layer(0));
            Assert.False(EmblemStack.TryCreate(twenty, out _));

            // And a hand-made code carrying twenty-one is refused too, so the
            // ceiling is a property of the FORMAT and not only of the editor.
            string code = "3-" + new string('0', 21 * EmblemLayerCode.Width);
            Assert.False(EmblemArtwork.TryParse(code, out _));
        }

        /// <summary>
        /// A layer whose numbers are out of range is refused wholesale. Not
        /// clamped, and not "that layer dropped": a code carrying a value this
        /// build does not understand is a code from a different vocabulary, and
        /// drawing part of it would show a player an emblem they never made.
        /// </summary>
        [Fact]
        public void One_bad_layer_refuses_the_whole_code()
        {
            EmblemStack good = Stack(Layer(0), Layer(1), Layer(2));
            string code = good.ToCode();

            // Size zero is under the floor; the alphabet's first character is 0.
            string broken = code.Substring(0, 2 + EmblemLayerCode.Width + 6) + "00"
                + code.Substring(2 + EmblemLayerCode.Width + 8);

            Assert.Equal(code.Length, broken.Length);
            Assert.False(EmblemArtwork.TryParse(broken, out _));
        }

        [Fact]
        public void An_unknown_flag_bit_is_refused_rather_than_masked_off()
        {
            string code = Stack(Layer(0)).ToCode();

            // The flags are the last character; 8 is the first bit we have no
            // meaning for.
            string future = code.Substring(0, code.Length - 1) + EmblemLayerCode.Alphabet[8];

            Assert.False(EmblemArtwork.TryParse(future, out _));
        }

        [Fact]
        public void Nothing_in_the_alphabet_needs_escaping_in_a_url_or_a_column()
        {
            Assert.Equal(64, EmblemLayerCode.Alphabet.Length);
            Assert.Equal(64, EmblemLayerCode.Alphabet.Distinct().Count());

            foreach (char c in EmblemLayerCode.Alphabet)
            {
                bool unreserved = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                    || c == '.' || c == '_' || c == '~';

                Assert.True(unreserved, "'" + c + "' is not unreserved in a URL");

                // The hyphen is deliberately absent: it is the heraldic code's
                // separator, and a payload that could contain one would make
                // telling the two forms apart a matter of counting.
                Assert.NotEqual('-', c);
            }
        }

        [Fact]
        public void A_code_survives_url_encoding_unchanged()
        {
            string code = Stack(
                Layer(EmblemObjects.Count - 1, x: -2000, y: 1999, size: 2000, rotation: 359,
                    colour: EmblemVocabulary.ColourCount - 1, opacity: EmblemLayer.OpacitySteps,
                    flipX: true, flipY: true, locked: true)).ToCode();

            Assert.Equal(code, Uri.EscapeDataString(code));
        }
    }
}

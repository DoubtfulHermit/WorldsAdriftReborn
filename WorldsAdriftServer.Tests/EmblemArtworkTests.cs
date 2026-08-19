using System.Security.Cryptography;
using WorldsAdriftServer.Emblems;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// THE COMPATIBILITY SUITE. An alliance is wearing a crest right now, and the
    /// string in its <c>emblem_url</c> column is
    /// <c>wareborn:emblem:2-0-7-39-9-9-4</c>.
    ///
    /// The layered editor did NOT migrate that into layers, and this file is where
    /// that decision is held. A heraldic crest has a rim inked round the shield's
    /// own outline, a division clipped to that shield, a keyline round the device
    /// and a top-lit gradient; the layered model has none of those by design. Any
    /// conversion would be an approximation, and the player-visible result of
    /// shipping one is that somebody's emblem quietly becomes a different picture.
    /// So both forms survive, the old renderer is untouched, and the assertions
    /// below are about that being true rather than about it having been intended.
    /// </summary>
    public class EmblemArtworkTests
    {
        /// <summary>The code in the live database at the time the layered editor
        /// was written.</summary>
        private const string LiveCode = "2-0-7-39-9-9-4";

        // ------------------------------------------------------ the older forms

        [Theory]
        [InlineData(LiveCode)]
        [InlineData("1-0-6-3-1-7-13")]
        [InlineData("2-4-9-10-5-13-0")]
        public void An_older_code_still_parses_and_is_still_heraldic(string code)
        {
            Assert.True(EmblemArtwork.TryParse(code, out EmblemArtwork artwork));

            Assert.False(artwork.IsLayered);
            Assert.False(artwork.IsBlank);
        }

        /// <summary>
        /// The heraldic path is LITERALLY the old renderer, not a re-implementation
        /// that happens to look the same. If somebody ever routes it through the
        /// layer painter, this stops being true immediately.
        /// </summary>
        [Fact]
        public void A_heraldic_emblem_is_drawn_by_the_heraldic_painter()
        {
            Assert.True(EmblemSpec.TryParse(LiveCode, out EmblemSpec spec));

            byte[] throughArtwork = EmblemArtwork.Of(spec).RenderPixels(64);
            byte[] throughPainter = EmblemPainter.Render(spec, 64);

            Assert.Equal(throughPainter, throughArtwork);
            Assert.Equal(EmblemSvg.Compose(spec), EmblemArtwork.Of(spec).ToSvg());
        }

        /// <summary>
        /// A GOLDEN. The live emblem's pixels, pinned, so that any future change to
        /// the geometry, the palette's first sixteen entries, the shading or the
        /// supersampling has to be a deliberate one that comes here and says so.
        ///
        /// A hash rather than a stored image because the failure this guards
        /// against is "somebody changed a shared constant", and for that a single
        /// value that either matches or does not is more useful than 262,144 bytes
        /// nobody will diff.
        /// </summary>
        [Fact]
        public void The_live_alliances_crest_still_renders_the_same_pixels()
        {
            Assert.True(EmblemArtwork.TryParse(LiveCode, out EmblemArtwork artwork));

            byte[] pixels = artwork.RenderPixels(EmblemPainter.Size);
            string digest = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();

            Assert.Equal("a8a9ee760b99f39e5b552486234395d7b3e3ff78e5ca624e989116f0f72288df", digest);
        }

        /// <summary>
        /// THE GENERATED DEFAULTS ARE FROZEN TOO, and this is the subtler half of
        /// the same promise. An alliance that has never opened the editor has NO
        /// stored code: its crest is recomputed from its own guid on every request.
        /// The palette grew when the layered editor landed, and a roll taken across
        /// the table as it stands would have silently recoloured every one of those
        /// alliances. <see cref="EmblemSpec.DefaultFor"/> rolls in the frozen
        /// sixteen-colour space instead, which this asserts by consequence: no
        /// generated crest can name a colour that did not exist before.
        /// </summary>
        [Fact]
        public void No_generated_default_uses_a_colour_the_palette_did_not_already_have()
        {
            Random random = new Random(1789);

            for (int trial = 0; trial < 2000; trial++)
            {
                byte[] bytes = new byte[16];
                random.NextBytes(bytes);

                EmblemSpec spec = EmblemSpec.DefaultFor(new Guid(bytes));

                Assert.InRange(spec.FieldColour, 0, EmblemVocabulary.LegacyColourCount - 1);
                Assert.InRange(spec.DetailColour, 0, EmblemVocabulary.LegacyColourCount - 1);
                Assert.InRange(spec.ChargeColour, 0, EmblemVocabulary.LegacyColourCount - 1);
            }
        }

        [Fact]
        public void The_palette_only_ever_grew()
        {
            // The first sixteen are the ones every stored crest indexes into. A
            // test that pins them is a test that turns "we appended" from a claim
            // into a fact.
            int[] original =
            {
                0x1E2833, 0x3C4A57, 0x6E7F8B, 0xC9D3D8, 0xF2EDE1, 0x7D4D2A, 0xB07A46,
                0xE0B070, 0xA8321F, 0xD9603C, 0x4B934F, 0x204C8A, 0xBC9BE2, 0xEED059,
                0x2C6B52, 0x59C3D1,
            };

            Assert.True(EmblemVocabulary.ColourCount >= original.Length);

            for (int i = 0; i < original.Length; i++)
            {
                Assert.Equal(original[i], EmblemVocabulary.Palette[i]);
            }

            Assert.Equal(EmblemVocabulary.ColourCount, EmblemVocabulary.PaletteNames.Count);
        }

        // ------------------------------------------------------ the newer form

        [Fact]
        public void A_layered_code_parses_as_layered_and_round_trips()
        {
            Assert.True(EmblemLayer.TryCreate(3, 100, -200, 750, 45, 5, 30, true, false, true,
                out EmblemLayer layer));
            Assert.True(EmblemStack.TryCreate(new[] { layer }, out EmblemStack stack));

            string code = stack.ToCode();

            Assert.True(EmblemArtwork.TryParse(code, out EmblemArtwork artwork));
            Assert.True(artwork.IsLayered);
            Assert.Equal(code, artwork.ToCode());
            Assert.Equal(stack, artwork.Stack);
        }

        /// <summary>
        /// The two forms share no strings. A heraldic code is seven
        /// hyphen-separated integers; a layered payload contains no hyphen at all.
        /// </summary>
        [Fact]
        public void The_two_code_forms_cannot_be_mistaken_for_each_other()
        {
            Assert.False(EmblemSpec.TryParse("3-0000000000000", out _));
            Assert.False(EmblemStack.TryParsePayload("2-0-7-39-9-9-4", out _));

            Assert.True(EmblemArtwork.TryParse("2-0-7-39-9-9-4", out EmblemArtwork heraldic));
            Assert.False(heraldic.IsLayered);
        }

        [Fact]
        public void An_empty_layered_design_is_blank_and_a_heraldic_one_never_is()
        {
            Assert.True(EmblemArtwork.TryParse("3-", out EmblemArtwork empty));
            Assert.True(empty.IsBlank);

            Assert.True(EmblemArtwork.TryParse(LiveCode, out EmblemArtwork heraldic));
            Assert.False(heraldic.IsBlank);
        }

        [Fact]
        public void A_default_artwork_is_a_heraldic_one_and_never_throws()
        {
            EmblemArtwork artwork = default;

            Assert.False(artwork.IsLayered);
            Assert.NotNull(artwork.Stack);
            Assert.NotNull(artwork.ToCode());
            Assert.NotEmpty(artwork.RenderPixels(8));
        }

        // ------------------------------------------------------------ the gates

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("4-0")]
        [InlineData("nonsense")]
        [InlineData("3")]
        public void Anything_that_is_not_a_code_is_refused(string? code)
        {
            Assert.False(EmblemArtwork.TryParse(code, out _));
        }

        /// <summary>
        /// A length gate BEFORE any parsing, so a megabyte of junk in a query
        /// string cannot become a megabyte of allocation on an unauthenticated
        /// route.
        /// </summary>
        [Fact]
        public void An_absurdly_long_code_is_refused_without_being_parsed()
        {
            Assert.False(EmblemArtwork.TryParse("3-" + new string('0', 1_000_000), out _));
            Assert.False(EmblemArtwork.TryParse(new string('-', 1_000_000), out _));
        }

        [Fact]
        public void The_length_gate_admits_the_longest_legal_code()
        {
            List<EmblemLayer> layers = new List<EmblemLayer>();
            for (int i = 0; i < EmblemStack.MaxLayers; i++)
            {
                Assert.True(EmblemLayer.TryCreate(0, 0, 0, 500, 0, 0, 40, false, false, false,
                    out EmblemLayer layer));
                layers.Add(layer);
            }

            Assert.True(EmblemStack.TryCreate(layers, out EmblemStack stack));

            string code = stack.ToCode();
            Assert.Equal(EmblemArtwork.MaxCodeLength, code.Length);
            Assert.True(EmblemArtwork.TryParse(code, out _));
        }

        // ------------------------------------------------------------ the store

        [Fact]
        public void A_layered_design_stores_and_reads_back_through_the_column_marker()
        {
            Assert.True(EmblemLayer.TryCreate(7, -300, 400, 900, 210, 2, 12, false, true, false,
                out EmblemLayer layer));
            Assert.True(EmblemStack.TryCreate(new[] { layer }, out EmblemStack stack));

            EmblemArtwork artwork = EmblemArtwork.Of(stack);
            string stored = EmblemUrlPolicy.Store(artwork);

            Assert.StartsWith("wareborn:emblem:3-", stored, StringComparison.Ordinal);
            Assert.True(EmblemUrlPolicy.TryReadStored(stored, out EmblemArtwork back));
            Assert.Equal(artwork, back);
        }

        /// <summary>
        /// The URL a game client is handed carries the DESIGN, which is the whole
        /// reason it can be cached immutably: there is no address whose picture can
        /// change.
        /// </summary>
        [Fact]
        public void Two_different_designs_never_share_a_url()
        {
            Guid alliance = Guid.Parse("11111111-2222-3333-4444-555555555555");

            Assert.True(EmblemLayer.TryCreate(1, 0, 0, 500, 0, 0, 40, false, false, false, out EmblemLayer a));
            Assert.True(EmblemLayer.TryCreate(1, 0, 0, 501, 0, 0, 40, false, false, false, out EmblemLayer b));

            Assert.True(EmblemStack.TryCreate(new[] { a }, out EmblemStack first));
            Assert.True(EmblemStack.TryCreate(new[] { b }, out EmblemStack second));

            string one = EmblemUrlPolicy.PublicUrl("http://host", alliance, EmblemArtwork.Of(first));
            string two = EmblemUrlPolicy.PublicUrl("http://host", alliance, EmblemArtwork.Of(second));

            Assert.NotEqual(one, two);

            // Plain http, and NOT because a test said so - because the origin it
            // was given was. The game client's Mono TLS stack stops at TLS 1.0 and
            // cannot fetch https at all; see EmblemOrigin.
            Assert.StartsWith("http://", one, StringComparison.Ordinal);
        }

        [Fact]
        public void The_etag_distinguishes_the_two_forms_and_the_two_formats()
        {
            Assert.True(EmblemArtwork.TryParse(LiveCode, out EmblemArtwork heraldic));
            Assert.True(EmblemArtwork.TryParse("3-", out EmblemArtwork layered));

            string png = EmblemImages.ETag(heraldic, EmblemUrlPolicy.Format.Png);
            string svg = EmblemImages.ETag(heraldic, EmblemUrlPolicy.Format.Svg);

            Assert.NotEqual(png, svg);
            Assert.NotEqual(png, EmblemImages.ETag(layered, EmblemUrlPolicy.Format.Png));

            // The tag carries the CODE, and a version 3 code is a string no crest
            // has ever been served under - so an old cached tag cannot collide with
            // a new one and the tag's prefix did not need to move.
            Assert.Contains(LiveCode, png, StringComparison.Ordinal);
        }
    }
}

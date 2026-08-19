using WorldsAdriftServer.Emblems;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The emblem code, which is the entire attack surface of the crest builder.
    ///
    /// That claim is what these tests exist to hold. An emblem is six indices into
    /// closed tables, so there is no file, no MIME type, no path and no remote
    /// host anywhere in the feature - which means "is this input safe" reduces
    /// completely to "does TryParse refuse everything that is not a canonical
    /// code". If any of these pass something through, the reduction is false.
    /// </summary>
    public class EmblemSpecTests
    {
        [Fact]
        public void A_code_round_trips()
        {
            Assert.True(EmblemSpec.TryCreate(2, 5, 7, 11, 3, 13, out EmblemSpec spec));

            string code = spec.ToCode();
            Assert.Equal("2-2-5-7-11-3-13", code);

            Assert.True(EmblemSpec.TryParse(code, out EmblemSpec back));
            Assert.Equal(spec, back);
        }

        [Fact]
        public void Every_valid_combination_round_trips()
        {
            // Exhaustive over the shapes, divisions and devices, with the colour
            // axes sampled - the point is that no index in any position is lost or
            // aliased by the encoding, and this covers every device in the table
            // including all fifty traced ones.
            for (int shape = 0; shape < EmblemVocabulary.ShapeCount; shape++)
            for (int division = 0; division < EmblemVocabulary.DivisionCount; division++)
            for (int charge = 0; charge < EmblemVocabulary.ChargeCount; charge++)
            {
                int field = charge % EmblemVocabulary.ColourCount;
                int detail = (charge + 5) % EmblemVocabulary.ColourCount;
                int chargeColour = (charge + 11) % EmblemVocabulary.ColourCount;

                Assert.True(EmblemSpec.TryCreate(
                    shape, division, charge, field, detail, chargeColour, out EmblemSpec spec));
                Assert.True(EmblemSpec.TryParse(spec.ToCode(), out EmblemSpec back));
                Assert.Equal(spec, back);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1-0-0-0-0-0")]           // too few parts
        [InlineData("1-0-0-0-0-0-0-0")]       // too many parts
        [InlineData("3-0-0-0-0-0-0")]         // a version that does not exist yet
        [InlineData("0-0-0-0-0-0-0")]         // wrong version
        [InlineData("1-0-0-0-0-0-x")]         // not a number
        [InlineData("1-0-0-0-0-0--1")]        // negative, via an extra hyphen
        [InlineData("1--1-0-0-0-0-0")]
        [InlineData("1-0-0-0-0-0-+1")]        // signed
        [InlineData("1-0-0-0-0-0- 1")]        // padded
        [InlineData("1-0-0-0-0-0-1 ")]
        [InlineData("1-5-0-0-0-0-0")]         // shape out of range (5 shapes: 0..4)
        [InlineData("1-0-10-0-0-0-0")]        // division out of range
        [InlineData("1-0-0-14-0-0-0")]        // device out of range for version 1
        [InlineData("1-0-0-60-0-0-0")]        // a version 2 device index in a version 1 code
        [InlineData("2-0-0-61-0-0-0")]        // device out of range
        [InlineData("1-0-0-0-16-0-0")]        // field colour out of range
        [InlineData("1-0-0-0-0-16-0")]        // detail colour out of range
        [InlineData("1-0-0-0-0-0-16")]        // charge colour out of range
        [InlineData("1-0-0-0-0-0-999999999999999999999")] // overflows an int
        [InlineData("1_0_0_0_0_0_0")]
        [InlineData("../../etc/passwd")]
        [InlineData("<script>alert(1)</script>")]
        [InlineData("1-0-0-0-0-0-0; DROP TABLE alliances")]
        public void A_code_that_is_not_canonical_is_refused(string? code)
        {
            Assert.False(EmblemSpec.TryParse(code, out EmblemSpec spec));
            Assert.Equal(default, spec);
        }

        [Fact]
        public void An_absurdly_long_code_is_refused_without_allocating_it()
        {
            // The length gate runs BEFORE the split, so a megabyte of hyphens
            // cannot make the parser build a million-element array. Asserting the
            // refusal is the observable half; the point of the gate is the half
            // that is not observable.
            Assert.False(EmblemSpec.TryParse(new string('-', 100_000), out _));
            Assert.False(EmblemSpec.TryParse("1-0-0-0-0-0-" + new string('0', 500), out _));
        }

        [Fact]
        public void The_generated_default_is_stable_for_one_alliance()
        {
            Guid id = Guid.Parse("2f9b6f2e-1c31-4f4a-9a3e-8d0f9c6b7a10");

            EmblemSpec first = EmblemSpec.DefaultFor(id);
            EmblemSpec second = EmblemSpec.DefaultFor(id);

            Assert.Equal(first, second);

            // Derived from the GUID's own bytes, NOT from string.GetHashCode,
            // which is randomised per process - so this also has to hold across
            // runs, and the literal below is what pins that. If this line ever
            // fails after a runtime upgrade, every alliance's generated crest just
            // silently changed.
            Assert.Equal("2-3-4-40-6-1-4", first.ToCode());
        }

        [Fact]
        public void A_generated_default_always_has_a_visible_device_and_distinct_colours()
        {
            // The two ways a generated crest can come out looking broken rather
            // than merely unlucky: no device at all, or a device the same colour
            // as the field it sits on.
            Random random = new Random(20260819);

            for (int i = 0; i < 2000; i++)
            {
                byte[] bytes = new byte[16];
                random.NextBytes(bytes);
                EmblemSpec spec = EmblemSpec.DefaultFor(new Guid(bytes));

                Assert.NotEqual(EmblemVocabulary.Charge.None, spec.Charge);
                Assert.NotEqual(spec.FieldColour, spec.DetailColour);
                Assert.NotEqual(spec.FieldColour, spec.ChargeColour);
                Assert.NotEqual(spec.DetailColour, spec.ChargeColour);
            }
        }

        [Fact]
        public void Different_alliances_get_different_generated_crests()
        {
            // Not a uniqueness guarantee - the generated crests are drawn from a
            // deliberately narrow slice of the vocabulary (see DefaultFor) - but a
            // crest that ignored most of the uid would collide far
            // more often than that, and this is the cheap way to notice.
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            Random random = new Random(1234);

            for (int i = 0; i < 500; i++)
            {
                byte[] bytes = new byte[16];
                random.NextBytes(bytes);
                seen.Add(EmblemSpec.DefaultFor(new Guid(bytes)).ToCode());
            }

            Assert.True(seen.Count > 480, "500 alliances produced only " + seen.Count + " distinct crests.");
        }

        [Fact]
        public void The_vocabulary_tables_and_their_names_are_the_same_length()
        {
            // The builder renders a dropdown by index over the NAMES and stores
            // the index against the ENUM. A table longer than its names offers a
            // choice with no label; shorter, and a stored index has no picture.
            Assert.Equal(EmblemVocabulary.ShapeCount, EmblemVocabulary.ShapeNames.Count);
            Assert.Equal(EmblemVocabulary.DivisionCount, EmblemVocabulary.DivisionNames.Count);
            Assert.Equal(EmblemVocabulary.ChargeCount, EmblemVocabulary.ChargeNames.Count);
            Assert.Equal(EmblemVocabulary.ChargeCount,
                EmblemVocabulary.FirstDrawnDevice + EmblemDeviceGeometry.Paths.Count);
            Assert.Equal(EmblemDeviceGeometry.Paths.Count, EmblemDeviceGeometry.Names.Count);
            Assert.Equal(EmblemVocabulary.ColourCount, EmblemVocabulary.PaletteNames.Count);

            Assert.Equal(EmblemVocabulary.ShapeNames.Count,
                Enum.GetValues(typeof(EmblemVocabulary.Shape)).Length);
            Assert.Equal(EmblemVocabulary.DivisionNames.Count,
                Enum.GetValues(typeof(EmblemVocabulary.Division)).Length);
            // The device enum names only the drawn-in-code half; the traced half
            // is index-addressed and named by the generated table. So the enum
            // covers exactly the entries below FirstDrawnDevice, and nothing
            // dispatches on the ones above it.
            Assert.Equal(EmblemVocabulary.FirstDrawnDevice,
                Enum.GetValues(typeof(EmblemVocabulary.Charge)).Length);
        }

        [Fact]
        public void Every_palette_colour_is_a_distinct_opaque_rgb()
        {
            HashSet<int> seen = new HashSet<int>();

            foreach (int colour in EmblemVocabulary.Palette)
            {
                Assert.InRange(colour, 0, 0xFFFFFF);
                Assert.True(seen.Add(colour), "duplicate palette colour 0x" + colour.ToString("x6"));
            }
        }
    }
}

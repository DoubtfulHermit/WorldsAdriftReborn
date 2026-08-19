using System.IO.Compression;
using WorldsAdriftServer.Emblems;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The renderer and the PNG encoder.
    ///
    /// Two properties matter and neither is "it looks nice", which no test can
    /// hold: the bytes must be a PNG that a strict decoder accepts (the client's
    /// decoder does NOT check whether it succeeded - it silently displays a
    /// garbage texture instead), and the same spec must always produce the same
    /// bytes (the URL carries the code and is served immutable for a year, so a
    /// renderer that drifted would leave players looking at a cached crest that no
    /// longer matches the one the builder shows).
    /// </summary>
    public class EmblemRenderTests
    {
        private static readonly byte[] Signature =
            { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private static EmblemSpec Spec(string code)
        {
            Assert.True(EmblemSpec.TryParse(code, out EmblemSpec spec));
            return spec;
        }

        [Fact]
        public void A_rendered_emblem_is_square_and_the_declared_size()
        {
            byte[] pixels = EmblemPainter.Render(Spec("1-0-0-2-11-7-4"));

            // Square is not cosmetic: the client's emblem Images set neither
            // preserveAspect nor SetNativeSize, so a non-square sprite arrives
            // stretched by an amount we cannot see.
            Assert.Equal(EmblemPainter.Size * EmblemPainter.Size * 4, pixels.Length);
        }

        [Fact]
        public void The_same_spec_always_renders_the_same_bytes()
        {
            EmblemSpec spec = Spec("1-3-6-11-8-3-13");

            byte[] first = PngWriter.Encode(
                EmblemPainter.Render(spec), EmblemPainter.Size, EmblemPainter.Size);
            byte[] second = PngWriter.Encode(
                EmblemPainter.Render(spec), EmblemPainter.Size, EmblemPainter.Size);

            Assert.Equal(first, second);
        }

        [Fact]
        public void Different_specs_render_different_bytes()
        {
            byte[] a = EmblemImages.Png(Spec("1-0-0-2-11-7-4"));
            byte[] b = EmblemImages.Png(Spec("1-0-0-2-11-7-5"));

            Assert.NotEqual(a, b);
        }

        [Fact]
        public void The_cache_returns_the_same_bytes_it_rendered()
        {
            EmblemSpec spec = Spec("1-4-9-12-5-13-0");

            byte[] cold = EmblemImages.Png(spec);
            byte[] warm = EmblemImages.Png(spec);

            Assert.Equal(cold, warm);
        }

        [Fact]
        public void The_corners_are_transparent_and_the_centre_is_not()
        {
            byte[] pixels = EmblemPainter.Render(Spec("1-0-0-0-11-7-4"));
            int size = EmblemPainter.Size;

            // Alpha is preserved by the client (ARGB32), so a crest that filled
            // its square would render as a coloured tile in the UI.
            Assert.Equal(0, pixels[3]);
            Assert.Equal(0, pixels[((size - 1) * size + (size - 1)) * 4 + 3]);

            int centre = ((size / 2) * size + (size / 2)) * 4;
            Assert.Equal(255, pixels[centre + 3]);
        }

        [Fact]
        public void Every_shape_renders_something_visible()
        {
            for (int shape = 0; shape < EmblemVocabulary.ShapeCount; shape++)
            {
                Assert.True(EmblemSpec.TryCreate(shape, 0, 2, 11, 7, 4, out EmblemSpec spec));
                byte[] pixels = EmblemPainter.Render(spec, 64);

                int opaque = 0;
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    if (pixels[i] > 200) opaque++;
                }

                // A shape whose containment test was inverted or mis-scaled would
                // come out empty or full; a real crest covers roughly a third to
                // three quarters of its square.
                double coverage = opaque / (double)(64 * 64);
                Assert.InRange(coverage, 0.25, 0.90);
            }
        }

        [Fact]
        public void Every_device_marks_the_field_it_sits_on()
        {
            // Device 0 is None and must NOT differ; every other device must -
            // including all fifty traced ones, so a device whose path failed to
            // parse into anything fillable is caught here rather than by a player.
            byte[] bare = EmblemPainter.Render(Spec("1-0-0-0-11-7-4"), 64);

            for (int charge = 1; charge < EmblemVocabulary.ChargeCount; charge++)
            {
                Assert.True(EmblemSpec.TryCreate(0, 0, charge, 11, 7, 4, out EmblemSpec spec));
                byte[] withCharge = EmblemPainter.Render(spec, 64);

                Assert.False(
                    bare.AsSpan().SequenceEqual(withCharge),
                    "device " + EmblemVocabulary.ChargeNames[charge] + " drew nothing.");
            }
        }

        [Fact]
        public void No_device_touches_the_rim_of_any_shape()
        {
            // The lozenge caught this one for real: a device sized for a roundel
            // put the star's tips through the lozenge's outline, where they were
            // silently CLIPPED - the painter tests the shape first, so an oversized
            // charge does not spill, it loses its points and nothing complains.
            //
            // So the assertion cannot be "did it paint outside". It is "is any
            // charge pixel touching the rim": find the pixels that adding the
            // device changed, and require that none of them is next to the outline
            // ink or to transparency. A clipped device always fails that.
            const int Size = 96;

            for (int shape = 0; shape < EmblemVocabulary.ShapeCount; shape++)
            for (int charge = 1; charge < EmblemVocabulary.ChargeCount; charge++)
            {
                Assert.True(EmblemSpec.TryCreate(shape, 0, 0, 11, 7, 4, out EmblemSpec bareSpec));
                Assert.True(EmblemSpec.TryCreate(shape, 0, charge, 11, 7, 4, out EmblemSpec spec));

                byte[] bare = EmblemPainter.Render(bareSpec, Size);
                byte[] drawn = EmblemPainter.Render(spec, Size);

                for (int y = 1; y < Size - 1; y++)
                for (int x = 1; x < Size - 1; x++)
                {
                    int p = (y * Size + x) * 4;
                    if (Same(bare, drawn, p)) continue;

                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int n = ((y + dy) * Size + (x + dx)) * 4;

                        Assert.False(
                            bare[n + 3] < 255 || IsOutlineInk(bare, n),
                            EmblemVocabulary.ChargeNames[charge] + " on the "
                            + EmblemVocabulary.ShapeNames[shape] + " reaches the rim at "
                            + x + "," + y + " - it is being clipped.");
                    }
                }
            }
        }

        private static bool Same(byte[] a, byte[] b, int offset) =>
            a[offset] == b[offset] && a[offset + 1] == b[offset + 1]
            && a[offset + 2] == b[offset + 2] && a[offset + 3] == b[offset + 3];

        /// <summary>
        /// The outline is the one thing the painter does NOT shade, so it is
        /// exactly the ink constant wherever it appears - which is what makes
        /// "is this pixel rim" answerable from the bytes alone.
        /// </summary>
        private static bool IsOutlineInk(byte[] pixels, int offset) =>
            pixels[offset] == ((EmblemVocabulary.OutlineInk >> 16) & 0xFF)
            && pixels[offset + 1] == ((EmblemVocabulary.OutlineInk >> 8) & 0xFF)
            && pixels[offset + 2] == (EmblemVocabulary.OutlineInk & 0xFF)
            && pixels[offset + 3] == 255;

        // ------------------------------------------------------------------ png

        [Fact]
        public void The_encoder_writes_a_structurally_valid_png()
        {
            byte[] png = EmblemImages.Png(Spec("1-2-1-3-9-4-0"));

            Assert.True(png.AsSpan(0, 8).SequenceEqual(Signature));

            // Walk the chunks: IHDR first, IEND last, every CRC correct.
            int offset = 8;
            List<string> chunks = new List<string>();

            while (offset < png.Length)
            {
                int length = ReadBigEndian(png, offset);
                string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
                chunks.Add(type);

                uint declared = (uint)ReadBigEndian(png, offset + 8 + length);
                uint actual = Crc(png, offset + 4, 4 + length);
                Assert.Equal(declared, actual);

                offset += 12 + length;
            }

            Assert.Equal(png.Length, offset);
            Assert.Equal("IHDR", chunks[0]);
            Assert.Equal("IEND", chunks[^1]);
            Assert.Contains("IDAT", chunks);
        }

        [Fact]
        public void The_header_declares_the_size_and_an_rgba_8_bit_image()
        {
            byte[] png = EmblemImages.Png(Spec("1-1-0-6-0-4-8"));

            Assert.Equal(EmblemPainter.Size, ReadBigEndian(png, 16));
            Assert.Equal(EmblemPainter.Size, ReadBigEndian(png, 20));
            Assert.Equal(8, png[24]);   // bit depth
            Assert.Equal(6, png[25]);   // colour type 6 = truecolour with alpha
            Assert.Equal(0, png[26]);   // deflate
            Assert.Equal(0, png[27]);   // adaptive filtering
            Assert.Equal(0, png[28]);   // no interlace
        }

        [Fact]
        public void The_idat_is_a_zlib_stream_that_inflates_back_to_the_pixels()
        {
            // The classic way a hand-rolled PNG comes out broken is writing a bare
            // deflate block where PNG wants a zlib stream. Inflating it with
            // ZLibStream is what proves the two-byte header and the adler32 are
            // there; comparing the result to the pixels proves the scanline filter
            // bytes are too.
            EmblemSpec spec = Spec("1-0-4-5-8-3-13");
            byte[] pixels = EmblemPainter.Render(spec);
            byte[] png = PngWriter.Encode(pixels, EmblemPainter.Size, EmblemPainter.Size);

            byte[] idat = ConcatenatedIdat(png);

            using MemoryStream compressed = new MemoryStream(idat);
            using ZLibStream inflate = new ZLibStream(compressed, CompressionMode.Decompress);
            using MemoryStream raw = new MemoryStream();
            inflate.CopyTo(raw);

            byte[] scanlines = raw.ToArray();
            int stride = EmblemPainter.Size * 4;

            Assert.Equal((stride + 1) * EmblemPainter.Size, scanlines.Length);

            for (int y = 0; y < EmblemPainter.Size; y++)
            {
                Assert.Equal(0, scanlines[y * (stride + 1)]);

                for (int x = 0; x < stride; x++)
                {
                    Assert.Equal(pixels[y * stride + x], scanlines[y * (stride + 1) + 1 + x]);
                }
            }
        }

        [Fact]
        public void The_encoder_refuses_a_buffer_that_is_not_the_declared_size()
        {
            Assert.Throws<ArgumentException>(() => PngWriter.Encode(new byte[10], 4, 4));
            Assert.Throws<ArgumentNullException>(() => PngWriter.Encode(null!, 4, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => PngWriter.Encode(Array.Empty<byte>(), 0, 0));
        }

        private static byte[] ConcatenatedIdat(byte[] png)
        {
            using MemoryStream idat = new MemoryStream();
            int offset = 8;

            while (offset < png.Length)
            {
                int length = ReadBigEndian(png, offset);
                string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
                if (type == "IDAT") idat.Write(png, offset + 8, length);
                offset += 12 + length;
            }

            return idat.ToArray();
        }

        private static int ReadBigEndian(byte[] bytes, int offset) =>
            (bytes[offset] << 24) | (bytes[offset + 1] << 16)
            | (bytes[offset + 2] << 8) | bytes[offset + 3];

        private static uint Crc(byte[] bytes, int offset, int length)
        {
            uint c = 0xFFFFFFFFu;

            for (int i = 0; i < length; i++)
            {
                c ^= bytes[offset + i];
                for (int k = 0; k < 8; k++)
                {
                    c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                }
            }

            return c ^ 0xFFFFFFFFu;
        }
    }
}

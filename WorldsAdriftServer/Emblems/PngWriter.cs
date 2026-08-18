using System.IO.Compression;

namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// A minimal, dependency-free PNG encoder: 8-bit RGBA, no interlacing, one
    /// IDAT.
    ///
    /// WHY HAND-ROLLED RATHER THAN A LIBRARY. The login server is published as a
    /// SELF-CONTAINED binary, so every dependency is a thing that has to come with
    /// it. ImageSharp would have worked and is pure managed, but SkiaSharp - the
    /// other obvious choice - carries a native libSkiaSharp per RID and turns a
    /// one-file publish into a publish with a payload. Against that, the whole
    /// encoder is the ninety lines below: PNG's container is four length-tag-
    /// data-CRC chunks, its only compression is zlib, and .NET 6 ships
    /// <see cref="ZLibStream"/> in the box. Adding a NuGet package to a deployed
    /// server to avoid ninety lines of well-specified format was the worse trade.
    ///
    /// The client's decoder is the constraint that made PNG mandatory rather than
    /// merely convenient: <c>HTTPResponse.DataAsTexture2D</c> builds an ARGB32
    /// texture and calls <c>Texture2D.LoadImage</c>, which in Unity 5.6 decodes
    /// PNG and JPEG and nothing else. PNG is the one of those two that keeps the
    /// alpha channel, and a crest with no alpha is a crest in a square box.
    ///
    /// Deterministic by construction: the same pixels always produce the same
    /// bytes. Filter type 0 on every row (no adaptive filtering - the emblems are
    /// flat colour, where filtering buys little and a heuristic would be one more
    /// thing that could differ between runtimes), and a fixed compression level.
    ///
    /// Pure: bytes in, bytes out. No disk, no clock.
    /// </summary>
    internal static class PngWriter
    {
        private static readonly byte[] Signature =
            { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>
        /// Encodes <paramref name="rgba"/> - <paramref name="width"/> *
        /// <paramref name="height"/> * 4 bytes, row-major, non-premultiplied - as
        /// a PNG.
        /// </summary>
        internal static byte[] Encode(byte[] rgba, int width, int height)
        {
            if (rgba == null) throw new ArgumentNullException(nameof(rgba));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (rgba.Length != width * height * 4)
            {
                throw new ArgumentException(
                    "Pixel buffer is " + rgba.Length + " bytes; " + width + "x" + height
                    + " RGBA needs " + (width * height * 4) + ".", nameof(rgba));
            }

            using MemoryStream png = new MemoryStream();
            png.Write(Signature, 0, Signature.Length);

            // IHDR: width, height, bit depth 8, colour type 6 (RGBA), deflate,
            // adaptive filtering, no interlace.
            byte[] ihdr = new byte[13];
            WriteBigEndian(ihdr, 0, (uint)width);
            WriteBigEndian(ihdr, 4, (uint)height);
            ihdr[8] = 8;
            ihdr[9] = 6;
            ihdr[10] = 0;
            ihdr[11] = 0;
            ihdr[12] = 0;
            WriteChunk(png, "IHDR", ihdr);

            WriteChunk(png, "IDAT", Deflate(Scanlines(rgba, width, height)));
            WriteChunk(png, "IEND", Array.Empty<byte>());

            return png.ToArray();
        }

        /// <summary>
        /// The raw image data PNG compresses: every row prefixed with its filter
        /// byte. Always 0 (None) - see the determinism note on the class.
        /// </summary>
        private static byte[] Scanlines(byte[] rgba, int width, int height)
        {
            int stride = width * 4;
            byte[] raw = new byte[(stride + 1) * height];

            for (int y = 0; y < height; y++)
            {
                int dst = y * (stride + 1);
                raw[dst] = 0;
                Buffer.BlockCopy(rgba, y * stride, raw, dst + 1, stride);
            }

            return raw;
        }

        private static byte[] Deflate(byte[] raw)
        {
            using MemoryStream compressed = new MemoryStream();

            // ZLibStream, not DeflateStream: PNG's IDAT is a zlib STREAM (2-byte
            // header, adler32 trailer), not a bare deflate block. Writing a bare
            // deflate here produces a file every decoder rejects, and it is the
            // classic way a hand-rolled PNG comes out broken.
            using (ZLibStream zlib = new ZLibStream(
                compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(raw, 0, raw.Length);
            }

            return compressed.ToArray();
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            byte[] header = new byte[4];
            WriteBigEndian(header, 0, (uint)data.Length);
            stream.Write(header, 0, 4);

            byte[] typeBytes =
            {
                (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3],
            };
            stream.Write(typeBytes, 0, 4);
            stream.Write(data, 0, data.Length);

            // The CRC covers the type AND the data, but not the length.
            uint crc = Crc32.Of(typeBytes, data);
            byte[] trailer = new byte[4];
            WriteBigEndian(trailer, 0, crc);
            stream.Write(trailer, 0, 4);
        }

        private static void WriteBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        /// <summary>The CRC-32 PNG specifies, with the usual 256-entry table.</summary>
        private static class Crc32
        {
            private static readonly uint[] Table = BuildTable();

            private static uint[] BuildTable()
            {
                uint[] table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                    {
                        c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                    }
                    table[n] = c;
                }
                return table;
            }

            internal static uint Of(byte[] first, byte[] second)
            {
                uint c = 0xFFFFFFFFu;
                foreach (byte b in first) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
                foreach (byte b in second) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
                return c ^ 0xFFFFFFFFu;
            }
        }
    }
}

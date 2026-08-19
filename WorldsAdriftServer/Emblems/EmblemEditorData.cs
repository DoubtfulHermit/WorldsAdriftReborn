using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// What the browser is told about the emblem vocabulary, as JSON.
    ///
    /// EVERYTHING THE EDITOR DRAWS COMES FROM HERE, and that is the point. The
    /// object palette's silhouettes, the live preview's outlines, the colour grid
    /// and the units the code is written in are all emitted from the same C#
    /// tables the rasteriser reads - so a shape appended to
    /// <see cref="EmblemObjects"/> appears on the panel with no edit to the page or
    /// the script, and a browser cannot be working from a different palette, a
    /// different set of shapes or a different idea of what a position means.
    ///
    /// THE CATALOGUE IS ITS OWN CACHEABLE DOCUMENT, not part of the page. Fifty
    /// traced devices are several hundred kilobytes of coordinates; inlining them
    /// would put that in front of a player on every visit to
    /// <c>/account</c>, including the visits that are about a password. So it is a
    /// separate response with the catalogue's own revision in its URL - which makes
    /// it immutable, exactly like the crest PNG and for exactly the same reason:
    /// the content is IN the address, so no cache can hold a stale copy, and a
    /// shape added tomorrow changes the URL rather than needing anybody to
    /// remember to bust anything.
    ///
    /// Pure: no clock, no disk, no request. Built once and cached in memory.
    /// </summary>
    internal static class EmblemEditorData
    {
        internal const string ContentType = "application/json; charset=utf-8";

        /// <summary>
        /// The catalogue's revision: a fold of every name and every coordinate in
        /// it.
        ///
        /// A HASH rather than a count, because the failure it guards against is a
        /// shape being RETOUCHED - same index, same name, different outline - which
        /// a count would sail straight past and which would leave every browser
        /// that had ever loaded the editor drawing the old artwork against a server
        /// drawing the new one.
        /// </summary>
        internal static string Revision => Built.Value.Revision;

        internal static string Catalogue => Built.Value.Json;

        /// <summary>
        /// The same document, gzipped.
        ///
        /// THIS IS THE ONE RESPONSE ON THIS SERVER BIG ENOUGH TO NEED IT. Two
        /// hundred more traced objects took the catalogue past a megabyte, which is
        /// more than everything else the account page loads put together; coordinate
        /// text folds to about a third of that, and the saving is paid on the first
        /// visit of every player who opens the editor. Everything else here is
        /// kilobytes and gets nothing from compressing, so this stays a property of
        /// the catalogue rather than becoming a layer in front of the whole router.
        ///
        /// Built once beside the text, never on a request. The identity body is
        /// still there and is still what a client that did not ask for gzip gets.
        /// </summary>
        internal static byte[] CatalogueGzip => Compressed.Value;

        private static readonly Lazy<byte[]> Compressed = new Lazy<byte[]>(Compress, isThreadSafe: true);

        private static byte[] Compress()
        {
            byte[] raw = Encoding.UTF8.GetBytes(Catalogue);

            using MemoryStream target = new MemoryStream(raw.Length / 2);

            // SmallestSize, not Optimal: this is compressed ONCE per process for a
            // body that a browser then keeps forever, so the only cost that matters
            // is the one on the wire.
            using (GZipStream gzip = new GZipStream(target, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzip.Write(raw, 0, raw.Length);
            }

            return target.ToArray();
        }

        /// <summary>The URL the page points the editor at.</summary>
        internal static string CatalogueUrl =>
            EmblemUrlPolicy.RoutePrefix + CatalogueName + "?" + RevisionParameter + "=" + Revision;

        internal const string CatalogueName = "objects.json";

        internal const string RevisionParameter = "v";

        private static readonly Lazy<(string Json, string Revision)> Built =
            new Lazy<(string, string)>(Build, isThreadSafe: true);

        private static (string Json, string Revision) Build()
        {
            StringBuilder json = new StringBuilder(512 * 1024);
            json.Append("{\"objects\":[");

            for (int i = 0; i < EmblemObjects.Count; i++)
            {
                EmblemObjects.Entry entry = EmblemObjects.All[i];

                if (i > 0) json.Append(',');

                json.Append("{\"n\":");
                Quote(json, entry.Name);
                json.Append(",\"c\":");
                Quote(json, entry.Category);

                // Present only when true, and true only rarely - so the flag costs
                // the wire nothing on the two hundred and eighty objects that are
                // simply offered. A hidden object is still SENT, at its own index,
                // because a crest that already uses one has to keep drawing it; the
                // flag only takes it off the panel.
                if (entry.Hidden) json.Append(",\"h\":1");

                // The SVG path data, emitted by the SAME writer the vector export
                // uses, off the SAME path object the rasteriser samples. The
                // browser never converts anything: it is handed a 'd' attribute.
                json.Append(",\"d\":\"");
                entry.Path.AppendPathData(json, 1.0, 0.0, EmblemObjects.Unit);
                json.Append("\"}");
            }

            json.Append("]}");

            string text = json.ToString();
            return (text, Fold(text));
        }

        /// <summary>
        /// The palette, as the page needs it: the hex the browser fills with and
        /// the name a player reads on hover.
        ///
        /// Small enough to live in the page itself, unlike the catalogue - sixteen
        /// or thirty-two short strings against half a megabyte of coordinates.
        /// </summary>
        internal static string PaletteJson()
        {
            StringBuilder json = new StringBuilder(1024);
            json.Append('[');

            for (int i = 0; i < EmblemVocabulary.ColourCount; i++)
            {
                if (i > 0) json.Append(',');

                json.Append("{\"h\":\"").Append(EmblemStackSvg.Hex(EmblemVocabulary.Palette[i]))
                    .Append("\",\"n\":");
                Quote(json, EmblemVocabulary.PaletteNames[i]);
                json.Append('}');
            }

            json.Append(']');
            return json.ToString();
        }

        /// <summary>
        /// The numbers the script must not be free to choose for itself: the code's
        /// alphabet and field widths, the limits a control clamps to, and the layer
        /// ceiling.
        ///
        /// Stamped in by the server for the reason <c>account.js</c>'s emblem
        /// version already is - a page that goes on building codes in a shape the
        /// parser has moved past produces saves that are silently refused, and the
        /// player is told only that something went wrong.
        /// </summary>
        internal static string LimitsJson()
        {
            StringBuilder json = new StringBuilder(512);

            json.Append("{\"version\":").Append(EmblemStack.Version)
                .Append(",\"maxLayers\":").Append(EmblemStack.MaxLayers)
                .Append(",\"unit\":").Append(EmblemLayer.Unit)
                .Append(",\"maxOffset\":").Append(EmblemLayer.MaxOffset)
                .Append(",\"minSize\":").Append(EmblemLayer.MinSize)
                .Append(",\"maxSize\":").Append(EmblemLayer.MaxSize)
                .Append(",\"rotationSteps\":").Append(EmblemLayer.RotationSteps)
                .Append(",\"opacitySteps\":").Append(EmblemLayer.OpacitySteps)
                .Append(",\"opacityUnit\":").Append(EmblemLayer.OpacityUnit)
                .Append(",\"codeWidth\":").Append(EmblemLayerCode.Width)
                .Append(",\"offsetBias\":").Append(EmblemLayerCode.OffsetBias)
                .Append(",\"alphabet\":\"").Append(EmblemLayerCode.Alphabet)
                .Append("\"}");

            return json.ToString();
        }

        /// <summary>
        /// A JSON string literal.
        ///
        /// Hand-rolled and deliberately paranoid rather than pulled from a
        /// serialiser: every name here comes from a table in this repository, but
        /// this output is embedded in an HTML page, and the one character that
        /// turns an embedded document back into markup is the one a naive escaper
        /// forgets. So the ASCII controls, both slashes and the angle brackets all
        /// go out as escapes.
        /// </summary>
        private static void Quote(StringBuilder json, string value)
        {
            json.Append('"');

            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    case '<': json.Append("\\u003c"); break;
                    case '>': json.Append("\\u003e"); break;
                    case '&': json.Append("\\u0026"); break;
                    default:
                        if (c < 0x20 || c == 0x7F)
                        {
                            json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            json.Append(c);
                        }
                        break;
                }
            }

            json.Append('"');
        }

        /// <summary>
        /// An FNV-1a fold of the whole catalogue, as eight hex digits.
        ///
        /// Not <c>string.GetHashCode</c>: that is randomised per process, so the
        /// catalogue URL would change on every restart and every browser would
        /// re-download half a megabyte for nothing.
        /// </summary>
        private static string Fold(string text)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in text)
                {
                    hash ^= (byte)(c & 0xFF);
                    hash *= 16777619u;
                    hash ^= (byte)(c >> 8);
                    hash *= 16777619u;
                }

                return hash.ToString("x8", CultureInfo.InvariantCulture);
            }
        }
    }
}

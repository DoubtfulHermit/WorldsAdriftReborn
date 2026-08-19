using System.Text;

namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// A layered emblem: up to twenty <see cref="EmblemLayer"/>s, bottom first.
    ///
    /// THE WHOLE DESIGN IS IN THE CODE, AND THE CODE IS IN THE URL. That is the
    /// one architectural decision in this file and it is deliberate, so it is
    /// worth stating against the alternative.
    ///
    /// The heraldic builder's crest URL is <c>?e=2-0-7-39-9-9-4</c> - the design
    /// itself, not a key to one. Because of that, a changed emblem is a DIFFERENT
    /// URL, which is what lets the route answer with
    /// <c>Cache-Control: immutable, max-age=1y</c> and lets the game client's
    /// always-on BestHTTP disk cache work for us: no cache anywhere, ours or the
    /// client's or a proxy's, can ever serve a stale crest, because there is no
    /// URL whose picture has changed. It is also what makes the builder's live
    /// preview possible at all: <see cref="EmblemHandler"/> is stateless and
    /// unauthenticated, so previewing a design nobody has saved is the same
    /// request with <c>preview</c> in place of an alliance uid.
    ///
    /// The obvious alternative for twenty layers is to store the design somewhere
    /// server-side and put a short content HASH in the URL. That preserves
    /// cache-safety, and it costs three things this design does not pay:
    /// <list type="bullet">
    /// <item>a STORE. Either a new table - and a schema migration means the game
    ///   server and the login server must ship together or persistence silently
    ///   stops (see <see cref="EmblemUrlPolicy"/>) - or a blob in the column,
    ///   which is the same bytes this code is, only base64 and unreadable;</item>
    /// <item>a LOOKUP on the render path. The emblem route is unauthenticated and
    ///   answers the game client directly; making it hit the database turns "a
    ///   crest is a pure function of its URL" into "a crest is whatever the
    ///   database said, if it was up";</item>
    /// <item>the PREVIEW. A hash addresses a design that has been SAVED. A player
    ///   dragging a layer around has not saved anything, so the editor would need
    ///   a scratch store, a lifetime for it, and a way to clean it up - a whole
    ///   subsystem to show somebody a picture of what they are already looking
    ///   at.</item>
    /// </list>
    ///
    /// And the reason usually given for the hash - that a twenty-layer design will
    /// not fit in a query string - does not survive measurement. A layer is
    /// THIRTEEN characters (see <see cref="EmblemLayerCode"/>), so the longest
    /// emblem this vocabulary can express is <c>3-</c> plus 260, and the whole URL
    /// including the alliance uid is under 330 characters. Query strings are
    /// limited by servers at four to eight kilobytes; the column is
    /// <c>TEXT</c>; the ETag is a header. Nothing here is close to a limit. So the
    /// design stays IN the URL, content-addressing stays a property of the format
    /// rather than of a hash function that could collide or a store that could
    /// drift from it, and no schema moves.
    ///
    /// Pure and immutable: no clock, no disk, no request.
    /// </summary>
    internal sealed class EmblemStack : IEquatable<EmblemStack>
    {
        /// <summary>
        /// The layer ceiling, and it is the retail editor's own: twenty, counted
        /// at the top of the layers panel.
        ///
        /// It is a RENDER budget as much as a design one. Every layer is another
        /// containment test at every one of the rasteriser's 1.6 million
        /// subsamples, so an unbounded stack is an unbounded request - and an
        /// unauthenticated route that will do arbitrary work for whatever is in a
        /// query string is a denial of service with extra steps.
        /// </summary>
        internal const int MaxLayers = 20;

        /// <summary>The version this form of the code is written under. Distinct
        /// from <see cref="EmblemSpec.Version"/>'s 1 and 2, which are the heraldic
        /// builder's, and which still parse - see <see cref="EmblemArtwork"/>.</summary>
        internal const int Version = 3;

        private readonly EmblemLayer[] _layers;

        /// <summary>Bottom first, so index 0 is the layer everything else is drawn
        /// over - the same order the layers panel lists them in, reversed.</summary>
        internal IReadOnlyList<EmblemLayer> Layers => _layers;

        internal int Count => _layers.Length;

        private EmblemStack(EmblemLayer[] layers)
        {
            _layers = layers;
        }

        /// <summary>The empty canvas. Renders as nothing at all, which is why
        /// saving one is refused rather than accepted - see
        /// <see cref="EmblemFormPolicy"/>.</summary>
        internal static readonly EmblemStack Empty = new EmblemStack(Array.Empty<EmblemLayer>());

        /// <summary>Builds a stack, refusing more than <see cref="MaxLayers"/>.</summary>
        internal static bool TryCreate(IReadOnlyList<EmblemLayer>? layers, out EmblemStack stack)
        {
            stack = Empty;

            if (layers == null) return false;
            if (layers.Count > MaxLayers) return false;
            if (layers.Count == 0) return true;

            EmblemLayer[] copy = new EmblemLayer[layers.Count];
            for (int i = 0; i < layers.Count; i++) copy[i] = layers[i];

            stack = new EmblemStack(copy);
            return true;
        }

        /// <summary>
        /// Reads the PAYLOAD of a version 3 code - everything after
        /// <c>"3-"</c>. Total: every rejection returns false, and nothing here
        /// throws on any input.
        /// </summary>
        internal static bool TryParsePayload(string? payload, out EmblemStack stack)
        {
            stack = Empty;

            if (payload == null) return false;
            if (payload.Length == 0) return true;

            if (payload.Length % EmblemLayerCode.Width != 0) return false;

            int count = payload.Length / EmblemLayerCode.Width;
            if (count > MaxLayers) return false;

            EmblemLayer[] layers = new EmblemLayer[count];

            for (int i = 0; i < count; i++)
            {
                if (!EmblemLayerCode.TryRead(payload, i * EmblemLayerCode.Width, out layers[i]))
                {
                    return false;
                }
            }

            stack = new EmblemStack(layers);
            return true;
        }

        /// <summary>
        /// The canonical code, version prefix included.
        ///
        /// Canonical in the strict sense the cache depends on: one design has
        /// exactly one string, because every field is a fixed-width group of
        /// integers with no optional part and no separator to vary.
        /// </summary>
        internal string ToCode()
        {
            StringBuilder code = new StringBuilder(2 + _layers.Length * EmblemLayerCode.Width);
            code.Append(Version).Append('-');

            foreach (EmblemLayer layer in _layers) EmblemLayerCode.Append(code, layer);

            return code.ToString();
        }

        public bool Equals(EmblemStack? other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (_layers.Length != other._layers.Length) return false;

            for (int i = 0; i < _layers.Length; i++)
            {
                if (!_layers[i].Equals(other._layers[i])) return false;
            }

            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as EmblemStack);

        public override int GetHashCode() => ToCode().GetHashCode(StringComparison.Ordinal);

        public override string ToString() => ToCode();
    }

    /// <summary>
    /// How one <see cref="EmblemLayer"/> is written into and read out of a code.
    ///
    /// THIRTEEN CHARACTERS, FIXED, NO SEPARATORS. Fixed width is what makes the
    /// code canonical - there is no optional field, no omitted default and no
    /// delimiter that could be doubled, so exactly one string maps to each design
    /// and the URL is safe to use as a cache key. It is also what makes parsing
    /// total by construction: the length is either a multiple of thirteen or the
    /// code is not one of ours.
    ///
    /// THE ALPHABET IS URL-SAFE AND DELIBERATELY EXCLUDES '-'. Every character is
    /// RFC 3986 <i>unreserved</i>, so a code never percent-encodes and survives a
    /// query string, a database column and a log line unchanged. The hyphen is
    /// left out on purpose even though it is unreserved: it is the separator of
    /// the heraldic code form, and a payload that could contain one would make
    /// telling the two forms apart a matter of counting rather than of looking.
    /// Digits come first, so the small values a hand-written test uses read as
    /// themselves.
    /// </summary>
    internal static class EmblemLayerCode
    {
        internal const string Alphabet =
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_.";

        /// <summary>Characters per layer.</summary>
        internal const int Width = 13;

        /// <summary>The largest value a two-character group can carry.</summary>
        internal const int PairMax = 64 * 64 - 1;

        /// <summary>
        /// What is added to a signed offset to make it a non-negative pair value.
        /// A position runs -2000..2000, so biasing by 2000 lands it in 0..4000,
        /// which fits a pair with room to spare.
        /// </summary>
        internal const int OffsetBias = EmblemLayer.MaxOffset;

        internal static void Append(StringBuilder code, EmblemLayer layer)
        {
            Pair(code, layer.Object);
            Pair(code, layer.X + OffsetBias);
            Pair(code, layer.Y + OffsetBias);
            Pair(code, layer.Size);
            Pair(code, layer.Rotation);
            Single(code, layer.Colour);
            Single(code, layer.Opacity);
            Single(code, layer.Flags);
        }

        internal static bool TryRead(string payload, int at, out EmblemLayer layer)
        {
            layer = default;

            if (!Pair(payload, at, out int obj)) return false;
            if (!Pair(payload, at + 2, out int x)) return false;
            if (!Pair(payload, at + 4, out int y)) return false;
            if (!Pair(payload, at + 6, out int size)) return false;
            if (!Pair(payload, at + 8, out int rotation)) return false;
            if (!Single(payload, at + 10, out int colour)) return false;
            if (!Single(payload, at + 11, out int opacity)) return false;
            if (!Single(payload, at + 12, out int flags)) return false;

            if (flags > (EmblemLayer.FlipXBit | EmblemLayer.FlipYBit | EmblemLayer.LockedBit))
            {
                // An unknown flag bit is a code from a vocabulary this build does
                // not have. Refused rather than masked off, because masking would
                // silently draw a layer that is missing whatever the bit meant.
                return false;
            }

            return EmblemLayer.TryCreate(
                obj, x - OffsetBias, y - OffsetBias, size, rotation, colour, opacity,
                (flags & EmblemLayer.FlipXBit) != 0,
                (flags & EmblemLayer.FlipYBit) != 0,
                (flags & EmblemLayer.LockedBit) != 0,
                out layer);
        }

        private static void Pair(StringBuilder code, int value)
        {
            code.Append(Alphabet[(value >> 6) & 63]).Append(Alphabet[value & 63]);
        }

        private static void Single(StringBuilder code, int value) => code.Append(Alphabet[value & 63]);

        private static bool Pair(string payload, int at, out int value)
        {
            value = 0;

            if (!Single(payload, at, out int high)) return false;
            if (!Single(payload, at + 1, out int low)) return false;

            value = (high << 6) | low;
            return true;
        }

        private static bool Single(string payload, int at, out int value)
        {
            value = 0;

            if (at < 0 || at >= payload.Length) return false;

            int index = Alphabet.IndexOf(payload[at]);
            if (index < 0) return false;

            value = index;
            return true;
        }
    }
}

namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// An alliance crest, in whichever of the two forms it was composed in: the
    /// heraldic builder's shield-and-device (<see cref="EmblemSpec"/>) or the
    /// layered editor's stack of objects (<see cref="EmblemStack"/>).
    ///
    /// WHY BOTH FORMS SURVIVE, RATHER THAN THE OLD ONE BEING MIGRATED. The obvious
    /// tidy answer - convert every stored heraldic code into an equivalent stack
    /// and delete the old renderer - would CHANGE PICTURES, and there is a crest
    /// in the live database right now
    /// (<c>wareborn:emblem:2-0-7-39-9-9-4</c>). A heraldic crest is not a pile of
    /// silhouettes: it has a rim inked round the shield's own outline, a field
    /// division clipped to that shield, a keyline round the device, and a top-lit
    /// gradient across the whole thing. The layered model has none of those, by
    /// design - it is a free composition on transparency, which is what the retail
    /// editor was. Any conversion would therefore be an approximation, and the
    /// player-visible result of shipping it is that somebody's crest quietly
    /// becomes a different picture. So the old renderer is untouched and still
    /// produces the same bytes for the same code, forever; version 3 codes are the
    /// new thing, and the two meet only here.
    ///
    /// It is a STRUCT holding both, rather than an interface with two
    /// implementations, because it is passed through the URL policy, the image
    /// cache, the view model and the page - and every one of those wants a value
    /// it can compare, hash and put in a record. There is exactly one branch on
    /// <see cref="IsLayered"/> per renderer and no other place in the server asks.
    ///
    /// Pure: a code in, pixels or vectors out.
    /// </summary>
    internal readonly struct EmblemArtwork : IEquatable<EmblemArtwork>
    {
        /// <summary>Whether this is a layered design rather than a heraldic one.</summary>
        internal bool IsLayered { get; }

        /// <summary>Meaningful only when <see cref="IsLayered"/> is false.</summary>
        internal EmblemSpec Heraldic { get; }

        /// <summary>Meaningful only when <see cref="IsLayered"/> is true. Never
        /// null: a default-constructed artwork is the empty heraldic spec, and this
        /// reads back as <see cref="EmblemStack.Empty"/>.</summary>
        internal EmblemStack Stack => _stack ?? EmblemStack.Empty;

        private readonly EmblemStack? _stack;

        private EmblemArtwork(bool layered, EmblemSpec heraldic, EmblemStack? stack)
        {
            IsLayered = layered;
            Heraldic = heraldic;
            _stack = stack;
        }

        internal static EmblemArtwork Of(EmblemSpec spec) => new EmblemArtwork(false, spec, null);

        internal static EmblemArtwork Of(EmblemStack stack) =>
            new EmblemArtwork(true, default, stack ?? EmblemStack.Empty);

        /// <summary>
        /// Every heraldic spec is an artwork, so the whole of the older feature
        /// goes on compiling and reading as it did. There is no conversion the
        /// other way, and there must not be: not every artwork is a spec.
        /// </summary>
        public static implicit operator EmblemArtwork(EmblemSpec spec) => Of(spec);

        /// <summary>
        /// Reads either form of code.
        ///
        /// The two are told apart by the leading version, which is what that field
        /// has always been for. A version 3 payload cannot be mistaken for a
        /// heraldic one in either direction: the heraldic form is seven
        /// hyphen-separated integers and the layered payload contains no hyphen at
        /// all (see <see cref="EmblemLayerCode.Alphabet"/>).
        ///
        /// Total: every rejection returns false, and nothing here throws on any
        /// input including null, empty, absurdly long or full of unicode.
        /// </summary>
        internal static bool TryParse(string? code, out EmblemArtwork artwork)
        {
            artwork = default;

            if (string.IsNullOrEmpty(code)) return false;

            // A cheap length gate BEFORE any splitting, sized to the longest code
            // this vocabulary can express: the version, the hyphen and twenty
            // thirteen-character layers. Same purpose as the heraldic parser's own
            // gate - a megabyte of junk in a query string must not become a
            // megabyte of allocation.
            if (code!.Length > MaxCodeLength) return false;

            if (code.StartsWith(LayeredPrefix, StringComparison.Ordinal))
            {
                if (!EmblemStack.TryParsePayload(code.Substring(LayeredPrefix.Length), out EmblemStack stack))
                {
                    return false;
                }

                artwork = Of(stack);
                return true;
            }

            if (!EmblemSpec.TryParse(code, out EmblemSpec spec)) return false;

            artwork = Of(spec);
            return true;
        }

        private const string LayeredPrefix = "3-";

        /// <summary>The longest code any form of this vocabulary can produce.</summary>
        internal const int MaxCodeLength =
            2 + EmblemStack.MaxLayers * EmblemLayerCode.Width;

        /// <summary>The canonical code. Round-trips through
        /// <see cref="TryParse"/> exactly, which is what lets the PNG route treat
        /// the code as a cache key.</summary>
        internal string ToCode() => IsLayered ? Stack.ToCode() : Heraldic.ToCode();

        /// <summary>
        /// Whether this artwork would draw nothing at all.
        ///
        /// Only a layered design can be empty - a heraldic one always has a
        /// shield. Asked by the save path, which refuses it: an alliance whose
        /// crest is fully transparent looks in game exactly like an alliance whose
        /// crest failed to download, and the player who did it has no way to tell
        /// those apart.
        /// </summary>
        internal bool IsBlank => IsLayered && Stack.Count == 0;

        /// <summary>Non-premultiplied RGBA, row-major, top row first - what
        /// <see cref="PngWriter.Encode"/> wants.</summary>
        internal byte[] RenderPixels(int size) =>
            IsLayered ? EmblemStackPainter.Render(Stack, size) : EmblemPainter.Render(Heraldic, size);

        /// <summary>The same crest as a standalone SVG document, for a player to
        /// download. The game never sees this - see
        /// <see cref="EmblemUrlPolicy.Format"/>.</summary>
        internal string ToSvg() =>
            IsLayered ? EmblemStackSvg.Compose(Stack) : EmblemSvg.Compose(Heraldic);

        public bool Equals(EmblemArtwork other)
        {
            if (IsLayered != other.IsLayered) return false;
            return IsLayered ? Stack.Equals(other.Stack) : Heraldic.Equals(other.Heraldic);
        }

        public override bool Equals(object? obj) => obj is EmblemArtwork other && Equals(other);

        public override int GetHashCode() => ToCode().GetHashCode(StringComparison.Ordinal);

        public override string ToString() => ToCode();
    }
}

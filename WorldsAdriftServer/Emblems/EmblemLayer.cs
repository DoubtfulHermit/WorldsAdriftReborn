using System.Text;

namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// One object placed on the emblem canvas: which shape, where, how big, how
    /// turned, which way round, what colour and how solid.
    ///
    /// EVERY FIELD IS AN INTEGER, AND THAT IS THE WHOLE DESIGN OF THIS TYPE.
    /// The layered emblem is drawn in three places - the server's rasteriser
    /// (<see cref="EmblemStackPainter"/>), the server's vector export
    /// (<see cref="EmblemStackSvg"/>), and the browser's live preview while a
    /// player drags a layer around. Those three must produce the same picture, or
    /// the crest a player composed is not the crest the game shows, and the way
    /// that normally breaks is not a wrong formula - it is a number that was
    /// written as <c>0.4823529411764706</c> by one of them and <c>0.482</c> by
    /// another.
    ///
    /// So nothing here is a float. A position is THOUSANDTHS of the canvas box, a
    /// size is thousandths, a rotation is a WHOLE DEGREE, an opacity is one of
    /// forty steps. Every string any renderer writes is built from those integers
    /// by integer arithmetic (see <see cref="Thousandths"/>), so the SVG the
    /// browser composes while you drag and the SVG the server hands you to
    /// download are byte-identical for the same layer - not "agree to six decimal
    /// places". <c>EmblemLayerMirrorTests</c> pins that against the real script the
    /// page serves, in a real JavaScript engine.
    ///
    /// COORDINATES are the painter's: a square [-1, 1] with y pointing DOWN, so a
    /// layer written here reads the way it renders. Positions are allowed OUT to
    /// +/-2 so a layer can hang off the edge and be clipped, which is how you make
    /// a shape read as a band rather than as a floating blob.
    ///
    /// Pure: no clock, no disk, no request.
    /// </summary>
    internal readonly struct EmblemLayer : IEquatable<EmblemLayer>
    {
        /// <summary>How many thousandths make one unit of the [-1, 1] canvas.</summary>
        internal const int Unit = 1000;

        /// <summary>How far off-centre a layer may sit, in <see cref="Unit"/>s.</summary>
        internal const int MaxOffset = 2000;

        /// <summary>The largest a layer may be scaled, in <see cref="Unit"/>s.</summary>
        internal const int MaxSize = 2000;

        /// <summary>
        /// The smallest a layer may be scaled, in <see cref="Unit"/>s.
        ///
        /// Not zero. A zero-sized layer is invisible, occupies a slot out of
        /// twenty, and looks to a player exactly like a layer that failed to be
        /// added - so the editor cannot make one and the code cannot carry one.
        /// </summary>
        internal const int MinSize = 10;

        /// <summary>Rotation is whole degrees, so <c>rotate(37)</c> is the only
        /// string any renderer can write for it.</summary>
        internal const int RotationSteps = 360;

        /// <summary>
        /// How many opacity steps the slider has. Forty, so one step is 2.5% and
        /// the alpha is always a whole number of thousandths
        /// (<c>step * 25</c>) - which is what keeps the fill-opacity string
        /// integer-built rather than a rounded division.
        /// </summary>
        internal const int OpacitySteps = 40;

        /// <summary>Thousandths of alpha per opacity step.</summary>
        internal const int OpacityUnit = Unit / OpacitySteps;

        internal int Object { get; }

        /// <summary>Centre offset, in <see cref="Unit"/>s, x right.</summary>
        internal int X { get; }

        /// <summary>Centre offset, in <see cref="Unit"/>s, y DOWN.</summary>
        internal int Y { get; }

        /// <summary>Uniform scale, in <see cref="Unit"/>s: 1000 draws the object at
        /// its natural size, which fills the canvas.</summary>
        internal int Size { get; }

        /// <summary>Clockwise on screen, whole degrees, 0 to 359.</summary>
        internal int Rotation { get; }

        /// <summary>An index into <see cref="EmblemVocabulary.Palette"/>.</summary>
        internal int Colour { get; }

        /// <summary>0 to <see cref="OpacitySteps"/>.</summary>
        internal int Opacity { get; }

        internal bool FlipX { get; }
        internal bool FlipY { get; }

        /// <summary>
        /// Whether this layer also draws its REFLECTION across the canvas's
        /// vertical axis.
        ///
        /// A PROPERTY OF THE LAYER RATHER THAN A SECOND LAYER, and that is the
        /// whole reason it exists. Heraldry is overwhelmingly symmetrical - a pair
        /// of wings, a pair of supporters, a border of repeated marks - and the two
        /// ways to build that are a pair of ordinary layers or this. A pair costs
        /// TWO of the twenty slots per element, so a ten-element symmetrical design
        /// does not fit at all; worse, the two halves are independent, so every
        /// later nudge of one of them is a chance to leave the crest very slightly
        /// crooked in a way nobody notices until it is in game. Here, moving the
        /// layer moves both halves because there is only one thing to move.
        ///
        /// It costs the code NOTHING: the flags field is a base-64 character with
        /// six bits and three of them were spoken for, so this is
        /// <see cref="MirrorBit"/> in a character that was already being written.
        /// A layer is still thirteen characters and a full design is still 262.
        ///
        /// It is NOT free to draw - see <see cref="EmblemStackPainter"/>, which
        /// places two regions for a mirrored layer.
        ///
        /// THE AXIS IS VERTICAL AND ONLY VERTICAL. A horizontal mirror is a
        /// different bit for a symmetry heraldry almost never uses, and "which
        /// axis" would need a control on a canvas that already has flip X, flip Y
        /// and a rotation handle. Turn the layer ninety degrees if you want the
        /// other one.
        /// </summary>
        internal bool Mirror { get; }

        /// <summary>
        /// Whether the editor refuses to change this layer.
        ///
        /// Carried in the SAVED code rather than only in the browser, because a
        /// lock a reload forgets is a lock that protected nothing: the entire
        /// point of locking the four layers you have finished is to go on editing
        /// the fifth tomorrow. It changes no pixel - see
        /// <see cref="EmblemStackPainter"/>, which never reads it.
        /// </summary>
        internal bool Locked { get; }

        private EmblemLayer(
            int obj, int x, int y, int size, int rotation, int colour, int opacity,
            bool flipX, bool flipY, bool mirror, bool locked)
        {
            Object = obj;
            X = x;
            Y = y;
            Size = size;
            Rotation = rotation;
            Colour = colour;
            Opacity = opacity;
            FlipX = flipX;
            FlipY = flipY;
            Mirror = mirror;
            Locked = locked;
        }

        /// <summary>
        /// Builds a layer, refusing anything out of range.
        ///
        /// A REFUSAL rather than a clamp, for the reason
        /// <see cref="EmblemSpec.TryCreate"/> gives: these arrive from a form, and
        /// a value the builder does not offer means the form did not come from the
        /// builder. Quietly substituting a legal one would draw a picture nobody
        /// composed.
        /// </summary>
        internal static bool TryCreate(
            int obj, int x, int y, int size, int rotation, int colour, int opacity,
            bool flipX, bool flipY, bool mirror, bool locked,
            out EmblemLayer layer)
        {
            layer = default;

            if (obj < 0 || obj >= EmblemObjects.Count) return false;
            if (x < -MaxOffset || x > MaxOffset) return false;
            if (y < -MaxOffset || y > MaxOffset) return false;
            if (size < MinSize || size > MaxSize) return false;
            if (rotation < 0 || rotation >= RotationSteps) return false;
            if (colour < 0 || colour >= EmblemVocabulary.ColourCount) return false;
            if (opacity < 0 || opacity > OpacitySteps) return false;

            layer = new EmblemLayer(
                obj, x, y, size, rotation, colour, opacity, flipX, flipY, mirror, locked);
            return true;
        }

        /// <summary>A layer with everything at its default: centred, half size,
        /// upright, fully opaque, not mirrored. What clicking an object in the
        /// palette adds.</summary>
        internal static EmblemLayer Placed(int obj, int colour)
        {
            TryCreate(obj, 0, 0, 500, 0, colour, OpacitySteps, false, false, false, false,
                out EmblemLayer layer);
            return layer;
        }

        internal EmblemLayer WithLocked(bool locked) =>
            new EmblemLayer(Object, X, Y, Size, Rotation, Colour, Opacity, FlipX, FlipY, Mirror, locked);

        // ------------------------------------------------------------- geometry

        /// <summary>The scale as the painter uses it.</summary>
        internal double Scale => Size / (double)Unit;

        /// <summary>The centre, in the painter's [-1, 1] space.</summary>
        internal double CentreX => X / (double)Unit;

        internal double CentreY => Y / (double)Unit;

        /// <summary>The rotation in radians, clockwise in the painter's y-down space.</summary>
        internal double Radians => Rotation * Math.PI / 180.0;

        /// <summary>Alpha, 0 to 1.</summary>
        internal double Alpha => Opacity * OpacityUnit / (double)Unit;

        /// <summary>The signed x scale - the flip folded into it, exactly as the
        /// SVG transform folds it into <c>scale()</c>.</summary>
        internal int SignedSizeX => FlipX ? -Size : Size;

        internal int SignedSizeY => FlipY ? -Size : Size;

        /// <summary>The object this layer draws, or null if the catalogue has
        /// nothing at that index (which <see cref="TryCreate"/> forbids).</summary>
        internal EmblemPath? Path => EmblemObjects.PathAt(Object);

        // ------------------------------------------------------------ instances

        /// <summary>
        /// How many times this layer is DRAWN: two when it is mirrored, one
        /// otherwise.
        ///
        /// Every renderer walks this rather than asking about
        /// <see cref="Mirror"/> itself, so "a mirrored layer draws twice" is
        /// stated once and the rasteriser, the vector export and the browser
        /// cannot disagree about how many shapes there are.
        /// </summary>
        internal int Instances => Mirror ? 2 : 1;

        /// <summary>Instance 0 is the layer as placed; instance 1 is its
        /// reflection.</summary>
        internal const int Reflection = 1;

        /// <summary>
        /// THE REFLECTION, DERIVED RATHER THAN STORED, and it is three integer
        /// negations because of what reflecting a transform actually is.
        ///
        /// The placed instance is <c>F = T(x, y) R(r) S(sx, sy)</c>. Its mirror
        /// image across the canvas's vertical axis is <c>M F</c> where
        /// <c>M = diag(-1, 1)</c>, and M pushes right through the list:
        /// <c>M T(x, y) = T(-x, y) M</c>, and <c>M R(r) M = R(-r)</c> (M is its own
        /// inverse), so <c>M F = T(-x, y) R(-r) S(-sx, sy)</c>. Same shape of
        /// string, same three fields, all still integers - which is what lets the
        /// reflection be written by exactly the code that writes the original.
        ///
        /// The rotation is taken back into 0..359 so it is a value this vocabulary
        /// can express and, more to the point, so the rasteriser takes its sine of
        /// the same whole degree the SVG string names.
        /// </summary>
        internal int InstanceX(int instance) => instance == Reflection ? -X : X;

        internal int InstanceRotation(int instance) =>
            instance == Reflection ? (RotationSteps - Rotation) % RotationSteps : Rotation;

        internal int InstanceSizeX(int instance) =>
            instance == Reflection ? -SignedSizeX : SignedSizeX;

        /// <summary>The centre of an instance, in the painter's [-1, 1] space.</summary>
        internal double InstanceCentreX(int instance) => InstanceX(instance) / (double)Unit;

        /// <summary>The instance's rotation in radians - taken from the same whole
        /// degree <see cref="AppendTransform"/> writes.</summary>
        internal double InstanceRadians(int instance) => InstanceRotation(instance) * Math.PI / 180.0;

        /// <summary>The instance's signed x scale, as the painter uses it.</summary>
        internal double InstanceScaleX(int instance) => InstanceSizeX(instance) / (double)Unit;

        /// <summary>
        /// One INSTANCE's transform, in the form BOTH SVG writers emit and the
        /// browser's preview builds: <c>translate(x y) rotate(deg) scale(sx sy)</c>.
        ///
        /// The order is not a style. SVG applies a transform list right to left to
        /// the geometry, so the path is scaled (and mirrored, because the flip is
        /// the sign of the scale), then turned, then moved - which is the same
        /// order <see cref="EmblemStackPainter"/> undoes when it asks whether a
        /// point is inside. Any other order would put a rotated layer somewhere
        /// else entirely.
        ///
        /// Every number is written from an integer. See the note on this type.
        /// </summary>
        internal void AppendTransform(StringBuilder target, int instance)
        {
            target.Append("translate(").Append(InstanceX(instance)).Append(' ').Append(Y)
                  .Append(") rotate(").Append(InstanceRotation(instance)).Append(") scale(");
            Thousandths(target, InstanceSizeX(instance));
            target.Append(' ');
            Thousandths(target, SignedSizeY);
            target.Append(')');
        }

        /// <summary>The placed instance's transform.</summary>
        internal void AppendTransform(StringBuilder target) => AppendTransform(target, 0);

        internal string Transform() => Transform(0);

        internal string Transform(int instance)
        {
            StringBuilder text = new StringBuilder(48);
            AppendTransform(text, instance);
            return text.ToString();
        }

        /// <summary>The <c>fill-opacity</c> this layer is painted at.</summary>
        internal string FillOpacity() => Thousandths(Opacity * OpacityUnit);

        /// <summary>
        /// A whole number of thousandths as a decimal, built with no floating
        /// point anywhere: sign, integer part, dot, three padded digits.
        ///
        /// This function IS the parity contract. It is short enough to be
        /// transcribed into the page's script without judgement, and it is what
        /// makes "the browser preview and the server render agree" a property of
        /// the string rather than of a tolerance.
        /// </summary>
        internal static void Thousandths(StringBuilder target, int value)
        {
            if (value < 0)
            {
                target.Append('-');
                value = -value;
            }

            int whole = value / Unit;
            int fraction = value % Unit;

            target.Append(whole).Append('.');
            if (fraction < 100) target.Append('0');
            if (fraction < 10) target.Append('0');
            target.Append(fraction);
        }

        internal static string Thousandths(int value)
        {
            StringBuilder text = new StringBuilder(12);
            Thousandths(text, value);
            return text.ToString();
        }

        public bool Equals(EmblemLayer other) =>
            Object == other.Object && X == other.X && Y == other.Y && Size == other.Size
            && Rotation == other.Rotation && Colour == other.Colour && Opacity == other.Opacity
            && FlipX == other.FlipX && FlipY == other.FlipY && Mirror == other.Mirror
            && Locked == other.Locked;

        public override bool Equals(object? obj) => obj is EmblemLayer other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Object, X, Y, Size, Rotation, Colour, (Opacity << 4) | Flags);

        /// <summary>
        /// The four booleans as the bits the code carries them in.
        ///
        /// FOUR BITS OF A SIX-BIT CHARACTER. The flags are one character of the
        /// code's base-64 alphabet, so there were three spare when the mirror bit
        /// was added and there are two now. That is why symmetry cost the encoding
        /// nothing at all - see <see cref="Mirror"/>.
        /// </summary>
        internal int Flags =>
            (FlipX ? FlipXBit : 0) | (FlipY ? FlipYBit : 0)
            | (Locked ? LockedBit : 0) | (Mirror ? MirrorBit : 0);

        internal const int FlipXBit = 1;
        internal const int FlipYBit = 2;
        internal const int LockedBit = 4;

        /// <summary>
        /// Added AFTER the locked bit, not squeezed in beside the flips, because
        /// the bit values are in the live database. Every code written before this
        /// build has a flags character of at most 7, and every one of them still
        /// means what it always did.
        /// </summary>
        internal const int MirrorBit = 8;

        /// <summary>Every bit this build has a meaning for. A code carrying more
        /// than this is from a vocabulary we do not have - see
        /// <see cref="EmblemLayerCode.TryRead"/>.</summary>
        internal const int KnownFlags = FlipXBit | FlipYBit | LockedBit | MirrorBit;
    }
}

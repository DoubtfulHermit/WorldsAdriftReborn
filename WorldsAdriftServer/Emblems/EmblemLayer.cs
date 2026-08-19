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
            bool flipX, bool flipY, bool locked)
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
            bool flipX, bool flipY, bool locked,
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

            layer = new EmblemLayer(obj, x, y, size, rotation, colour, opacity, flipX, flipY, locked);
            return true;
        }

        /// <summary>A layer with everything at its default: centred, half size,
        /// upright, fully opaque. What clicking an object in the palette adds.</summary>
        internal static EmblemLayer Placed(int obj, int colour)
        {
            TryCreate(obj, 0, 0, 500, 0, colour, OpacitySteps, false, false, false, out EmblemLayer layer);
            return layer;
        }

        internal EmblemLayer WithLocked(bool locked) =>
            new EmblemLayer(Object, X, Y, Size, Rotation, Colour, Opacity, FlipX, FlipY, locked);

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

        /// <summary>
        /// The layer's transform, in the form BOTH SVG writers emit and the
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
        internal void AppendTransform(StringBuilder target)
        {
            target.Append("translate(").Append(X).Append(' ').Append(Y).Append(") rotate(")
                  .Append(Rotation).Append(") scale(");
            Thousandths(target, SignedSizeX);
            target.Append(' ');
            Thousandths(target, SignedSizeY);
            target.Append(')');
        }

        internal string Transform()
        {
            StringBuilder text = new StringBuilder(48);
            AppendTransform(text);
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
            && FlipX == other.FlipX && FlipY == other.FlipY && Locked == other.Locked;

        public override bool Equals(object? obj) => obj is EmblemLayer other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Object, X, Y, Size, Rotation, Colour, (Opacity << 3) | Flags);

        /// <summary>The three booleans as the bits the code carries them in.</summary>
        internal int Flags => (FlipX ? 1 : 0) | (FlipY ? 2 : 0) | (Locked ? 4 : 0);

        internal const int FlipXBit = 1;
        internal const int FlipYBit = 2;
        internal const int LockedBit = 4;
    }
}

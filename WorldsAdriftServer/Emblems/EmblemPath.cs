using System.Globalization;
using System.Text;

namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// A closed vector outline, and the two things the emblem feature needs to do
    /// with one: ask whether a point is inside it, and write it out as SVG.
    ///
    /// WHY THIS TYPE EXISTS. The painter used to carry a bespoke region test per
    /// device - a polar star, a hand-written anchor, a gear built from three
    /// radius bands. That does not scale to fifty pieces of drawn artwork, and it
    /// cannot be exported: an SVG of a device that only exists as C# arithmetic
    /// has to be written a second time, and two descriptions of one picture drift.
    /// So every shape in this feature - the shield outlines, the field divisions,
    /// the ten drawn-in-code devices and the fifty traced ones - is now one of
    /// these, and BOTH renderers read it.
    ///
    /// NON-ZERO FILL. A point is inside when the outline winds around it a net
    /// non-zero number of times. Chosen over even-odd for one concrete reason:
    /// several devices are naturally built as OVERLAPPING pieces - the cross is
    /// two bars, the saltire is two, the quarterly division is two rectangles -
    /// and under even-odd every overlap would punch a hole through itself. The
    /// traced artwork works under either rule, because the tracer walks every
    /// contour with the ink on the same hand and checks that it did (see
    /// tools/emblem-devices/trace_devices.py), so non-zero costs nothing there
    /// and buys unions everywhere else.
    ///
    /// WHY IT IS BUCKETED. The painter asks <see cref="Contains"/> up to 1.6
    /// million times per 256px emblem, and a traced device has around 750 edges.
    /// Testing all of them every time is a billion operations for one picture. So
    /// the edges are indexed by the horizontal band they span: a query touches
    /// only the handful that could possibly cross its scanline, which turns the
    /// per-sample cost from "the whole device" into single digits and makes a
    /// traced device no more expensive to draw than the old procedural star.
    ///
    /// Immutable and thread-safe once built. Pure: no I/O, no clock.
    /// </summary>
    internal sealed class EmblemPath
    {
        /// <summary>
        /// How many horizontal bands the edges are indexed into.
        ///
        /// 128 puts roughly six edges of a 750-edge traced device in each band,
        /// which is where the win flattens out - more bands would shrink the scan
        /// but grow the per-device build and the memory for no measurable gain at
        /// the one size this renders at.
        /// </summary>
        private const int Buckets = 128;

        private readonly double[][] _contours;

        // Edges as x0, y0, x1, y1 quads. Horizontal edges are dropped on the way
        // in: they can never satisfy the crossing test below, so keeping them
        // would only lengthen every scan.
        private readonly double[] _edges;

        private readonly int[] _bucketOffsets;
        private readonly int[] _bucketEdges;

        private readonly double _bandScale;

        internal double MinX { get; }
        internal double MaxX { get; }
        internal double MinY { get; }
        internal double MaxY { get; }

        /// <summary>
        /// The furthest any point of this path sits from the centre along either
        /// axis. The painter uses it to size a device inside a shield without
        /// putting its outermost ink through the rim.
        /// </summary>
        internal double Reach { get; }

        private EmblemPath(double[][] contours)
        {
            if (contours == null) throw new ArgumentNullException(nameof(contours));
            if (contours.Length == 0) throw new ArgumentException("A path needs a contour.", nameof(contours));

            _contours = contours;

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            List<double> edges = new List<double>();

            foreach (double[] contour in contours)
            {
                if (contour.Length < 6 || contour.Length % 2 != 0)
                {
                    throw new ArgumentException("A contour is at least three x,y pairs.", nameof(contours));
                }

                int points = contour.Length / 2;

                for (int i = 0; i < points; i++)
                {
                    double x = contour[i * 2], y = contour[i * 2 + 1];

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;

                    int j = (i + 1) % points;
                    double nx = contour[j * 2], ny = contour[j * 2 + 1];

                    if (y == ny) continue;

                    edges.Add(x);
                    edges.Add(y);
                    edges.Add(nx);
                    edges.Add(ny);
                }
            }

            if (edges.Count == 0) throw new ArgumentException("A path needs a non-horizontal edge.", nameof(contours));

            _edges = edges.ToArray();

            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
            Reach = Math.Max(Math.Max(Math.Abs(minX), Math.Abs(maxX)),
                             Math.Max(Math.Abs(minY), Math.Abs(maxY)));

            double span = maxY - minY;
            _bandScale = span > 0 ? Buckets / span : 0.0;

            int count = _edges.Length / 4;
            int[] counts = new int[Buckets];

            for (int e = 0; e < count; e++)
            {
                Band(e, out int lo, out int hi);
                for (int b = lo; b <= hi; b++) counts[b]++;
            }

            _bucketOffsets = new int[Buckets + 1];
            for (int b = 0; b < Buckets; b++) _bucketOffsets[b + 1] = _bucketOffsets[b] + counts[b];

            _bucketEdges = new int[_bucketOffsets[Buckets]];
            int[] cursor = (int[])_bucketOffsets.Clone();

            for (int e = 0; e < count; e++)
            {
                Band(e, out int lo, out int hi);
                for (int b = lo; b <= hi; b++) _bucketEdges[cursor[b]++] = e;
            }
        }

        private void Band(int edge, out int lo, out int hi)
        {
            double y0 = _edges[edge * 4 + 1];
            double y1 = _edges[edge * 4 + 3];

            lo = Clamp((int)((Math.Min(y0, y1) - MinY) * _bandScale));
            hi = Clamp((int)((Math.Max(y0, y1) - MinY) * _bandScale));
        }

        private static int Clamp(int band)
        {
            if (band < 0) return 0;
            if (band >= Buckets) return Buckets - 1;
            return band;
        }

        /// <summary>
        /// How many non-horizontal edges the winding test has to choose between.
        ///
        /// The honest measure of what a shape COSTS to draw - not its contour count
        /// and not its point count, because horizontal edges are dropped on the way
        /// in and never scanned. Exposed so the render budget test can pick the
        /// twenty most expensive objects in the catalogue by measurement rather than
        /// by position, which stops "the worst case" quietly becoming "the last
        /// twenty things appended" the next time the catalogue grows.
        /// </summary>
        internal int EdgeCount => _edges.Length / 4;

        /// <summary>Builds a path from contours of flat x,y pairs.</summary>
        internal static EmblemPath FromContours(params double[][] contours) => new EmblemPath(contours);

        /// <summary>
        /// Parses the compact form the traced table is stored in: contours
        /// separated by <c>|</c>, coordinates separated by spaces, in integer
        /// units of <paramref name="unit"/> per half-box.
        ///
        /// Stored as text rather than as C# arrays because 37,000 numbers of
        /// <c>double</c> literal is a file the compiler has to think about and a
        /// diff nobody can read; as strings it is one token per device.
        /// </summary>
        internal static EmblemPath Parse(string data, double unit)
        {
            if (string.IsNullOrEmpty(data)) throw new ArgumentException("Empty path.", nameof(data));

            string[] parts = data.Split('|');
            double[][] contours = new double[parts.Length][];

            for (int i = 0; i < parts.Length; i++)
            {
                string[] numbers = parts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                double[] contour = new double[numbers.Length];

                for (int j = 0; j < numbers.Length; j++)
                {
                    contour[j] = int.Parse(numbers[j], CultureInfo.InvariantCulture) / unit;
                }

                contours[i] = contour;
            }

            return new EmblemPath(contours);
        }

        /// <summary>
        /// Parses the OTHER spelling of the same thing: SVG path data made of
        /// <c>M</c>, <c>L</c> and <c>Z</c> with straight segments only, which is
        /// what <c>tools/emblem-objects/trace_objects.py</c> writes.
        ///
        /// TWO SPELLINGS, ONE GEOMETRY. The compact form <see cref="Parse"/> reads
        /// and this one carry identical numbers in identical units - integer
        /// thousandths of the [-1, 1] box, y down, filled non-zero - and differ only
        /// in whether the commands are written down. So this is not a conversion
        /// between coordinate systems and there is nothing here to get subtly
        /// wrong; it drops the letters and starts a contour at each <c>M</c>. That
        /// the two really are the same is not taken on trust: every one of the
        /// traced objects is round-tripped back through
        /// <see cref="AppendPathData"/> in EmblemObjectSheetTests and must come out
        /// as the string the tracer wrote.
        ///
        /// <c>Z</c> is accepted and ignored. Every contour here is closed, and
        /// <see cref="EmblemPath"/> closes each one itself by wrapping the last
        /// point back to the first, so an explicit close would only duplicate a
        /// point and give the winding test a zero-length edge to chew on.
        /// </summary>
        internal static EmblemPath ParseDrawing(string data, double unit)
        {
            if (string.IsNullOrEmpty(data)) throw new ArgumentException("Empty path.", nameof(data));

            List<double[]> contours = new List<double[]>();
            List<double> contour = new List<double>();

            int i = 0;
            while (i < data.Length)
            {
                char c = data[i];

                if (c == ' ') { i++; continue; }

                if (c == 'Z' || c == 'z') { i++; continue; }

                if (c == 'M' || c == 'm')
                {
                    if (contour.Count > 0) { contours.Add(contour.ToArray()); contour.Clear(); }
                    i++;
                }
                else if (c == 'L' || c == 'l')
                {
                    i++;
                }
                else if (c != '-' && (c < '0' || c > '9'))
                {
                    // Curves, arcs and relative commands are not in this dialect and
                    // never have been. Refused loudly: a silently skipped command
                    // would land as a shape with a straight line where a curve was.
                    throw new ArgumentException(
                        "Unsupported path command '" + c + "'.", nameof(data));
                }

                contour.Add(Number(data, ref i) / unit);
                contour.Add(Number(data, ref i) / unit);
            }

            if (contour.Count > 0) contours.Add(contour.ToArray());

            return new EmblemPath(contours.ToArray());
        }

        /// <summary>One integer coordinate, leaving the cursor after it.</summary>
        private static int Number(string data, ref int i)
        {
            while (i < data.Length && data[i] == ' ') i++;

            int start = i;
            if (i < data.Length && data[i] == '-') i++;
            while (i < data.Length && data[i] >= '0' && data[i] <= '9') i++;

            if (i == start) throw new ArgumentException("A coordinate is missing.", nameof(data));

            return int.Parse(data.AsSpan(start, i - start), provider: CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Whether the point is inside, by the non-zero winding rule.
        ///
        /// The crossing test is deliberately half-open in y (<c>y0 &lt;= y</c>
        /// against <c>y1 &gt; y</c>): that is what stops a vertex that sits
        /// exactly on the scanline from being counted twice and flipping the
        /// answer for a whole run of pixels.
        /// </summary>
        internal bool Contains(double x, double y)
        {
            if (y < MinY || y > MaxY || x < MinX || x > MaxX) return false;

            int band = Clamp((int)((y - MinY) * _bandScale));
            int winding = 0;

            for (int i = _bucketOffsets[band]; i < _bucketOffsets[band + 1]; i++)
            {
                int e = _bucketEdges[i] * 4;

                double x0 = _edges[e], y0 = _edges[e + 1];
                double x1 = _edges[e + 2], y1 = _edges[e + 3];

                if (y0 <= y)
                {
                    if (y1 > y && Side(x0, y0, x1, y1, x, y) > 0) winding++;
                }
                else if (y1 <= y && Side(x0, y0, x1, y1, x, y) < 0)
                {
                    winding--;
                }
            }

            return winding != 0;
        }

        /// <summary>Which side of the directed edge the point falls on.</summary>
        private static double Side(double x0, double y0, double x1, double y1, double x, double y) =>
            (x1 - x0) * (y - y0) - (x - x0) * (y1 - y0);

        /// <summary>
        /// Writes this path as SVG path data, scaled and shifted the same way the
        /// painter scales and shifts it.
        ///
        /// Coordinates come out as integers in the SVG's own thousandths viewBox.
        /// That is not a shortcut: the traced table is already stored in
        /// thousandths, so at scale 1 the numbers written here are the numbers the
        /// tracer produced, and the vector a player downloads is the vector the
        /// server rasterises rather than a second rounding of it.
        /// </summary>
        internal void AppendPathData(StringBuilder target, double scale, double offsetY, double unit)
        {
            foreach (double[] contour in _contours)
            {
                for (int i = 0; i < contour.Length; i += 2)
                {
                    target.Append(i == 0 ? 'M' : 'L');
                    Append(target, contour[i] * scale * unit);
                    target.Append(' ');
                    Append(target, (contour[i + 1] * scale + offsetY) * unit);
                    target.Append(' ');
                }

                target.Append('Z');
            }
        }

        private static void Append(StringBuilder target, double value)
        {
            target.Append(((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>The path data at natural size, for tests and for one-off exports.</summary>
        internal string ToPathData(double unit = 1000.0)
        {
            StringBuilder text = new StringBuilder();
            AppendPathData(text, 1.0, 0.0, unit);
            return text.ToString();
        }
    }
}

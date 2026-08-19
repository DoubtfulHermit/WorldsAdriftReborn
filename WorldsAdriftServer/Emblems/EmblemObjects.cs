using System.Globalization;
using System.Text;

namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// The object catalogue the layered emblem editor puts on its left-hand
    /// panel: every silhouette a player can drop onto the canvas as a layer.
    ///
    /// IT IS A LIST, AND ADDING TO IT IS ONE ROW. That is the requirement this
    /// type exists to satisfy - the artwork is expected to grow, and growing it
    /// must not mean touching the painter, the SVG writer, the page, the script or
    /// the code format. Nothing anywhere dispatches on WHICH object a layer draws;
    /// every renderer asks <see cref="PathAt"/> for an outline and fills it.
    ///
    /// HOW TO ADD ONE - the whole procedure:
    /// <list type="number">
    /// <item>get the outline as path data in this file's compact form: integer
    ///   THOUSANDTHS of the [-1, 1] box, y DOWN, coordinates separated by spaces,
    ///   contours by '|', filled non-zero. Traced artwork already comes out of
    ///   <c>tools/emblem-devices/trace_devices.py</c> in exactly that form;</item>
    /// <item>APPEND a row to <see cref="Primitives"/> - or, for a traced sheet
    ///   icon, add it to the END of a sheet and re-run the tracer, which is the
    ///   whole of it: <see cref="EmblemObjectSheets"/> reads the tracer's output
    ///   directly, so there is no table here to regenerate;</item>
    /// <item>there is no step three. The palette, the browser preview, the
    ///   rasteriser and the vector export all read this table.</item>
    /// </list>
    ///
    /// APPEND, NEVER INSERT OR REORDER. A layer stores its object as an INDEX into
    /// this list, and those indices are in codes that are already in the database
    /// and in URLs the game client has cached. Inserting a shape in the middle
    /// would silently redraw every saved emblem with the wrong artwork - the same
    /// hazard <see cref="EmblemSpec.Version"/> documents for the charge table, and
    /// the reason the palette's DISPLAY order (<see cref="Category"/> plus name)
    /// is deliberately separate from the index order: a new shape can appear
    /// beside its relatives on the panel while its index is still on the end.
    ///
    /// Pure data, built once, never written again.
    /// </summary>
    internal static class EmblemObjects
    {
        /// <summary>The unit the path data is stored in: 1000 = the box edge.</summary>
        internal const double Unit = 1000.0;

        /// <summary>The panels the editor groups the catalogue into.</summary>
        internal const string ShieldCategory = "Shields";

        internal const string ShapeCategory = "Shapes";

        internal const string DeviceCategory = "Devices";

        internal readonly struct Entry
        {
            internal string Name { get; }
            internal string Category { get; }
            internal EmblemPath Path { get; }

            /// <summary>
            /// Whether the panel offers this object to build NEW layers from.
            ///
            /// A hidden object is still at its index and still DRAWS. That is the
            /// point of hiding rather than deleting: an object nobody should pick
            /// again may already be on somebody's crest, and the crest must not
            /// change. See <see cref="EmblemObjectSheets"/> for the list and how to
            /// add to it.
            /// </summary>
            internal bool Hidden { get; }

            internal Entry(string name, string category, EmblemPath path, bool hidden = false)
            {
                Name = name;
                Category = category;
                Path = path;
                Hidden = hidden;
            }
        }

        /// <summary>
        /// The catalogue, in INDEX order - which is the frozen order, not the
        /// order the panel shows.
        ///
        /// Filled by the static constructor rather than by its own initialiser,
        /// and that is not a style choice: static field initialisers run in
        /// DECLARATION order, so building the table where it is declared would
        /// read <see cref="Primitives"/> - declared below it, for readability -
        /// while it was still null, and the whole type would fail to initialise.
        /// A static constructor runs after every field initialiser has.
        /// </summary>
        internal static readonly IReadOnlyList<Entry> All;

        static EmblemObjects()
        {
            All = Build();
        }

        internal static int Count => All.Count;

        /// <summary>
        /// The outline at an index, or null when there is nothing there.
        ///
        /// Null rather than a throw, because this is reached from the renderer
        /// while answering a request: an index the catalogue has since lost should
        /// cost that one layer, not the whole picture. <see cref="EmblemLayer"/>
        /// bounds-checks on the way in, so in practice this never returns null.
        /// </summary>
        internal static EmblemPath? PathAt(int index) =>
            index >= 0 && index < All.Count ? All[index].Path : null;

        internal static string NameAt(int index) =>
            index >= 0 && index < All.Count ? All[index].Name : "object " + index.ToString(CultureInfo.InvariantCulture);

        private static IReadOnlyList<Entry> Build()
        {
            List<Entry> entries = new List<Entry>();

            // 1. The five shield outlines. Reused rather than redrawn - a player
            //    building a crest wants the same heater the heraldic builder cut
            //    its field to, and a second description of it would drift.
            for (int i = 0; i < EmblemVocabulary.ShapeCount; i++)
            {
                entries.Add(new Entry(
                    EmblemVocabulary.ShapeNames[i],
                    ShieldCategory,
                    EmblemGeometry.Shape((EmblemVocabulary.Shape)i)));
            }

            // 2. The plain geometry a composition needs and the heraldic charges
            //    never offered: a filled disc, a square, bars, a diamond.
            foreach ((string name, string data) in Primitives)
            {
                entries.Add(new Entry(name, ShapeCategory, EmblemPath.Parse(data, Unit)));
            }

            // 3. The ten drawn-in-code heraldic charges, minus None.
            for (int i = 1; i < EmblemVocabulary.FirstDrawnDevice; i++)
            {
                EmblemPath? path = EmblemGeometry.Device((EmblemVocabulary.Charge)i);
                if (path == null) continue;

                entries.Add(new Entry(EmblemVocabulary.ChargeNames[i], ShapeCategory, path));
            }

            // 4. The traced artwork sheet.
            for (int i = 0; i < EmblemDeviceGeometry.Names.Count; i++)
            {
                int charge = EmblemVocabulary.FirstDrawnDevice + i;

                EmblemPath? path = EmblemGeometry.Device((EmblemVocabulary.Charge)charge);
                if (path == null) continue;

                entries.Add(new Entry(EmblemDeviceGeometry.Names[i], DeviceCategory, path));
            }

            // 5. The two hundred objects off the four later sheets, which arrive
            //    already in this file's coordinate system, fill rule and winding -
            //    so this is a move, not a conversion. They come LAST because they
            //    came last: everything above keeps the index it shipped with.
            foreach (EmblemObjectSheets.Icon icon in EmblemObjectSheets.All)
            {
                entries.Add(new Entry(icon.Name, icon.Category, icon.Path, icon.Hidden));
            }

            return entries;
        }

        // ------------------------------------------------------------ primitives

        /// <summary>
        /// The hand-authored shapes, as name and path data.
        ///
        /// APPEND ONLY - see the note on this class. The curved ones are emitted by
        /// the helpers below rather than typed as three hundred numbers, but they
        /// go through the SAME door: a string of integer thousandths that
        /// <see cref="EmblemPath.Parse"/> reads, so a shape somebody hands us as
        /// path data and a shape generated here are the same kind of thing.
        /// </summary>
        private static readonly (string Name, string Data)[] Primitives =
        {
            ("Disc",             Polygon(128, 980, 0)),
            ("Square",           Rect(-880, -880, 880, 880)),
            ("Bar",              Rect(-980, -320, 980, 320)),
            ("Slim bar",         Rect(-980, -120, 980, 120)),
            ("Post",             Rect(-160, -980, 160, 980)),
            ("Diamond",          "0 -980 980 0 0 980 -980 0"),
            ("Pentagon",         Polygon(5, 970, -90)),
            ("Octagon",          Polygon(8, 970, -90)),
            ("Chevron",          "0 -820 900 220 900 720 0 -280 -900 720 -900 220"),
            ("Arrowhead",        "0 -940 880 200 320 200 320 940 -320 940 -320 200 -880 200"),
            ("Half disc",        HalfDisc(980)),
            ("Trapezoid",        "-520 -760 520 -760 940 760 -940 760"),
            ("Right triangle",   "-900 -900 900 900 -900 900"),
            ("Blade",            Blade(980, 1180)),
            ("Four-point star",  Star(4, 980, 300)),
            ("Six-point star",   Star(6, 980, 480)),
            ("Eight-point star", Star(8, 980, 560)),
            ("Thin ring",        Ring(980, 840)),
        };

        // ------------------------------------------------------------- emitters

        /// <summary>A regular polygon with a vertex at <paramref name="degrees"/>
        /// (0 is to the right, and y points down, so -90 is straight up).</summary>
        private static string Polygon(int sides, int radius, int degrees)
        {
            StringBuilder data = new StringBuilder();

            for (int i = 0; i < sides; i++)
            {
                double angle = (degrees * Math.PI / 180.0) + 2.0 * Math.PI * i / sides;
                Point(data, radius * Math.Cos(angle), radius * Math.Sin(angle));
            }

            return data.ToString();
        }

        /// <summary>A star with <paramref name="points"/> points, first point up.</summary>
        private static string Star(int points, int outer, int inner)
        {
            StringBuilder data = new StringBuilder();

            for (int i = 0; i < points * 2; i++)
            {
                double angle = Math.PI * i / points;
                double radius = (i % 2 == 0) ? outer : inner;
                Point(data, radius * Math.Sin(angle), -radius * Math.Cos(angle));
            }

            return data.ToString();
        }

        /// <summary>
        /// An annulus: an outer ring and an inner one wound the OTHER way, which is
        /// what makes the non-zero rule read the middle as a hole rather than as
        /// more ring.
        /// </summary>
        private static string Ring(int outer, int inner)
        {
            StringBuilder data = new StringBuilder(Polygon(128, outer, 0));
            data.Append('|');

            for (int i = 127; i >= 0; i--)
            {
                double angle = 2.0 * Math.PI * i / 128;
                Point(data, inner * Math.Cos(angle), inner * Math.Sin(angle));
            }

            return data.ToString();
        }

        /// <summary>A disc cut flat across its middle - the flat edge at the top,
        /// so it reads as a dome the way a player expects.</summary>
        private static string HalfDisc(int radius)
        {
            StringBuilder data = new StringBuilder();

            for (int i = 0; i <= 64; i++)
            {
                double angle = Math.PI * i / 64;
                Point(data, -radius * Math.Cos(angle), radius * Math.Sin(angle));
            }

            return data.ToString();
        }

        /// <summary>
        /// A vesica - two circular arcs meeting at a point top and bottom. The one
        /// primitive here that is not a polygon or a star, and it earns its place:
        /// it is the leaf, the blade and the eye, and none of those can be built
        /// out of the others.
        ///
        /// The two arcs are the FAR halves of two circles whose centres sit either
        /// side of the origin: the circle on the LEFT supplies the right-hand
        /// boundary and the one on the right supplies the left-hand boundary. Both
        /// pass through the two tips because <c>offset^2 + half^2 = radius^2</c>,
        /// which is what makes them meet at a point instead of crossing.
        /// </summary>
        private static string Blade(int half, int radius)
        {
            double offset = Math.Sqrt((double)radius * radius - (double)half * half);

            // Where the tips sit, as an angle from either centre.
            double tip = Math.Asin(half / (double)radius);

            StringBuilder data = new StringBuilder();

            // Up the right-hand boundary, from the bottom tip to the top one.
            Arc(data, -offset, radius, -tip, tip, 48);

            // And back down the left-hand boundary. Half a turn away, so this is
            // the far side of the right-hand circle rather than the near side of
            // it - the branch that reads "correct" from the angle alone is the one
            // that puts the whole shape off to one side.
            Arc(data, offset, radius, Math.PI - tip, Math.PI + tip, 48);

            return data.ToString();
        }

        private static void Arc(
            StringBuilder data, double cx, double radius, double from, double to, int steps)
        {
            for (int i = 0; i <= steps; i++)
            {
                double angle = from + (to - from) * i / steps;
                Point(data, cx + radius * Math.Cos(angle), radius * Math.Sin(angle));
            }
        }

        private static string Rect(int x0, int y0, int x1, int y1) =>
            Join(x0, y0, x1, y0, x1, y1, x0, y1);

        private static string Join(params int[] values) =>
            string.Join(" ", values.Select(v => v.ToString(CultureInfo.InvariantCulture)));

        private static void Point(StringBuilder data, double x, double y)
        {
            if (data.Length > 0 && data[data.Length - 1] != '|') data.Append(' ');

            data.Append(((long)Math.Round(x)).ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(((long)Math.Round(y)).ToString(CultureInfo.InvariantCulture));
        }
    }
}

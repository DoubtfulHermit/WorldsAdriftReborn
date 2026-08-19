namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// Every outline the emblem feature draws, as <see cref="EmblemPath"/>, built
    /// once.
    ///
    /// ONE SOURCE OF SHAPE. The shield outlines, the field divisions and the ten
    /// drawn-in-code devices used to be region tests inside the painter, and the
    /// fifty traced devices arrived as path data. Two descriptions of one picture
    /// drift - this repo has paid for that lesson - and the SVG route makes it
    /// concrete: an exported crest whose heater shield came from a second
    /// implementation would eventually stop matching the PNG the game shows. So
    /// the arithmetic lives here, it produces paths, and both the rasteriser and
    /// the SVG writer read the same paths.
    ///
    /// WHY CURVES ARE FLATTENED. A shield's parabola and a ring's circle become
    /// polygons at build time. The error is bounded by the segment count and is
    /// chosen to be far under a pixel at the size anything renders at - a 128-gon
    /// standing in for the roundel departs from the true circle by four
    /// hundredths of a pixel at 256px - and a polygon is the one representation
    /// that both a scanline test and an SVG path can share without either of them
    /// growing a curve solver.
    ///
    /// COORDINATES are the painter's: a square [-1, 1] with y pointing DOWN.
    ///
    /// Pure and immutable; every field is built in the static initialiser and
    /// never written again.
    /// </summary>
    internal static class EmblemGeometry
    {
        /// <summary>Segments per full circle where a curve is flattened.</summary>
        private const int CircleSegments = 128;

        /// <summary>
        /// How far past the shield a division region is drawn.
        ///
        /// Divisions are always clipped by the shield, so their own outlines only
        /// have to be big enough to cover it. Oversizing means no division has to
        /// know which shape it is being painted on.
        /// </summary>
        private const double Overhang = 1.2;

        // ------------------------------------------------------------- shapes

        private static readonly EmblemPath[] Shapes =
        {
            BuildHeater(),
            BuildRegularPolygon(CircleSegments, 0.95, 0.0),
            BuildRegularPolygon(6, 0.97, -Math.PI / 2),
            EmblemPath.FromContours(new[] { 0.0, -0.97, 0.78, 0.0, 0.0, 0.97, -0.78, 0.0 }),
            EmblemPath.FromContours(new[]
            {
                -0.74, -0.94,
                0.74, -0.94,
                0.74, 0.94,
                0.00, 0.60,
                -0.74, 0.94,
            }),
        };

        /// <summary>The outline a field is cut to.</summary>
        internal static EmblemPath Shape(EmblemVocabulary.Shape shape)
        {
            int index = (int)shape;
            return Shapes[index >= 0 && index < Shapes.Length ? index : 0];
        }

        /// <summary>
        /// The heater shield: square shoulders with a small corner radius, sides
        /// straight down to the waist, then a sweep to a point.
        ///
        /// The sweep is a PARABOLA and not an ellipse quarter. An ellipse reaches
        /// zero width with a vertical tangent, so its sides come together almost
        /// horizontally and the shield ends in a rounded U; (1 - t^2) reaches zero
        /// with a finite slope and gives the heater its actual point.
        /// </summary>
        private static EmblemPath BuildHeater()
        {
            const double Top = -0.92, Waist = 0.12, Foot = 0.96, Half = 0.84, Corner = 0.12;
            const int Sweep = 48, Round = 12;

            List<double> points = new List<double>();

            void Add(double x, double y)
            {
                points.Add(x);
                points.Add(y);
            }

            Add(-(Half - Corner), Top);
            Add(Half - Corner, Top);

            // The top-right corner, sweeping from the top edge round to the side.
            for (int i = 1; i <= Round; i++)
            {
                double angle = -Math.PI / 2 + (Math.PI / 2) * i / Round;
                Add(Half - Corner + Corner * Math.Cos(angle), Top + Corner + Corner * Math.Sin(angle));
            }

            for (int i = 1; i <= Sweep; i++)
            {
                double t = (double)i / Sweep;
                Add(Half * (1.0 - t * t), Waist + (Foot - Waist) * t);
            }

            for (int i = Sweep - 1; i >= 0; i--)
            {
                double t = (double)i / Sweep;
                Add(-Half * (1.0 - t * t), Waist + (Foot - Waist) * t);
            }

            // The top-left corner, from the side back round to the top edge. The
            // angle INCREASES past pi, because y points down: the top of the arc is
            // at three-quarter turn, not at a quarter.
            for (int i = 0; i < Round; i++)
            {
                double angle = Math.PI + (Math.PI / 2) * i / Round;
                Add(-(Half - Corner) + Corner * Math.Cos(angle), Top + Corner + Corner * Math.Sin(angle));
            }

            return EmblemPath.FromContours(points.ToArray());
        }

        /// <summary>
        /// A regular polygon with <paramref name="sides"/> sides and a vertex at
        /// <paramref name="rotation"/>.
        /// </summary>
        private static EmblemPath BuildRegularPolygon(int sides, double radius, double rotation) =>
            EmblemPath.FromContours(RegularPolygon(sides, radius, rotation));

        private static double[] RegularPolygon(int sides, double radius, double rotation)
        {
            double[] points = new double[sides * 2];

            for (int i = 0; i < sides; i++)
            {
                double angle = rotation + 2.0 * Math.PI * i / sides;
                points[i * 2] = radius * Math.Cos(angle);
                points[i * 2 + 1] = radius * Math.Sin(angle);
            }

            return points;
        }

        // ---------------------------------------------------------- divisions

        /// <summary>
        /// The region a division paints in the DETAIL colour, or null where there
        /// is nothing to paint.
        ///
        /// <see cref="EmblemVocabulary.Division.Bordure"/> is null and handled by
        /// the callers, because it is the one division whose region depends on the
        /// shield: it is the shield minus a smaller copy of the shield, so it has
        /// no outline of its own that would work on all five.
        /// </summary>
        internal static EmblemPath? Division(EmblemVocabulary.Division division)
        {
            int index = (int)division;
            return index >= 0 && index < Divisions.Length ? Divisions[index] : null;
        }

        /// <summary>How far in the bordure's inner edge sits, as a fraction of the shield.</summary>
        internal const double BordureInset = 0.80;

        private static readonly EmblemPath?[] Divisions =
        {
            null,                                                    // Solid
            Rectangle(-Overhang, 0.0, Overhang, Overhang),           // Per fess
            Rectangle(0.0, -Overhang, Overhang, Overhang),           // Per pale
            EmblemPath.FromContours(new[]                            // Per bend
            {
                Overhang, -Overhang,
                Overhang, Overhang,
                -Overhang, Overhang,
            }),
            EmblemPath.FromContours(new[]                            // Chevron
            {
                -Overhang, 0.18 + 0.72 * Overhang,
                0.0, 0.18,
                Overhang, 0.18 + 0.72 * Overhang,
                Overhang, Overhang,
                -Overhang, Overhang,
            }),
            EmblemPath.FromContours(                                 // Quarterly
                Rect(-Overhang, 0.0, 0.0, Overhang),
                Rect(0.0, -Overhang, Overhang, 0.0)),
            null,                                                    // Bordure
            Rectangle(-Overhang, -Overhang, Overhang, -0.46),        // Chief
            Rectangle(-0.24, -Overhang, 0.24, Overhang),             // Pale
            Rectangle(-Overhang, -0.22, Overhang, 0.22),             // Fess
        };

        private static EmblemPath Rectangle(double x0, double y0, double x1, double y1) =>
            EmblemPath.FromContours(Rect(x0, y0, x1, y1));

        /// <summary>
        /// A rectangle, wound clockwise in screen order.
        ///
        /// The winding matters: two rectangles of the SAME winding union under the
        /// non-zero rule, which is what makes the quarterly division two overlapping
        /// pieces rather than a hand-cut outline, and the cross two bars.
        /// </summary>
        private static double[] Rect(double x0, double y0, double x1, double y1) =>
            new[] { x0, y0, x1, y0, x1, y1, x0, y1 };

        // ------------------------------------------------------ drawn devices

        /// <summary>
        /// The devices that are drawn in code rather than traced from artwork:
        /// clean heraldic geometry that stays legible at the sixteen-pixel roster
        /// crest, where a filigree animal reads as a smudge. Indexed by
        /// <see cref="EmblemVocabulary.Charge"/>, with index 0 (None) null.
        /// </summary>
        private static readonly EmblemPath?[] Geometric =
        {
            null,
            BuildRegularPolygon(6, 0.94, -Math.PI / 2),                     // Hexagon
            BuildStar(5, 0.98, 0.42),                                       // Star
            BuildGear(),                                                    // Gear
            EmblemPath.FromContours(new[]                                   // Bolt
            {
                0.30, -0.96,
                -0.52, 0.14,
                -0.06, 0.14,
                -0.30, 0.96,
                0.54, -0.20,
                0.06, -0.20,
            }),
            BuildRing(0.94, 0.56),                                          // Ring
            EmblemPath.FromContours(new[]                                   // Triangle
            {
                0.00, -0.94,
                0.92, 0.72,
                -0.92, 0.72,
            }),
            BuildCrescent(0.94, 0.80, 0.40),                                // Crescent
            EmblemPath.FromContours(                                        // Saltire
                Bar(0.30, 0.90, true),
                Bar(0.30, 0.90, false)),
            EmblemPath.FromContours(                                        // Cross
                Rect(-0.28, -0.92, 0.28, 0.92),
                Rect(-0.92, -0.28, 0.92, 0.28)),
            BuildChevrons(),                                                // Chevrons
        };

        /// <summary>
        /// The outline for a charge, or null for <see cref="EmblemVocabulary.Charge.None"/>.
        ///
        /// The two halves of the table meet here and nowhere else: below
        /// <see cref="EmblemVocabulary.FirstDrawnDevice"/> the outline was built by
        /// the arithmetic above, at or over it the outline was traced off the
        /// artwork sheet.
        /// </summary>
        internal static EmblemPath? Device(EmblemVocabulary.Charge charge)
        {
            int index = (int)charge;

            if (index < 0) return null;
            if (index < Geometric.Length) return Geometric[index];

            int drawn = index - EmblemVocabulary.FirstDrawnDevice;
            return drawn >= 0 && drawn < Traced.Length ? Traced[drawn] : null;
        }

        /// <summary>
        /// The traced artwork, parsed once.
        ///
        /// Eagerly, not lazily: fifty paths is a few milliseconds of parsing at
        /// startup, where a lazy table would need locking to stay thread-safe under
        /// a server that answers emblem requests concurrently, and would move that
        /// cost into the first player's page load instead.
        /// </summary>
        private static readonly EmblemPath[] Traced = BuildTraced();

        private static EmblemPath[] BuildTraced()
        {
            EmblemPath[] paths = new EmblemPath[EmblemDeviceGeometry.Paths.Count];

            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = EmblemPath.Parse(EmblemDeviceGeometry.Paths[i], EmblemDeviceGeometry.Unit);
            }

            return paths;
        }

        /// <summary>
        /// A star with <paramref name="points"/> points, first point straight up.
        /// </summary>
        private static EmblemPath BuildStar(int points, double outer, double inner)
        {
            double[] vertices = new double[points * 4];

            for (int i = 0; i < points * 2; i++)
            {
                // Angle 0 is straight UP in a y-down space.
                double angle = Math.PI * i / points;
                double radius = (i % 2 == 0) ? outer : inner;

                vertices[i * 2] = radius * Math.Sin(angle);
                vertices[i * 2 + 1] = -radius * Math.Cos(angle);
            }

            return EmblemPath.FromContours(vertices);
        }

        /// <summary>
        /// An eight-toothed gear: a body, teeth standing off it, and a bore.
        ///
        /// The bore is a second contour wound the OTHER way, which is what makes
        /// the non-zero rule read it as a hole rather than as more gear.
        /// </summary>
        private static EmblemPath BuildGear()
        {
            const int Teeth = 8;
            const double Body = 0.68, Tip = 0.97, Bore = 0.30;
            const double Fill = 0.52;
            const int Arc = 6;

            List<double> outline = new List<double>();

            void Add(double angle, double radius)
            {
                outline.Add(radius * Math.Cos(angle));
                outline.Add(radius * Math.Sin(angle));
            }

            double sector = 2.0 * Math.PI / Teeth;

            for (int tooth = 0; tooth < Teeth; tooth++)
            {
                double start = tooth * sector;
                double end = start + sector * Fill;

                Add(start, Body);
                Add(start, Tip);
                for (int i = 1; i < Arc; i++) Add(start + (end - start) * i / Arc, Tip);
                Add(end, Tip);
                Add(end, Body);

                for (int i = 1; i < Arc; i++) Add(end + (start + sector - end) * i / Arc, Body);
            }

            return EmblemPath.FromContours(
                outline.ToArray(),
                Reverse(RegularPolygon(CircleSegments / 2, Bore, 0.0)));
        }

        private static EmblemPath BuildRing(double outer, double inner) =>
            EmblemPath.FromContours(
                RegularPolygon(CircleSegments, outer, 0.0),
                Reverse(RegularPolygon(CircleSegments, inner, 0.0)));

        /// <summary>
        /// A disc with a second disc bitten out of it.
        ///
        /// Built as ONE contour - the arc of the disc that survives, then the arc
        /// of the bite walked back - rather than as two subpaths. Two subpaths
        /// would be wrong under either fill rule, because the biting disc reaches
        /// outside the bitten one and the region between them belongs to neither.
        /// </summary>
        private static EmblemPath BuildCrescent(double radius, double bite, double offset)
        {
            // Where the two circles cross.
            double x = (radius * radius - bite * bite + offset * offset) / (2.0 * offset);
            double y = Math.Sqrt(Math.Max(radius * radius - x * x, 0.0));

            double outerFrom = Math.Atan2(y, x);
            double outerTo = Math.Atan2(-y, x) + 2.0 * Math.PI;
            double biteFrom = Math.Atan2(-y, x - offset) + 2.0 * Math.PI;
            double biteTo = Math.Atan2(y, x - offset);

            List<double> points = new List<double>();
            int steps = CircleSegments;

            for (int i = 0; i <= steps; i++)
            {
                double angle = outerFrom + (outerTo - outerFrom) * i / steps;
                points.Add(radius * Math.Cos(angle));
                points.Add(radius * Math.Sin(angle));
            }

            for (int i = 0; i <= steps; i++)
            {
                double angle = biteFrom + (biteTo - biteFrom) * i / steps;
                points.Add(offset + bite * Math.Cos(angle));
                points.Add(bite * Math.Sin(angle));
            }

            return EmblemPath.FromContours(points.ToArray());
        }

        /// <summary>
        /// One arm of a saltire: the band of a given half-width about a diagonal,
        /// clipped to the square the device sits in.
        /// </summary>
        private static double[] Bar(double half, double extent, bool rising)
        {
            // The band |y -/+ x| <= half, cut by the square: a hexagon, because two
            // of the square's corners fall inside the band and two do not.
            double[] points = rising
                ? new[]
                {
                    -extent, -extent,
                    -extent + half, -extent,
                    extent, extent - half,
                    extent, extent,
                    extent - half, extent,
                    -extent, -extent + half,
                }
                : new[]
                {
                    -extent, extent,
                    -extent + half, extent,
                    extent, -extent + half,
                    extent, -extent,
                    extent - half, -extent,
                    -extent, extent - half,
                };

            // Both arms must wind the SAME way. Two arms of opposite winding would
            // cancel where they cross and leave a hole through the middle of the
            // saltire - which is the one place a saltire has to be solid.
            return Wind(points, true);
        }

        /// <summary>
        /// The same contour, reversed if it is not wound the way asked for.
        ///
        /// Written as a check rather than by getting the vertex order right by
        /// hand, because the vertex order that reads naturally in source is not the
        /// same one for a rising diagonal and a falling one, and the failure is
        /// invisible until two pieces are unioned.
        /// </summary>
        private static double[] Wind(double[] contour, bool positive)
        {
            double twice = 0.0;
            int points = contour.Length / 2;

            for (int i = 0; i < points; i++)
            {
                int j = (i + 1) % points;
                twice += contour[i * 2] * contour[j * 2 + 1] - contour[j * 2] * contour[i * 2 + 1];
            }

            return (twice > 0) == positive ? contour : Reverse(contour);
        }

        /// <summary>Three stacked chevrons, narrowing as they rise.</summary>
        private static EmblemPath BuildChevrons()
        {
            double[][] contours = new double[3][];

            for (int i = 0; i < 3; i++)
            {
                double apex = -0.58 + i * 0.44;
                double span = 0.94 - i * 0.10;
                double rise = 0.62 * span;

                contours[i] = new[]
                {
                    -span, apex + rise - 0.13,
                    0.0, apex - 0.13,
                    span, apex + rise - 0.13,
                    span, apex + rise + 0.13,
                    0.0, apex + 0.13,
                    -span, apex + rise + 0.13,
                };
            }

            return EmblemPath.FromContours(contours);
        }

        private static double[] Reverse(double[] contour)
        {
            int points = contour.Length / 2;
            double[] flipped = new double[contour.Length];

            for (int i = 0; i < points; i++)
            {
                flipped[i * 2] = contour[(points - 1 - i) * 2];
                flipped[i * 2 + 1] = contour[(points - 1 - i) * 2 + 1];
            }

            return flipped;
        }
    }
}

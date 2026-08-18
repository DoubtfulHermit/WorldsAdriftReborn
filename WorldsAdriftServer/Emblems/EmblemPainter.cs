namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// Draws an <see cref="EmblemSpec"/> into an RGBA pixel buffer. WAREBORN
    /// TUNING, like the rest of the builder.
    ///
    /// WHY IT IS A SAMPLER RATHER THAN A CANVAS. Every element of an emblem is a
    /// region test - "is this point inside the shield", "inside the charge",
    /// "inside the outline band" - so the whole picture is one pure function from
    /// a point to a colour. That buys three things a scanline canvas would not:
    /// antialiasing comes free from supersampling the same function; the
    /// composition order is a few lines of straight-line code instead of a draw
    /// list; and the renderer is deterministic and testable without a graphics
    /// context, which matters because this is the ONLY renderer - the builder's
    /// preview is a fetch of this PNG, not a second implementation in canvas or
    /// SVG. Two renderers of one picture drift, and this repo has already paid for
    /// that lesson once with the map mirror.
    ///
    /// COORDINATES. Everything below works in a square [-1, 1] space with y
    /// pointing DOWN (screen order), so a shape written here reads the same way it
    /// renders. The output is always SQUARE, and that is not a preference: the
    /// client's three emblem Images set neither <c>preserveAspect</c> nor
    /// <c>SetNativeSize</c> - the sprite is stretched to whatever the prefab rect
    /// is - so any non-square emblem we served would arrive distorted by an
    /// amount we cannot see or control.
    ///
    /// Pure: a spec and a size in, pixels out. No disk, no clock, no request.
    /// </summary>
    internal static class EmblemPainter
    {
        /// <summary>
        /// The rendered edge length, in pixels.
        ///
        /// The client cannot tell us what it wants: the emblem Images' sizes live
        /// in prefab data, not in any code in the decompile, so there is no
        /// recovered number to match. 256 is chosen to be comfortably larger than
        /// any crest a 2017-era 1080p UI draws - the large panel crest and the
        /// small roster mark are both well under it - so the sprite is downscaled
        /// rather than magnified in both places. It is also only a few kilobytes
        /// of flat colour once deflated, which is what makes "just serve a bigger
        /// one than anybody needs" the cheap answer rather than a wasteful one.
        /// </summary>
        internal const int Size = 256;

        /// <summary>
        /// Samples per pixel per axis. 5 means 25 samples and 26 possible alpha
        /// levels along an edge, which is past the point where a flat-colour
        /// silhouette shows stair-stepping. It is also the entire antialiasing
        /// strategy - there is no separate edge pass to disagree with it.
        /// </summary>
        private const int Supersample = 5;

        /// <summary>
        /// Where the outline band starts, as a fraction of the shape.
        ///
        /// The band is the shape minus a copy of itself scaled about the centre,
        /// so its width follows the silhouette instead of being a constant offset.
        /// That is an approximation of a stroke and it is deliberately a cheap
        /// one: for shapes this convex the width varies by well under a pixel's
        /// worth of noticeable, and a true offset curve would be a lot of maths
        /// for a difference nobody can see at 256 pixels.
        /// </summary>
        private const double OutlineInset = 0.955;

        /// <summary>
        /// The charge's half-size and vertical centre, PER SHAPE.
        ///
        /// Per shape rather than one constant because the shapes do not offer the
        /// same room: a lozenge stood on its point has barely half a heater's
        /// width at the height a device sits at, and a single size large enough to
        /// look right on a roundel put the star's tips through the lozenge's
        /// outline. The offsets go the other way - a banner's lower third is its
        /// swallow-tail and a heater's is its point, so a device centred by
        /// arithmetic in either reads as sitting low.
        ///
        /// Indexed by <see cref="EmblemVocabulary.Shape"/>.
        /// </summary>
        private static readonly double[] ChargeScales = { 0.50, 0.55, 0.54, 0.42, 0.46 };

        private static readonly double[] ChargeCentres = { -0.06, 0.00, 0.00, 0.00, -0.10 };

        /// <summary>Renders at the standard <see cref="Size"/>.</summary>
        internal static byte[] Render(EmblemSpec spec) => Render(spec, Size);

        /// <summary>
        /// Renders at an arbitrary edge length. Returns
        /// <paramref name="size"/> * <paramref name="size"/> * 4 bytes of
        /// non-premultiplied RGBA, row-major, top row first - exactly what
        /// <see cref="PngWriter.Encode"/> wants.
        /// </summary>
        internal static byte[] Render(EmblemSpec spec, int size)
        {
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));

            int field = EmblemVocabulary.ColourAt(spec.FieldColour);
            int detail = EmblemVocabulary.ColourAt(spec.DetailColour);
            int charge = EmblemVocabulary.ColourAt(spec.ChargeColour);

            byte[] pixels = new byte[size * size * 4];

            double step = 2.0 / size;
            double subStep = step / Supersample;
            double subOrigin = subStep * 0.5;

            for (int py = 0; py < size; py++)
            {
                double rowTop = -1.0 + py * step;

                for (int px = 0; px < size; px++)
                {
                    double colLeft = -1.0 + px * step;

                    // Premultiplied accumulation. Summing straight colour would
                    // average the colour of covered samples with the (undefined)
                    // colour of uncovered ones and fringe every edge dark.
                    double sumR = 0, sumG = 0, sumB = 0, sumA = 0;

                    for (int sy = 0; sy < Supersample; sy++)
                    {
                        double y = rowTop + subOrigin + sy * subStep;

                        for (int sx = 0; sx < Supersample; sx++)
                        {
                            double x = colLeft + subOrigin + sx * subStep;

                            if (!Sample(spec, x, y, field, detail, charge, out int rgb))
                            {
                                continue;
                            }

                            sumR += (rgb >> 16) & 0xFF;
                            sumG += (rgb >> 8) & 0xFF;
                            sumB += rgb & 0xFF;
                            sumA += 1.0;
                        }
                    }

                    int index = (py * size + px) * 4;

                    if (sumA <= 0)
                    {
                        // Left as four zero bytes: transparent AND black. A
                        // transparent white would bleed white into the edges when
                        // the client's UI filters the sprite down.
                        continue;
                    }

                    pixels[index] = Clamp(sumR / sumA);
                    pixels[index + 1] = Clamp(sumG / sumA);
                    pixels[index + 2] = Clamp(sumB / sumA);
                    pixels[index + 3] = Clamp(255.0 * sumA / (Supersample * Supersample));
                }
            }

            return pixels;
        }

        /// <summary>
        /// The whole picture, at one point. False means "nothing here" - the
        /// transparent surround outside the shield.
        ///
        /// The order below IS the composition order and it is the only place it is
        /// expressed: field, then division, then charge and its edge, then the
        /// shield's own outline on top of everything, then the light.
        /// </summary>
        private static bool Sample(
            EmblemSpec spec, double x, double y, int field, int detail, int chargeColour, out int rgb)
        {
            rgb = 0;

            if (!InShape(spec.Shape, x, y)) return false;

            // The shield's outline, painted last conceptually but tested first
            // because it wins over everything and short-circuits the rest. Never
            // shaded - a rim whose brightness varies stops reading as a drawn
            // line and starts reading as a lighting artefact.
            if (!InShape(spec.Shape, x / OutlineInset, y / OutlineInset))
            {
                rgb = EmblemVocabulary.OutlineInk;
                return true;
            }

            int colour = DivisionColour(spec, x, y, field, detail);

            if (spec.Charge != EmblemVocabulary.Charge.None)
            {
                int shape = (int)spec.Shape;
                double scale = ChargeScales[shape];
                double cx = x / scale;
                double cy = (y - ChargeCentres[shape]) / scale;

                if (InCharge(spec.Charge, cx, cy))
                {
                    colour = chargeColour;

                    // A dark keyline round the charge. Without it a charge whose
                    // colour is adjacent to the field's - two of the palette's
                    // blues, say - dissolves at roster size, which is the size
                    // that matters most and the one nobody checks.
                    if (!InCharge(spec.Charge, cx / 0.90, cy / 0.90))
                    {
                        colour = Blend(EmblemVocabulary.OutlineInk, chargeColour, 0.35);
                    }
                }
            }

            rgb = Shade(colour, y);
            return true;
        }

        /// <summary>
        /// A gentle top-lit gradient over the whole crest.
        ///
        /// Flat fills at this size look like clip art; a twelve percent swing from
        /// top to bottom is enough to read as a physical object and small enough
        /// that the palette colour is still recognisably the one that was picked.
        /// </summary>
        private static int Shade(int colour, double y)
        {
            double k = -y * 0.12;

            int r = (colour >> 16) & 0xFF, g = (colour >> 8) & 0xFF, b = colour & 0xFF;

            if (k >= 0)
            {
                r = (int)(r + (255 - r) * k);
                g = (int)(g + (255 - g) * k);
                b = (int)(b + (255 - b) * k);
            }
            else
            {
                double d = 1.0 + k;
                r = (int)(r * d);
                g = (int)(g * d);
                b = (int)(b * d);
            }

            return (Clamp(r) << 16) | (Clamp(g) << 8) | Clamp(b);
        }

        private static int Blend(int a, int b, double t)
        {
            int ar = (a >> 16) & 0xFF, ag = (a >> 8) & 0xFF, ab = a & 0xFF;
            int br = (b >> 16) & 0xFF, bg = (b >> 8) & 0xFF, bb = b & 0xFF;

            return (Clamp(ar + (br - ar) * t) << 16)
                 | (Clamp(ag + (bg - ag) * t) << 8)
                 | Clamp(ab + (bb - ab) * t);
        }

        // ---------------------------------------------------------- divisions

        private static int DivisionColour(EmblemSpec spec, double x, double y, int field, int detail)
        {
            switch (spec.Division)
            {
                case EmblemVocabulary.Division.PerFess:
                    return y < 0 ? field : detail;

                case EmblemVocabulary.Division.PerPale:
                    return x < 0 ? field : detail;

                case EmblemVocabulary.Division.PerBend:
                    return (x + y) < 0 ? field : detail;

                case EmblemVocabulary.Division.Chevron:
                    return y > (0.18 + 0.72 * Math.Abs(x)) ? detail : field;

                case EmblemVocabulary.Division.Quarterly:
                    return (x < 0) ^ (y < 0) ? detail : field;

                case EmblemVocabulary.Division.Bordure:
                    return InShape(spec.Shape, x / 0.80, y / 0.80) ? field : detail;

                case EmblemVocabulary.Division.Chief:
                    return y < -0.46 ? detail : field;

                case EmblemVocabulary.Division.Pale:
                    return Math.Abs(x) < 0.24 ? detail : field;

                case EmblemVocabulary.Division.Fess:
                    return Math.Abs(y) < 0.22 ? detail : field;

                default:
                    return field;
            }
        }

        // ------------------------------------------------------------- shapes

        private static bool InShape(EmblemVocabulary.Shape shape, double x, double y)
        {
            switch (shape)
            {
                case EmblemVocabulary.Shape.Round:
                    return x * x + y * y <= 0.95 * 0.95;

                case EmblemVocabulary.Shape.Hex:
                    return InRegularPolygon(x, y, 6, 0.97, -Math.PI / 2);

                case EmblemVocabulary.Shape.Kite:
                    return Math.Abs(x) / 0.78 + Math.Abs(y) / 0.97 <= 1.0;

                case EmblemVocabulary.Shape.Banner:
                    if (Math.Abs(x) > 0.74 || y < -0.94) return false;
                    return y <= 0.60 + 0.34 * (Math.Abs(x) / 0.74);

                default:
                    return InHeater(x, y);
            }
        }

        /// <summary>
        /// The heater shield: square shoulders with a small corner radius, sides
        /// straight down to the waist, then an elliptical sweep to a point.
        /// </summary>
        private static bool InHeater(double x, double y)
        {
            const double Top = -0.92, Waist = 0.12, Foot = 0.96, Half = 0.84, Corner = 0.12;

            if (y < Top || y > Foot) return false;

            double ax = Math.Abs(x);

            if (y <= Waist)
            {
                if (ax > Half) return false;

                // Round only the two TOP corners; the waist is a straight side.
                if (y < Top + Corner && ax > Half - Corner)
                {
                    double dx = ax - (Half - Corner);
                    double dy = (Top + Corner) - y;
                    return dx * dx + dy * dy <= Corner * Corner;
                }

                return true;
            }

            // Parabolic, not elliptical. An ellipse quarter reaches zero width
            // with a vertical tangent, so the sides come together almost
            // horizontally and the shield ends in a rounded U; (1 - t^2) reaches
            // zero with a finite slope and gives the heater its actual point.
            double t = (y - Waist) / (Foot - Waist);
            double width = Half * (1.0 - t * t);
            return ax <= width;
        }

        private static bool InRegularPolygon(double x, double y, int sides, double radius, double rotation)
        {
            // Convex, centred on the origin: inside iff the point is on the inner
            // side of every edge's supporting line. Written as a half-plane test
            // rather than a ray cast because it is exact and branch-free.
            double apothem = radius * Math.Cos(Math.PI / sides);

            for (int i = 0; i < sides; i++)
            {
                double angle = rotation + (2.0 * Math.PI * i / sides) + (Math.PI / sides);
                if (x * Math.Cos(angle) + y * Math.Sin(angle) > apothem) return false;
            }

            return true;
        }

        // ------------------------------------------------------------ charges

        private static bool InCharge(EmblemVocabulary.Charge charge, double x, double y)
        {
            switch (charge)
            {
                case EmblemVocabulary.Charge.Hexagon:
                    return InRegularPolygon(x, y, 6, 0.94, -Math.PI / 2);

                case EmblemVocabulary.Charge.Star:
                    return InStar(x, y, 5, 0.98, 0.42);

                case EmblemVocabulary.Charge.Compass:
                    return InStar(x, y, 8, 0.98, 0.30);

                case EmblemVocabulary.Charge.Gear:
                    return InGear(x, y);

                case EmblemVocabulary.Charge.Bolt:
                    return InPolygon(BoltOutline, x, y);

                case EmblemVocabulary.Charge.Ring:
                {
                    double r2 = x * x + y * y;
                    return r2 <= 0.94 * 0.94 && r2 >= 0.56 * 0.56;
                }

                case EmblemVocabulary.Charge.Triangle:
                    return InPolygon(TriangleOutline, x, y);

                case EmblemVocabulary.Charge.Crescent:
                {
                    bool inDisc = x * x + y * y <= 0.94 * 0.94;
                    double bx = x - 0.40;
                    bool inBite = bx * bx + y * y <= 0.80 * 0.80;
                    return inDisc && !inBite;
                }

                case EmblemVocabulary.Charge.Saltire:
                {
                    if (Math.Abs(x) > 0.90 || Math.Abs(y) > 0.90) return false;
                    return Math.Abs(x - y) <= 0.30 || Math.Abs(x + y) <= 0.30;
                }

                case EmblemVocabulary.Charge.Cross:
                {
                    if (Math.Abs(x) > 0.92 || Math.Abs(y) > 0.92) return false;
                    return Math.Abs(x) <= 0.28 || Math.Abs(y) <= 0.28;
                }

                case EmblemVocabulary.Charge.Anchor:
                    return InAnchor(x, y);

                case EmblemVocabulary.Charge.Chevrons:
                    return InChevrons(x, y);

                case EmblemVocabulary.Charge.Sun:
                    return InSun(x, y);

                default:
                    return false;
            }
        }

        /// <summary>
        /// A star with <paramref name="points"/> points, first point straight up.
        /// Done in polar - fold the angle into one point's sector and compare the
        /// radius against the straight edge running from the outer tip to the
        /// inner valley - because a polygon of 2n vertices is the same shape with
        /// n times the arithmetic.
        /// </summary>
        private static bool InStar(double x, double y, int points, double outer, double inner)
        {
            double r = Math.Sqrt(x * x + y * y);
            if (r > outer) return false;
            if (r <= inner) return true;

            double sector = Math.PI / points;

            // atan2(x, -y) puts angle 0 at straight UP in a y-down space.
            double angle = Math.Atan2(x, -y);
            angle -= 2.0 * sector * Math.Floor((angle + sector) / (2.0 * sector));

            // Distance from the centre to the tip-to-valley edge, along this ray.
            double a = Math.Abs(angle);
            double limit = outer * inner * Math.Sin(sector)
                / (outer * Math.Sin(a) + inner * Math.Sin(sector - a));

            return r <= limit;
        }

        private static bool InGear(double x, double y)
        {
            double r = Math.Sqrt(x * x + y * y);

            if (r <= 0.30) return false;      // the bore
            if (r <= 0.68) return true;       // the body

            if (r > 0.97) return false;

            // Eight teeth, each filling half of its 45-degree sector.
            const int Teeth = 8;
            double angle = Math.Atan2(y, x);
            double sector = 2.0 * Math.PI / Teeth;
            double within = angle - sector * Math.Floor(angle / sector);

            return within < sector * 0.52;
        }

        private static bool InAnchor(double x, double y)
        {
            double ax = Math.Abs(x);

            // The ring at the head.
            double ry = y + 0.76;
            double ringR2 = x * x + ry * ry;
            if (ringR2 <= 0.30 * 0.30 && ringR2 >= 0.15 * 0.15) return true;

            // The shank.
            if (ax <= 0.13 && y >= -0.76 && y <= 0.66) return true;

            // The stock.
            if (ax <= 0.56 && Math.Abs(y + 0.30) <= 0.11) return true;

            // The arms: the lower half of a thick ring, so they sweep up into
            // points the way a drawn anchor's do.
            double ar = Math.Sqrt(x * x + (y - 0.02) * (y - 0.02));
            if (y >= 0.34 && ar <= 0.82 && ar >= 0.60) return true;

            // The flukes: a barb on the end of each arm.
            double bx = ax - 0.71;
            if (y >= 0.28 && y <= 0.58 && bx >= -0.16 && bx <= 0.18
                && (y - 0.28) <= (0.30 * (0.18 - bx) / 0.34)) return true;

            return false;
        }

        private static bool InChevrons(double x, double y)
        {
            double ax = Math.Abs(x);
            if (ax > 0.94) return false;

            // Three stacked chevrons, narrowing as they rise, so the group reads
            // as one device rather than three separate bars.
            for (int i = 0; i < 3; i++)
            {
                double apex = -0.58 + i * 0.44;
                double span = 0.94 - i * 0.10;
                if (ax > span) continue;

                double line = apex + 0.62 * ax;
                if (Math.Abs(y - line) <= 0.13) return true;
            }

            return false;
        }

        private static bool InSun(double x, double y)
        {
            double r = Math.Sqrt(x * x + y * y);

            if (r <= 0.50) return true;
            if (r > 0.98) return false;

            // Twelve rays, tapering: a ray is a wedge whose angular half-width
            // shrinks as the radius grows, which is what makes it look pointed
            // rather than like a comb tooth.
            const int Rays = 12;
            double sector = 2.0 * Math.PI / Rays;
            double angle = Math.Atan2(y, x);
            double within = angle - sector * Math.Floor(angle / sector);
            double centred = Math.Abs(within - sector * 0.5);

            double taper = (0.98 - r) / (0.98 - 0.50);
            return centred <= sector * 0.30 * taper;
        }

        // -------------------------------------------------------- polygon bits

        private static readonly double[] TriangleOutline =
        {
            0.00, -0.94,
            0.92, 0.72,
            -0.92, 0.72,
        };

        private static readonly double[] BoltOutline =
        {
            0.30, -0.96,
            -0.52, 0.14,
            -0.06, 0.14,
            -0.30, 0.96,
            0.54, -0.20,
            0.06, -0.20,
        };

        /// <summary>Even-odd point-in-polygon over a flat x,y,x,y array.</summary>
        private static bool InPolygon(double[] poly, double x, double y)
        {
            bool inside = false;
            int count = poly.Length / 2;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                double xi = poly[i * 2], yi = poly[i * 2 + 1];
                double xj = poly[j * 2], yj = poly[j * 2 + 1];

                if ((yi > y) != (yj > y)
                    && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static byte Clamp(double value)
        {
            if (value <= 0) return 0;
            if (value >= 255) return 255;
            return (byte)(value + 0.5);
        }

        private static int Clamp(int value)
        {
            if (value <= 0) return 0;
            if (value >= 255) return 255;
            return value;
        }
    }
}

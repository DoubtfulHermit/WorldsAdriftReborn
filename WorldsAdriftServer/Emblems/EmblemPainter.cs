namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// Draws an <see cref="EmblemSpec"/> into an RGBA pixel buffer. WAREBORN
    /// TUNING, like the rest of the builder.
    ///
    /// WHY IT IS A SAMPLER RATHER THAN A CANVAS. Every element of an emblem is a
    /// region test - "is this point inside the shield", "inside the device",
    /// "inside the outline band" - so the whole picture is one pure function from
    /// a point to a colour. That buys three things a scanline canvas would not:
    /// antialiasing comes free from supersampling the same function; the
    /// composition order is a few lines of straight-line code instead of a draw
    /// list; and the renderer is deterministic and testable without a graphics
    /// context. The builder's preview is a fetch of this PNG rather than a
    /// re-drawing of the crest in canvas, for the same reason.
    ///
    /// THE SVG ROUTE IS NOT A SECOND RENDERER. <see cref="EmblemSvg"/> writes the
    /// same crest out as vectors for players to download, and it does that by
    /// emitting the SAME <see cref="EmblemPath"/> objects this file samples, in
    /// the same order, using the same constants - which is why the outline inset,
    /// the shading strength, the keyline inset and the device scales below are
    /// internal rather than private. Nothing about a shield or a device is
    /// described twice. Two descriptions of one picture drift, and this repo has
    /// already paid for that lesson once with the map mirror.
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
        /// Samples per pixel per axis ABOVE <see cref="Size"/>, for the same
        /// reason and on the same terms as
        /// <c>EmblemStackPainter.LargeSupersample</c>: an old heraldic crest can
        /// be downloaded at 1024 too, and twenty-five samples per pixel at that
        /// edge length is sixteen times the work for an edge already four times
        /// finer. The thinning starts strictly ABOVE <see cref="Size"/>, so the
        /// bytes the game is served - and the golden hashes that pin them - do not
        /// move.
        /// </summary>
        private const int LargeSupersample = 2;

        internal static int SupersampleFor(int size) =>
            size <= Size ? Supersample : LargeSupersample;

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
        internal const double OutlineInset = 0.955;

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
        internal static readonly double[] ChargeScales = { 0.50, 0.55, 0.54, 0.42, 0.46 };

        /// <summary>
        /// The same, for the TRACED devices, and smaller for a reason worth
        /// stating. A geometric device is drawn to fit the box it is scaled into -
        /// the star's tips reach 0.98 and its flanks are empty - so the corners of
        /// that box are free. Traced artwork fills its own bounding box in both
        /// axes by construction, so at the geometric scale the corner of a wide
        /// device lands on the lozenge's rim, where the painter would silently CLIP
        /// it. These values are the largest that keep all fifty devices clear of
        /// all five rims, which is what
        /// <c>No_device_touches_the_rim_of_any_shape</c> holds.
        ///
        /// Kept separate rather than shrinking <see cref="ChargeScales"/>, so that
        /// a crest saved before the sheet landed still draws its device at exactly
        /// the size it did.
        /// </summary>
        internal static readonly double[] DeviceScales = { 0.48, 0.53, 0.52, 0.36, 0.44 };

        internal static readonly double[] ChargeCentres = { -0.06, 0.00, 0.00, 0.00, -0.10 };

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

            int supersample = SupersampleFor(size);

            double step = 2.0 / size;
            double subStep = step / supersample;
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

                    for (int sy = 0; sy < supersample; sy++)
                    {
                        double y = rowTop + subOrigin + sy * subStep;

                        for (int sx = 0; sx < supersample; sx++)
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
                    pixels[index + 3] = Clamp(255.0 * sumA / (supersample * supersample));
                }
            }

            return pixels;
        }

        /// <summary>
        /// The whole picture, at one point. False means "nothing here" - the
        /// transparent surround outside the shield.
        ///
        /// The order below IS the composition order and it is the only place it is
        /// expressed: field, then division, then the device and its edge, then the
        /// shield's own outline on top of everything, then the light.
        /// </summary>
        private static bool Sample(
            EmblemSpec spec, double x, double y, int field, int detail, int chargeColour, out int rgb)
        {
            rgb = 0;

            EmblemPath outline = EmblemGeometry.Shape(spec.Shape);

            if (!outline.Contains(x, y)) return false;

            // The shield's outline, painted last conceptually but tested first
            // because it wins over everything and short-circuits the rest. Never
            // shaded - a rim whose brightness varies stops reading as a drawn
            // line and starts reading as a lighting artefact.
            if (!outline.Contains(x / OutlineInset, y / OutlineInset))
            {
                rgb = EmblemVocabulary.OutlineInk;
                return true;
            }

            int colour = DivisionColour(spec, x, y, field, detail);

            EmblemPath? device = EmblemGeometry.Device(spec.Charge);

            if (device != null)
            {
                int shape = (int)spec.Shape;
                bool drawn = EmblemVocabulary.IsDrawnDevice(spec.Charge);

                double scale = drawn ? DeviceScales[shape] : ChargeScales[shape];
                double cx = x / scale;
                double cy = (y - ChargeCentres[shape]) / scale;

                if (device.Contains(cx, cy))
                {
                    colour = chargeColour;

                    // A dark keyline round the geometric devices. Without it a
                    // device whose colour is adjacent to the field's - two of the
                    // palette's blues, say - dissolves at roster size, which is the
                    // size that matters most and the one nobody checks.
                    //
                    // NOT on the traced ones, and that is a judgement rather than
                    // an omission. The keyline is a SCALE, not an offset: it inks
                    // whatever falls between the device and a 90% copy of itself.
                    // On a convex blob that is a rim. On a filigree wolf made of
                    // sixty separate strokes, a 90% copy lands nowhere near the
                    // original, so the "rim" would be most of the animal and the
                    // device would come out as mud. A true offset outline would be
                    // the fix if one were wanted; at the size these draw, the
                    // artwork's own negative space already separates it from the
                    // field.
                    if (!drawn && !device.Contains(cx / KeylineInset, cy / KeylineInset))
                    {
                        colour = KeylineColour(chargeColour);
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
        /// <summary>
        /// How far the shading swings from top to bottom, as a fraction. Shared
        /// with <see cref="EmblemSvg"/>, which expresses the same light as a pair
        /// of gradients and must not be free to disagree about how strong it is.
        /// </summary>
        internal const double ShadeStrength = 0.12;

        /// <summary>How far in the geometric devices' keyline reaches.</summary>
        internal const double KeylineInset = 0.90;

        /// <summary>The ink a geometric device is edged in, for a given device colour.</summary>
        internal static int KeylineColour(int chargeColour) =>
            Blend(EmblemVocabulary.OutlineInk, chargeColour, 0.35);

        private static int Shade(int colour, double y)
        {
            double k = -y * ShadeStrength;

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
            // The bordure is the one division whose region depends on the shield:
            // it is the shield minus a smaller copy of itself, so unlike the other
            // nine it has no outline of its own that would work on all five shapes.
            if (spec.Division == EmblemVocabulary.Division.Bordure)
            {
                return EmblemGeometry.Shape(spec.Shape).Contains(
                    x / EmblemGeometry.BordureInset, y / EmblemGeometry.BordureInset)
                    ? field : detail;
            }

            EmblemPath? region = EmblemGeometry.Division(spec.Division);
            return region != null && region.Contains(x, y) ? detail : field;
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

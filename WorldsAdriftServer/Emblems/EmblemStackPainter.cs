namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// Draws an <see cref="EmblemStack"/> into an RGBA pixel buffer.
    ///
    /// A SAMPLER, like <see cref="EmblemPainter"/>, and for the same reasons: every
    /// layer is a region test, so the whole picture is a pure function from a point
    /// to a colour; antialiasing falls out of supersampling that function; and the
    /// renderer needs no graphics context to be tested. What is new here is that
    /// there are up to twenty regions instead of four, they OVERLAP, and each
    /// carries its own alpha - so this file composites where the heraldic painter
    /// only had to pick. A MIRRORED layer is two regions rather than one, so the
    /// ceiling is forty; what that costs is measured in EmblemStackRenderTests,
    /// and the answer is much less than double because the two halves sit on
    /// opposite sides of the canvas and the per-pixel bounds test drops whichever
    /// one is not there.
    ///
    /// IT IS FLAT, AND THAT IS A DECISION. The heraldic crest carries a top-lit
    /// gradient because a shield with one flat fill reads as clip art. A layered
    /// emblem gets none, because the player is composing the depth themselves and
    /// because the browser's live preview has to be able to reproduce this exactly
    /// while they drag - a shading formula is one more thing the two would have to
    /// agree about, for a nicety on a picture they did not ask us to improve.
    ///
    /// COMPOSITED FROM THE TOP DOWN, which is not the order the layers are drawn
    /// in. Walking the stack downwards and accumulating <c>alpha * (1 - covered)</c>
    /// gives exactly the same result as painting bottom-up with source-over - it is
    /// the same sum, reassociated - and it buys the one optimisation that matters:
    /// once a sample is fully covered, nothing underneath can change it, so the
    /// scan stops. A design whose bottom layer is an opaque disc (which is most of
    /// them - that is how you get a field) therefore costs a couple of tests per
    /// sample rather than twenty.
    ///
    /// COORDINATES are the painter's: a square [-1, 1] with y pointing DOWN. The
    /// output is SQUARE for the reason <see cref="EmblemPainter"/> gives - the
    /// client's emblem Images set neither preserveAspect nor SetNativeSize, so a
    /// non-square emblem arrives stretched by an amount we cannot see.
    ///
    /// Pure: a stack and a size in, pixels out. No disk, no clock, no request.
    /// </summary>
    internal static class EmblemStackPainter
    {
        /// <summary>
        /// Samples per pixel per axis.
        ///
        /// FOUR, where the heraldic painter uses five, and the difference is
        /// deliberate rather than a copy that drifted. That painter picks among at
        /// most four regions per sample; this one composites up to TWENTY layers,
        /// so the same per-pixel budget buys fewer samples - and the pathological
        /// design is a request an unauthenticated caller can ask for, so what it
        /// costs is a real number and not a micro-optimisation. Sixteen samples
        /// still give seventeen alpha levels along an edge, which is past the point
        /// where a flat-colour silhouette shows stair-stepping at the size this
        /// renders at; it is also the entire antialiasing strategy, so there is no
        /// separate edge pass to disagree with it.
        /// </summary>
        private const int Supersample = 4;

        /// <summary>
        /// Samples per pixel per axis ABOVE the crest size.
        ///
        /// THINNED, because the cost of this renderer is the number of SAMPLES and
        /// that is (size * supersample) squared - so a 1024-pixel download at the
        /// crest's four-by-four would be sixteen times the crest's work, and this
        /// route is unauthenticated. Two-by-two at 1024 puts the same number of
        /// samples on an edge as four-by-four at 512 and FOUR TIMES as many as the
        /// crest the game downloads, which is the largest number anything here is
        /// allowed to ask for (see <see cref="EmblemUrlPolicy.DownloadSizes"/>).
        ///
        /// It costs nothing visible. Antialiasing error is a fraction of a PIXEL,
        /// and a pixel at 1024 is a quarter of one at 256: four samples per pixel
        /// at 1024 resolve an edge two times finer than sixteen samples per pixel
        /// at 256 do. What it must never do is change the crest itself, so the
        /// thinning starts strictly ABOVE <see cref="EmblemPainter.Size"/> - every
        /// picture the game has ever been served is still sampled exactly as it
        /// was, which is what the golden hashes in EmblemArtworkTests pin.
        /// </summary>
        private const int LargeSupersample = 2;

        internal static int SupersampleFor(int size) =>
            size <= EmblemPainter.Size ? Supersample : LargeSupersample;

        /// <summary>
        /// Where the top-down scan gives up looking for more coverage. Not 1.0:
        /// twenty alphas of 0.975 sum to within a rounding error of opaque, and
        /// carrying on to test layers that can contribute less than a quarter of
        /// one of 255 levels is work for a pixel nobody can see.
        /// </summary>
        private const double Opaque = 0.9995;

        internal static byte[] Render(EmblemStack stack) => Render(stack, EmblemPainter.Size);

        /// <summary>
        /// Renders at an arbitrary edge length. Returns
        /// <paramref name="size"/> * <paramref name="size"/> * 4 bytes of
        /// non-premultiplied RGBA, row-major, top row first.
        /// </summary>
        internal static byte[] Render(EmblemStack stack, int size)
        {
            if (stack == null) throw new ArgumentNullException(nameof(stack));
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));

            byte[] pixels = new byte[size * size * 4];

            Placed[] placed = Place(stack);
            if (placed.Length == 0) return pixels;

            int supersample = SupersampleFor(size);

            double step = 2.0 / size;
            double subStep = step / supersample;
            double subOrigin = subStep * 0.5;

            // THE BOUNDS ARE NARROWED TWICE, AND THAT IS WHERE THE TIME GOES.
            // Every layer's bounds are tested once per ROW and then once per PIXEL,
            // leaving the twenty-five subsamples inside that pixel to walk only the
            // layers that could possibly reach it. Testing them per SUBSAMPLE
            // instead - which is what reads naturally - does the same twenty-odd
            // comparisons twenty-five times over for every pixel of the emblem, and
            // measured about four times the whole render.
            //
            // Both arrays are allocated once per render rather than per row.
            int[] onRow = new int[placed.Length];
            int[] onPixel = new int[placed.Length];

            for (int py = 0; py < size; py++)
            {
                double rowTop = -1.0 + py * step;
                double rowBottom = rowTop + step;

                int rowCount = 0;
                for (int i = 0; i < placed.Length; i++)
                {
                    if (placed[i].MaxY < rowTop || placed[i].MinY > rowBottom) continue;
                    onRow[rowCount++] = i;
                }

                if (rowCount == 0) continue;

                for (int px = 0; px < size; px++)
                {
                    double colLeft = -1.0 + px * step;
                    double colRight = colLeft + step;

                    int count = 0;
                    for (int i = 0; i < rowCount; i++)
                    {
                        ref Placed candidate = ref placed[onRow[i]];
                        if (candidate.MaxX < colLeft || candidate.MinX > colRight) continue;
                        onPixel[count++] = onRow[i];
                    }

                    if (count == 0) continue;

                    double sumR = 0, sumG = 0, sumB = 0, sumA = 0;

                    for (int sy = 0; sy < supersample; sy++)
                    {
                        double y = rowTop + subOrigin + sy * subStep;

                        for (int sx = 0; sx < supersample; sx++)
                        {
                            double x = colLeft + subOrigin + sx * subStep;

                            double r = 0, g = 0, b = 0, a = 0;

                            // TOP DOWN - see the note on this class.
                            for (int i = count - 1; i >= 0; i--)
                            {
                                if (a >= Opaque) break;

                                ref Placed layer = ref placed[onPixel[i]];

                                if (!layer.Contains(x, y)) continue;

                                double contribution = layer.Alpha * (1.0 - a);

                                r += layer.R * contribution;
                                g += layer.G * contribution;
                                b += layer.B * contribution;
                                a += contribution;
                            }

                            sumR += r;
                            sumG += g;
                            sumB += b;
                            sumA += a;
                        }
                    }

                    if (sumA <= 0) continue;

                    int index = (py * size + px) * 4;

                    // Premultiplied throughout, un-premultiplied once here. Summing
                    // straight colour would average covered samples with the
                    // undefined colour of uncovered ones and fringe every edge.
                    pixels[index] = Clamp(sumR / sumA);
                    pixels[index + 1] = Clamp(sumG / sumA);
                    pixels[index + 2] = Clamp(sumB / sumA);
                    pixels[index + 3] = Clamp(255.0 * sumA / (supersample * supersample));

                    // Nothing is written when sumA is zero, so the surround stays
                    // four zero bytes: transparent AND black. A transparent white
                    // would bleed white into the edges when the client's UI filters
                    // the sprite down.
                }
            }

            return pixels;
        }

        /// <summary>
        /// One layer, with its transform inverted and its bounds known.
        ///
        /// Built once per render rather than per sample, because a 256-pixel emblem
        /// asks <see cref="Contains"/> up to 1.6 million times per layer and a
        /// sine per ask would dominate everything else in this file.
        /// </summary>
        private struct Placed
        {
            internal EmblemPath Path;

            internal double Cos;
            internal double Sin;
            internal double CentreX;
            internal double CentreY;
            internal double InverseScaleX;
            internal double InverseScaleY;

            internal double MinX, MaxX, MinY, MaxY;

            internal double Alpha;
            internal double R, G, B;

            /// <summary>
            /// Whether a point of the CANVAS falls inside this layer.
            ///
            /// The forward transform is the one every renderer writes as
            /// <c>translate(x y) rotate(deg) scale(sx sy)</c>, applied to the
            /// geometry right to left: scale (with the flip as the scale's sign),
            /// then turn, then move. This undoes it in the opposite order - move
            /// back, turn back, unscale - which is the whole of the agreement
            /// between this rasteriser and the vectors the browser draws.
            /// </summary>
            internal bool Contains(double x, double y)
            {
                double dx = x - CentreX;
                double dy = y - CentreY;

                // Rotate by -theta. In a y-down space a positive angle turns
                // clockwise on screen, which is what SVG's rotate() does, so this
                // is the transpose of that matrix and not its mirror.
                double ux = dx * Cos + dy * Sin;
                double uy = -dx * Sin + dy * Cos;

                return Path.Contains(ux * InverseScaleX, uy * InverseScaleY);
            }
        }

        private static Placed[] Place(EmblemStack stack)
        {
            List<Placed> placed = new List<Placed>(stack.Count);

            foreach (EmblemLayer layer in stack.Layers)
            {
                EmblemPath? path = layer.Path;
                if (path == null) continue;

                // A layer with no size or no opacity draws nothing. Dropped here
                // rather than tested per sample: it cannot become visible later.
                if (layer.Size <= 0 || layer.Opacity <= 0) continue;

                int rgb = EmblemVocabulary.ColourAt(layer.Colour);
                double scaleY = layer.FlipY ? -layer.Scale : layer.Scale;

                // ONE REGION PER INSTANCE. A mirrored layer is two, and they are
                // ordinary entries in this list rather than a special case in
                // Contains - so the compositing, the early-out and the bounds
                // narrowing all treat a reflection exactly as they treat any other
                // shape, and the only thing symmetry adds is one more region.
                //
                // Each instance's angle and x-scale come from the SAME integers
                // EmblemLayer writes into the transform string, not from a second
                // derivation of "what a mirror does" - which is what keeps this
                // agreeing with the SVG the browser draws.
                for (int instance = 0; instance < layer.Instances; instance++)
                {
                    double angle = layer.InstanceRadians(instance);
                    double cos = Math.Cos(angle);
                    double sin = Math.Sin(angle);

                    double scaleX = layer.InstanceScaleX(instance);
                    double centreX = layer.InstanceCentreX(instance);

                    Placed entry = new Placed
                    {
                        Path = path,
                        Cos = cos,
                        Sin = sin,
                        CentreX = centreX,
                        CentreY = layer.CentreY,
                        InverseScaleX = 1.0 / scaleX,
                        InverseScaleY = 1.0 / scaleY,
                        Alpha = layer.Alpha,
                        R = (rgb >> 16) & 0xFF,
                        G = (rgb >> 8) & 0xFF,
                        B = rgb & 0xFF,
                    };

                    Bounds(path, cos, sin, scaleX, scaleY, centreX, layer.CentreY, ref entry);

                    placed.Add(entry);
                }
            }

            return placed.ToArray();
        }

        /// <summary>
        /// The layer's axis-aligned bounds on the canvas.
        ///
        /// The rotated box's own bounding box, so it is conservative for a turned
        /// layer - which is exactly what a REJECTION test wants. It is only ever
        /// used to skip work, never to decide a pixel, so being generous costs a
        /// few containment tests and being wrong the other way would clip artwork.
        /// </summary>
        private static void Bounds(
            EmblemPath path, double cos, double sin, double scaleX, double scaleY,
            double centreX, double centreY, ref Placed entry)
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            for (int corner = 0; corner < 4; corner++)
            {
                double lx = (corner == 0 || corner == 3) ? path.MinX : path.MaxX;
                double ly = corner < 2 ? path.MinY : path.MaxY;

                double sxv = lx * scaleX;
                double syv = ly * scaleY;

                double x = sxv * cos - syv * sin + centreX;
                double y = sxv * sin + syv * cos + centreY;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            entry.MinX = minX;
            entry.MaxX = maxX;
            entry.MinY = minY;
            entry.MaxY = maxY;
        }

        private static byte Clamp(double value)
        {
            if (value <= 0) return 0;
            if (value >= 255) return 255;
            return (byte)(value + 0.5);
        }
    }
}

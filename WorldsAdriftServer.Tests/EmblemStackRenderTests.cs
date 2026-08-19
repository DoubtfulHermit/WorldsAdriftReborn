using System.Diagnostics;
using WorldsAdriftServer.Emblems;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The layered rasteriser, and the one property everything else rests on: that
    /// where it puts ink is where the TRANSFORM STRING says the ink goes.
    ///
    /// That string - <c>translate(x y) rotate(d) scale(sx sy)</c> - is what the
    /// downloadable vector carries and what the browser builds while a player
    /// drags. This suite does not ask the painter whether it agrees with itself: it
    /// re-derives the forward transform from SVG's own semantics (a transform list
    /// applies right to left to the geometry), maps known points of a known
    /// outline through it, and asserts the rendered pixel there is the layer's
    /// colour. A painter that composed its rotation the other way round, or that
    /// applied the flip after the turn instead of as the scale's sign, fails here
    /// even though it would still be perfectly self-consistent.
    /// </summary>
    public class EmblemStackRenderTests
    {
        private const int Size = 128;

        /// <summary>
        /// A right triangle, because it is the only primitive whose every symmetry
        /// is broken: mirroring it, turning it or transposing the transform order
        /// all move ink somewhere a symmetric shape would have hidden.
        /// </summary>
        private static readonly int Triangle = ObjectNamed("Right triangle");

        private static readonly int Disc = ObjectNamed("Disc");

        private static int ObjectNamed(string name)
        {
            for (int i = 0; i < EmblemObjects.Count; i++)
            {
                if (EmblemObjects.All[i].Name == name) return i;
            }

            throw new InvalidOperationException("the catalogue has no '" + name + "'");
        }

        private static EmblemLayer Layer(
            int obj, int x = 0, int y = 0, int size = 500, int rotation = 0,
            int colour = 0, int opacity = EmblemLayer.OpacitySteps,
            bool flipX = false, bool flipY = false, bool locked = false)
        {
            Assert.True(EmblemLayer.TryCreate(
                obj, x, y, size, rotation, colour, opacity, flipX, flipY, locked,
                out EmblemLayer layer));
            return layer;
        }

        private static EmblemStack Stack(params EmblemLayer[] layers)
        {
            Assert.True(EmblemStack.TryCreate(layers, out EmblemStack stack));
            return stack;
        }

        private static (int R, int G, int B, int A) Pixel(byte[] rgba, int size, int px, int py)
        {
            int at = (py * size + px) * 4;
            return (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]);
        }

        /// <summary>
        /// A point of the object's own outline, mapped onto the canvas exactly the
        /// way <c>translate(x y) rotate(d) scale(sx sy)</c> maps it - scale first
        /// (the flip being the scale's sign), then turn, then move.
        /// </summary>
        private static (int Px, int Py) Place(EmblemLayer layer, double localX, double localY, int size)
        {
            double sx = layer.FlipX ? -layer.Size : layer.Size;
            double sy = layer.FlipY ? -layer.Size : layer.Size;

            double x = localX * sx / EmblemLayer.Unit;
            double y = localY * sy / EmblemLayer.Unit;

            double radians = layer.Rotation * Math.PI / 180.0;
            double cos = Math.Cos(radians), sin = Math.Sin(radians);

            double turnedX = x * cos - y * sin;
            double turnedY = x * sin + y * cos;

            double worldX = (turnedX + layer.X) / EmblemLayer.Unit;
            double worldY = (turnedY + layer.Y) / EmblemLayer.Unit;

            return ((int)((worldX + 1.0) * 0.5 * size), (int)((worldY + 1.0) * 0.5 * size));
        }

        // The right triangle's own corners, in the units the catalogue stores.
        private const double TriA = -900, TriB = 900;

        /// <summary>Well inside the triangle: the centroid.</summary>
        private static readonly (double X, double Y) Inside = ((TriA + TriB + TriA) / 3, (TriA + TriB + TriB) / 3);

        /// <summary>Inside its BOUNDING BOX but the other side of the hypotenuse,
        /// which is the point a transposed or mirrored transform would fill.</summary>
        private static readonly (double X, double Y) Outside = (700, -700);

        // -------------------------------------------------------- the transform

        [Theory]
        [InlineData(0, 0, 700, 0, false, false)]
        [InlineData(300, -200, 500, 0, false, false)]
        [InlineData(0, 0, 700, 90, false, false)]
        [InlineData(0, 0, 700, 45, false, false)]
        [InlineData(-250, 250, 400, 200, false, false)]
        [InlineData(0, 0, 700, 0, true, false)]
        [InlineData(0, 0, 700, 0, false, true)]
        [InlineData(0, 0, 700, 0, true, true)]
        [InlineData(120, 90, 600, 315, true, false)]
        [InlineData(-400, -400, 350, 137, false, true)]
        public void Ink_lands_exactly_where_the_transform_string_says_it_does(
            int x, int y, int size, int rotation, bool flipX, bool flipY)
        {
            EmblemLayer layer = Layer(Triangle, x, y, size, rotation, colour: 8);
            byte[] pixels = EmblemStackPainter.Render(Stack(layer), Size);

            (int px, int py) = Place(layer, Inside.X, Inside.Y, Size);
            (int r, int g, int b, int a) = Pixel(pixels, Size, px, py);

            int expected = EmblemVocabulary.ColourAt(8);

            Assert.True(a == 255,
                "the layer's own interior is not opaque at " + px + "," + py
                + " for " + layer.Transform());
            Assert.Equal((expected >> 16) & 0xFF, r);
            Assert.Equal((expected >> 8) & 0xFF, g);
            Assert.Equal(expected & 0xFF, b);

            (int qx, int qy) = Place(layer, Outside.X, Outside.Y, Size);
            Assert.Equal(0, Pixel(pixels, Size, qx, qy).A);
        }

        [Fact]
        public void The_canvas_is_transparent_where_no_layer_reaches()
        {
            byte[] pixels = EmblemStackPainter.Render(Stack(Layer(Disc, size: 200)), Size);

            foreach ((int px, int py) in new[] { (0, 0), (Size - 1, 0), (0, Size - 1), (Size - 1, Size - 1) })
            {
                (int r, int g, int b, int a) = Pixel(pixels, Size, px, py);

                Assert.Equal(0, a);

                // Transparent AND black. A transparent white bleeds white into the
                // edges when the client's UI filters the sprite down.
                Assert.Equal(0, r + g + b);
            }
        }

        [Fact]
        public void An_empty_design_renders_nothing_at_all()
        {
            byte[] pixels = EmblemStackPainter.Render(EmblemStack.Empty, 16);

            Assert.All(pixels, b => Assert.Equal(0, b));
        }

        // -------------------------------------------------------- the compositing

        [Fact]
        public void Opacity_reaches_the_alpha_channel()
        {
            byte[] full = EmblemStackPainter.Render(
                Stack(Layer(Disc, size: 900, opacity: EmblemLayer.OpacitySteps)), Size);
            byte[] half = EmblemStackPainter.Render(
                Stack(Layer(Disc, size: 900, opacity: EmblemLayer.OpacitySteps / 2)), Size);

            Assert.Equal(255, Pixel(full, Size, Size / 2, Size / 2).A);

            // Half of forty steps is exactly a half, so the alpha is 127 or 128
            // depending only on which way the byte rounds.
            int alpha = Pixel(half, Size, Size / 2, Size / 2).A;
            Assert.InRange(alpha, 127, 128);
        }

        [Fact]
        public void A_layer_at_zero_opacity_draws_nothing()
        {
            byte[] pixels = EmblemStackPainter.Render(Stack(Layer(Disc, size: 900, opacity: 0)), 16);

            Assert.All(pixels, b => Assert.Equal(0, b));
        }

        /// <summary>
        /// LATER LAYERS GO OVER EARLIER ONES. The painter walks the stack from the
        /// TOP down and accumulates coverage, which is the same sum as painting
        /// bottom-up with source-over, reassociated - so this is the assertion that
        /// the reassociation is right and not merely fast.
        /// </summary>
        [Fact]
        public void The_last_layer_is_the_one_in_front()
        {
            byte[] pixels = EmblemStackPainter.Render(
                Stack(Layer(Disc, size: 900, colour: 8), Layer(Disc, size: 500, colour: 10)), Size);

            int front = EmblemVocabulary.ColourAt(10);
            (int r, int g, int b, int a) = Pixel(pixels, Size, Size / 2, Size / 2);

            Assert.Equal(255, a);
            Assert.Equal((front >> 16) & 0xFF, r);
            Assert.Equal((front >> 8) & 0xFF, g);
            Assert.Equal(front & 0xFF, b);
        }

        [Fact]
        public void A_translucent_layer_blends_with_what_is_under_it()
        {
            int under = EmblemVocabulary.ColourAt(4);
            int over = EmblemVocabulary.ColourAt(0);

            byte[] pixels = EmblemStackPainter.Render(
                Stack(
                    Layer(Disc, size: 900, colour: 4),
                    Layer(Disc, size: 500, colour: 0, opacity: EmblemLayer.OpacitySteps / 2)),
                Size);

            (int r, int g, int b, int a) = Pixel(pixels, Size, Size / 2, Size / 2);

            Assert.Equal(255, a);

            // Half and half, to within the byte rounding of each channel.
            Assert.InRange(r, (((under >> 16) & 0xFF) + ((over >> 16) & 0xFF)) / 2 - 1,
                              (((under >> 16) & 0xFF) + ((over >> 16) & 0xFF)) / 2 + 1);
            Assert.InRange(g, (((under >> 8) & 0xFF) + ((over >> 8) & 0xFF)) / 2 - 1,
                              (((under >> 8) & 0xFF) + ((over >> 8) & 0xFF)) / 2 + 1);
            Assert.InRange(b, ((under & 0xFF) + (over & 0xFF)) / 2 - 1,
                              ((under & 0xFF) + (over & 0xFF)) / 2 + 1);
        }

        /// <summary>
        /// The lock is an EDITOR fact. It travels in the saved code so a reload
        /// does not forget it, and it must change no pixel - a lock that altered
        /// the picture would be a lock nobody could safely use.
        /// </summary>
        [Fact]
        public void Locking_a_layer_changes_nothing_about_the_picture()
        {
            byte[] unlocked = EmblemStackPainter.Render(Stack(Layer(Triangle, size: 800)), Size);
            byte[] locked = EmblemStackPainter.Render(Stack(Layer(Triangle, size: 800, locked: true)), Size);

            Assert.Equal(unlocked, locked);
        }

        [Fact]
        public void A_layer_may_hang_off_the_edge_and_is_clipped_rather_than_moved()
        {
            EmblemLayer layer = Layer(Disc, x: 1000, size: 900);
            byte[] pixels = EmblemStackPainter.Render(Stack(layer), Size);

            // Its left half is on the canvas; its right half is not, and nothing
            // has wrapped round to the far side.
            Assert.Equal(255, Pixel(pixels, Size, Size - 4, Size / 2).A);
            Assert.Equal(0, Pixel(pixels, Size, 2, Size / 2).A);
        }

        // ------------------------------------------------------------ the budget

        /// <summary>
        /// Twenty overlapping layers of the heaviest traced artwork, none of them
        /// quite opaque so the top-down scan's early-out can never fire and every
        /// layer is tested at every sample. This is the most expensive picture the
        /// vocabulary can express, and therefore the only honest input to any
        /// question about what this route costs.
        ///
        /// THE TWENTY ARE CHOSEN BY MEASUREMENT, not by position. This used to take
        /// the last twenty entries, which was the same thing while the end of the
        /// catalogue was the traced device sheet - and stopped being the same thing
        /// the moment two hundred objects were appended after it, most of them plain
        /// geometry an order of magnitude cheaper. A worst case that silently
        /// becomes an easy case is worse than no worst case, because it still
        /// reports green.
        /// </summary>
        private static EmblemStack WorstCase()
        {
            int[] heaviest = Enumerable.Range(0, EmblemObjects.Count)
                .OrderByDescending(i => EmblemObjects.All[i].Path.EdgeCount)
                .ThenBy(i => i)
                .Take(EmblemStack.MaxLayers)
                .ToArray();

            List<EmblemLayer> layers = new List<EmblemLayer>();

            for (int i = 0; i < EmblemStack.MaxLayers; i++)
            {
                layers.Add(Layer(
                    heaviest[i],
                    x: (i % 5) * 60 - 120,
                    y: (i % 7) * 40 - 120,
                    size: 900,
                    rotation: i * 17,
                    colour: i % EmblemVocabulary.ColourCount,
                    opacity: EmblemLayer.OpacitySteps - 1));
            }

            return Stack(layers.ToArray());
        }

        /// <summary>
        /// A FULL DESIGN AT FULL SIZE, timed.
        ///
        /// The emblem route is unauthenticated and renders whatever is in a query
        /// string, so "how long can a stranger make this take" is a real question
        /// and not a micro-optimisation. Twenty overlapping layers of the heaviest
        /// traced artwork is the worst case the vocabulary can express.
        ///
        /// The bound is generous on purpose - it is a guard against a change that
        /// makes this seconds, not a benchmark - and the result is cached by code
        /// afterwards, so a given design is paid for once.
        /// </summary>
        [Fact]
        public void The_worst_design_the_vocabulary_allows_renders_in_well_under_a_second()
        {
            EmblemStack stack = WorstCase();

            // Warmed first: the traced paths are parsed and indexed on first touch,
            // and that cost belongs to startup rather than to a request.
            EmblemStackPainter.Render(stack, EmblemPainter.Size);

            Stopwatch clock = Stopwatch.StartNew();
            byte[] pixels = EmblemStackPainter.Render(stack, EmblemPainter.Size);
            clock.Stop();

            Assert.Equal(EmblemPainter.Size * EmblemPainter.Size * 4, pixels.Length);

            // MEASURED AS A RATIO against the crest this route has always served,
            // not in milliseconds. A wall-clock bound on a suite that runs its
            // collections in parallel is a test that fails on a busy machine and
            // passes on a quiet one, which teaches a reader that red is weather.
            // Both renders are slowed by the same load, so the ratio is not.
            Assert.True(EmblemSpec.TryParse("2-0-7-39-9-9-4", out EmblemSpec heraldic));
            EmblemPainter.Render(heraldic, EmblemPainter.Size);

            Stopwatch reference = Stopwatch.StartNew();
            EmblemPainter.Render(heraldic, EmblemPainter.Size);
            reference.Stop();

            double ratio = clock.Elapsed.TotalMilliseconds
                / Math.Max(0.001, reference.Elapsed.TotalMilliseconds);

            // Seven on a warm release build at the time of writing (340 ms against
            // 47). Twenty is the guard rail: it catches "this became a second per
            // request" without failing over a factor of two.
            Assert.True(ratio < 20,
                "a full twenty-layer emblem cost " + ratio.ToString("0.0")
                + " times a heraldic crest (" + clock.ElapsedMilliseconds + " ms against "
                + reference.ElapsedMilliseconds + " ms)");
        }

        /// <summary>
        /// THE SAME QUESTION FOR THE DOWNLOAD SIZES, which is where it gets sharp:
        /// the route renders whatever a stranger puts in a query string, and the
        /// cost of a render is (edge length * samples per axis) squared. A 1024
        /// download at the crest's four-by-four supersampling would be SIXTEEN
        /// times the worst case above - measured at 4.4 seconds on the box this
        /// was written on, unauthenticated, per request.
        ///
        /// So the painter thins its sampling above the crest size, and this is the
        /// test that says why that number was chosen: with it, the most expensive
        /// picture anybody can ask for is about four times the crest's, and 512 -
        /// the middle offer - costs LESS than the 256 the game already downloads.
        /// Measured as a ratio for the reason the test above gives.
        /// </summary>
        [Fact]
        public void No_size_the_download_offers_costs_much_more_than_the_crest_does()
        {
            EmblemStack stack = WorstCase();

            // Warm both painters and both paths through the size gate.
            EmblemStackPainter.Render(stack, EmblemPainter.Size);
            Assert.True(EmblemSpec.TryParse("2-0-7-39-9-9-4", out EmblemSpec heraldic));
            EmblemPainter.Render(heraldic, EmblemPainter.Size);

            Stopwatch reference = Stopwatch.StartNew();
            EmblemStackPainter.Render(stack, EmblemPainter.Size);
            reference.Stop();

            double crest = Math.Max(0.001, reference.Elapsed.TotalMilliseconds);

            foreach (int size in EmblemUrlPolicy.DownloadSizes)
            {
                Stopwatch clock = Stopwatch.StartNew();
                byte[] pixels = EmblemStackPainter.Render(stack, size);
                clock.Stop();

                Assert.Equal(size * size * 4, pixels.Length);

                double ratio = clock.Elapsed.TotalMilliseconds / crest;

                // Four times the crest is the arithmetic - 1024 with two samples
                // per axis is a 2048-sample grid against the crest's 1024 - and
                // ten is the guard rail, which catches "somebody put the
                // supersampling back" (that would be sixteen) without failing over
                // a busy machine.
                Assert.True(ratio < 10,
                    "a full twenty-layer emblem at " + size + " cost " + ratio.ToString("0.0")
                    + " times the same design at " + EmblemPainter.Size + " ("
                    + clock.ElapsedMilliseconds + " ms against " + reference.ElapsedMilliseconds
                    + " ms)");
            }
        }

        /// <summary>
        /// The thinning must start strictly ABOVE the crest size, or every emblem
        /// the game has ever been served changes - which is what the golden hashes
        /// in EmblemArtworkTests pin, and this says out loud.
        /// </summary>
        [Fact]
        public void The_crest_the_game_downloads_is_sampled_exactly_as_it_always_was()
        {
            Assert.Equal(4, EmblemStackPainter.SupersampleFor(EmblemPainter.Size));
            Assert.Equal(4, EmblemStackPainter.SupersampleFor(EmblemPainter.Size - 1));
            Assert.Equal(2, EmblemStackPainter.SupersampleFor(EmblemPainter.Size + 1));

            Assert.Equal(5, EmblemPainter.SupersampleFor(EmblemPainter.Size));
            Assert.Equal(2, EmblemPainter.SupersampleFor(EmblemPainter.Size + 1));
        }

        // --------------------------------------------------------------- the svg

        [Fact]
        public void The_vector_carries_one_group_per_layer_with_the_same_transform()
        {
            EmblemLayer first = Layer(Triangle, x: -100, y: 50, size: 620, rotation: 33, colour: 3);
            EmblemLayer second = Layer(Disc, size: 900, colour: 11, opacity: 20, flipX: true);

            string svg = EmblemStackSvg.Compose(Stack(first, second));

            Assert.Contains("<g transform=\"" + first.Transform() + "\">", svg, StringComparison.Ordinal);
            Assert.Contains("<g transform=\"" + second.Transform() + "\">", svg, StringComparison.Ordinal);
            Assert.Contains("fill-opacity=\"0.500\"", svg, StringComparison.Ordinal);

            // Bottom of the stack first, so the vector composites the way the
            // rasteriser does.
            Assert.True(
                svg.IndexOf(first.Transform(), StringComparison.Ordinal)
                < svg.IndexOf(second.Transform(), StringComparison.Ordinal));
        }

        /// <summary>
        /// NO PLAYER TEXT, EVER. An SVG is script-capable and served from the
        /// account page's origin, so an alliance name inside one would be stored
        /// XSS with extra steps. Every byte comes from the closed vocabulary.
        /// </summary>
        [Fact]
        public void The_vector_contains_nothing_but_the_vocabulary()
        {
            string svg = EmblemStackSvg.Compose(Stack(Layer(Triangle), Layer(Disc)));

            Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<title", svg, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<text", svg, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("href", svg, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_vector_and_the_raster_come_from_the_same_path_data()
        {
            EmblemLayer layer = Layer(Triangle, size: 700, rotation: 21);
            string svg = EmblemStackSvg.Compose(Stack(layer));

            Assert.Contains(EmblemObjects.All[Triangle].Path.ToPathData(EmblemObjects.Unit),
                svg, StringComparison.Ordinal);
        }
    }
}

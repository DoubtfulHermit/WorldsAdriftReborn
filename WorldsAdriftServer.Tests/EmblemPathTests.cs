using WorldsAdriftServer.Emblems;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The path filler, which is now the only thing that knows what "inside a
    /// shape" means anywhere in the emblem feature.
    ///
    /// Two properties carry the whole file. The fill rule has to be NON-ZERO,
    /// because several devices are unions of overlapping pieces and even-odd would
    /// punch holes through them; and the bucketed edge index has to give the same
    /// answer a brute-force scan would, because it is a performance structure and
    /// a wrong one would show up as a device with a torn band across it rather
    /// than as an error.
    /// </summary>
    public class EmblemPathTests
    {
        private static double[] Square(double half, bool clockwise)
        {
            double[] points = { -half, -half, half, -half, half, half, -half, half };
            if (clockwise) return points;

            return new[] { -half, half, half, half, half, -half, -half, -half };
        }

        [Fact]
        public void A_square_contains_its_inside_and_not_its_outside()
        {
            EmblemPath path = EmblemPath.FromContours(Square(0.5, true));

            Assert.True(path.Contains(0.0, 0.0));
            Assert.True(path.Contains(0.49, -0.49));

            Assert.False(path.Contains(0.51, 0.0));
            Assert.False(path.Contains(0.0, -0.51));
            Assert.False(path.Contains(2.0, 2.0));
        }

        [Fact]
        public void An_opposite_wound_inner_contour_is_a_hole()
        {
            EmblemPath ring = EmblemPath.FromContours(Square(0.9, true), Square(0.4, false));

            Assert.True(ring.Contains(0.6, 0.0));
            Assert.False(ring.Contains(0.0, 0.0));
        }

        [Fact]
        public void Two_overlapping_contours_of_the_same_winding_union_rather_than_cancel()
        {
            // This is the whole reason the rule is non-zero. Under even-odd the
            // overlap below would be a hole, and the cross - which is exactly this,
            // two bars - would come out with its middle missing.
            double[] wide = { -0.9, -0.2, 0.9, -0.2, 0.9, 0.2, -0.9, 0.2 };
            double[] tall = { -0.2, -0.9, 0.2, -0.9, 0.2, 0.9, -0.2, 0.9 };

            EmblemPath cross = EmblemPath.FromContours(wide, tall);

            Assert.True(cross.Contains(0.0, 0.0));
            Assert.True(cross.Contains(0.7, 0.0));
            Assert.True(cross.Contains(0.0, 0.7));
            Assert.False(cross.Contains(0.7, 0.7));
        }

        [Fact]
        public void The_bucketed_index_agrees_with_a_brute_force_scan()
        {
            // The index exists only to make Contains cheap. If it ever disagrees
            // with the naive answer, a device grows a torn band at some scanline
            // and nothing throws.
            EmblemPath path = EmblemPath.Parse(EmblemDeviceGeometry.Paths[10], EmblemDeviceGeometry.Unit);
            double[][] contours = Contours(EmblemDeviceGeometry.Paths[10]);

            for (int i = 0; i < 200; i++)
            for (int j = 0; j < 200; j++)
            {
                double x = -1.0 + 2.0 * i / 199.0;
                double y = -1.0 + 2.0 * j / 199.0;

                Assert.Equal(BruteForce(contours, x, y), path.Contains(x, y));
            }
        }

        [Fact]
        public void Every_traced_device_parses_and_fits_the_box_it_is_scaled_into()
        {
            Assert.Equal(50, EmblemDeviceGeometry.Paths.Count);

            for (int i = 0; i < EmblemDeviceGeometry.Paths.Count; i++)
            {
                EmblemPath path = EmblemPath.Parse(EmblemDeviceGeometry.Paths[i], EmblemDeviceGeometry.Unit);

                // Inside the unit box, because the painter multiplies these by a
                // per-shape scale chosen on the assumption that they are.
                Assert.InRange(path.Reach, 0.5, 1.0);

                // Filled on its longer axis: the tracer normalises each icon to the
                // box, so a device that came out much smaller than the box lost
                // most of its artwork somewhere.
                Assert.True(
                    Math.Max(path.MaxX - path.MinX, path.MaxY - path.MinY) > 1.9,
                    EmblemDeviceGeometry.Names[i] + " does not fill its box - the trace lost something.");
            }
        }

        [Fact]
        public void Every_traced_device_covers_a_sane_share_of_its_box()
        {
            // A trace that inverted, or whose contours were wound wrongly, comes
            // out either almost empty or almost solid. Real tribal artwork is
            // neither - it is line work with a lot of negative space.
            for (int i = 0; i < EmblemDeviceGeometry.Paths.Count; i++)
            {
                EmblemPath path = EmblemPath.Parse(EmblemDeviceGeometry.Paths[i], EmblemDeviceGeometry.Unit);

                int inside = 0;
                for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    if (path.Contains(-1.0 + 2.0 * (x + 0.5) / 128, -1.0 + 2.0 * (y + 0.5) / 128)) inside++;
                }

                double coverage = inside / (double)(128 * 128);
                Assert.InRange(coverage, 0.08, 0.75);
            }
        }

        [Fact]
        public void The_path_data_written_out_is_the_data_that_was_read_in()
        {
            // The SVG a player downloads must be the same numbers the server
            // rasterises, not a second rounding of them.
            string stored = EmblemDeviceGeometry.Paths[0];
            EmblemPath path = EmblemPath.Parse(stored, EmblemDeviceGeometry.Unit);

            string written = path.ToPathData(EmblemDeviceGeometry.Unit);
            string rebuilt = string.Join("|", written.Split('Z', StringSplitOptions.RemoveEmptyEntries)
                .Select(contour => contour.Replace("M", string.Empty).Replace("L", " ").Trim()));

            Assert.Equal(Normalise(stored), Normalise(rebuilt));
        }

        [Fact]
        public void A_path_with_nothing_in_it_is_refused()
        {
            Assert.Throws<ArgumentNullException>(() => EmblemPath.FromContours(null!));
            Assert.Throws<ArgumentException>(() => EmblemPath.FromContours());
            Assert.Throws<ArgumentException>(() => EmblemPath.FromContours(new[] { 0.0, 0.0 }));
            Assert.Throws<ArgumentException>(() => EmblemPath.Parse(string.Empty, 1000.0));

            // Three collinear horizontal points enclose nothing and leave the
            // filler with no edge that could ever cross a scanline.
            Assert.Throws<ArgumentException>(
                () => EmblemPath.FromContours(new[] { -1.0, 0.0, 0.0, 0.0, 1.0, 0.0 }));
        }

        private static string Normalise(string data) =>
            string.Join(" ", data.Split(new[] { ' ', '|' }, StringSplitOptions.RemoveEmptyEntries));

        private static double[][] Contours(string data) =>
            data.Split('|')
                .Select(part => part.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => int.Parse(n) / EmblemDeviceGeometry.Unit).ToArray())
                .ToArray();

        private static bool BruteForce(double[][] contours, double x, double y)
        {
            int winding = 0;

            foreach (double[] contour in contours)
            {
                int points = contour.Length / 2;

                for (int i = 0; i < points; i++)
                {
                    int j = (i + 1) % points;
                    double x0 = contour[i * 2], y0 = contour[i * 2 + 1];
                    double x1 = contour[j * 2], y1 = contour[j * 2 + 1];

                    double side = (x1 - x0) * (y - y0) - (x - x0) * (y1 - y0);

                    if (y0 <= y)
                    {
                        if (y1 > y && side > 0) winding++;
                    }
                    else if (y1 <= y && side < 0)
                    {
                        winding--;
                    }
                }
            }

            return winding != 0;
        }
    }
}

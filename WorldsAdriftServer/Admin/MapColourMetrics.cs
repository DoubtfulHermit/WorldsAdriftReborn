using System.Globalization;

namespace WorldsAdriftServer.Admin
{
    /// <summary>How a colour is seen. Used to measure a palette, never to render one.</summary>
    internal enum ColourVision
    {
        /// <summary>Trichromatic vision.</summary>
        Normal,

        /// <summary>Missing L cones. Roughly 1 man in 75.</summary>
        Protanopia,

        /// <summary>Missing M cones. Roughly 1 man in 16.</summary>
        Deuteranopia,

        /// <summary>Missing S cones. Rare, and not sex-linked.</summary>
        Tritanopia,

        /// <summary>Lightness only - a monochrome print, or a failing display.</summary>
        Greyscale,
    }

    /// <summary>
    /// The colour maths the map palette is judged by: CIELAB, CIEDE2000 and the
    /// Machado/Oliveira/Fernandes 2009 colour-vision-deficiency simulation.
    ///
    /// This module deliberately knows nothing about tiers or CSS. It exists so the
    /// palette's separation claims are *measured in a test* rather than asserted in
    /// a comment, and so a future palette change is forced to re-measure rather
    /// than re-assert. Nothing renders through it.
    /// </summary>
    internal static class MapColourMetrics
    {
        /// <summary>
        /// Machado 2009 severity-1.0 linear-RGB matrices. Row-major 3x3.
        /// </summary>
        private static readonly double[] Protanopia =
        {
            0.152286, 1.052583, -0.204868,
            0.114503, 0.786281, 0.099216,
            -0.003882, -0.048116, 1.051998,
        };

        private static readonly double[] Deuteranopia =
        {
            0.367322, 0.860646, -0.227968,
            0.280085, 0.672501, 0.047413,
            -0.011820, 0.042940, 0.968881,
        };

        private static readonly double[] Tritanopia =
        {
            1.255528, -0.076749, -0.178779,
            -0.078411, 0.930809, 0.147602,
            0.004733, 0.691367, 0.303900,
        };

        /// <summary>Every vision model a palette is measured against.</summary>
        internal static IReadOnlyList<ColourVision> AllVisions { get; } = new[]
        {
            ColourVision.Normal, ColourVision.Protanopia, ColourVision.Deuteranopia,
            ColourVision.Tritanopia, ColourVision.Greyscale,
        };

        /// <summary>The "#rrggbb" a given vision sees when shown <paramref name="hex"/>.</summary>
        internal static string Simulate(string hex, ColourVision vision)
        {
            (double r, double g, double b) = ParseSrgb(hex);
            if (vision == ColourVision.Normal)
                return Format(r, g, b);
            if (vision == ColourVision.Greyscale)
            {
                double y = MapTierPalette.RelativeLuminance(hex);
                double grey = Delinearize(y);
                return Format(grey, grey, grey);
            }

            double[] m = vision switch
            {
                ColourVision.Protanopia => Protanopia,
                ColourVision.Deuteranopia => Deuteranopia,
                ColourVision.Tritanopia => Tritanopia,
                _ => throw new ArgumentOutOfRangeException(nameof(vision), vision, null),
            };
            double lr = Linearize(r), lg = Linearize(g), lb = Linearize(b);
            return Format(
                Delinearize(m[0] * lr + m[1] * lg + m[2] * lb),
                Delinearize(m[3] * lr + m[4] * lg + m[5] * lb),
                Delinearize(m[6] * lr + m[7] * lg + m[8] * lb));
        }

        /// <summary>
        /// CIEDE2000 between two "#rrggbb" colours, as seen by <paramref name="vision"/>.
        /// Roughly: 1 is a just-noticeable difference, 2-3 is a difference a careful
        /// eye finds, and 10+ is "obviously a different colour" at a glance.
        /// </summary>
        internal static double Difference(string first, string second, ColourVision vision)
            => Ciede2000(Lab(Simulate(first, vision)), Lab(Simulate(second, vision)));

        /// <summary>
        /// The smallest CIEDE2000 between any two of <paramref name="colours"/> under
        /// <paramref name="vision"/> - i.e. how close the palette comes to collapsing.
        /// </summary>
        internal static double ClosestPair(IReadOnlyList<string> colours, ColourVision vision)
        {
            if (colours is null) throw new ArgumentNullException(nameof(colours));
            if (colours.Count < 2)
                throw new ArgumentException("Need at least two colours to compare.", nameof(colours));
            double worst = double.MaxValue;
            for (int i = 0; i < colours.Count; i++)
                for (int j = i + 1; j < colours.Count; j++)
                    worst = Math.Min(worst, Difference(colours[i], colours[j], vision));
            return worst;
        }

        /// <summary>CIELAB (D65, 2 degree observer) of an "#rrggbb" colour.</summary>
        internal static (double L, double A, double B) Lab(string hex)
        {
            (double r, double g, double b) = ParseSrgb(hex);
            double lr = Linearize(r), lg = Linearize(g), lb = Linearize(b);
            double x = (0.4124564 * lr + 0.3575761 * lg + 0.1804375 * lb) / 0.95047;
            double y = 0.2126729 * lr + 0.7151522 * lg + 0.0721750 * lb;
            double z = (0.0193339 * lr + 0.1191920 * lg + 0.9503041 * lb) / 1.08883;
            double fx = F(x), fy = F(y), fz = F(z);
            return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
        }

        private static double F(double t)
        {
            const double D = 6.0 / 29.0;
            return t > D * D * D ? Math.Cbrt(t) : t / (3 * D * D) + 4.0 / 29.0;
        }

        private static double Ciede2000((double L, double A, double B) one,
                                        (double L, double A, double B) two)
        {
            double c1 = Math.Sqrt(one.A * one.A + one.B * one.B);
            double c2 = Math.Sqrt(two.A * two.A + two.B * two.B);
            double cBar = (c1 + c2) / 2;
            double c7 = Math.Pow(cBar, 7);
            double g = 0.5 * (1 - Math.Sqrt(c7 / (c7 + Math.Pow(25, 7))));
            double a1 = (1 + g) * one.A, a2 = (1 + g) * two.A;
            double c1p = Math.Sqrt(a1 * a1 + one.B * one.B);
            double c2p = Math.Sqrt(a2 * a2 + two.B * two.B);
            double h1 = Angle(one.B, a1), h2 = Angle(two.B, a2);

            double dL = two.L - one.L;
            double dC = c2p - c1p;
            double dh;
            if (c1p * c2p == 0) dh = 0;
            else if (Math.Abs(h2 - h1) <= 180) dh = h2 - h1;
            else if (h2 - h1 > 180) dh = h2 - h1 - 360;
            else dh = h2 - h1 + 360;
            double dH = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(Radians(dh) / 2);

            double lBar = (one.L + two.L) / 2;
            double cBarP = (c1p + c2p) / 2;
            double hBar;
            if (c1p * c2p == 0) hBar = h1 + h2;
            else if (Math.Abs(h1 - h2) <= 180) hBar = (h1 + h2) / 2;
            else if (h1 + h2 < 360) hBar = (h1 + h2 + 360) / 2;
            else hBar = (h1 + h2 - 360) / 2;

            double t = 1
                - 0.17 * Math.Cos(Radians(hBar - 30))
                + 0.24 * Math.Cos(Radians(2 * hBar))
                + 0.32 * Math.Cos(Radians(3 * hBar + 6))
                - 0.20 * Math.Cos(Radians(4 * hBar - 63));
            double dTheta = 30 * Math.Exp(-Math.Pow((hBar - 275) / 25, 2));
            double cBarP7 = Math.Pow(cBarP, 7);
            double rc = 2 * Math.Sqrt(cBarP7 / (cBarP7 + Math.Pow(25, 7)));
            double sL = 1 + 0.015 * Math.Pow(lBar - 50, 2) / Math.Sqrt(20 + Math.Pow(lBar - 50, 2));
            double sC = 1 + 0.045 * cBarP;
            double sH = 1 + 0.015 * cBarP * t;
            double rt = -Math.Sin(Radians(2 * dTheta)) * rc;

            return Math.Sqrt(
                Math.Pow(dL / sL, 2) + Math.Pow(dC / sC, 2) + Math.Pow(dH / sH, 2)
                + rt * (dC / sC) * (dH / sH));
        }

        private static double Angle(double b, double a)
        {
            if (a == 0 && b == 0) return 0;
            double deg = Math.Atan2(b, a) * 180 / Math.PI;
            return deg < 0 ? deg + 360 : deg;
        }

        private static double Radians(double degrees) => degrees * Math.PI / 180;

        private static double Linearize(double channel)
            => channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

        private static double Delinearize(double channel)
            => channel <= 0.0031308 ? channel * 12.92 : 1.055 * Math.Pow(channel, 1 / 2.4) - 0.055;

        private static string Format(double r, double g, double b)
            => "#" + Byte(r) + Byte(g) + Byte(b);

        private static string Byte(double channel)
            => ((int)Math.Round(Math.Clamp(channel, 0, 1) * 255))
                .ToString("x2", CultureInfo.InvariantCulture);

        private static (double R, double G, double B) ParseSrgb(string hex)
        {
            if (hex is null) throw new ArgumentNullException(nameof(hex));
            string digits = hex.StartsWith('#') ? hex[1..] : hex;
            if (digits.Length != 6)
                throw new FormatException($"Expected an #rrggbb colour, got '{hex}'.");
            return (Channel(digits, 0), Channel(digits, 2), Channel(digits, 4));
        }

        private static double Channel(string digits, int offset)
            => int.Parse(digits.AsSpan(offset, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture) / 255.0;
    }
}

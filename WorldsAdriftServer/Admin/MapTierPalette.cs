using System.Globalization;
using System.Text;

namespace WorldsAdriftServer.Admin
{
    /// <summary>
    /// One tier's rendered colours.
    ///
    /// <paramref name="Hue"/> is the authored colour written into the stylesheet;
    /// <paramref name="Fill"/> is what the eye actually receives once that hue has
    /// been composited over the ocean at <see cref="MapTierPalette.FillOpacity"/>.
    /// Everything a reader is shown, and everything this palette is measured by,
    /// uses <paramref name="Fill"/> - never the authored hue. That distinction is
    /// the whole reason the legend and the map cannot disagree again.
    /// </summary>
    internal readonly record struct MapTierColours(
        int Tier, string Hue, string Fill, string Ink, string Halo);

    /// <summary>
    /// The admin world map's tier colours, the transparency they are drawn at, and
    /// the WCAG maths that picks a readable label colour for the result.
    ///
    /// Tier is CATEGORICAL here, not a ramp. Each tier keeps its own hue - green
    /// Wilderness, blue Expanse, violet Remnants, gold Badlands - because that is
    /// the identity operators already read the map by, and because every cell
    /// prints its own "T&lt;n&gt; - Name" so colour is never the only channel. A
    /// single-axis sequential ramp was tried and rejected: it measured beautifully
    /// and looked like a heatmap of nothing.
    ///
    /// The four authored hues ("Nightfall"), and the colour each becomes once drawn
    /// at <see cref="FillOpacity"/> over <see cref="Ocean"/>:
    ///
    ///   T1 Wilderness  #4b934f -> #3b7543  green   OKLCh L .600  h 145
    ///   T2 Expanse     #204c8a -> #1a3f70  navy    OKLCh L .420  h 258
    ///   T3 Remnants    #bc9be2 -> #917bb3  lilac   OKLCh L .745  h 305
    ///   T4 Badlands    #eed059 -> #b7a34b  gold    OKLCh L .860  h  95
    ///
    /// TRANSPARENCY. The cells are zones over a world, and are drawn as such: the
    /// stylesheet emits the authored hue with <c>fill-opacity</c>, and the legend
    /// swatch is emitted as the COMPOSITED hex, computed here by
    /// <see cref="Composite"/>. The previous transparent palette shipped the raw
    /// hex in the legend and a 38%-composited colour on the map, so the key was a
    /// lie about the picture; that cannot recur while both come off this one list,
    /// and a unit test re-derives the composite rather than trusting it.
    ///
    /// Nothing but the ocean is ever drawn under a tier cell - the cells are the
    /// first layer inside the world clip, and the Haven corridor is clipped away
    /// from them - so one composite is the whole story, not an approximation of it.
    ///
    /// WHY 0.76 AND NOT SOME OTHER NUMBER. The console has exactly two label inks,
    /// <see cref="LightInk"/> and <see cref="DarkInk"/>. Their crossover sits at
    /// relative luminance .1780, and the best contrast obtainable there with either
    /// ink is 4.10:1 - under AA. So there is a forbidden band, Y .1582 to .2005, in
    /// which a fill CANNOT carry a legible label whichever ink is chosen. As alpha
    /// falls, every tier's luminance sweeps down through that band in turn: T1
    /// green is inside it from .82 to .92, T3 lilac from .62 to .68, T4 gold below
    /// .56. Alpha is therefore not a free dial. The intervals where no tier is in
    /// the band are (.94, 1.0], [.70, .80] and [.56, .60]; .76 is the middle of the
    /// widest of them, which is also the largest visible amount of transparency
    /// that keeps every tier clear of the band with room to spare.
    ///
    /// Measured on the COMPOSITED fills (CIEDE2000 between the two closest tiers,
    /// Machado 2009 at severity 1.0): normal 29.1, protanopia 20.8, deuteranopia
    /// 25.4, tritanopia 16.0. Greyscale is the one axis traded away - T3 and T4 sit
    /// 9.9 apart there - which is deliberate, and covered by the tier text printed
    /// on every cell. The old Sheets palette collapsed to dE00 2.1 under protanopia.
    ///
    /// The label colour is *computed*, never hand-picked: whichever of the light
    /// and dark inks has the greater contrast ratio against the COMPOSITED fill
    /// wins, and <see cref="MinimumInkContrast"/> is enforced by the unit tests.
    /// Compositing moved T1 from dark ink to light ink - at full strength the green
    /// took dark ink at 5.02:1, at .76 it takes light ink at 4.92:1 - which is
    /// exactly why the ink is re-derived from the composite and not from the hue.
    /// </summary>
    internal static class MapTierPalette
    {
        internal const int MinTier = 1;
        internal const int MaxTier = 4;

        /// <summary>Light label ink, matching the console's --text.</summary>
        internal const string LightInk = "#edf3f5";

        /// <summary>Dark label ink, matching the console's darkest surface.</summary>
        internal const string DarkInk = "#0a1219";

        /// <summary>
        /// The map's ocean. Tier cells are the first layer drawn inside the world
        /// clip, so this is the only thing ever behind them - which is what makes a
        /// single composited colour per tier exact rather than representative. The
        /// <c>.map-ocean</c> rule is emitted from here too, so the backdrop the
        /// composite assumes and the backdrop the browser paints are one value.
        /// </summary>
        internal const string Ocean = "#09151d";

        /// <summary>
        /// How much of the authored hue reaches the screen. See the type comment
        /// for why this is quantised rather than free.
        /// </summary>
        internal const double FillOpacity = 0.76;

        /// <summary>WCAG AA for normal-size text. Map labels are small at full-world zoom.</summary>
        internal const double MinimumInkContrast = 4.5;

        /// <summary>
        /// The floor for the closest pair of tier fills under normal vision and
        /// under each simulated colour deficiency. 10 is "comfortably a different
        /// colour at a glance"; the palette is not asked to go further, because
        /// chasing a bigger number is exactly what produced an ugly map last time,
        /// and the tier text on every cell already carries the value losslessly.
        /// Greyscale is excluded on purpose - see the type comment.
        /// </summary>
        internal const double MinimumTierDifference = 10.0;

        // Ordered low tier -> high tier. Index 0 is tier 1. These are the AUTHORED
        // hues; what lands on screen is Composite(hue, Ocean, FillOpacity).
        private static readonly string[] TierHues =
        {
            "#4b934f", // T1 Wilderness  green   -> #3b7543
            "#204c8a", // T2 Expanse     navy    -> #1a3f70
            "#bc9be2", // T3 Remnants    lilac   -> #917bb3
            "#eed059", // T4 Badlands    gold    -> #b7a34b
        };

        internal static IReadOnlyList<MapTierColours> All { get; } = BuildAll();

        private static IReadOnlyList<MapTierColours> BuildAll()
        {
            MapTierColours[] built = new MapTierColours[TierHues.Length];
            for (int i = 0; i < TierHues.Length; i++)
            {
                string hue = TierHues[i];
                string fill = Composite(hue, Ocean, FillOpacity);
                string ink = InkFor(fill);
                string halo = ink == LightInk ? DarkInk : LightInk;
                built[i] = new MapTierColours(MinTier + i, hue, fill, ink, halo);
            }
            return built;
        }

        internal static MapTierColours For(int tier)
        {
            if (tier < MinTier || tier > MaxTier)
                throw new ArgumentOutOfRangeException(nameof(tier), tier,
                    $"Tier must be between {MinTier} and {MaxTier}.");
            return All[tier - MinTier];
        }

        /// <summary>
        /// Source-over compositing of <paramref name="foreground"/> at
        /// <paramref name="alpha"/> onto <paramref name="background"/>, in the same
        /// non-linear sRGB space a browser blends <c>fill-opacity</c> in. This is
        /// the function that makes the legend swatch a statement about the map
        /// rather than about the stylesheet.
        /// </summary>
        internal static string Composite(string foreground, string background, double alpha)
        {
            if (alpha is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(alpha), alpha,
                    "Alpha must be between 0 and 1.");
            (double fr, double fg, double fb) = ParseSrgb(foreground);
            (double br, double bg, double bb) = ParseSrgb(background);
            return "#" + Byte(fr * alpha + br * (1 - alpha))
                       + Byte(fg * alpha + bg * (1 - alpha))
                       + Byte(fb * alpha + bb * (1 - alpha));
        }

        /// <summary>
        /// The label ink for a fill: whichever of the two inks is further from it.
        /// This is the whole "light text on dark swatches, dark text on light
        /// swatches" decision, computed rather than guessed - and computed against
        /// the composited fill, because that is what the label sits on.
        /// </summary>
        internal static string InkFor(string fill)
            => ContrastRatio(LightInk, fill) >= ContrastRatio(DarkInk, fill) ? LightInk : DarkInk;

        /// <summary>
        /// The luminance interval in which NEITHER ink reaches
        /// <see cref="MinimumInkContrast"/>. Any fill landing inside it is
        /// unlabelable, whatever ink is chosen; the shipped alpha is picked to keep
        /// every tier out of it, and a test enforces that.
        /// </summary>
        internal static (double Low, double High) UnlabelableLuminanceBand()
        {
            double light = RelativeLuminance(LightInk);
            double dark = RelativeLuminance(DarkInk);
            return ((light + 0.05) / MinimumInkContrast - 0.05,
                    MinimumInkContrast * (dark + 0.05) - 0.05);
        }

        /// <summary>WCAG 2.x relative luminance of an "#rrggbb" colour.</summary>
        internal static double RelativeLuminance(string hex)
        {
            (double r, double g, double b) = ParseSrgb(hex);
            return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
        }

        /// <summary>WCAG 2.x contrast ratio between two "#rrggbb" colours, 1.0 .. 21.0.</summary>
        internal static double ContrastRatio(string first, string second)
        {
            double a = RelativeLuminance(first);
            double b = RelativeLuminance(second);
            double lighter = Math.Max(a, b);
            double darker = Math.Min(a, b);
            return (lighter + 0.05) / (darker + 0.05);
        }

        /// <summary>The <c>fill-opacity</c> value, exactly as it reaches the stylesheet.</summary>
        internal static string FillOpacityCss
            => FillOpacity.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>
        /// The CSS for every tier-coloured surface, so the map cell, the cell label
        /// and the legend swatch can never drift apart: they are all emitted from
        /// the same <see cref="All"/> list.
        ///
        /// The cell gets the authored hue plus <c>fill-opacity</c>; the legend
        /// swatch gets the composite of exactly those two things over exactly the
        /// ocean this file also emits. The legend is therefore the picture, not a
        /// description of the picture.
        /// </summary>
        internal static string Css()
        {
            StringBuilder css = new();
            css.Append(".map-ocean{fill:").Append(Ocean).Append('}');
            foreach (MapTierColours tier in All)
            {
                css.Append(".map-biome.type-").Append(tier.Tier)
                   .Append("{fill:").Append(tier.Hue)
                   .Append(";fill-opacity:").Append(FillOpacityCss).Append('}');
                css.Append(".map-biome.type-").Append(tier.Tier)
                   .Append(".unassigned{stroke:").Append(tier.Ink).Append('}');
                css.Append(".map-cell-label.type-").Append(tier.Tier)
                   .Append("{fill:").Append(tier.Ink)
                   .Append(";stroke:").Append(tier.Halo).Append('}');
                css.Append(".map-cell-label.type-").Append(tier.Tier)
                   .Append(" .tier{fill:").Append(tier.Ink).Append('}');
                css.Append(".map-cell-label.type-").Append(tier.Tier)
                   .Append(" .stock{fill:").Append(tier.Ink).Append('}');
                css.Append(".map-swatch.tier-").Append(tier.Tier)
                   .Append("{background:").Append(tier.Fill).Append('}');
                // The ledger's tier chip is a fourth tier-coloured surface, and it
                // carries text, so it takes the composited fill AND the ink that
                // was computed for it - never a fifth hand-picked pairing.
                css.Append(".tierchip.tier-").Append(tier.Tier)
                   .Append("{background:").Append(tier.Fill)
                   .Append(";color:").Append(tier.Ink).Append('}');
            }
            return css.ToString();
        }

        private static double Linearize(double channel)
            => channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

        private static string Byte(double channel)
            => ((int)Math.Round(Math.Clamp(channel, 0, 1) * 255, MidpointRounding.ToEven))
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

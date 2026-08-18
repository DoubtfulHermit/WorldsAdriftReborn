using System.Globalization;
using System.Text;

namespace WorldsAdriftServer.Admin
{
    /// <summary>One tier's rendered colours: the cell fill and the label drawn on it.</summary>
    internal readonly record struct MapTierColours(int Tier, string Fill, string Ink, string Halo);

    /// <summary>
    /// The admin world map's tier colours, and the WCAG maths that picks a
    /// readable label colour for each of them.
    ///
    /// Tier is CATEGORICAL here, not a ramp. Each tier keeps its own hue - green
    /// Wilderness, blue Expanse, violet Remnants, gold Badlands - because that is
    /// the identity operators already read the map by, and because every cell
    /// prints its own "T&lt;n&gt; - Name" so colour is never the only channel. A
    /// single-axis sequential ramp was tried and rejected: it measured beautifully
    /// and looked like a heatmap of nothing.
    ///
    /// What *is* computed rather than picked is where each hue sits in lightness.
    /// Under protanopia and deuteranopia the four hues collapse into two families:
    /// green and gold both land on the yellow side, blue and violet both on the
    /// blue side. Two colours in the same family can then only be told apart by
    /// lightness. Gold has to be the light member of its family (a dark yellow is
    /// an olive), which forces green dark; violet is placed dark and blue light for
    /// the same reason. That is the whole derivation of these four values:
    ///
    ///   T1 Wilderness  #134e26  OKLCh L .376  h 150  deep forest green
    ///   T2 Expanse     #4f89c1  OKLCh L .615  h 249  mid slate blue
    ///   T3 Remnants    #694189  OKLCh L .454  h 308  deep violet
    ///   T4 Badlands    #cdb236  OKLCh L .766  h  96  gold
    ///
    /// Chroma is held in a narrow band (.090 - .140, rising only where the hue
    /// needs it to stay saturated at that lightness), which is what makes four
    /// unrelated hues read as one designed set instead of four defaults.
    ///
    /// Measured on the shipped values (CIEDE2000 between the two closest tiers,
    /// Machado 2009 simulation at severity 1.0): normal 30.7, protanopia 22.6,
    /// deuteranopia 17.4, tritanopia 26.6. The previous categorical palette
    /// collapsed to 2.1 under protanopia; the rejected ramp reached 18.2 but at
    /// the cost of the map's looks. Greyscale is the one axis this trades away -
    /// T1 and T3 sit 5.1 apart there - which is deliberate, and covered by the
    /// tier text printed on every cell.
    ///
    /// The label colour is *computed*, never hand-picked: whichever of the light
    /// and dark inks has the greater contrast ratio against the fill wins, and
    /// <see cref="MinimumInkContrast"/> is enforced by the unit tests. Here that
    /// puts light ink on T1/T3 and dark ink on T2/T4.
    /// </summary>
    internal static class MapTierPalette
    {
        internal const int MinTier = 1;
        internal const int MaxTier = 4;

        /// <summary>Light label ink, matching the console's --text.</summary>
        internal const string LightInk = "#edf3f5";

        /// <summary>Dark label ink, matching the console's darkest surface.</summary>
        internal const string DarkInk = "#0a1219";

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

        // Ordered low tier -> high tier. Index 0 is tier 1.
        private static readonly string[] TierFills =
        {
            "#134e26", // T1 Wilderness  green   L* 28.7
            "#4f89c1", // T2 Expanse     blue    L* 55.5
            "#694189", // T3 Remnants    violet  L* 35.2
            "#cdb236", // T4 Badlands    gold    L* 73.0
        };

        internal static IReadOnlyList<MapTierColours> All { get; } = BuildAll();

        private static IReadOnlyList<MapTierColours> BuildAll()
        {
            MapTierColours[] built = new MapTierColours[TierFills.Length];
            for (int i = 0; i < TierFills.Length; i++)
            {
                string fill = TierFills[i];
                string ink = InkFor(fill);
                string halo = ink == LightInk ? DarkInk : LightInk;
                built[i] = new MapTierColours(MinTier + i, fill, ink, halo);
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
        /// The label ink for a fill: whichever of the two inks is further from it.
        /// This is the whole "light text on dark swatches, dark text on light
        /// swatches" decision, computed rather than guessed.
        /// </summary>
        internal static string InkFor(string fill)
            => ContrastRatio(LightInk, fill) >= ContrastRatio(DarkInk, fill) ? LightInk : DarkInk;

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

        /// <summary>
        /// The CSS for every tier-coloured surface, so the map cell, the cell label
        /// and the legend swatch can never drift apart: they are all emitted from
        /// the same <see cref="All"/> list.
        /// </summary>
        internal static string Css()
        {
            StringBuilder css = new();
            foreach (MapTierColours tier in All)
            {
                css.Append(".map-biome.type-").Append(tier.Tier)
                   .Append("{fill:").Append(tier.Fill).Append('}');
                css.Append(".map-biome.type-").Append(tier.Tier)
                   .Append(".unassigned{stroke:").Append(tier.Ink).Append('}');
                css.Append(".map-cell-label.type-").Append(tier.Tier)
                   .Append("{fill:").Append(tier.Ink)
                   .Append(";stroke:").Append(tier.Halo).Append('}');
                css.Append(".map-cell-label.type-").Append(tier.Tier)
                   .Append(" .tier{fill:").Append(tier.Ink).Append('}');
                css.Append(".map-swatch.tier-").Append(tier.Tier)
                   .Append("{background:").Append(tier.Fill).Append('}');
            }
            return css.ToString();
        }

        private static double Linearize(double channel)
            => channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

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

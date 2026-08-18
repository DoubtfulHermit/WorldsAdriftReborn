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
    /// Tier is an *ordered* quantity (1..4), so the fills are a sequential
    /// perceptually-uniform ramp, not an arbitrary categorical palette: they are
    /// cividis sampled at t = 0.06 / 0.34 / 0.62 / 0.90 and then mixed 85/15 with
    /// the map's ocean so the console keeps its dark character. Cividis is chosen
    /// because it varies along the blue-yellow axis, which is the axis red-green
    /// colour deficiency preserves, and because its lightness is monotone - so the
    /// ramp still reads in order under protanopia, deuteranopia, tritanopia and in
    /// plain greyscale.
    ///
    /// Measured on the shipped values (CIEDE2000 between the two closest tiers):
    /// normal 18.2, protanopia 18.2, deuteranopia 20.0, tritanopia 16.8.
    /// Relative luminance is strictly increasing (0.024, 0.086, 0.214, 0.445).
    ///
    /// The label colour is *computed*, never hand-picked: whichever of the light
    /// and dark inks has the greater contrast ratio against the fill wins, and
    /// <see cref="MinimumInkContrast"/> is enforced by the unit tests. The crossover
    /// sits at relative luminance ~0.178, so tiers 1-2 take light ink and 3-4 take
    /// dark ink.
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
        /// Minimum WCAG contrast between adjacent tiers. Above 1 it guarantees the
        /// ramp is separable by lightness alone, i.e. in greyscale and for any
        /// colour deficiency at all.
        /// </summary>
        internal const double MinimumAdjacentContrast = 1.7;

        // Ordered low tier -> high tier. Index 0 is tier 1.
        private static readonly string[] TierFills =
        {
            "#01295d", // T1 Wilderness  L* 17.4
            "#4d5361", // T2 Expanse     L* 35.3
            "#848069", // T3 Remnants    L* 53.3
            "#c4b34a", // T4 Badlands    L* 72.5
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

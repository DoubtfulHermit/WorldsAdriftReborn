using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The admin map colours tier by an ordered quantity, so these tests pin the
    /// three properties that make an ordinal encoding legible: the ramp is
    /// monotone in lightness, adjacent steps stay apart, and the label ink on each
    /// swatch clears WCAG AA. They are the guard against someone dropping a
    /// pretty-but-categorical palette back in.
    /// </summary>
    public class MapTierPaletteTests
    {
        [Fact]
        public void Relative_luminance_matches_the_wcag_reference_points()
        {
            Assert.Equal(1.0, MapTierPalette.RelativeLuminance("#ffffff"), 6);
            Assert.Equal(0.0, MapTierPalette.RelativeLuminance("#000000"), 6);
            // WCAG's worked example: mid grey #808080 sits at 0.2159.
            Assert.Equal(0.2159, MapTierPalette.RelativeLuminance("#808080"), 4);
        }

        [Fact]
        public void Contrast_ratio_is_symmetric_and_bounded_by_twenty_one()
        {
            Assert.Equal(21.0, MapTierPalette.ContrastRatio("#ffffff", "#000000"), 4);
            Assert.Equal(21.0, MapTierPalette.ContrastRatio("#000000", "#ffffff"), 4);
            Assert.Equal(1.0, MapTierPalette.ContrastRatio("#123456", "#123456"), 6);
        }

        [Fact]
        public void Contrast_ratio_accepts_colours_with_or_without_the_hash()
        {
            Assert.Equal(MapTierPalette.ContrastRatio("#ffffff", "#000000"),
                         MapTierPalette.ContrastRatio("ffffff", "000000"), 6);
        }

        [Theory]
        [InlineData("#zzzzzz")]
        [InlineData("#fff")]
        public void Malformed_colours_are_rejected(string bad)
        {
            Assert.ThrowsAny<Exception>(() => MapTierPalette.RelativeLuminance(bad));
        }

        [Fact]
        public void The_palette_covers_exactly_the_authored_tier_range_in_order()
        {
            Assert.Equal(MapTierPalette.MaxTier - MapTierPalette.MinTier + 1, MapTierPalette.All.Count);
            for (int i = 0; i < MapTierPalette.All.Count; i++)
                Assert.Equal(MapTierPalette.MinTier + i, MapTierPalette.All[i].Tier);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(-1)]
        public void Tiers_outside_the_authored_range_are_rejected(int tier)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MapTierPalette.For(tier));
        }

        [Fact]
        public void The_fills_are_a_sequential_ramp_not_a_categorical_palette()
        {
            // Strictly increasing luminance is what makes the encoding ordinal: it
            // survives greyscale printing and every form of colour deficiency,
            // because lightness is never the channel that is lost.
            for (int tier = MapTierPalette.MinTier; tier < MapTierPalette.MaxTier; tier++)
            {
                double lower = MapTierPalette.RelativeLuminance(MapTierPalette.For(tier).Fill);
                double higher = MapTierPalette.RelativeLuminance(MapTierPalette.For(tier + 1).Fill);
                Assert.True(higher > lower,
                    $"Tier {tier + 1} must be lighter than tier {tier} ({higher} vs {lower}).");
            }
        }

        [Fact]
        public void Adjacent_tiers_stay_apart_by_lightness_alone()
        {
            for (int tier = MapTierPalette.MinTier; tier < MapTierPalette.MaxTier; tier++)
            {
                double ratio = MapTierPalette.ContrastRatio(
                    MapTierPalette.For(tier).Fill, MapTierPalette.For(tier + 1).Fill);
                Assert.True(ratio >= MapTierPalette.MinimumAdjacentContrast,
                    $"Tiers {tier} and {tier + 1} are only {ratio:0.00}:1 apart.");
            }
        }

        [Fact]
        public void Every_label_ink_clears_wcag_aa_against_its_own_swatch()
        {
            foreach (MapTierColours tier in MapTierPalette.All)
            {
                double ratio = MapTierPalette.ContrastRatio(tier.Ink, tier.Fill);
                Assert.True(ratio >= MapTierPalette.MinimumInkContrast,
                    $"Tier {tier.Tier} label is {ratio:0.00}:1 on {tier.Fill}.");
            }
        }

        [Fact]
        public void The_ink_is_chosen_by_contrast_rather_than_by_hand()
        {
            foreach (MapTierColours tier in MapTierPalette.All)
            {
                double chosen = MapTierPalette.ContrastRatio(tier.Ink, tier.Fill);
                double rejected = MapTierPalette.ContrastRatio(
                    tier.Ink == MapTierPalette.LightInk ? MapTierPalette.DarkInk : MapTierPalette.LightInk,
                    tier.Fill);
                Assert.True(chosen >= rejected,
                    $"Tier {tier.Tier} picked the worse ink ({chosen:0.00} < {rejected:0.00}).");
            }
        }

        [Fact]
        public void Ink_flips_across_the_luminance_crossover()
        {
            // Both inks are fixed, so the crossover is a single luminance value.
            // Below it light ink wins, above it dark ink does.
            Assert.Equal(MapTierPalette.LightInk, MapTierPalette.InkFor("#000000"));
            Assert.Equal(MapTierPalette.DarkInk, MapTierPalette.InkFor("#ffffff"));
            Assert.Equal(MapTierPalette.LightInk, MapTierPalette.For(1).Ink);
            Assert.Equal(MapTierPalette.LightInk, MapTierPalette.For(2).Ink);
            Assert.Equal(MapTierPalette.DarkInk, MapTierPalette.For(3).Ink);
            Assert.Equal(MapTierPalette.DarkInk, MapTierPalette.For(4).Ink);
        }

        [Fact]
        public void The_halo_is_always_the_opposite_of_the_ink()
        {
            foreach (MapTierColours tier in MapTierPalette.All)
            {
                Assert.NotEqual(tier.Ink, tier.Halo);
                Assert.True(tier.Halo == MapTierPalette.LightInk || tier.Halo == MapTierPalette.DarkInk);
            }
        }

        [Fact]
        public void The_shipped_values_are_the_measured_ones()
        {
            // Pinned so a palette change has to be a deliberate, re-measured act.
            Assert.Equal("#01295d", MapTierPalette.For(1).Fill);
            Assert.Equal("#4d5361", MapTierPalette.For(2).Fill);
            Assert.Equal("#848069", MapTierPalette.For(3).Fill);
            Assert.Equal("#c4b34a", MapTierPalette.For(4).Fill);
        }

        [Fact]
        public void The_css_drives_cell_label_and_legend_from_the_same_values()
        {
            string css = MapTierPalette.Css();
            foreach (MapTierColours tier in MapTierPalette.All)
            {
                Assert.Contains($".map-biome.type-{tier.Tier}{{fill:{tier.Fill}}}", css);
                Assert.Contains($".map-swatch.tier-{tier.Tier}{{background:{tier.Fill}}}", css);
                Assert.Contains($".map-cell-label.type-{tier.Tier}{{fill:{tier.Ink};stroke:{tier.Halo}}}", css);
                Assert.Contains($".map-cell-label.type-{tier.Tier} .tier{{fill:{tier.Ink}}}", css);
                Assert.Contains($".map-biome.type-{tier.Tier}.unassigned{{stroke:{tier.Ink}}}", css);
            }
            Assert.DoesNotContain(".map-biome.type-0", css);
            Assert.DoesNotContain($".map-biome.type-{MapTierPalette.MaxTier + 1}", css);
        }

        [Fact]
        public void The_retired_categorical_palette_is_gone()
        {
            // The old Google-Sheets swatches: green/blue/purple/orange. Under
            // deuteranopia the blue and purple collapsed to dE00 3.5, and under
            // protanopia the green and orange collapsed to dE00 1.9.
            string css = MapTierPalette.Css();
            foreach (string retired in new[] { "#93c47d", "#6d9eeb", "#8e7cc3", "#f6b26b" })
                Assert.DoesNotContain(retired, css);
        }
    }
}

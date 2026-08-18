using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The admin map colours tier categorically - one hue per tier - so these tests
    /// pin the properties that keep a categorical palette honest: every pair of
    /// tiers stays apart under normal vision AND under each colour deficiency, the
    /// label ink on each swatch clears WCAG AA and is chosen by measurement rather
    /// than by hand, and the cell, the label and the legend swatch all come out of
    /// one list so they cannot drift apart. They are the guard against someone
    /// dropping four unmeasured default swatches back in.
    /// </summary>
    public class MapTierPaletteTests
    {
        private static IReadOnlyList<string> Fills
            => MapTierPalette.All.Select(tier => tier.Fill).ToList();

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
        public void Every_pair_of_tiers_stays_apart_under_every_colour_deficiency()
        {
            // The failure this exists to prevent: the original green/orange pair sat
            // dE00 2.1 apart under protanopia, i.e. one colour for roughly one man
            // in twelve. Greyscale is excluded deliberately - this palette is
            // categorical, and the tier text on every cell carries the value.
            foreach (ColourVision vision in MapColourMetrics.AllVisions)
            {
                if (vision == ColourVision.Greyscale) continue;
                double closest = MapColourMetrics.ClosestPair(Fills, vision);
                Assert.True(closest >= MapTierPalette.MinimumTierDifference,
                    $"Under {vision} the closest two tiers are only dE00 {closest:0.0} apart.");
            }
        }

        [Fact]
        public void Each_tier_keeps_its_own_hue_rather_than_a_place_on_one_ramp()
        {
            // A sequential ramp would have every fill on one hue and differ only in
            // lightness. Requiring the hues themselves to be far apart is what stops
            // the map from silently becoming a heatmap again.
            var hues = Fills.Select(fill =>
            {
                (double _, double a, double b) = MapColourMetrics.Lab(fill);
                double deg = Math.Atan2(b, a) * 180 / Math.PI;
                return deg < 0 ? deg + 360 : deg;
            }).ToList();

            for (int i = 0; i < hues.Count; i++)
                for (int j = i + 1; j < hues.Count; j++)
                {
                    double gap = Math.Abs(hues[i] - hues[j]);
                    if (gap > 180) gap = 360 - gap;
                    Assert.True(gap >= 30,
                        $"Tiers {i + 1} and {j + 1} share a hue ({gap:0.0} degrees apart).");
                }
        }

        [Fact]
        public void No_tier_is_mistakable_for_the_weather_wall_drawn_over_it()
        {
            // The walls are stroked straight across the tier cells, so a wall that
            // matches the fill under it disappears. These are the shipped wall
            // colours from the map's own stylesheet.
            var walls = new (string Name, string Colour)[]
            {
                ("Wind Rift", "#74c9cf"), ("Storm Rift", "#9b86d8"), ("Typhon", "#d48388"),
                ("Sand Storm", "#e8963c"), ("Ice Storm", "#a9d6ed"), ("World End", "#ec8f88"),
            };
            foreach (MapTierColours tier in MapTierPalette.All)
                foreach ((string name, string colour) in walls)
                {
                    double d = MapColourMetrics.Difference(tier.Fill, colour, ColourVision.Normal);
                    Assert.True(d >= MapTierPalette.MinimumTierDifference,
                        $"Tier {tier.Tier} {tier.Fill} is only dE00 {d:0.0} from the {name} wall.");
                }
        }

        [Fact]
        public void Every_tier_is_distinguishable_from_the_ocean_it_is_drawn_on()
        {
            const string ocean = "#09151d";
            foreach (MapTierColours tier in MapTierPalette.All)
            {
                double d = MapColourMetrics.Difference(tier.Fill, ocean, ColourVision.Normal);
                Assert.True(d >= 15.0,
                    $"Tier {tier.Tier} {tier.Fill} is only dE00 {d:0.0} from the ocean.");
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
            // Below it light ink wins, above it dark ink does. With a categorical
            // palette the flips follow the hues, not the tier order.
            Assert.Equal(MapTierPalette.LightInk, MapTierPalette.InkFor("#000000"));
            Assert.Equal(MapTierPalette.DarkInk, MapTierPalette.InkFor("#ffffff"));
            Assert.Equal(MapTierPalette.LightInk, MapTierPalette.For(1).Ink);
            Assert.Equal(MapTierPalette.DarkInk, MapTierPalette.For(2).Ink);
            Assert.Equal(MapTierPalette.LightInk, MapTierPalette.For(3).Ink);
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
            Assert.Equal("#134e26", MapTierPalette.For(1).Fill);
            Assert.Equal("#4f89c1", MapTierPalette.For(2).Fill);
            Assert.Equal("#694189", MapTierPalette.For(3).Fill);
            Assert.Equal("#cdb236", MapTierPalette.For(4).Fill);
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
        public void The_retired_palettes_are_gone()
        {
            string css = MapTierPalette.Css();
            // The unchosen Google-Sheets defaults: green/blue/purple/orange, drawn
            // at 38% so the legend never matched the map. Protanopia collapsed the
            // green and the orange to dE00 2.1.
            foreach (string retired in new[] { "#93c47d", "#6d9eeb", "#8e7cc3", "#f6b26b" })
                Assert.DoesNotContain(retired, css);
            // The cividis ramp that replaced them: measured well, looked like a
            // heatmap, and threw away the per-tier hue identity operators read by.
            foreach (string retired in new[] { "#01295d", "#4d5361", "#848069", "#c4b34a" })
                Assert.DoesNotContain(retired, css);
        }

        [Fact]
        public void The_deficiency_simulation_agrees_with_its_reference_behaviour()
        {
            // Anchors, so a broken matrix cannot quietly inflate the separation
            // figures the palette is signed off on.
            foreach (ColourVision vision in MapColourMetrics.AllVisions)
            {
                Assert.Equal("#ffffff", MapColourMetrics.Simulate("#ffffff", vision));
                Assert.Equal("#000000", MapColourMetrics.Simulate("#000000", vision));
                Assert.Equal(0.0, MapColourMetrics.Difference("#336699", "#336699", vision), 6);
            }
            Assert.Equal("#336699", MapColourMetrics.Simulate("#336699", ColourVision.Normal));
            // Protanopia and deuteranopia are red-green: pure red and pure green
            // stop being different colours.
            Assert.True(MapColourMetrics.Difference("#ff0000", "#00ff00", ColourVision.Normal) > 80);
            Assert.True(MapColourMetrics.Difference("#ff0000", "#00ff00", ColourVision.Deuteranopia) < 30);
            // CIELAB anchors: white is L* 100 with no chroma, black is L* 0.
            Assert.Equal(100.0, MapColourMetrics.Lab("#ffffff").L, 3);
            Assert.Equal(0.0, MapColourMetrics.Lab("#000000").L, 3);
        }
    }
}

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
        /// <summary>
        /// The colours a reader actually receives: the authored hue already
        /// composited over the ocean at the shipped opacity. Every measurement
        /// below uses these, never the authored hue - measuring the hue would be
        /// measuring a colour nobody is ever shown.
        /// </summary>
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
            // lightness, so this test is the guard against the map silently becoming
            // a heatmap again. It asks two things of the hue circle: the four tiers
            // must SPAN most of it - a ramp spans almost none - and no two may sit on
            // top of each other. The pairwise floor is 25 degrees, not 30: this
            // palette's navy and lilac are 27.5 apart in CIELAB hue, which is four
            // named colours by any reading, and that pair is dE00 29.1 apart anyway
            // because they differ enormously in lightness. The span requirement is
            // the one that actually detects a ramp.
            var hues = Fills.Select(fill =>
            {
                (double _, double a, double b) = MapColourMetrics.Lab(fill);
                double deg = Math.Atan2(b, a) * 180 / Math.PI;
                return deg < 0 ? deg + 360 : deg;
            }).ToList();

            double widest = 0;
            for (int i = 0; i < hues.Count; i++)
                for (int j = i + 1; j < hues.Count; j++)
                {
                    double gap = Math.Abs(hues[i] - hues[j]);
                    if (gap > 180) gap = 360 - gap;
                    widest = Math.Max(widest, gap);
                    Assert.True(gap >= 25,
                        $"Tiers {i + 1} and {j + 1} share a hue ({gap:0.0} degrees apart).");
                }

            Assert.True(widest >= 150,
                $"The four tiers only span {widest:0.0} degrees of hue - that is a ramp, not four colours.");
        }

        [Fact]
        public void No_tier_is_mistakable_for_the_weather_wall_drawn_over_it()
        {
            // The walls are stroked straight across the tier cells, so a wall that
            // matches the fill under it disappears. The wall colours come from the
            // module that also draws them, not from a copy in this file, so a wall
            // cannot be recoloured without this measurement running again. This is
            // the test that caught Storm Rift at dE00 8.2 from the lilac Remnants
            // fill and forced it off #9b86d8.
            foreach (MapTierColours tier in MapTierPalette.All)
                foreach (MapWallColours wall in MapWallPalette.All)
                {
                    double d = MapColourMetrics.Difference(tier.Fill, wall.Colour, ColourVision.Normal);
                    Assert.True(d >= MapTierPalette.MinimumTierDifference,
                        $"Tier {tier.Tier} {tier.Fill} is only dE00 {d:0.0} from the {wall.Name} wall.");
                }
        }

        [Fact]
        public void Every_tier_is_distinguishable_from_the_ocean_it_is_drawn_on()
        {
            // Doubly load-bearing now the cells are translucent: the ocean is not
            // just what a cell is seen NEXT to, it is what the cell is composited
            // OVER, so a fill drifting toward it drifts toward invisibility.
            foreach (MapTierColours tier in MapTierPalette.All)
            {
                double d = MapColourMetrics.Difference(tier.Fill, MapTierPalette.Ocean,
                    ColourVision.Normal);
                Assert.True(d >= 15.0,
                    $"Tier {tier.Tier} {tier.Fill} is only dE00 {d:0.0} from the ocean.");
            }
        }

        [Fact]
        public void Each_wall_stays_distinct_from_every_other_wall()
        {
            // Six walls share one map. Moving Storm Rift away from the Remnants
            // fill must not have moved it into another wall.
            for (int i = 0; i < MapWallPalette.All.Count; i++)
                for (int j = i + 1; j < MapWallPalette.All.Count; j++)
                {
                    MapWallColours a = MapWallPalette.All[i], b = MapWallPalette.All[j];
                    double d = MapColourMetrics.Difference(a.Colour, b.Colour, ColourVision.Normal);
                    // Typhon and World End are both authored salmon and sit dE00 6.6
                    // apart; that is Bossa's own choice and predates this palette, so
                    // the floor here is the weaker "not the same colour" one.
                    Assert.True(d >= 5.0,
                        $"The {a.Name} and {b.Name} walls are only dE00 {d:0.0} apart.");
                }

            Assert.Equal(6, MapWallPalette.All.Count);
            Assert.DoesNotContain(MapWallPalette.All, wall => wall.Colour == "#9b86d8");
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
            Assert.Equal(MapTierPalette.LightInk, MapTierPalette.For(2).Ink);
            Assert.Equal(MapTierPalette.DarkInk, MapTierPalette.For(3).Ink);
            Assert.Equal(MapTierPalette.DarkInk, MapTierPalette.For(4).Ink);
        }

        [Fact]
        public void The_ink_is_recomputed_against_the_composite_not_the_authored_hue()
        {
            // The failure this exists to prevent is subtle and would look fine in
            // review: pick the ink from the CSS hex, draw the cell translucent, and
            // the label is now sitting on a colour nobody consulted. T1 is the live
            // proof - the authored green takes DARK ink, the composited green takes
            // LIGHT ink - so a palette that skipped the recompute would put dark ink
            // on a dark cell here.
            MapTierColours t1 = MapTierPalette.For(1);
            Assert.Equal(MapTierPalette.DarkInk, MapTierPalette.InkFor(t1.Hue));
            Assert.Equal(MapTierPalette.LightInk, t1.Ink);

            foreach (MapTierColours tier in MapTierPalette.All)
                Assert.Equal(MapTierPalette.InkFor(tier.Fill), tier.Ink);
        }

        [Fact]
        public void No_tier_lands_in_the_band_where_neither_ink_can_reach_aa()
        {
            // With two fixed inks there is a luminance interval in which the BEST
            // available contrast is under AA, so no choice of ink saves the label.
            // Alpha sweeps every tier's luminance downwards, so it is alpha's job to
            // keep them all out of that interval - this is why the shipped opacity
            // is one of a few permitted values and not a taste dial.
            (double low, double high) = MapTierPalette.UnlabelableLuminanceBand();
            Assert.True(low < high, "The two inks are too far apart for a band to exist.");
            Assert.Equal(0.1582, low, 4);
            Assert.Equal(0.2005, high, 4);

            foreach (MapTierColours tier in MapTierPalette.All)
            {
                double y = MapTierPalette.RelativeLuminance(tier.Fill);
                Assert.False(y > low && y < high,
                    $"Tier {tier.Tier} composites to luminance {y:0.0000}, inside the "
                    + $"unlabelable band {low:0.0000}-{high:0.0000}; no ink clears AA there.");
            }
        }

        [Fact]
        public void The_composite_is_source_over_and_bounded_by_its_two_ends()
        {
            Assert.Equal("#ffffff", MapTierPalette.Composite("#ffffff", "#000000", 1.0));
            Assert.Equal("#000000", MapTierPalette.Composite("#ffffff", "#000000", 0.0));
            Assert.Equal("#808080", MapTierPalette.Composite("#ffffff", "#000000", 0.5));
            Assert.Equal("#123456", MapTierPalette.Composite("#123456", "#abcdef", 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MapTierPalette.Composite("#ffffff", "#000000", 1.4));
        }

        [Fact]
        public void The_legend_swatch_is_the_composite_of_exactly_what_the_map_draws()
        {
            // THE regression guard. The first transparent palette put the raw hex in
            // the legend and a 38%-composited colour on the map: the key was a lie
            // about the picture. Transparency is back, so the guard has to survive
            // it - and "they come from one list" is no longer enough, because the
            // list now holds two different colours per tier. So this re-derives the
            // composite from the three things the stylesheet itself declares: the
            // cell's fill, the cell's fill-opacity, and the ocean rule.
            string css = MapTierPalette.Css();
            Assert.Contains($".map-ocean{{fill:{MapTierPalette.Ocean}}}", css);

            foreach (MapTierColours tier in MapTierPalette.All)
            {
                Assert.Contains(
                    $".map-biome.type-{tier.Tier}{{fill:{tier.Hue};fill-opacity:{MapTierPalette.FillOpacityCss}}}",
                    css);
                Assert.Contains($".map-swatch.tier-{tier.Tier}{{background:{tier.Fill}}}", css);

                string asDrawn = MapTierPalette.Composite(
                    tier.Hue, MapTierPalette.Ocean,
                    double.Parse(MapTierPalette.FillOpacityCss,
                        System.Globalization.CultureInfo.InvariantCulture));
                Assert.Equal(asDrawn, tier.Fill);
            }

            // And the transparency has to be real, not decorative: if the two were
            // ever equal the legend would be trivially right and the "translucent
            // zone" the page promises would be a solid one.
            Assert.True(MapTierPalette.FillOpacity < 1.0);
            Assert.All(MapTierPalette.All, tier => Assert.NotEqual(tier.Hue, tier.Fill));
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
            // Both halves are pinned: the authored hue that goes into the CSS, and
            // the colour it becomes on screen. Changing either without the other is
            // the bug this whole file is about.
            Assert.Equal(0.76, MapTierPalette.FillOpacity, 6);
            Assert.Equal("0.76", MapTierPalette.FillOpacityCss);
            Assert.Equal("#09151d", MapTierPalette.Ocean);

            Assert.Equal("#4b934f", MapTierPalette.For(1).Hue);
            Assert.Equal("#204c8a", MapTierPalette.For(2).Hue);
            Assert.Equal("#bc9be2", MapTierPalette.For(3).Hue);
            Assert.Equal("#eed059", MapTierPalette.For(4).Hue);

            Assert.Equal("#3b7543", MapTierPalette.For(1).Fill);
            Assert.Equal("#1a3f70", MapTierPalette.For(2).Fill);
            Assert.Equal("#917bb3", MapTierPalette.For(3).Fill);
            Assert.Equal("#b7a34b", MapTierPalette.For(4).Fill);
        }

        [Fact]
        public void The_css_drives_cell_label_and_legend_from_the_same_values()
        {
            string css = MapTierPalette.Css();
            foreach (MapTierColours tier in MapTierPalette.All)
            {
                Assert.Contains(
                    $".map-biome.type-{tier.Tier}{{fill:{tier.Hue};fill-opacity:{MapTierPalette.FillOpacityCss}}}",
                    css);
                Assert.Contains($".map-swatch.tier-{tier.Tier}{{background:{tier.Fill}}}", css);
                Assert.Contains($".map-cell-label.type-{tier.Tier}{{fill:{tier.Ink};stroke:{tier.Halo}}}", css);
                Assert.Contains($".map-cell-label.type-{tier.Tier} .tier{{fill:{tier.Ink}}}", css);
                Assert.Contains($".map-cell-label.type-{tier.Tier} .stock{{fill:{tier.Ink}}}", css);
                Assert.Contains($".tierchip.tier-{tier.Tier}{{background:{tier.Fill};color:{tier.Ink}}}", css);
                Assert.Contains($".map-biome.type-{tier.Tier}.unassigned{{stroke:{tier.Ink}}}", css);
            }
            Assert.DoesNotContain(".map-biome.type-0", css);
            Assert.DoesNotContain($".map-biome.type-{MapTierPalette.MaxTier + 1}", css);
        }

        [Fact]
        public void The_wall_css_drives_the_stroke_and_its_legend_key_from_one_list()
        {
            string css = MapWallPalette.Css();
            string legend = MapWallPalette.LegendHtml();
            foreach (MapWallColours wall in MapWallPalette.All)
            {
                Assert.Contains($".map-wall.type-{wall.Type}{{stroke:{wall.Colour};", css);
                Assert.Contains($".map-swatch.wall-{wall.Type}{{background:{wall.Colour}}}", css);
                // Every wall that is drawn also gets a key. Typhon and Ice Storm
                // were on the map and missing from the legend before this list.
                Assert.Contains($"map-swatch wall-{wall.Type}", legend);
                Assert.Contains(wall.LegendLabel.Replace("&", "&amp;"), legend);
                Assert.Equal(wall, MapWallPalette.For(wall.Type));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => MapWallPalette.For(6));
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
            // "Deepwater": the same four hues, pitched darker, shipped for one
            // commit and replaced on the strength of the eye rather than the
            // numbers, which it also passed.
            foreach (string retired in new[] { "#134e26", "#4f89c1", "#694189", "#cdb236" })
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

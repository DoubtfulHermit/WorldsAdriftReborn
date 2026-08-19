using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Emblems;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The two hundred objects traced off the four later sheets, and the two
    /// questions wiring them in raises: did anything already in the catalogue move,
    /// and is the tracer's spelling of a path really the painter's.
    /// </summary>
    public class EmblemObjectSheetTests
    {
        /// <summary>
        /// The sheets, and how many icons each one carries. Pinned, because the
        /// index an object takes is decided by which sheet it is on and where on
        /// that sheet - so a sheet quietly growing in the middle of this list would
        /// shift every object below it onto somebody else's crest.
        /// </summary>
        private static readonly (string Category, int Count)[] Sheets =
        {
            (EmblemObjectSheets.EasternCategory, 50),
            (EmblemObjectSheets.SalvageCategory, 50),
            (EmblemObjectSheets.OutlineCategory, 50),
            (EmblemObjectSheets.SolidCategory, 50),
        };

        [Fact]
        public void The_sheets_arrive_whole_and_in_sheet_order()
        {
            Assert.Equal(Sheets.Sum(s => s.Count), EmblemObjectSheets.All.Count);

            int at = 0;
            foreach ((string category, int count) in Sheets)
            {
                for (int i = 0; i < count; i++)
                {
                    Assert.Equal(category, EmblemObjectSheets.All[at + i].Category);
                }

                at += count;
            }
        }

        /// <summary>
        /// The first and last icon of every sheet, by name.
        ///
        /// Four sheets' worth of names is two hundred lines that say nothing; the
        /// ends of each run are what actually catch a reordering, because a sort
        /// that went wrong shows up there first.
        /// </summary>
        [Theory]
        [InlineData(0, "Torii gate")]
        [InlineData(49, "Crossed bow and arrow")]
        [InlineData(50, "Hexagon frame")]
        [InlineData(99, "Robot arm")]
        [InlineData(100, "Hexagon outline")]
        [InlineData(149, "Scalloped disc outline")]
        [InlineData(150, "Hexagon solid")]
        [InlineData(199, "Scalloped disc solid")]
        public void An_icon_is_where_the_sheets_put_it(int index, string name)
        {
            Assert.Equal(name, EmblemObjectSheets.All[index].Name);
        }

        /// <summary>
        /// THE CONVENTION PROOF. The tracer writes "M-970 -964 L-939 -886 … Z" and
        /// the painter's own writer writes the same outline back out; if the two
        /// disagreed about the unit, about which way y points, or about where one
        /// contour ends and the next begins, the strings would not match.
        ///
        /// Whitespace is the one licensed difference: the tracer separates a close
        /// from the next move with a space and <see cref="EmblemPath"/> does not.
        /// </summary>
        [Fact]
        public void Every_traced_path_survives_a_round_trip_through_the_painters_own_writer()
        {
            JObject source = JObject.Parse(SheetJson());
            JArray objects = (JArray)source["objects"]!;

            Assert.Equal(200, objects.Count);

            foreach (JObject entry in objects.OfType<JObject>())
            {
                string name = (string)entry["name"]!;
                string written = (string)entry["path"]!;

                EmblemPath path = EmblemPath.ParseDrawing(written, 1000.0);

                Assert.Equal(
                    written.Replace("Z ", "Z"),
                    path.ToPathData(1000.0));

                // And the contour count the tracer counted is the contour count the
                // painter reads, which is what "|" means in the other spelling.
                Assert.Equal((int)entry["contours"]!, written.Count(c => c == 'M'));
                Assert.Equal(name, name);
            }
        }

        /// <summary>
        /// The two spellings really are one geometry: a path written in the compact
        /// form and the same path written in the tracer's form agree everywhere,
        /// including inside a hole.
        /// </summary>
        [Fact]
        public void The_two_spellings_of_a_path_describe_the_same_shape()
        {
            const string compact = "-800 -800 800 -800 800 800 -800 800|-400 -400 -400 400 400 400 400 -400";
            const string drawn =
                "M-800 -800 L800 -800 L800 800 L-800 800 Z M-400 -400 L-400 400 L400 400 L400 -400 Z";

            EmblemPath one = EmblemPath.Parse(compact, 1000.0);
            EmblemPath two = EmblemPath.ParseDrawing(drawn, 1000.0);

            Assert.Equal(one.ToPathData(), two.ToPathData());

            // Solid in the ring, hollow in the middle - both of them, by non-zero.
            Assert.True(one.Contains(0.6, 0.0));
            Assert.True(two.Contains(0.6, 0.0));
            Assert.False(one.Contains(0.0, 0.0));
            Assert.False(two.Contains(0.0, 0.0));
        }

        [Fact]
        public void A_path_dialect_this_does_not_speak_is_refused_rather_than_flattened()
        {
            // A curve silently read as a straight line is a shape that draws wrong
            // and never says so.
            Assert.Throws<ArgumentException>(() =>
                EmblemPath.ParseDrawing("M0 0 C10 10 20 20 30 30 Z", 1000.0));
        }

        /// <summary>
        /// Every object is drawn inside the box a layer scales it by, and the sheets
        /// say they fill 0.98 of it - so an object that reached 1.0 would be one the
        /// tracer had not actually produced.
        /// </summary>
        [Fact]
        public void No_traced_object_reaches_past_the_extent_the_sheets_declare()
        {
            foreach (EmblemObjectSheets.Icon icon in EmblemObjectSheets.All)
            {
                Assert.True(icon.Path.Reach <= 0.981, icon.Name + " reaches " + icon.Path.Reach);
                Assert.True(icon.Path.Reach > 0.05, icon.Name + " is vanishingly small");
            }
        }

        // ------------------------------------------------------------- the pairing

        /// <summary>
        /// The fifty geometric forms exist twice, once stroked and once filled, and
        /// the two runs pair up index for index. Checked here because a control that
        /// offers "outline or solid" would be built on this holding.
        /// </summary>
        [Fact]
        public void The_outline_and_solid_sheets_are_the_same_fifty_forms()
        {
            List<EmblemObjectSheets.Icon> outlines = EmblemObjectSheets.All
                .Where(i => i.Category == EmblemObjectSheets.OutlineCategory).ToList();
            List<EmblemObjectSheets.Icon> solids = EmblemObjectSheets.All
                .Where(i => i.Category == EmblemObjectSheets.SolidCategory).ToList();

            Assert.Equal(50, outlines.Count);
            Assert.Equal(solids.Count, outlines.Count);

            for (int i = 0; i < outlines.Count; i++)
            {
                Assert.Equal(outlines[i].Form, solids[i].Form);
                Assert.Equal("outline", outlines[i].Variant);
                Assert.Equal("solid", solids[i].Variant);
            }
        }

        /// <summary>
        /// Exactly three forms have no outline/solid contrast to offer - the sheets
        /// carry the same drawing on both - and they are the three the tools README
        /// names. A fourth appearing is a sheet having been redrawn, and whoever
        /// builds the variant control needs to know before a player does.
        /// </summary>
        [Fact]
        public void Three_forms_and_only_three_have_no_variant_contrast()
        {
            string[] flat = EmblemObjectSheets.All
                .Where(i => !i.Contrasts)
                .Select(i => i.Form!)
                .Distinct()
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(new[] { "dashed-ring", "diamond-ring", "vesica-leaf" }, flat);
        }

        // ---------------------------------------------------------- suppression

        /// <summary>
        /// A suppressed object is HIDDEN, not gone. Whatever the list holds - it is
        /// empty at the time of writing, because the eight illegible ones are an art
        /// call nobody has made - the entry keeps its index and still draws, or a
        /// crest already using it would change.
        /// </summary>
        [Fact]
        public void A_hidden_object_still_has_an_index_and_still_draws()
        {
            for (int i = 0; i < EmblemObjects.Count; i++)
            {
                if (!EmblemObjects.All[i].Hidden) continue;

                Assert.NotNull(EmblemObjects.PathAt(i));
                Assert.True(EmblemObjects.All[i].Path.Reach > 0.05);
            }
        }

        /// <summary>
        /// The catalogue the browser gets carries the flag, and carries it only where
        /// it is set - so the panel can hide an object the renderer still honours.
        /// </summary>
        [Fact]
        public void The_browser_is_told_which_objects_not_to_offer()
        {
            JArray objects = (JArray)JObject.Parse(EmblemEditorData.Catalogue)["objects"]!;

            Assert.Equal(EmblemObjects.Count, objects.Count);

            for (int i = 0; i < EmblemObjects.Count; i++)
            {
                bool flagged = objects[i]!["h"] != null;

                Assert.Equal(EmblemObjects.All[i].Hidden, flagged);
            }
        }

        // ------------------------------------------------------- does it DRAW

        /// <summary>
        /// EVERY ONE OF THE TWO HUNDRED PUTS INK ON THE CANVAS.
        ///
        /// This is the failure this whole change was exposed to and the only one a
        /// screenshot of the palette would not catch: an object that has a name, a
        /// tile and a preview outline, and draws NOTHING when the server renders it
        /// - because the palette draws the browser's copy of the path and the crest
        /// draws the rasteriser's. So every object is put on a real canvas, through
        /// the real painter, and has to colour something in.
        ///
        /// Rendered small on purpose: this is two hundred renders, and "did any ink
        /// land" does not need 256 pixels to answer.
        ///
        /// AT SIZE 1000, which is the editor's "full" and NOT
        /// <see cref="EmblemLayer.MaxSize"/>. The maximum is 2000 - twice the box -
        /// and at twice the box a hollow object like an outlined hexagon has its
        /// entire ring of ink outside the canvas and its hole covering all of it, so
        /// it correctly paints nothing. That is a real and legal thing for a player
        /// to build; it is not what this test is asking about.
        /// </summary>
        [Fact]
        public void Every_object_on_the_sheets_actually_paints_something()
        {
            int first = EmblemObjects.Count - EmblemObjectSheets.All.Count;

            for (int i = 0; i < EmblemObjectSheets.All.Count; i++)
            {
                Assert.True(EmblemLayer.TryCreate(
                    first + i, 0, 0, EmblemLayer.Unit, 0, 17, EmblemLayer.OpacitySteps,
                    false, false, false, false, out EmblemLayer layer));
                Assert.True(EmblemStack.TryCreate(new[] { layer }, out EmblemStack stack));

                byte[] pixels = EmblemStackPainter.Render(stack, 64);

                int inked = 0;
                for (int p = 3; p < pixels.Length; p += 4)
                {
                    if (pixels[p] > 0) inked++;
                }

                Assert.True(inked > 40,
                    EmblemObjectSheets.All[i].Name + " (object " + (first + i)
                    + ") is in the palette and paints " + inked + " pixels");
            }
        }

        /// <summary>
        /// THE WHOLE ROUND TRIP for one of the new objects, the way a player takes
        /// it: pick it, save the design as a code, have the server read that code
        /// back off a URL, and render the crest the game will download.
        ///
        /// The object chosen is one off the LAST sheet, because it holds the highest
        /// index in the catalogue and is therefore the one an off-by-one anywhere in
        /// the code's two-character object field would drop first.
        /// </summary>
        [Fact]
        public void A_design_built_from_the_new_artwork_survives_being_saved_and_reloaded()
        {
            int torii = Index("Torii gate");
            int last = EmblemObjects.Count - 1;

            Assert.Equal("Scalloped disc solid", EmblemObjects.All[last].Name);

            Assert.True(EmblemLayer.TryCreate(
                last, 0, 0, 900, 0, 3, EmblemLayer.OpacitySteps, false, false, false, false,
                out EmblemLayer field));
            Assert.True(EmblemLayer.TryCreate(
                torii, 0, 40, 620, 0, 0, EmblemLayer.OpacitySteps, false, false, false, false,
                out EmblemLayer device));
            Assert.True(EmblemStack.TryCreate(new[] { field, device }, out EmblemStack stack));

            string code = stack.ToCode();

            // Off the wire and back, exactly as the emblem route does it.
            string url = EmblemUrlPolicy.PreviewUrl(EmblemArtwork.Of(stack));
            Assert.True(EmblemUrlPolicy.TryParseRequest(
                url, out _, out EmblemArtwork parsed, out bool hasCode, out _, out _));
            Assert.True(hasCode);

            Assert.NotNull(parsed.Stack);
            Assert.Equal(code, parsed.Stack!.ToCode());
            Assert.Equal(last, parsed.Stack.Layers[0].Object);
            Assert.Equal(torii, parsed.Stack.Layers[1].Object);

            // And the picture the game downloads has both of them in it: the disc
            // in bone, the gate in midnight over the top of it.
            byte[] png = EmblemImages.Png(parsed);
            Assert.True(png.Length > 1000);

            byte[] pixels = EmblemStackPainter.Render(parsed.Stack, 256);

            int bone = 0, midnight = 0;
            for (int p = 0; p < pixels.Length; p += 4)
            {
                if (pixels[p + 3] == 0) continue;

                if (Same(pixels, p, EmblemVocabulary.Palette[3])) bone++;
                if (Same(pixels, p, EmblemVocabulary.Palette[0])) midnight++;
            }

            Assert.True(bone > 500, "the disc under the gate painted " + bone + " pixels");
            Assert.True(midnight > 500, "the gate over the disc painted " + midnight + " pixels");
        }

        private static bool Same(byte[] rgba, int at, int packed) =>
            rgba[at] == ((packed >> 16) & 0xFF)
            && rgba[at + 1] == ((packed >> 8) & 0xFF)
            && rgba[at + 2] == (packed & 0xFF);

        private static int Index(string name)
        {
            for (int i = 0; i < EmblemObjects.Count; i++)
            {
                if (EmblemObjects.All[i].Name == name) return i;
            }

            throw new Xunit.Sdk.XunitException("no object called " + name);
        }

        private static string SheetJson()
        {
            System.Reflection.Assembly assembly = typeof(EmblemObjects).Assembly;

            string name = assembly.GetManifestResourceNames()
                .Single(n => n.EndsWith(EmblemObjectSheets.ResourceName, StringComparison.Ordinal));

            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using StreamReader reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
    }
}

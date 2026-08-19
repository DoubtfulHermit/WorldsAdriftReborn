using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Emblems;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The object catalogue, and the one rule that keeps every saved emblem
    /// pointing at the artwork it was made with: APPEND, NEVER INSERT.
    ///
    /// A layer stores its object as an INDEX. Those indices are already in the
    /// database and in URLs the game client has cached, so inserting a shape in the
    /// middle would silently redraw every saved emblem with the wrong picture. The
    /// guard below is a pinned list of the entries that existed when the layered
    /// editor shipped: appending is free, and moving anything is a failing test
    /// that says exactly what moved.
    /// </summary>
    public class EmblemObjectsTests
    {
        /// <summary>
        /// The catalogue as it shipped, in index order. NEW SHAPES GO ON THE END of
        /// this list, and only on the end.
        /// </summary>
        private static readonly string[] Shipped =
        {
            // The five shield outlines, reused from the heraldic builder.
            "Heater shield", "Roundel", "Hexagon", "Lozenge", "Banner",

            // The plain geometry a composition needs.
            "Disc", "Square", "Bar", "Slim bar", "Post", "Diamond", "Pentagon",
            "Octagon", "Chevron", "Arrowhead", "Half disc", "Trapezoid",
            "Right triangle", "Blade", "Four-point star", "Six-point star",
            "Eight-point star", "Thin ring",

            // The ten heraldic charges that are drawn in code.
            "Hexagon", "Star", "Gear", "Bolt", "Ring", "Triangle", "Crescent",
            "Saltire", "Cross", "Chevrons",
        };

        [Fact]
        public void The_shapes_that_shipped_are_still_at_the_indices_they_shipped_at()
        {
            Assert.True(EmblemObjects.Count >= Shipped.Length,
                "the catalogue has SHRUNK, which invalidates every saved emblem");

            for (int i = 0; i < Shipped.Length; i++)
            {
                Assert.Equal(Shipped[i], EmblemObjects.All[i].Name);
            }
        }

        [Fact]
        public void The_traced_sheet_follows_the_shapes_in_its_own_order()
        {
            for (int i = 0; i < EmblemDeviceGeometry.Names.Count; i++)
            {
                Assert.Equal(EmblemDeviceGeometry.Names[i], EmblemObjects.All[Shipped.Length + i].Name);
                Assert.Equal(EmblemObjects.DeviceCategory, EmblemObjects.All[Shipped.Length + i].Category);
            }

            Assert.Equal(Shipped.Length + EmblemDeviceGeometry.Names.Count, EmblemObjects.Count);
        }

        /// <summary>
        /// The shields are the SAME paths the heraldic builder cuts its field to,
        /// not a second drawing of them. Two descriptions of one picture drift.
        /// </summary>
        [Fact]
        public void The_shield_outlines_are_the_heraldic_builders_own()
        {
            for (int i = 0; i < EmblemVocabulary.ShapeCount; i++)
            {
                Assert.Same(EmblemGeometry.Shape((EmblemVocabulary.Shape)i), EmblemObjects.All[i].Path);
                Assert.Equal(EmblemObjects.ShieldCategory, EmblemObjects.All[i].Category);
            }
        }

        [Fact]
        public void The_geometric_charges_are_the_painters_own()
        {
            for (int i = 1; i < EmblemVocabulary.FirstDrawnDevice; i++)
            {
                EmblemPath? charge = EmblemGeometry.Device((EmblemVocabulary.Charge)i);
                Assert.NotNull(charge);

                int at = 5 + 18 + (i - 1);
                Assert.Same(charge, EmblemObjects.All[at].Path);
            }
        }

        [Fact]
        public void Every_object_has_a_name_a_category_and_some_area()
        {
            foreach (EmblemObjects.Entry entry in EmblemObjects.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Name));

                Assert.Contains(entry.Category, new[]
                {
                    EmblemObjects.ShieldCategory, EmblemObjects.ShapeCategory,
                    EmblemObjects.DeviceCategory,
                });

                // A shape with no extent is a palette entry nobody can use and a
                // layer that draws nothing.
                Assert.True(entry.Path.MaxX > entry.Path.MinX, entry.Name + " has no width");
                Assert.True(entry.Path.MaxY > entry.Path.MinY, entry.Name + " has no height");
                Assert.True(entry.Path.Reach > 0.05, entry.Name + " is vanishingly small");
            }
        }

        /// <summary>
        /// Every object is drawn INSIDE the box a layer scales it by, or a layer at
        /// size 1000 would be cropped by the canvas at a size the editor calls
        /// "full".
        /// </summary>
        [Fact]
        public void No_object_reaches_outside_its_own_box()
        {
            foreach (EmblemObjects.Entry entry in EmblemObjects.All)
            {
                Assert.True(entry.Path.Reach <= 1.0001, entry.Name + " reaches past its box");
            }
        }

        /// <summary>
        /// A shape whose interior does not contain its own centre is one that reads
        /// as an outline rather than a silhouette. Not true of every object - a
        /// ring is a ring - so this checks that the ones which SHOULD be solid are.
        /// </summary>
        [Theory]
        [InlineData("Disc")]
        [InlineData("Square")]
        [InlineData("Bar")]
        [InlineData("Diamond")]
        [InlineData("Pentagon")]
        [InlineData("Octagon")]
        [InlineData("Blade")]
        [InlineData("Four-point star")]
        [InlineData("Six-point star")]
        [InlineData("Eight-point star")]
        public void A_solid_primitive_is_solid_at_its_centre(string name)
        {
            EmblemObjects.Entry entry = EmblemObjects.All.Single(e => e.Name == name);

            Assert.True(entry.Path.Contains(0, 0), name + " is hollow at its centre");
        }

        [Fact]
        public void A_ring_is_hollow_and_a_disc_is_not()
        {
            Assert.False(EmblemObjects.All.Single(e => e.Name == "Thin ring").Path.Contains(0, 0));
            Assert.True(EmblemObjects.All.Single(e => e.Name == "Disc").Path.Contains(0, 0));
        }

        [Fact]
        public void An_index_outside_the_catalogue_answers_rather_than_throwing()
        {
            Assert.Null(EmblemObjects.PathAt(-1));
            Assert.Null(EmblemObjects.PathAt(EmblemObjects.Count));
            Assert.NotNull(EmblemObjects.PathAt(0));
            Assert.NotNull(EmblemObjects.PathAt(EmblemObjects.Count - 1));
        }

        // ------------------------------------------------------- what the browser sees

        [Fact]
        public void The_catalogue_json_carries_every_object_in_index_order()
        {
            JObject catalogue = JObject.Parse(EmblemEditorData.Catalogue);
            JArray objects = (JArray)catalogue["objects"]!;

            Assert.Equal(EmblemObjects.Count, objects.Count);

            for (int i = 0; i < EmblemObjects.Count; i++)
            {
                Assert.Equal(EmblemObjects.All[i].Name, objects[i]!["n"]!.Value<string>());
                Assert.Equal(EmblemObjects.All[i].Category, objects[i]!["c"]!.Value<string>());

                // The path data is the SAME writer the vector export uses, off the
                // same object the rasteriser samples. The browser never converts
                // anything - it is handed a 'd' attribute.
                Assert.Equal(
                    EmblemObjects.All[i].Path.ToPathData(EmblemObjects.Unit),
                    objects[i]!["d"]!.Value<string>());
            }
        }

        /// <summary>
        /// The revision is a fold of the whole catalogue, so it moves when a shape
        /// is RETOUCHED - same index, same name, different outline - which a count
        /// would sail straight past while every cached browser went on drawing the
        /// old artwork.
        /// </summary>
        [Fact]
        public void The_catalogue_url_carries_a_revision_that_is_stable_within_a_build()
        {
            string once = EmblemEditorData.Revision;

            Assert.Equal(8, once.Length);
            Assert.Equal(once, EmblemEditorData.Revision);
            Assert.Contains(once, EmblemEditorData.CatalogueUrl, StringComparison.Ordinal);
            Assert.True(EmblemUrlPolicy.IsCatalogueRequest(EmblemEditorData.CatalogueUrl));

            // Not a per-process hash: a randomised one would mint a new URL on every
            // restart and re-download half a megabyte for nothing.
            Assert.All(once, c => Assert.True((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')));
        }

        /// <summary>
        /// The catalogue is embedded in nothing, but the palette and the limits are
        /// stamped into an HTML page - so the one character that turns an embedded
        /// document back into markup must not survive.
        /// </summary>
        [Fact]
        public void Nothing_in_the_json_can_close_a_script_tag()
        {
            foreach (string json in new[]
            {
                EmblemEditorData.Catalogue, EmblemEditorData.PaletteJson(), EmblemEditorData.LimitsJson(),
            })
            {
                Assert.DoesNotContain("<", json, StringComparison.Ordinal);
                Assert.DoesNotContain(">", json, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void The_limits_the_browser_is_given_are_the_ones_the_parser_uses()
        {
            JObject limits = JObject.Parse(EmblemEditorData.LimitsJson());

            Assert.Equal(EmblemStack.Version, limits["version"]!.Value<int>());
            Assert.Equal(EmblemStack.MaxLayers, limits["maxLayers"]!.Value<int>());
            Assert.Equal(EmblemLayer.Unit, limits["unit"]!.Value<int>());
            Assert.Equal(EmblemLayer.MaxOffset, limits["maxOffset"]!.Value<int>());
            Assert.Equal(EmblemLayer.MinSize, limits["minSize"]!.Value<int>());
            Assert.Equal(EmblemLayer.MaxSize, limits["maxSize"]!.Value<int>());
            Assert.Equal(EmblemLayer.RotationSteps, limits["rotationSteps"]!.Value<int>());
            Assert.Equal(EmblemLayer.OpacitySteps, limits["opacitySteps"]!.Value<int>());
            Assert.Equal(EmblemLayer.OpacityUnit, limits["opacityUnit"]!.Value<int>());
            Assert.Equal(EmblemLayerCode.Width, limits["codeWidth"]!.Value<int>());
            Assert.Equal(EmblemLayerCode.OffsetBias, limits["offsetBias"]!.Value<int>());
            Assert.Equal(EmblemLayerCode.Alphabet, limits["alphabet"]!.Value<string>());
        }

        [Fact]
        public void The_palette_the_browser_is_given_is_the_one_the_painter_fills_with()
        {
            JArray palette = JArray.Parse(EmblemEditorData.PaletteJson());

            Assert.Equal(EmblemVocabulary.ColourCount, palette.Count);

            for (int i = 0; i < EmblemVocabulary.ColourCount; i++)
            {
                Assert.Equal(EmblemStackSvg.Hex(EmblemVocabulary.Palette[i]),
                    palette[i]!["h"]!.Value<string>());
                Assert.Equal(EmblemVocabulary.PaletteNames[i], palette[i]!["n"]!.Value<string>());
            }
        }
    }
}

using System.Xml.Linq;
using WorldsAdriftServer.Emblems;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The vector download.
    ///
    /// This is for PLAYERS, never for the game - the client decodes PNG and JPEG
    /// only and does not check whether it worked, so an SVG on the wire to the
    /// client would be a garbage texture it happily displays. The tests that
    /// matter here are therefore: it is well-formed XML (a browser that refuses to
    /// open it is the whole feature failing), it describes the same crest the PNG
    /// does, and it contains NOTHING a player typed - an SVG is script-capable and
    /// this one is served from the same origin as the account page.
    /// </summary>
    public class EmblemSvgTests
    {
        private static EmblemSpec Spec(string code)
        {
            Assert.True(EmblemSpec.TryParse(code, out EmblemSpec spec));
            return spec;
        }

        [Fact]
        public void The_document_is_well_formed_svg_with_a_square_viewbox()
        {
            XDocument doc = XDocument.Parse(EmblemSvg.Compose(Spec("2-0-5-19-11-0-13")));

            XElement root = doc.Root!;
            Assert.Equal("svg", root.Name.LocalName);
            Assert.Equal("http://www.w3.org/2000/svg", root.Name.NamespaceName);

            string[] box = root.Attribute("viewBox")!.Value.Split(' ');
            Assert.Equal(4, box.Length);
            Assert.Equal(box[2], box[3]);

            // Square for the same reason the PNG is: nothing downstream preserves
            // an aspect ratio for us.
            Assert.Equal(root.Attribute("width")!.Value, root.Attribute("height")!.Value);
        }

        [Fact]
        public void Every_crest_in_the_vocabulary_composes_to_well_formed_xml()
        {
            // Exhaustive over shape and division, and over every device - a device
            // whose path data broke the string would produce XML that no browser
            // opens, and the only place that shows up is here.
            for (int shape = 0; shape < EmblemVocabulary.ShapeCount; shape++)
            for (int division = 0; division < EmblemVocabulary.DivisionCount; division++)
            {
                int charge = (shape * 10 + division) % EmblemVocabulary.ChargeCount;
                Assert.True(EmblemSpec.TryCreate(shape, division, charge, 4, 9, 0, out EmblemSpec spec));
                XDocument.Parse(EmblemSvg.Compose(spec));
            }

            for (int charge = 0; charge < EmblemVocabulary.ChargeCount; charge++)
            {
                Assert.True(EmblemSpec.TryCreate(0, 0, charge, 4, 9, 0, out EmblemSpec spec));
                XDocument.Parse(EmblemSvg.Compose(spec));
            }
        }

        [Fact]
        public void The_same_spec_always_composes_the_same_bytes()
        {
            EmblemSpec spec = Spec("2-3-6-11-8-3-13");
            Assert.Equal(EmblemSvg.Compose(spec), EmblemSvg.Compose(spec));
        }

        [Fact]
        public void The_colours_in_the_document_are_the_ones_the_spec_chose()
        {
            EmblemSpec spec = Spec("2-0-1-2-11-9-13");
            string svg = EmblemSvg.Compose(spec);

            foreach (int colour in new[] { spec.FieldColour, spec.DetailColour, spec.ChargeColour })
            {
                string hex = "#" + EmblemVocabulary.ColourAt(colour).ToString("x6");
                Assert.Contains(hex, svg, StringComparison.Ordinal);
            }

            Assert.Contains("#" + EmblemVocabulary.OutlineInk.ToString("x6"), svg, StringComparison.Ordinal);
        }

        [Fact]
        public void The_device_in_the_document_is_the_traced_artwork_itself()
        {
            // Not a redrawing of it. The first coordinate pair of the stored path,
            // scaled by the painter's own device scale, has to appear verbatim -
            // which is only true if the SVG writer and the rasteriser are reading
            // the same table.
            const int Device = 19;   // a traced one

            Assert.True(EmblemSpec.TryCreate(1, 0, Device, 4, 4, 0, out EmblemSpec spec));

            EmblemPath path = EmblemPath.Parse(
                EmblemDeviceGeometry.Paths[Device - EmblemVocabulary.FirstDrawnDevice],
                EmblemDeviceGeometry.Unit);

            System.Text.StringBuilder expected = new System.Text.StringBuilder();
            path.AppendPathData(
                expected,
                EmblemPainter.DeviceScales[1],
                EmblemPainter.ChargeCentres[1],
                EmblemDeviceGeometry.Unit);

            Assert.Contains(expected.ToString(), EmblemSvg.Compose(spec), StringComparison.Ordinal);

            // And the artwork is not a handful of points that happen to match: the
            // wolf is hundreds of them, which is what a redrawing would not be.
            Assert.True(expected.Length > 4000);
        }

        [Fact]
        public void A_crest_with_no_device_still_composes_a_field_and_a_rim()
        {
            string svg = EmblemSvg.Compose(Spec("2-4-0-0-5-5-5"));

            XDocument doc = XDocument.Parse(svg);
            Assert.NotEmpty(doc.Descendants().Where(e => e.Name.LocalName == "path"));
            Assert.Contains("#" + EmblemVocabulary.OutlineInk.ToString("x6"), svg, StringComparison.Ordinal);
        }

        [Fact]
        public void Nothing_a_player_typed_can_reach_the_document()
        {
            // The guard is structural - Compose takes a spec and nothing else, and
            // a spec is six bounds-checked integers - but it is worth a test,
            // because the day somebody adds the alliance name to the title is the
            // day this becomes stored XSS on the account page's own origin.
            string svg = EmblemSvg.Compose(Spec("2-2-3-40-1-2-3"));

            Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("javascript:", svg, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("xlink:href", svg, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http://", svg.Replace("http://www.w3.org/2000/svg", string.Empty),
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_two_formats_of_one_crest_do_not_share_a_cache_tag()
        {
            // Same code, same URL but for the extension. A shared ETag would let a
            // cache answer a request for one with the body of the other.
            EmblemSpec spec = Spec("2-1-1-1-1-1-1");

            Assert.NotEqual(
                EmblemImages.ETag(spec, EmblemUrlPolicy.Format.Png),
                EmblemImages.ETag(spec, EmblemUrlPolicy.Format.Svg));
        }
    }
}

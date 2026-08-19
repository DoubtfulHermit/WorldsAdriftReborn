using System.Globalization;
using System.Text;

namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// The same crest as <see cref="EmblemPainter"/> draws, written out as SVG.
    ///
    /// WHO THIS IS FOR. Not the game - the game gets the PNG and always will,
    /// because <c>Texture2D.LoadImage</c> decodes PNG and JPEG and nothing else,
    /// and it does not check whether it succeeded: hand the client an SVG and it
    /// displays a garbage texture rather than failing. This is for the PLAYER, so
    /// a crest a leader designed can be pulled down as a vector, printed on a
    /// banner, dropped into a Discord header or recoloured, at any size.
    ///
    /// WHY IT CANNOT DRIFT FROM THE PNG. It reads the same
    /// <see cref="EmblemPath"/> objects the rasteriser samples - the shield
    /// outlines, the division regions, the device artwork - and it composes them
    /// in the same order, with the same constants. There is no second description
    /// of the heater's parabola or of the wolf here, and nothing in this file
    /// knows what any device IS. The one thing expressed twice is the top-lit
    /// shading, which is a formula in the painter and a pair of gradients here;
    /// they are the same formula, and the comment on
    /// <see cref="AppendShading"/> says how.
    ///
    /// NO PLAYER TEXT, EVER. An SVG is script-capable and this one is served from
    /// the same origin as the account page, so an alliance name inside it would be
    /// stored XSS with extra steps. Every byte below comes from the closed
    /// vocabulary; the only variable input is six small integers that have already
    /// been bounds-checked by <see cref="EmblemSpec"/>. Keep it that way.
    ///
    /// Pure: a spec in, a string out.
    /// </summary>
    internal static class EmblemSvg
    {
        /// <summary>
        /// The coordinate system: the painter's [-1, 1] box in thousandths.
        ///
        /// Thousandths and not, say, a 0-256 box because that is the unit the
        /// traced device table is already stored in - so at device scale 1 the
        /// numbers written here are the numbers the tracer produced, and the
        /// vector a player downloads is the vector the server rasterises rather
        /// than a second rounding of it.
        /// </summary>
        private const double Unit = 1000.0;

        /// <summary>The nominal pixel size, so a browser that ignores the viewBox
        /// still gets the size the PNG route serves.</summary>
        private const int NominalSize = EmblemPainter.Size;

        internal const string ContentType = "image/svg+xml; charset=utf-8";

        /// <summary>Renders the crest as a standalone SVG document.</summary>
        internal static string Compose(EmblemSpec spec)
        {
            int shape = (int)spec.Shape;

            string field = Hex(EmblemVocabulary.ColourAt(spec.FieldColour));
            string detail = Hex(EmblemVocabulary.ColourAt(spec.DetailColour));
            string charge = Hex(EmblemVocabulary.ColourAt(spec.ChargeColour));

            EmblemPath outline = EmblemGeometry.Shape(spec.Shape);

            StringBuilder svg = new StringBuilder(8192);

            svg.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"")
               .Append(-(int)Unit).Append(' ').Append(-(int)Unit).Append(' ')
               .Append((int)Unit * 2).Append(' ').Append((int)Unit * 2)
               .Append("\" width=\"").Append(NominalSize)
               .Append("\" height=\"").Append(NominalSize)
               .Append("\" role=\"img\" aria-label=\"Alliance crest\">\n");

            // A comment, not a <title> element with the alliance in it - see the
            // note on this class. The code is safe to state: it is digits and
            // hyphens by construction, and it is what makes the file reproducible.
            svg.Append("<!-- Worlds Adrift Reborn alliance crest ").Append(spec.ToCode())
               .Append(" -->\n");

            svg.Append("<defs>\n");

            svg.Append("<clipPath id=\"o\"><path d=\"");
            outline.AppendPathData(svg, 1.0, 0.0, Unit);
            svg.Append("\"/></clipPath>\n");

            svg.Append("<clipPath id=\"i\"><path d=\"");
            outline.AppendPathData(svg, EmblemPainter.OutlineInset, 0.0, Unit);
            svg.Append("\"/></clipPath>\n");

            AppendGradients(svg);

            svg.Append("</defs>\n");

            // The rim: the shield in ink, with everything else painted over the
            // inset copy of it. Same construction as the painter's - the band is
            // the shape minus a scaled copy, so its width follows the silhouette.
            svg.Append("<g clip-path=\"url(#o)\">\n");
            svg.Append("<path fill=\"").Append(Hex(EmblemVocabulary.OutlineInk)).Append("\" d=\"");
            outline.AppendPathData(svg, 1.0, 0.0, Unit);
            svg.Append("\"/>\n");

            svg.Append("<g clip-path=\"url(#i)\">\n");

            AppendField(svg, spec, outline, field, detail);
            AppendDevice(svg, spec, shape, charge);
            AppendShading(svg);

            svg.Append("</g>\n</g>\n</svg>\n");

            return svg.ToString();
        }

        /// <summary>
        /// The field and its division.
        ///
        /// Every division except the bordure is a fixed region that overhangs the
        /// shield and is cut back by the clip, which is why none of them has to
        /// know which shape it is on. The bordure is the exception in both
        /// renderers for the same reason: it is the shield minus a smaller copy of
        /// the shield, so it is painted the other way round - detail underneath,
        /// field on top of it.
        /// </summary>
        private static void AppendField(
            StringBuilder svg, EmblemSpec spec, EmblemPath outline, string field, string detail)
        {
            if (spec.Division == EmblemVocabulary.Division.Bordure)
            {
                AppendFill(svg, detail);
                svg.Append("<path fill=\"").Append(field).Append("\" d=\"");
                outline.AppendPathData(svg, EmblemGeometry.BordureInset, 0.0, Unit);
                svg.Append("\"/>\n");
                return;
            }

            AppendFill(svg, field);

            EmblemPath? region = EmblemGeometry.Division(spec.Division);
            if (region == null) return;

            svg.Append("<path fill=\"").Append(detail).Append("\" d=\"");
            region.AppendPathData(svg, 1.0, 0.0, Unit);
            svg.Append("\"/>\n");
        }

        /// <summary>
        /// The device, at the scale and vertical centre the painter puts it at.
        ///
        /// The keyline on a geometric device is drawn the way the painter tests
        /// for it: the device at full size in the keyline colour, then a
        /// nine-tenths copy in the device colour over it, which leaves exactly the
        /// band between the two inked. The traced devices get no keyline, for the
        /// reason set out in the painter.
        /// </summary>
        private static void AppendDevice(StringBuilder svg, EmblemSpec spec, int shape, string charge)
        {
            EmblemPath? device = EmblemGeometry.Device(spec.Charge);
            if (device == null) return;

            bool drawn = EmblemVocabulary.IsDrawnDevice(spec.Charge);
            double scale = drawn ? EmblemPainter.DeviceScales[shape] : EmblemPainter.ChargeScales[shape];
            double centre = EmblemPainter.ChargeCentres[shape];

            if (!drawn)
            {
                string keyline = Hex(EmblemPainter.KeylineColour(EmblemVocabulary.ColourAt(spec.ChargeColour)));

                svg.Append("<path fill=\"").Append(keyline).Append("\" d=\"");
                device.AppendPathData(svg, scale, centre, Unit);
                svg.Append("\"/>\n");

                // CLIPPED to the device, and that is not tidiness. The painter's
                // test is "inside the device AND inside a nine-tenths copy of it",
                // and for a device with a hole in it - the ring, the gear - the
                // shrunk copy escapes THROUGH the hole and covers ground the
                // full-size device never occupied. Painting it unclipped filled the
                // ring's bore with a band of device colour the PNG does not have.
                svg.Append("<clipPath id=\"c\"><path d=\"");
                device.AppendPathData(svg, scale, centre, Unit);
                svg.Append("\"/></clipPath>\n");

                svg.Append("<g clip-path=\"url(#c)\"><path fill=\"").Append(charge).Append("\" d=\"");
                device.AppendPathData(svg, scale * EmblemPainter.KeylineInset, centre, Unit);
                svg.Append("\"/></g>\n");
                return;
            }

            svg.Append("<path fill=\"").Append(charge).Append("\" d=\"");
            device.AppendPathData(svg, scale, centre, Unit);
            svg.Append("\"/>\n");
        }

        /// <summary>
        /// The top-lit gradient, as two overlays.
        ///
        /// The painter's shading is <c>k = -y * 0.12</c>, lightening toward white
        /// where k is positive and darkening toward black where it is negative -
        /// which is exactly compositing white at alpha k over the top half and
        /// black at alpha -k over the bottom. Two gradients rather than one
        /// white-to-black gradient because SVG interpolates colour and opacity
        /// separately, so a single stop list would run through translucent grey at
        /// the middle instead of through nothing at all.
        /// </summary>
        private static void AppendShading(StringBuilder svg)
        {
            AppendRect(svg, "url(#l)");
            AppendRect(svg, "url(#d)");
        }

        private static void AppendGradients(StringBuilder svg)
        {
            string strength = EmblemPainter.ShadeStrength.ToString("0.###", CultureInfo.InvariantCulture);

            svg.Append("<linearGradient id=\"l\" gradientUnits=\"userSpaceOnUse\" x1=\"0\" y1=\"")
               .Append(-(int)Unit).Append("\" x2=\"0\" y2=\"0\">")
               .Append("<stop offset=\"0\" stop-color=\"#ffffff\" stop-opacity=\"").Append(strength)
               .Append("\"/><stop offset=\"1\" stop-color=\"#ffffff\" stop-opacity=\"0\"/>")
               .Append("</linearGradient>\n");

            svg.Append("<linearGradient id=\"d\" gradientUnits=\"userSpaceOnUse\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"")
               .Append((int)Unit).Append("\">")
               .Append("<stop offset=\"0\" stop-color=\"#000000\" stop-opacity=\"0\"/>")
               .Append("<stop offset=\"1\" stop-color=\"#000000\" stop-opacity=\"").Append(strength)
               .Append("\"/></linearGradient>\n");
        }

        private static void AppendFill(StringBuilder svg, string colour) => AppendRect(svg, colour);

        private static void AppendRect(StringBuilder svg, string fill)
        {
            svg.Append("<rect x=\"").Append(-(int)Unit).Append("\" y=\"").Append(-(int)Unit)
               .Append("\" width=\"").Append((int)Unit * 2).Append("\" height=\"").Append((int)Unit * 2)
               .Append("\" fill=\"").Append(fill).Append("\"/>\n");
        }

        private static string Hex(int colour) =>
            "#" + colour.ToString("x6", CultureInfo.InvariantCulture);
    }
}

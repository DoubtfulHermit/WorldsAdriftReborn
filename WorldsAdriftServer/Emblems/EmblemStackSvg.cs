using System.Globalization;
using System.Text;

namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// The same emblem <see cref="EmblemStackPainter"/> rasterises, written out as
    /// SVG - and, more importantly, the DEFINITION of what the browser draws while
    /// a player is dragging a layer around.
    ///
    /// THIS IS WHERE THE PREVIEW AND THE SERVER RENDER ARE HELD TOGETHER. The
    /// editor cannot round-trip a 256-pixel PNG to the server on every mouse move,
    /// so the live picture is drawn in the browser - and two descriptions of one
    /// picture drift, which this repository has already paid for once. Four things
    /// stop it here:
    /// <list type="number">
    /// <item>THE SHAPES are not described twice. The page is handed the very path
    ///   data this file emits, straight out of the same <see cref="EmblemPath"/>
    ///   objects the rasteriser samples (see <see cref="EmblemEditorData"/>), so
    ///   there is no second wolf, no second heater and no second circle anywhere;</item>
    /// <item>THE PALETTE and the code's units are stamped into the script by the
    ///   server, so the browser cannot be working in a different space;</item>
    /// <item>THE MARKUP for one layer is built by <see cref="AppendLayer"/> here
    ///   and by a marked mirror in the page's script, both from integers only, and
    ///   <c>EmblemLayerMirrorTests</c> runs the REAL script the server serves in a
    ///   JavaScript engine and asserts the two produce BYTE-IDENTICAL strings for
    ///   a corpus that includes every extreme the vocabulary allows. Not "agree to
    ///   six decimals" - the same bytes;</item>
    /// <item>and the editor SNAPS BACK to the server's own PNG a moment after the
    ///   last change, so if the two ever did disagree the crest would visibly jump
    ///   in front of the person editing it, rather than being wrong only in game.</item>
    /// </list>
    ///
    /// NO PLAYER TEXT, EVER - same rule as <see cref="EmblemSvg"/>. An SVG is
    /// script-capable and served from the account page's origin, so every byte
    /// below comes from the closed vocabulary and from integers that
    /// <see cref="EmblemLayer"/> has already bounds-checked.
    ///
    /// Pure: a stack in, a string out.
    /// </summary>
    internal static class EmblemStackSvg
    {
        /// <summary>The coordinate system: the painter's [-1, 1] box in
        /// thousandths, which is both the unit the traced device table is stored in
        /// and the unit an <see cref="EmblemLayer"/>'s position is expressed in. So
        /// a layer at x = 500 is written at SVG x = 500, with no conversion to get
        /// wrong.</summary>
        internal const double Unit = EmblemObjects.Unit;

        private const int NominalSize = EmblemPainter.Size;

        internal const string ContentType = EmblemSvg.ContentType;

        /// <summary>Renders the emblem as a standalone SVG document.</summary>
        internal static string Compose(EmblemStack stack)
        {
            if (stack == null) throw new ArgumentNullException(nameof(stack));

            StringBuilder svg = new StringBuilder(8192);

            svg.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"")
               .Append(-(int)Unit).Append(' ').Append(-(int)Unit).Append(' ')
               .Append((int)Unit * 2).Append(' ').Append((int)Unit * 2)
               .Append("\" width=\"").Append(NominalSize)
               .Append("\" height=\"").Append(NominalSize)
               .Append("\" role=\"img\" aria-label=\"Alliance emblem\">\n");

            // The code, as a comment. Safe to state - it is digits and the code
            // alphabet by construction - and it is what makes the file
            // reproducible from the URL it came from.
            svg.Append("<!-- Worlds Adrift Reborn alliance emblem ").Append(stack.ToCode())
               .Append(" -->\n");

            // A CLIP, not a background. The canvas is transparent by design, but a
            // layer is allowed to sit out to twice the box so it can read as a
            // band rather than a floating blob - and without this, a downloaded
            // vector would show the part of it that the PNG rasteriser crops.
            svg.Append("<clipPath id=\"b\"><rect x=\"").Append(-(int)Unit)
               .Append("\" y=\"").Append(-(int)Unit)
               .Append("\" width=\"").Append((int)Unit * 2)
               .Append("\" height=\"").Append((int)Unit * 2).Append("\"/></clipPath>\n");

            svg.Append("<g clip-path=\"url(#b)\">\n");

            foreach (EmblemLayer layer in stack.Layers) AppendLayer(svg, layer);

            svg.Append("</g>\n</svg>\n");

            return svg.ToString();
        }

        /// <summary>
        /// ONE LAYER, and the exact string the browser's mirror must also produce.
        ///
        /// Bottom of the stack first, painted over by whatever comes after, which
        /// is plain source-over compositing and is the same result
        /// <see cref="EmblemStackPainter"/> reaches by walking the other way.
        ///
        /// Every number is an integer or is built from one by
        /// <see cref="EmblemLayer.Thousandths"/>. There is no floating-point
        /// formatting anywhere in this path, which is what makes "identical bytes"
        /// a property rather than a hope.
        /// </summary>
        internal static void AppendLayer(StringBuilder svg, EmblemLayer layer)
        {
            EmblemPath? path = layer.Path;
            if (path == null) return;

            svg.Append("<g transform=\"");
            layer.AppendTransform(svg);
            svg.Append("\"><path fill=\"").Append(Hex(EmblemVocabulary.ColourAt(layer.Colour)))
               .Append("\" fill-opacity=\"").Append(layer.FillOpacity())
               .Append("\" d=\"");
            path.AppendPathData(svg, 1.0, 0.0, Unit);
            svg.Append("\"/></g>\n");
        }

        /// <summary>The markup for one layer on its own - what the mirror test
        /// compares against, and what the editor's preview builds one of per
        /// layer.</summary>
        internal static string LayerMarkup(EmblemLayer layer)
        {
            StringBuilder svg = new StringBuilder(512);
            AppendLayer(svg, layer);
            return svg.ToString();
        }

        internal static string Hex(int colour) =>
            "#" + colour.ToString("x6", CultureInfo.InvariantCulture);
    }
}

using System.Globalization;
using System.Text;
using WorldsAdriftServer.Emblems;
using WorldsAdriftServer.Portal;

namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The layered emblem editor's markup: three columns, an object catalogue, a
    /// canvas and a layers panel.
    ///
    /// WHAT IT IS. Worlds Adrift's retail emblem editor let a player stack up to
    /// twenty coloured silhouettes, move, scale, turn, mirror and fade each one,
    /// lock the ones they had finished with, and submit the result as their
    /// alliance's mark. This is that, in this console's clothes rather than in
    /// 2016 Flash-era chrome, and it replaces a builder that offered one shape,
    /// one pattern, one device and three colours.
    ///
    /// THIS FILE IS MARKUP AND NOTHING ELSE. It asks no permission question - the
    /// booleans arrive on <see cref="AllianceRights"/>, decided once against the
    /// ledger the handler will re-check the post against - and it computes no
    /// geometry. The shapes, the palette and the units all reach the browser from
    /// <see cref="EmblemEditorData"/>, which reads the same tables the server's
    /// rasteriser does.
    ///
    /// THE FORM POSTS ONE FIELD. Twenty layers of eight numbers each is a design
    /// whose LAYER ORDER is data, and an HTML form does not promise the order of
    /// its fields; the emblem code already carries all of it in one canonical
    /// string, so that string is what is posted. It is a visible textarea rather
    /// than a hidden input on purpose - see <see cref="AppendCodeBox"/>.
    ///
    /// A NOTE ON WHAT WAS NOT BUILT. Retail's save menu also offered "use as your
    /// profile picture" and "save to your gallery". This server has no profile
    /// pictures at all, and a personal gallery would need somewhere per-account to
    /// keep designs - which is a schema migration, and a migration means the game
    /// server and the login server must be deployed together or persistence
    /// silently stops. Neither is built. The design code is the substitute: it is
    /// the whole design, it is short enough to paste into a chat message, and
    /// pasting one back loads it.
    /// </summary>
    internal static class AccountEmblemEditor
    {
        private const string Action = "/account/alliance-emblem";

        internal static void Append(StringBuilder page, PortalView view)
        {
            bool any = false;

            foreach (CharacterCard card in view.Characters)
            {
                if (card.Alliance == null) continue;

                any = true;
                AppendOne(page, view.Csrf, card, card.Alliance);
            }

            if (!any)
            {
                page.Append(@"  <section class=""card empty"">
    <h2>Emblem</h2>
    <p>An emblem belongs to an alliance, and none of your characters is in one yet.</p>
  </section>
");
            }
        }

        private static void AppendOne(
            StringBuilder page, string csrf, CharacterCard card, AllianceCard alliance)
        {
            page.Append("  <section class=\"card editor-card\" id=\"e")
                .Append(alliance.AllianceId.ToString("N", CultureInfo.InvariantCulture)).Append("\">\n");
            page.Append("    <h2>").Append(AdminPage.HtmlEncode(alliance.Name)).Append("</h2>\n");
            page.Append("    <p class=\"as\">acting as <b>")
                .Append(AdminPage.HtmlEncode(card.Sheet.Name)).Append("</b></p>\n");

            if (!alliance.Rights.EditEmblem)
            {
                AppendReadOnly(page, alliance);
                page.Append("  </section>\n");
                return;
            }

            if (alliance.ExternalEmblemUrl != null)
            {
                page.Append("    <p class=\"notice\">This alliance currently wears an image an "
                    + "operator set by hand (<code>")
                    .Append(AdminPage.HtmlEncode(alliance.ExternalEmblemUrl))
                    .Append("</code>). Saving an emblem below replaces it.</p>\n");
            }

            page.Append("    <form method=\"post\" action=\"").Append(Action)
                .Append("\" class=\"editor\" data-emblem>\n      ");
            AccountPage.Csrf(page, csrf);
            AccountPage.Hidden(page, EmblemFormPolicy.AllianceField,
                alliance.AllianceId.ToString("D", CultureInfo.InvariantCulture));
            AccountPage.Hidden(page, EmblemFormPolicy.CharacterField,
                alliance.ActingCharacterUid.ToString("D", CultureInfo.InvariantCulture));
            page.Append('\n');

            page.Append("      <noscript><p class=\"locked\">The editor needs JavaScript to draw "
                + "and drag layers. Without it you can still paste a design code below and save "
                + "it, and the picture beside it is the one the game downloads.</p></noscript>\n");

            page.Append("      <div class=\"cols\">\n");
            AppendObjects(page);
            AppendCanvas(page, alliance);
            AppendLayers(page);
            page.Append("      </div>\n");

            AppendCodeBox(page, alliance);
            AppendFoot(page, alliance);

            page.Append("    </form>\n");
            page.Append("  </section>\n");
        }

        // ------------------------------------------------------------ read-only

        private static void AppendReadOnly(StringBuilder page, AllianceCard alliance)
        {
            page.Append("    <div class=\"stage lone\"><img class=\"preview\" "
                + "alt=\"Alliance emblem\" src=\"")
                .Append(AdminPage.HtmlEncode(EmblemUrlPolicy.PreviewUrl(alliance.Emblem)))
                .Append("\"></div>\n");

            page.Append("    <p class=\"row-of-links\"><a class=\"vector\" download href=\"")
                .Append(AdminPage.HtmlEncode(
                    EmblemUrlPolicy.VectorUrl(alliance.AllianceId, alliance.Emblem)))
                .Append("\">Download as SVG</a>");
            AppendRasterLinks(page, alliance.AllianceId, alliance.Emblem, live: false);
            page.Append("</p>\n");

            page.Append("    <p class=\"hint\">The vector is the same emblem as line art and scales "
                + "to any size; a PNG is a plain picture, for anywhere that will not take a "
                + "vector.</p>\n");

            if (!alliance.Rights.Nothing)
            {
                page.Append("    <p class=\"locked\">Changing the emblem needs a rank that grants "
                    + "<code>").Append(AccountPage.AlliancePermissionName(PortalAction.EditEmblem))
                    .Append("</code>.</p>\n");
            }
        }

        // --------------------------------------------------------- the catalogue

        /// <summary>
        /// The object panel.
        ///
        /// EMPTY IN THE MARKUP, and filled from
        /// <see cref="EmblemEditorData.CatalogueUrl"/>. The catalogue is several
        /// hundred kilobytes of traced coordinates; inlining it would put that in
        /// front of a player on every load of this tab rather than once, forever,
        /// in their browser cache. The CATEGORIES are stamped in here so the panel
        /// has its shape before the fetch lands.
        /// </summary>
        private static void AppendObjects(StringBuilder page)
        {
            page.Append("        <div class=\"pane objects\">\n");
            page.Append("          <h3>Objects</h3>\n");

            page.Append("          <div class=\"cats\" data-cats role=\"group\" aria-label=\"Object groups\">");
            string[] categories =
            {
                EmblemObjects.ShapeCategory, EmblemObjects.DeviceCategory, EmblemObjects.ShieldCategory,
            };
            for (int i = 0; i < categories.Length; i++)
            {
                page.Append("<button type=\"button\" class=\"cat")
                    .Append(i == 0 ? " on" : string.Empty)
                    .Append("\" data-cat=\"").Append(AdminPage.HtmlEncode(categories[i]))
                    .Append("\" aria-pressed=\"").Append(i == 0 ? "true" : "false").Append("\">")
                    .Append(AdminPage.HtmlEncode(categories[i])).Append("</button>");
            }
            page.Append("</div>\n");

            page.Append("          <label class=\"find\"><span>Find an object</span>"
                + "<input type=\"search\" data-find autocomplete=\"off\" "
                + "placeholder=\"wolf, star, bar\"></label>\n");

            page.Append("          <div class=\"objgrid\" data-objects>"
                + "<p class=\"waiting\">Loading the object catalogue&hellip;</p></div>\n");
            page.Append("        </div>\n");
        }

        // ------------------------------------------------------------- the canvas

        /// <summary>
        /// The canvas, the flips, the palette and the opacity slider.
        ///
        /// TWO PICTURES SIT ON TOP OF EACH OTHER HERE, and that is the whole
        /// answer to "how do you know the preview matches what the game gets". The
        /// SVG underneath is drawn in the browser from the server's own path data
        /// and redraws as you drag. The image on top is
        /// <c>/alliance-emblem/preview.png</c> - the real renderer, the same bytes
        /// the game downloads - and it is swapped in a moment after you stop
        /// moving. If the two ever disagreed, the emblem would visibly change in
        /// front of the person editing it, which is a bug that reports itself
        /// rather than one that only exists in game.
        /// </summary>
        private static void AppendCanvas(StringBuilder page, AllianceCard alliance)
        {
            page.Append("        <div class=\"pane canvas\">\n");

            page.Append("          <div class=\"canvas-head\"><h3>Canvas</h3>\n");
            page.Append("            <div class=\"flips\">"
                + "<button type=\"button\" class=\"quiet\" data-flip=\"x\">Flip X</button>"
                + "<button type=\"button\" class=\"quiet\" data-flip=\"y\">Flip Y</button></div>\n");
            page.Append("          </div>\n");

            page.Append("          <div class=\"stage\" data-stage>\n");
            page.Append("            <svg class=\"live\" data-live viewBox=\"-1000 -1000 2000 2000\" "
                + "role=\"img\" aria-label=\"Emblem canvas\"></svg>\n");
            page.Append("            <img class=\"served\" data-served alt=\"The emblem as the game "
                + "downloads it\" src=\"")
                .Append(AdminPage.HtmlEncode(EmblemUrlPolicy.PreviewUrl(alliance.Emblem)))
                .Append("\">\n");

            // THE SELECTION SITS ABOVE BOTH PICTURES, in its own layer. It has to:
            // the server's render is opaque once it settles, so a box drawn with
            // the live vectors underneath would vanish the moment a design stopped
            // moving - which is exactly when a player is looking at what they have
            // selected. Neither picture takes a pointer, so a click still finds the
            // shape it landed on.
            page.Append("            <svg class=\"overlay\" data-overlay "
                + "viewBox=\"-1000 -1000 2000 2000\" aria-hidden=\"true\"></svg>\n");
            page.Append("          </div>\n");

            page.Append("          <p class=\"hint\" data-hint>Pick an object on the left to add a "
                + "layer. Drag it to move, use the corner handle to resize and the top handle to "
                + "turn it; the arrow keys nudge it.</p>\n");

            page.Append("          <div class=\"palette\" data-palette role=\"group\" "
                + "aria-label=\"Layer colour\"></div>\n");

            page.Append("          <label class=\"opacity\"><span>Opacity <b data-opacity-value>"
                + "100%</b></span><input type=\"range\" data-opacity min=\"0\" max=\"")
                .Append(EmblemLayer.OpacitySteps.ToString(CultureInfo.InvariantCulture))
                .Append("\" value=\"").Append(EmblemLayer.OpacitySteps.ToString(CultureInfo.InvariantCulture))
                .Append("\" step=\"1\"></label>\n");

            page.Append("        </div>\n");
        }

        // ------------------------------------------------------------- the layers

        private static void AppendLayers(StringBuilder page)
        {
            page.Append("        <div class=\"pane layers\">\n");
            page.Append("          <div class=\"layers-head\"><h3>Layers</h3>"
                + "<span class=\"count\" data-count>0 / ")
                .Append(EmblemStack.MaxLayers.ToString(CultureInfo.InvariantCulture))
                .Append("</span></div>\n");

            page.Append("          <button type=\"button\" class=\"quiet danger\" data-delete-all>"
                + "Delete all</button>\n");

            page.Append("          <ol class=\"layerlist\" data-layers></ol>\n");
            page.Append("          <p class=\"hint\">Drag a row to bring a layer forward or send it "
                + "back. A locked layer can still be reordered and cloned.</p>\n");
            page.Append("        </div>\n");
        }

        // -------------------------------------------------------------- the code

        /// <summary>
        /// The design, as the one field the form posts.
        ///
        /// A VISIBLE TEXTAREA, not a hidden input, and it earns that three times
        /// over: it is the field the form posts, so a browser with no script can
        /// still paste a design and save it; it is how a player keeps a design
        /// this server has nowhere to store one (see the note on this class); and
        /// it is the only thing on the page that tells somebody what an emblem
        /// actually IS, which is a short string rather than a file.
        /// </summary>
        private static void AppendCodeBox(StringBuilder page, AllianceCard alliance)
        {
            page.Append("      <details class=\"codebox\">\n");
            page.Append("        <summary>Design code</summary>\n");
            page.Append("        <p class=\"hint\">The whole emblem, as text. Copy it to keep a "
                + "design, or paste one in and load it &mdash; this is also what gets posted when "
                + "you save.</p>\n");
            page.Append("        <textarea name=\"").Append(EmblemFormPolicy.DesignField)
                .Append("\" data-code rows=\"3\" spellcheck=\"false\" autocomplete=\"off\" "
                + "maxlength=\"")
                .Append(EmblemArtwork.MaxCodeLength.ToString(CultureInfo.InvariantCulture))
                .Append("\">").Append(AdminPage.HtmlEncode(alliance.Emblem.ToCode()))
                .Append("</textarea>\n");
            page.Append("        <button type=\"button\" class=\"quiet\" data-apply-code>"
                + "Load this code</button>\n");
            page.Append("      </details>\n");
        }

        private static void AppendFoot(StringBuilder page, AllianceCard alliance)
        {
            page.Append("      <div class=\"foot\">\n");
            page.Append("        <button type=\"button\" class=\"quiet\" data-undo>Undo changes"
                + "</button>\n");
            page.Append("        <button type=\"submit\" class=\"plank\" data-save>Save emblem"
                + "</button>\n");
            page.Append("      </div>\n");

            // The save menu. Shown by the script in place of submitting straight
            // away; with no script the button above just posts, which is the only
            // destination that exists anyway.
            page.Append("      <div class=\"savesheet\" data-savesheet hidden>\n");
            page.Append("        <h3>Save emblem</h3>\n");
            page.Append("        <div class=\"stage small\"><img data-savepreview alt=\"\" src=\"")
                .Append(AdminPage.HtmlEncode(EmblemUrlPolicy.PreviewUrl(alliance.Emblem)))
                .Append("\"></div>\n");
            page.Append("        <button type=\"submit\" class=\"plank\">Submit as the alliance "
                + "emblem</button>\n");
            page.Append("        <p class=\"hint\">Everyone in <b>")
                .Append(AdminPage.HtmlEncode(alliance.Name))
                .Append("</b> wears it. It appears in game the next time the alliance panel "
                + "loads.</p>\n");
            page.Append("        <p class=\"row-of-links\">"
                + "<a class=\"vector\" download data-savevector href=\"")
                .Append(AdminPage.HtmlEncode(
                    EmblemUrlPolicy.VectorUrl(alliance.AllianceId, alliance.Emblem)))
                .Append("\">Download as SVG</a>");
            AppendRasterLinks(page, alliance.AllianceId, alliance.Emblem, live: true);
            page.Append("<button type=\"button\" class=\"quiet\" data-copycode>Copy the design code</button>"
                + "<button type=\"button\" class=\"quiet\" data-savecancel>Back to the editor</button>"
                + "</p>\n");
            page.Append("        <p class=\"hint\">Retail also offered a profile picture and a "
                + "personal gallery. This server has neither, so the design code is how you keep "
                + "an emblem you are not ready to submit.</p>\n");
            page.Append("      </div>\n");
        }

        /// <summary>
        /// "Download as PNG" and one link per size the route renders.
        ///
        /// THREE LINKS RATHER THAN A PICKER, because three plain links work with
        /// no script at all - the same standard the design-code textarea is held
        /// to on this page - while a select plus a button would need script to
        /// mean anything, and would hide two of the three answers behind a click.
        ///
        /// THE SIZES ARE NOT WRITTEN HERE. They come from
        /// <see cref="EmblemUrlPolicy.DownloadSizes"/>, which is the same list the
        /// handler validates against, so the page cannot offer a size the server
        /// would refuse to render. The size also goes into a data attribute:
        /// <paramref name="live"/> markup is re-pointed at the design being edited
        /// by emblem-editor.js as the canvas changes, and it reads the size back
        /// off the link rather than carrying a second copy of this list in
        /// JavaScript.
        /// </summary>
        private static void AppendRasterLinks(
            StringBuilder page, Guid allianceId, EmblemArtwork artwork, bool live)
        {
            page.Append("<span class=\"raster\">Download as PNG");

            foreach (int size in EmblemUrlPolicy.DownloadSizes)
            {
                string pixels = size.ToString(CultureInfo.InvariantCulture);

                page.Append("<a class=\"px\" download")
                    .Append(live ? " data-savepng=\"" + pixels + "\"" : string.Empty)
                    .Append(" href=\"")
                    .Append(AdminPage.HtmlEncode(
                        EmblemUrlPolicy.RasterUrl(allianceId, artwork, size)))
                    .Append("\" aria-label=\"Download as PNG, ").Append(pixels)
                    .Append(" pixels\">").Append(pixels).Append("</a>");
            }

            page.Append("</span>");
        }
    }
}

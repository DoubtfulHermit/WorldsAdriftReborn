using System.Globalization;
using System.Text;
using WorldsAdriftServer.Emblems;

namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The signed-in player's account page, and the alliance emblem builder that
    /// is currently the only thing on it.
    ///
    /// WHY THE BUILDER LIVES ON THE WEBSITE AND NOT IN THE GAME. It has to. The
    /// retail client has no emblem control of any kind - three input fields on the
    /// create-alliance panel (name, description, MOTD) and no fourth - and adding
    /// one would mean shipping a client mod. What the client DOES do is fetch and
    /// display whatever image <c>emblemUrl</c> points at, unauthenticated, with no
    /// idea where it came from. So the composer can live anywhere, and the place
    /// it costs nothing is a page the player is already signed in to.
    ///
    /// THE PREVIEW IS THE REAL RENDERER. The picture next to the controls is an
    /// <c>&lt;img&gt;</c> pointed at <c>/alliance-emblem/preview.png</c> - the same
    /// route, the same <see cref="EmblemPainter"/>, the same bytes the game will
    /// get. It is deliberately NOT a canvas or an inline SVG drawn from the same
    /// options: a second renderer of one picture drifts from the first, silently,
    /// and this repository has already bought that lesson once with the map mirror
    /// (which now needs a 1e-9 parity test to hold two implementations together).
    /// The only thing the page's script computes is the twelve-character code -
    /// string concatenation, not drawing.
    ///
    /// Every value stamped in is HTML-encoded through
    /// <see cref="AdminPage.HtmlEncode"/>, the escaper the rest of the console
    /// uses, so an alliance name or a character name cannot break out of the
    /// markup.
    /// </summary>
    internal static class AccountPage
    {
        internal const string ContentType = "text/html; charset=utf-8";

        /// <summary>One alliance this account may re-crest, and who does it.</summary>
        internal sealed record Target(
            Guid AllianceId,
            string AllianceName,
            Guid CharacterUid,
            string CharacterName,
            EmblemSpec Spec,
            bool IsBuilt,
            string? ExternalUrl);

        /// <summary>
        /// Renders the page.
        ///
        /// <paramref name="targets"/> is every alliance the signed-in account may
        /// change the emblem of - normally one, but an account has up to five
        /// characters and nothing stops two of them leading different alliances,
        /// so the page loops rather than assuming.
        /// </summary>
        internal static string Render(
            string username, string csrf, IReadOnlyList<Target> targets, string? notice, bool noticeIsError)
        {
            StringBuilder body = new StringBuilder();

            if (notice != null)
            {
                body.Append("  <p class=\"notice")
                    .Append(noticeIsError ? " bad" : " good")
                    .Append("\">")
                    .Append(AdminPage.HtmlEncode(notice))
                    .Append("</p>\n");
            }

            if (targets.Count == 0)
            {
                body.Append(@"  <section class=""card empty"">
    <h2>Alliance crest</h2>
    <p>Nothing to set here yet. The crest builder appears once one of your
    characters founds an alliance, or is given a rank that may edit the
    alliance's details.</p>
  </section>
");
            }

            foreach (Target target in targets)
            {
                AppendBuilder(body, csrf, target);
            }

            return Shell(username, body.ToString());
        }

        private static void AppendBuilder(StringBuilder page, string csrf, Target target)
        {
            string id = target.AllianceId.ToString("D", CultureInfo.InvariantCulture);
            string safeId = "a" + id.Replace("-", string.Empty, StringComparison.Ordinal);

            page.Append("  <section class=\"card\">\n");
            page.Append("    <h2>").Append(AdminPage.HtmlEncode(target.AllianceName)).Append("</h2>\n");
            page.Append("    <p class=\"as\">as <b>")
                .Append(AdminPage.HtmlEncode(target.CharacterName))
                .Append("</b></p>\n");

            if (target.ExternalUrl != null)
            {
                page.Append("    <p class=\"notice\">This alliance currently wears an image an operator set by hand (<code>")
                    .Append(AdminPage.HtmlEncode(target.ExternalUrl))
                    .Append("</code>). Saving a crest below replaces it.</p>\n");
            }

            page.Append("    <form method=\"post\" action=\"/account/alliance-emblem\" class=\"builder\" id=\"f")
                .Append(safeId).Append("\">\n");
            page.Append("      <input type=\"hidden\" name=\"").Append(PlayerAuthPolicy.CsrfField)
                .Append("\" value=\"").Append(AdminPage.HtmlEncode(csrf)).Append("\">\n");
            page.Append("      <input type=\"hidden\" name=\"").Append(EmblemFormPolicy.AllianceField)
                .Append("\" value=\"").Append(AdminPage.HtmlEncode(id)).Append("\">\n");
            page.Append("      <input type=\"hidden\" name=\"").Append(EmblemFormPolicy.CharacterField)
                .Append("\" value=\"")
                .Append(AdminPage.HtmlEncode(target.CharacterUid.ToString("D", CultureInfo.InvariantCulture)))
                .Append("\">\n");

            page.Append("      <div class=\"stage\">\n");
            page.Append("        <img class=\"preview\" alt=\"Alliance crest preview\" src=\"")
                .Append(AdminPage.HtmlEncode(EmblemUrlPolicy.PreviewUrl(target.Spec)))
                .Append("\">\n");
            page.Append("        <p class=\"hint\">This is the picture the game downloads &mdash; it is drawn by the server, not by this page.</p>\n");

            // The vector of the same crest. The game never sees this - it decodes
            // PNG and JPEG only - but a leader who wants their alliance's mark on a
            // banner, a sticker or a Discord header should not have to screenshot a
            // 256-pixel square to get it.
            page.Append("        <p class=\"hint\"><a class=\"vector\" download href=\"")
                .Append(AdminPage.HtmlEncode(EmblemUrlPolicy.VectorUrl(target.AllianceId, target.Spec)))
                .Append("\">Download as SVG</a> &mdash; the same crest as vector art, at any size.</p>\n");
            page.Append("      </div>\n");

            page.Append("      <div class=\"controls\">\n");

            AppendSelect(page, "Shape", EmblemFormPolicy.ShapeField,
                EmblemVocabulary.ShapeNames, (int)target.Spec.Shape);
            AppendSelect(page, "Field pattern", EmblemFormPolicy.DivisionField,
                EmblemVocabulary.DivisionNames, (int)target.Spec.Division);
            AppendSelect(page, "Device", EmblemFormPolicy.ChargeField,
                EmblemVocabulary.ChargeNames, (int)target.Spec.Charge);

            AppendSwatches(page, "Field colour", EmblemFormPolicy.FieldColourField,
                target.Spec.FieldColour, safeId);
            AppendSwatches(page, "Pattern colour", EmblemFormPolicy.DetailColourField,
                target.Spec.DetailColour, safeId);
            AppendSwatches(page, "Device colour", EmblemFormPolicy.ChargeColourField,
                target.Spec.ChargeColour, safeId);

            page.Append("      </div>\n");
            page.Append("      <button class=\"plank\" type=\"submit\">Save crest</button>\n");
            page.Append("    </form>\n");
            page.Append("  </section>\n");
        }

        private static void AppendSelect(
            StringBuilder page, string label, string name, IReadOnlyList<string> options, int selected)
        {
            page.Append("        <label class=\"row\"><span>").Append(AdminPage.HtmlEncode(label))
                .Append("</span>\n          <select name=\"").Append(name).Append("\">\n");

            for (int i = 0; i < options.Count; i++)
            {
                page.Append("            <option value=\"")
                    .Append(i.ToString(CultureInfo.InvariantCulture))
                    .Append('"');
                if (i == selected) page.Append(" selected");
                page.Append('>').Append(AdminPage.HtmlEncode(options[i])).Append("</option>\n");
            }

            page.Append("          </select>\n        </label>\n");
        }

        /// <summary>
        /// A colour picked from the palette as a grid of radio buttons.
        ///
        /// Radios rather than an <c>&lt;input type=color&gt;</c> because the value
        /// is an INDEX, not a colour: the palette is closed on purpose (see
        /// <see cref="EmblemVocabulary"/>), and a free colour picker would both
        /// widen the input and let a player choose the one value that makes their
        /// own crest illegible. Radios also degrade to something usable with no
        /// script at all, which the whole form does.
        /// </summary>
        private static void AppendSwatches(
            StringBuilder page, string label, string name, int selected, string formId)
        {
            page.Append("        <fieldset class=\"row swatches\"><legend>")
                .Append(AdminPage.HtmlEncode(label)).Append("</legend>\n");

            for (int i = 0; i < EmblemVocabulary.ColourCount; i++)
            {
                string hex = "#" + EmblemVocabulary.Palette[i].ToString("x6", CultureInfo.InvariantCulture);

                page.Append("          <label class=\"sw\" title=\"")
                    .Append(AdminPage.HtmlEncode(EmblemVocabulary.PaletteNames[i]))
                    .Append("\" style=\"--sw:").Append(hex).Append("\">")
                    .Append("<input type=\"radio\" name=\"").Append(name)
                    .Append("\" value=\"").Append(i.ToString(CultureInfo.InvariantCulture)).Append('"');
                if (i == selected) page.Append(" checked");
                page.Append("><span></span></label>\n");
            }

            page.Append("        </fieldset>\n");
        }

        private static string Shell(string username, string body)
        {
            string name = AdminPage.HtmlEncode(username);

            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark"">
<title>Your account - Worlds Adrift Reborn</title>
<style>
:root {
  --ink:        #26313d;
  --ink-soft:   #43525f;
  --ink-faint:  #5d6b76;
  --field:      rgba(74, 80, 96, .60);
  --field-edge: rgba(30, 36, 48, .30);
  --field-ink:  #f0ece2;
  --timber-lo:  #c68d60;
  --timber-mid: #d9a074;
  --timber-hi:  #eebd8e;
  --timber-ink: #4a2c14;
  --batten:     #a97244;
  --batten-lo:  #8e5d36;
  --batten-edge:#7d4d2a;
  --rust:       #a8321f;
  --good:       #2c6b52;
  --veil:       rgba(255, 255, 255, .40);
}
@media (prefers-color-scheme: dark) {
  :root {
    --ink:       #e4e9ec;
    --ink-soft:  #b3c0c8;
    --ink-faint: #8b99a3;
    --field:     rgba(96, 106, 124, .40);
    --field-edge:rgba(180, 200, 215, .16);
    --rust:      #ef8a6b;
    --good:      #7fd2b3;
    --veil:      rgba(6, 12, 20, .52);
  }
}
* { box-sizing: border-box; }
body {
  margin: 0; min-height: 100vh; padding: 2.5rem 1.25rem 3rem;
  color: var(--ink);
  background: linear-gradient(180deg, #93b7c8, #bed2d8 55%, #dde7e2);
  font-family: 'Inter', 'Segoe UI', Roboto, 'Helvetica Neue', 'DejaVu Sans', Arial, sans-serif;
  font-size: 16px; line-height: 1.55;
}
@media (prefers-color-scheme: dark) {
  body { background: linear-gradient(180deg, #1b2530, #223038 55%, #2b3a3e); }
}
main { width: 100%; max-width: 46rem; margin: 0 auto; }
.mark {
  font-size: .68rem; letter-spacing: .38em; text-transform: uppercase;
  color: var(--ink-faint); margin: 0 0 .4rem; text-align: center;
}
h1 { font-size: 1.9rem; margin: 0 0 .25rem; font-weight: 300; letter-spacing: .03em; text-align: center; }
.greet { color: var(--ink-soft); margin: 0 0 1.8rem; text-align: center; }
.greet b { color: var(--ink); }
.greet a { color: inherit; }

.card {
  position: relative; padding: 1.6rem 1.6rem 1.9rem; margin: 0 0 1.4rem;
  border-radius: 14px; background: var(--veil);
  box-shadow: 0 10px 40px rgba(0,0,0,.16); backdrop-filter: blur(2px);
}
.card h2 { margin: 0; font-size: 1.25rem; font-weight: 500; letter-spacing: .02em; }
.card .as { margin: .1rem 0 1.2rem; color: var(--ink-faint); font-size: .88rem; }
.card .as b { color: var(--ink-soft); }
.card.empty p { color: var(--ink-soft); margin: .8rem 0 0; }

.notice {
  margin: 0 0 1.2rem; padding: .7rem .9rem; border-radius: 9px;
  background: var(--field); color: var(--field-ink); font-size: .9rem;
}
.notice.good { background: var(--good); color: #fff; }
.notice.bad  { background: var(--rust); color: #fff; }
.notice code { background: rgba(0,0,0,.22); padding: .05rem .3rem; border-radius: 4px; word-break: break-all; }

.builder { display: grid; grid-template-columns: 12rem 1fr; gap: 1.4rem; align-items: start; }
@media (max-width: 34rem) {
  .builder { grid-template-columns: 1fr; }
  .plank { grid-column: 1; justify-self: stretch; text-align: center; }
}

.stage { text-align: center; }
.preview {
  width: 11rem; height: 11rem; display: block; margin: 0 auto;
  image-rendering: auto;
  filter: drop-shadow(0 6px 14px rgba(0,0,0,.35));
}
.stage .hint { margin: .7rem 0 0; font-size: .7rem; line-height: 1.4; color: var(--ink-faint); }

.controls { display: grid; gap: .75rem; }
.row { display: block; }
.row > span, .row > legend {
  display: block; font-size: .7rem; letter-spacing: .14em; text-transform: uppercase;
  color: var(--ink-faint); margin: 0 0 .3rem; padding: 0;
}
select {
  width: 100%; padding: .5rem .6rem; font: inherit; font-size: .92rem;
  color: var(--ink); background: rgba(255,255,255,.55);
  border: 1px solid var(--field-edge); border-radius: 7px;
}
@media (prefers-color-scheme: dark) { select { background: rgba(255,255,255,.08); } }

fieldset.swatches { border: 0; margin: 0; padding: 0; display: block; }
fieldset.swatches { display: flex; flex-wrap: wrap; gap: .3rem; }
fieldset.swatches legend { width: 100%; float: left; }
.sw { cursor: pointer; line-height: 0; }
.sw input { position: absolute; opacity: 0; width: 0; height: 0; }
.sw span {
  display: block; width: 1.45rem; height: 1.45rem; border-radius: 5px;
  background: var(--sw); border: 1px solid rgba(0,0,0,.35);
  box-shadow: inset 0 1px 0 rgba(255,255,255,.28);
}
.sw input:checked + span { outline: 2px solid var(--ink); outline-offset: 2px; }
.sw input:focus-visible + span { outline: 2px dashed var(--ink); outline-offset: 2px; }

/* Under the CONTROLS, not under the preview: the button is the end of the
   sequence of choices, and spanning both columns parked it beneath the crest
   with a column of dead space above it. */
.plank {
  grid-column: 2; justify-self: start;
  margin: .3rem 0 0; padding: .8rem 2.2rem;
  font: inherit; font-size: .8rem; font-weight: 600; letter-spacing: .17em;
  text-transform: uppercase; cursor: pointer;
  color: var(--timber-ink); border: 1px solid #a4744a; border-radius: 1px;
  background-image:
    linear-gradient(180deg, rgba(255,255,255,.34), rgba(255,255,255,0) 44%),
    linear-gradient(180deg, var(--timber-hi), var(--timber-mid) 46%, var(--timber-lo));
  box-shadow: 0 2px 0 rgba(112,72,40,.42), 0 12px 26px -14px rgba(38,24,10,.85);
}
.plank:hover { filter: brightness(1.06); }
.plank:active { transform: translateY(1px); }

footer { margin-top: 2rem; font-size: .72rem; line-height: 1.5; color: var(--ink-faint); text-align: center; }
</style>
</head>
<body>
<main>
  <p class=""mark"">Worlds Adrift Reborn</p>
  <h1>Your account</h1>
  <p class=""greet"">Signed in as <b>" + name + @"</b> &middot; <a href=""/download"">Get the patcher</a></p>

" + body + @"
  <footer>
    An unofficial, fan-run community server. Not affiliated with, endorsed by, or supported by Bossa Studios.<br>
    Alliance crests are a Wareborn addition &mdash; the original game had no way to change one.
  </footer>
</main>
<script>
(function () {
  'use strict';

  // The ONLY thing this script draws is a string. The picture always comes from
  // /alliance-emblem/preview.png, which is the same renderer the game hits - so
  // there is no second implementation here to drift from the server's.
  var FIELDS = ['shape', 'division', 'charge', 'field', 'detail', 'chargeColour'];

  // Written by the server rather than typed here, so the page cannot go on
  // building codes in a version the parser has moved past.
  var VERSION = '" + EmblemSpec.Version.ToString(CultureInfo.InvariantCulture) + @"';

  function codeOf(form) {
    var parts = [VERSION];
    for (var i = 0; i < FIELDS.length; i++) {
      var el = form.elements[FIELDS[i]];
      if (!el) { return null; }
      parts.push(el.value);
    }
    return parts.join('-');
  }

  function wire(form) {
    var img = form.querySelector('.preview');
    if (!img) { return; }
    var pending = 0;

    function refresh() {
      var code = codeOf(form);
      if (code === null) { return; }
      var next = '/alliance-emblem/preview.png?e=' + encodeURIComponent(code);
      if (img.getAttribute('src') !== next) { img.setAttribute('src', next); }

      // The download link follows the preview, so what a leader saves is what
      // they are looking at rather than what they had when the page loaded.
      var vector = form.querySelector('a.vector');
      if (vector) {
        vector.setAttribute('href',
          '/alliance-emblem/preview.svg?e=' + encodeURIComponent(code));
      }
    }

    form.addEventListener('change', function () {
      // Debounced: dragging across the swatches fires a change per colour, and
      // every one of those is a render on the server.
      window.clearTimeout(pending);
      pending = window.setTimeout(refresh, 120);
    });
  }

  var forms = document.querySelectorAll('form.builder');
  for (var i = 0; i < forms.length; i++) { wire(forms[i]); }
})();
</script>
</body>
</html>
";
        }

    }
}

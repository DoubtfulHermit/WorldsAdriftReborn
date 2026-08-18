using System.Collections.Concurrent;
using System.Reflection;

namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The console's front-end assets, loaded from embedded resources instead
    /// of living inside C# string literals.
    ///
    /// WHY THEY MOVED OUT OF THE .cs FILE. The console is becoming several
    /// pages - the operator dashboard, the public map, and the per-area views
    /// after them - and they share one renderer. While every line of CSS and
    /// JavaScript sat in one 3,000-line string literal, "add a page" and "tune
    /// the fauna mirror" and "add an operator command" were all edits to the
    /// same file, so they queued behind each other and collided. Split into
    /// files, each area has somewhere to be edited that nobody else is holding.
    ///
    /// HOW THE PIECES GO BACK TOGETHER. The JavaScript files are fragments of
    /// ONE shared closure, not modules: they are concatenated in a fixed order
    /// inside a single <c>(function(){ 'use strict'; ... })()</c>, exactly as
    /// they were when they were one literal. That is deliberate. Function
    /// declarations hoist within the closure and every fragment sees the same
    /// state variables, so the split is purely editorial - it changes who has
    /// to touch which file, and changes nothing at all about what the browser
    /// runs. <see cref="AdminPageGoldenTests"/> pins that claim by comparing
    /// the composed page against a recorded copy of the pre-split output.
    ///
    /// A page picks the fragments it needs: the dashboard takes all of them,
    /// the public map takes the shared renderer and none of the operator ones.
    /// That is what keeps identity-bearing UI out of the public page by
    /// CONSTRUCTION rather than by a runtime flag someone can get wrong.
    ///
    /// Embedded, not read off disk, because the login server is deployed as a
    /// single self-contained binary; an asset that has to be copied beside it
    /// is an asset that will one day not be.
    /// </summary>
    internal static class WebAssets
    {
        private static readonly ConcurrentDictionary<string, string> Cache =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Reads one asset by file name (e.g. "map-render.js"). Cached: these
        /// are immutable for the process lifetime, and the dashboard is
        /// composed on every request.
        /// </summary>
        internal static string Read(string name) =>
            Cache.GetOrAdd(name, static key =>
            {
                Assembly assembly = typeof(WebAssets).Assembly;
                string? resource = assembly.GetManifestResourceNames()
                    .SingleOrDefault(n => n.EndsWith(".Web.Assets." + key, StringComparison.Ordinal));
                if (resource == null)
                {
                    throw new InvalidOperationException(
                        "Web asset '" + key + "' is not embedded in the login server. "
                        + "Assets under WorldsAdriftServer/Web/Assets are embedded by the "
                        + "csproj glob; a new one needs no csproj edit, but a RENAMED one "
                        + "needs its reference here updated.");
                }

                using Stream stream = assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException("Web asset '" + key + "' could not be opened.");
                using StreamReader reader = new StreamReader(stream);
                return reader.ReadToEnd();
            });

        /// <summary>
        /// Reads an asset without its trailing newline. Used when a fragment
        /// is substituted INTO a line of another fragment, where the file's
        /// own final newline would otherwise open a blank line.
        /// </summary>
        internal static string ReadTrimmed(string name) => Read(name).TrimEnd('\n');

        /// <summary>
        /// Concatenates JavaScript fragments into one shared-closure body, in
        /// the order given. The order is the page's business - see
        /// <see cref="AdminPage"/> and <see cref="PublicMapPage"/>.
        /// </summary>
        internal static string Script(params string[] names)
        {
            System.Text.StringBuilder body = new System.Text.StringBuilder();
            foreach (string name in names)
            {
                body.Append(Read(name));
            }
            return body.ToString();
        }

        /// <summary>
        /// Substitutes <c>{{name}}</c> placeholders. Used for the handful of
        /// values a page can only know at request time (the CSRF token) or
        /// that must come from a palette module rather than be written twice
        /// (the tier opacity, the weather-wall legend).
        ///
        /// Every placeholder must be supplied: an unresolved <c>{{...}}</c>
        /// reaching a browser is a bug that would otherwise be discovered by
        /// reading it on the page.
        /// </summary>
        internal static string Fill(string template, params (string Key, string Value)[] values)
        {
            string filled = template;
            foreach ((string key, string value) in values)
            {
                filled = filled.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
            }

            int stray = filled.IndexOf("{{", StringComparison.Ordinal);
            if (stray >= 0)
            {
                int end = filled.IndexOf("}}", stray, StringComparison.Ordinal);
                string name = end > stray
                    ? filled.Substring(stray, Math.Min(end + 2 - stray, 64))
                    : "{{...}}";
                throw new InvalidOperationException(
                    "Web asset placeholder " + name + " was never filled in.");
            }

            return filled;
        }
    }
}

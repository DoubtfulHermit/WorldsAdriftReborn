using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The console's front-end now lives in <c>Web/Assets</c> rather than in
    /// C# string literals, and these are the tests that keep that split
    /// honest.
    ///
    /// The risk the split introduces is a NEW one: with the markup and the
    /// script in separate files, "the file I edited" and "the bytes the
    /// browser got" are no longer trivially the same thing. A fragment could
    /// be dropped from a page's load order, or served in an order that breaks
    /// the shared closure, and every test that reads the RENDERED page would
    /// still pass while the console shipped something else. So the assertions
    /// below all run in the same direction: take the composed page, and prove
    /// each asset FILE is inside it, verbatim.
    ///
    /// This is the same discipline AdminFaunaParityTests states for the
    /// movement mirror - the copy under test must be the copy that ships -
    /// applied to the extraction as a whole.
    /// </summary>
    public class WebAssetCompositionTests
    {
        private const string Csrf = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        private static string Dashboard() =>
            AdminPage.Dashboard("{}", Csrf, ReleaseWorldMap.Json);

        /// <summary>
        /// A fragment as the DASHBOARD composes it: the per-request and
        /// per-page placeholders filled exactly as AdminPage fills them.
        /// </summary>
        private static string AsComposed(string fragment) =>
            WebAssets.Read(fragment)
                .Replace("{{csrfToken}}", Csrf, StringComparison.Ordinal)
                .Replace("{{refreshMs}}", "1500", StringComparison.Ordinal);

        [Fact]
        public void EveryAdminFragmentIsInTheServedPageVerbatim()
        {
            string html = Dashboard();
            foreach (string fragment in AdminPage.AdminScriptFragments)
            {
                string body = AsComposed(fragment);
                Assert.True(html.Contains(body, StringComparison.Ordinal),
                    "the dashboard does not carry '" + fragment + "' verbatim; "
                    + "either it was dropped from the load order or something "
                    + "rewrites it on the way to the browser");
            }
        }

        [Fact]
        public void TheStylesheetAndBodyAreInTheServedPageVerbatim()
        {
            string html = Dashboard();
            Assert.Contains(WebAssets.Read("console.css"), html, StringComparison.Ordinal);

            // The body carries three filled placeholders, so check the parts
            // either side of them rather than the whole file.
            string body = WebAssets.Read("admin-body.html");
            string head = body.Substring(0, body.IndexOf("{{", StringComparison.Ordinal));
            Assert.Contains(head, html, StringComparison.Ordinal);
            Assert.Contains(body.Substring(body.LastIndexOf("}}", StringComparison.Ordinal) + 2),
                html, StringComparison.Ordinal);
        }

        [Fact]
        public void FragmentsAreLoadedInAnOrderThatKeepsOneSharedClosure()
        {
            // The fragments are pieces of ONE closure, not modules: they must
            // appear in the page in the declared order, inside a single
            // (function(){ 'use strict'; ... })(). If a future page composes
            // them out of order, a var read at load time would see undefined.
            string html = Dashboard();
            Assert.Equal(1, Occurrences(html, "'use strict';"));

            int previous = -1;
            foreach (string fragment in AdminPage.AdminScriptFragments)
            {
                int at = html.IndexOf(AsComposed(fragment), StringComparison.Ordinal);
                Assert.True(at > previous,
                    "'" + fragment + "' is composed out of its declared order");
                previous = at;
            }
        }

        [Fact]
        public void TheSharedRendererIsTheSameFilesBothPagesUse()
        {
            // The point of the extraction: the public map must draw from the
            // SAME renderer files as the console, so a fix to a coastline or a
            // creature reaches both from one edit. A copy would drift.
            foreach (string shared in PublicMapPage.SharedRendererFragments)
            {
                Assert.Contains(shared, AdminPage.AdminScriptFragments);
            }
        }

        [Fact]
        public void ThePublicPageTakesNoOperatorFragment()
        {
            // The structural half of the privacy boundary: the operator-only
            // fragments carry the command UI, the player table and the terrain
            // matrix, and the public page must not be able to compose them
            // even by accident.
            foreach (string fragment in PublicMapPage.ScriptFragments)
            {
                Assert.DoesNotContain("admin", fragment, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void NoAssetReachesForAnExternalHost()
        {
            // The public map is served to anyone, so a CDN or font reference
            // would leak every visitor to a third party - and the console has
            // always been self-contained anyway.
            string[] assets =
            {
                "console.css", "console-core.js", "admin-domains.js",
                "map-render.js", "map-fauna.js", "map-ships.js",
                "map-interaction.js", "map-body.html",
                "admin-console.js", "admin-wiring.js", "admin-body.html",
                "admin-map-provenance.html", "admin-map-legend.html",
                "admin-map-authenticity.html", "admin-map-ledger.html",
                "public-map-body.html", "public-map.js",
                "public-map-legend.html", "public-map-ledger.html",
            };
            foreach (string name in assets)
            {
                // The W3C SVG namespace is an IDENTIFIER, not an address: it
                // is never fetched, and createElementNS requires it verbatim.
                string text = WebAssets.Read(name)
                    .Replace("http://www.w3.org/2000/svg", "", StringComparison.Ordinal);
                foreach (string reach in new[] { "http://", "https://", "//cdn", "@import url(" })
                {
                    Assert.False(text.Contains(reach, StringComparison.OrdinalIgnoreCase),
                        name + " reaches for an external host via '" + reach + "'");
                }
            }
        }

        /// <summary>
        /// THE COMPOSED SCRIPT MUST PARSE - on both pages.
        ///
        /// Every fragment is concatenated into ONE shared closure
        /// (<see cref="FragmentsAreLoadedInAnOrderThatKeepsOneSharedClosure"/>), so
        /// an unbalanced brace anywhere in any of them does not break that
        /// fragment - it breaks the WHOLE console, silently, in a browser nobody is
        /// looking at during a test run. Every other test in this file compares
        /// STRINGS, which a syntax error sails straight through.
        ///
        /// It PARSES rather than runs: <c>vm.Script</c> compiles the source without
        /// executing a line of it, so this needs no DOM, no fetch and no fixture,
        /// and it cannot be fooled by code that happens not to run on a test path.
        /// </summary>
        [NodeFact]
        public void TheComposedScriptOfBothPagesParses()
        {
            Check("admin", AdminPage.Dashboard("{}", new string('a', 64), ReleaseWorldMap.Json));
            Check("public", PublicMapPage.Html("{}", ReleaseWorldMap.Json));

            static void Check(string which, string html)
            {
                string source = ExtractScripts(html);
                Assert.True(source.Length > 1000,
                    "the " + which + " page composed no script at all");

                string directory = Path.Combine(Path.GetTempPath(),
                    "wareborn-script-parse-" + Guid.NewGuid().ToString("n"));
                Directory.CreateDirectory(directory);
                try
                {
                    string sourcePath = Path.Combine(directory, which + ".js");
                    File.WriteAllText(sourcePath, source);
                    string harnessPath = Path.Combine(directory, "parse.js");
                    File.WriteAllText(harnessPath, @"
const fs = require('fs'), vm = require('vm');
new vm.Script(fs.readFileSync(process.argv[2], 'utf8'), {filename: process.argv[2]});
process.stdout.write('ok');
");
                    Assert.Equal("ok", NodeFactAttribute.Run(harnessPath, sourcePath).Trim());
                }
                finally
                {
                    try { Directory.Delete(directory, true); } catch { }
                }
            }
        }

        /// <summary>
        /// Every executable script block of a served page, concatenated in order.
        /// JSON blocks are skipped: they are data the page reads, not code, and
        /// they are not JavaScript.
        /// </summary>
        private static string ExtractScripts(string html)
        {
            System.Text.StringBuilder source = new System.Text.StringBuilder();
            int at = 0;
            while (true)
            {
                int open = html.IndexOf("<script", at, StringComparison.Ordinal);
                if (open < 0) break;
                int openEnd = html.IndexOf('>', open);
                int close = html.IndexOf("</script>", openEnd, StringComparison.Ordinal);
                if (openEnd < 0 || close < 0) break;
                string tag = html.Substring(open, openEnd - open);
                if (!tag.Contains("application/json", StringComparison.Ordinal))
                {
                    source.Append(html, openEnd + 1, close - openEnd - 1);
                    source.Append('\n');
                }
                at = close + 1;
            }
            return source.ToString();
        }

        [Fact]
        public void AnUnfilledPlaceholderIsLoudRatherThanRenderedToABrowser()
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => WebAssets.Fill("before {{neverSupplied}} after"));
            Assert.Contains("neverSupplied", error.Message, StringComparison.Ordinal);

            // And the normal path still substitutes.
            Assert.Equal("before X after",
                WebAssets.Fill("before {{k}} after", ("k", "X")));
        }

        [Fact]
        public void AMissingAssetNamesItselfRatherThanReturningEmpty()
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => WebAssets.Read("no-such-asset.js"));
            Assert.Contains("no-such-asset.js", error.Message, StringComparison.Ordinal);
        }

        private static int Occurrences(string haystack, string needle)
        {
            int count = 0, at = 0;
            while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }
            return count;
        }
    }
}

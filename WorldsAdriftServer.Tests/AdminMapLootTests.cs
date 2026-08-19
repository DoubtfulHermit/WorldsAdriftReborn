using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// LOOT CONTAINERS ON THE OPERATOR MAP, AND NOT ON THE PUBLIC ONE.
    ///
    /// The maintainer asked for containers on the admin panel and said "no live
    /// map" in the same breath, so the boundary is the feature and it gets its own
    /// tests. It is enforced structurally rather than by a flag:
    /// <c>admin-map-loot.js</c> is in <see cref="AdminPage.AdminScriptFragments"/>
    /// and not in <see cref="PublicMapPage.ScriptFragments"/>, and the shared
    /// renderer reaches it only through <c>typeof wbLootX === 'function'</c> hooks -
    /// so the public page does not hide the loot UI, it never receives the code.
    ///
    /// The second half of this file is about the OTHER way this map has been broken
    /// before. Every <c>Web/Assets/*.js</c> file is concatenated into one shared
    /// closure, so a duplicate top-level name silently shadows another file's - an
    /// <c>svgEl</c> declared twice once took the whole map down. Nothing in the
    /// build catches that, so a test has to.
    /// </summary>
    public class AdminMapLootTests
    {
        private const string Csrf = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string Fragment = "admin-map-loot.js";

        private static string Dashboard() => AdminPage.Dashboard("{}", Csrf, ReleaseWorldMap.Json);

        private static string PublicMap() => PublicMapPage.Html("{}", ReleaseWorldMap.Json);

        [Fact]
        public void TheOperatorConsoleCarriesTheLootFragment()
        {
            Assert.Contains(Fragment, AdminPage.AdminScriptFragments);
            Assert.Contains(WebAssets.Read(Fragment), Dashboard(), StringComparison.Ordinal);
        }

        [Fact]
        public void ThePublicMapCarriesNoneOfIt()
        {
            Assert.DoesNotContain(Fragment, PublicMapPage.ScriptFragments);
            Assert.DoesNotContain(Fragment, PublicMapPage.SharedRendererFragments);

            string html = PublicMap();

            // Not one of the hook implementations reaches the public page. The
            // shared renderer's `typeof wbLootX === 'function'` guards do, and must -
            // they are what keeps one renderer instead of two.
            foreach (string name in LootFunctionNames())
            {
                Assert.False(
                    Regex.IsMatch(html, @"function\s+" + Regex.Escape(name) + @"\s*\("),
                    "the public map defines " + name + ", so the loot UI would draw there");
            }
        }

        [Fact]
        public void TheSharedRendererOnlyEverCallsTheHooksDefensively()
        {
            // An unguarded call would throw a ReferenceError on the public page and
            // take the island panel down with it - which is how a privacy boundary
            // becomes an outage.
            string renderer = WebAssets.Read("map-render.js");

            foreach (Match call in Regex.Matches(renderer, @"\bwbLoot[A-Za-z]*\b"))
            {
                int line = renderer.Take(call.Index).Count(c => c == '\n');
                string[] lines = renderer.Split('\n');

                // The guard is allowed to be on the same line or the line above,
                // which is how the existing hooks in this renderer are written.
                string window = lines[line] + (line > 0 ? lines[line - 1] : "");
                Assert.Contains("typeof wbLoot", window, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void EveryTopLevelNameInTheFragmentIsNamespaced()
        {
            // The shared-closure trap. Anything declared here that is not prefixed
            // could shadow a name another fragment relies on.
            foreach (string name in LootFunctionNames())
            {
                Assert.StartsWith("wbLoot", name, StringComparison.Ordinal);
            }

            // TOP-LEVEL only, so the anchor allows no leading whitespace: a `var`
            // inside a function body is scoped to it and cannot shadow anything.
            string body = WebAssets.Read(Fragment);
            foreach (Match declaration in Regex.Matches(body, @"(?m)^(?:var|let|const)\s+([A-Za-z_$][\w$]*)"))
            {
                Assert.StartsWith("wbLoot", declaration.Groups[1].Value, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void NoNameInTheFragmentCollidesWithAnotherAssetsTopLevelName()
        {
            HashSet<string> mine = new(LootFunctionNames(), StringComparer.Ordinal);

            foreach (string fragment in AdminPage.AdminScriptFragments)
            {
                if (fragment == Fragment) continue;

                foreach (Match declaration in Regex.Matches(
                             WebAssets.Read(fragment), @"(?m)^\s*function\s+([A-Za-z_$][\w$]*)"))
                {
                    Assert.DoesNotContain(declaration.Groups[1].Value, mine);
                }
            }
        }

        [Fact]
        public void TheOperatorPageStatesTheWorldsContainerCount()
        {
            // The end-to-end assertion: the number the game server seeds from
            // reaches the page the operator reads. A payload that stopped carrying
            // it would leave the map drawing zeroes with nothing failing.
            string json = ReleaseWorldMap.Json;
            Assert.Contains("\"lootContainers\"", json, StringComparison.Ordinal);
            Assert.Contains("\"islandsWithLoot\"", json, StringComparison.Ordinal);

            int total = WorldsAdriftRebornGameServer.Multiplayer.Islands
                .IslandResourceInventoryCatalog.Totals.LootContainers;
            Assert.True(total > 0,
                "the release catalogue seeds no loot containers at all, so the map has nothing to show");
            Assert.Contains("\"lootContainers\":" + total, json, StringComparison.Ordinal);
        }

        [Fact]
        public void TheIslandPanelAndTheLedgerBothReachTheLootHooks()
        {
            // Named individually rather than by a blanket grep, because "containers
            // appear somewhere on the page" is exactly the assertion that let the
            // tree work ship green while the feature was invisible.
            string renderer = WebAssets.Read("map-render.js");
            Assert.Contains("wbLootIslandStatTile", renderer, StringComparison.Ordinal);
            Assert.Contains("wbLootIslandBlock", renderer, StringComparison.Ordinal);
            Assert.Contains("wbLootLedgerValue", renderer, StringComparison.Ordinal);
            Assert.Contains("wbLootHoverFact", renderer, StringComparison.Ordinal);
            Assert.Contains("wbLootWorldStatTile", renderer, StringComparison.Ordinal);
        }

        private static string[] LootFunctionNames() =>
            Regex.Matches(WebAssets.Read(Fragment), @"(?m)^function\s+([A-Za-z_$][\w$]*)")
                .Select(m => m.Groups[1].Value)
                .ToArray();
    }
}

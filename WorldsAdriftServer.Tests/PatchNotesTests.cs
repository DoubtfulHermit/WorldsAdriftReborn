using WorldsAdriftServer.PatchNotes;
using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The routing half of the patch-notes page.
    ///
    /// The thing worth testing here is not "does /patchnotes work" - it is that
    /// NOTHING under the prefix escapes. This server does not answer a URL no
    /// handler claims: the socket is simply left open. So a mistyped path under a
    /// route we print on the game's own login screen would hang the browser
    /// rather than 404, and the only defence is that one handler owns the whole
    /// namespace.
    /// </summary>
    public class PatchNotesRoutesTests
    {
        [Theory]
        [InlineData("/patchnotes")]
        [InlineData("/patchnotes/")]
        [InlineData("/patchnotes?from=game")]
        [InlineData("/patchnotes#2026-08-18")]
        public void ThePageAnswersItsOwnUrlAndTheOnesBesideIt(string url)
        {
            Assert.Equal(PatchNotesRoute.Page, PatchNotesRoutes.Match("GET", url));
        }

        [Fact]
        public void HeadIsTheSameRouteAsGet()
        {
            Assert.Equal(PatchNotesRoute.Page, PatchNotesRoutes.Match("HEAD", "/patchnotes"));
        }

        [Fact]
        public void TheRawSourceHasItsOwnRoute()
        {
            Assert.Equal(PatchNotesRoute.Source, PatchNotesRoutes.Match("GET", "/patchnotes/source"));
            Assert.Equal(PatchNotesRoute.Source, PatchNotesRoutes.Match("GET", "/patchnotes/source/"));
        }

        [Theory]
        [InlineData("/patchnotes/2026")]
        [InlineData("/patchnotes/anything/at/all")]
        [InlineData("/patchnotes/source.json")]
        public void AnythingElseUnderThePrefixIsOursToRefuse(string url)
        {
            // NotFound, never None. None would let the request fall through to a
            // router that has no answer for it, and this server answers those by
            // saying nothing at all.
            Assert.Equal(PatchNotesRoute.NotFound, PatchNotesRoutes.Match("GET", url));
        }

        [Fact]
        public void AWriteToThePrefixIsRefusedRatherThanIgnored()
        {
            // There is nothing to POST to. Falling through would hang the socket.
            Assert.Equal(PatchNotesRoute.NotFound, PatchNotesRoutes.Match("POST", "/patchnotes"));
            Assert.Equal(PatchNotesRoute.NotFound, PatchNotesRoutes.Match("DELETE", "/patchnotes/source"));
        }

        [Theory]
        [InlineData("/patchnotesomething")]
        [InlineData("/patch")]
        [InlineData("/patch/manifest.json")]
        [InlineData("/map")]
        [InlineData("/")]
        [InlineData("")]
        [InlineData(null)]
        public void APathThatMerelyStartsTheSameIsNotOurs(string? url)
        {
            // "/patchnotesomething" shares a leading string with the prefix and
            // is a different path. Claiming it would also mean claiming /patch's
            // manifest, which another handler owns.
            Assert.Equal(PatchNotesRoute.None, PatchNotesRoutes.Match("GET", url));
        }
    }

    /// <summary>
    /// The inline markup, which is also the injection boundary: the notes source
    /// is operator-editable, so what this accepts is what somebody with the admin
    /// password can put on a public page.
    /// </summary>
    public class PatchNotesMarkupTests
    {
        [Fact]
        public void MarkupIsEscapedBeforeAnythingElseHappensToIt()
        {
            Assert.Equal("&lt;script&gt;alert(1)&lt;/script&gt;",
                PatchNotesMarkup.Inline("<script>alert(1)</script>"));
        }

        [Fact]
        public void BoldAndCodeAreTheOnlyTagsProseCanProduce()
        {
            Assert.Equal("a <strong>b</strong> c", PatchNotesMarkup.Inline("a **b** c"));
            Assert.Equal("run <code>x &amp; y</code>", PatchNotesMarkup.Inline("run `x & y`"));
        }

        [Fact]
        public void AnUnclosedMarkerIsJustAnAsterisk()
        {
            // Somebody typing "**" and changing their mind must not lose the rest
            // of the line to an unterminated tag.
            Assert.Equal("two ** stars", PatchNotesMarkup.Inline("two ** stars"));
            Assert.Equal("a `tick", PatchNotesMarkup.Inline("a `tick"));
        }

        [Fact]
        public void ALinkToThisSiteIsALink()
        {
            Assert.Equal("see <a href=\"/map\">the map</a>",
                PatchNotesMarkup.Inline("see [the map](/map)"));
            Assert.Equal("<a href=\"/patchnotes#2026-08-18\">that day</a>",
                PatchNotesMarkup.Inline("[that day](/patchnotes#2026-08-18)"));
        }

        [Theory]
        [InlineData("[x](https://example.com)", "x")]
        [InlineData("[x](//example.com)", "x")]
        [InlineData("[x](javascript:alert)", "x")]
        [InlineData("[x](data:text/html,y)", "x")]
        [InlineData("[x](/a/../../etc)", "x")]
        public void ALinkAnywhereElseKeepsItsWordsAndLosesItsLink(string source, string expected)
        {
            // The page must reach for nothing off this host, and it must not be
            // possible to make it do so by editing the notes.
            Assert.Equal(expected, PatchNotesMarkup.Inline(source));
        }

        [Theory]
        [InlineData("/map", true)]
        [InlineData("/account", true)]
        [InlineData("/patch/manifest.json", true)]
        [InlineData("#top", true)]
        [InlineData("https://example.com", false)]
        [InlineData("//example.com", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("data:text/html,x", false)]
        [InlineData("mailto:a@b.c", false)]
        [InlineData("map", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void OnlyPathsOnThisSiteCountAsInternal(string? href, bool internalHref)
        {
            Assert.Equal(internalHref, PatchNotesMarkup.IsInternalHref(href));
        }

        [Fact]
        public void AQuoteInsideALabelCannotCloseAnAttribute()
        {
            Assert.Equal("<a href=\"/map\">a&quot;b</a>",
                PatchNotesMarkup.Inline("[a\"b](/map)"));
        }
    }

    /// <summary>The source's grammar.</summary>
    public class PatchNotesDocumentTests
    {
        [Fact]
        public void AReleaseCarriesItsDateTitleAndBadge()
        {
            PatchNotesDocument document = PatchNotesDocument.Parse(
                "## 2026-08-18 | A world with something in it | six client patches");

            PatchNotesRelease release = Assert.Single(document.Releases);
            Assert.Equal("2026-08-18", release.Date);
            Assert.Equal("18 August 2026", release.DisplayDate);
            Assert.Equal("A world with something in it", release.Title);
            Assert.Equal("six client patches", release.Badge);
            Assert.Equal("2026-08-18", release.Anchor);
        }

        [Fact]
        public void ADateIsRenderedTheSameWhateverTheServersLocaleIs()
        {
            // The server runs in a German locale; the page is English. Parsing
            // and formatting both pin the invariant culture, so the month name a
            // reader sees does not depend on where the process happens to run.
            System.Globalization.CultureInfo previous =
                System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo("de-DE");
                Assert.Equal("18 August 2026",
                    PatchNotesDocument.Parse("## 2026-08-18 | x").Releases[0].DisplayDate);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Fact]
        public void HeadingsBulletsAndProseBecomeBlocksInOrder()
        {
            PatchNotesDocument document = PatchNotesDocument.Parse(string.Join("\n", new[]
            {
                "## 2026-08-18 | Title",
                "### Wildlife",
                "- one",
                "- two",
                "",
                "A sentence.",
                "Another line of the same paragraph.",
            }));

            PatchNotesRelease release = Assert.Single(document.Releases);
            Assert.Equal(3, release.Blocks.Count);
            Assert.Equal(PatchNotesBlockKind.Heading, release.Blocks[0].Kind);
            Assert.Equal("Wildlife", release.Blocks[0].Text);
            Assert.Equal(PatchNotesBlockKind.Bullets, release.Blocks[1].Kind);
            Assert.Equal(new[] { "one", "two" }, release.Blocks[1].Items);
            Assert.Equal(PatchNotesBlockKind.Paragraph, release.Blocks[2].Kind);
            Assert.Equal("A sentence. Another line of the same paragraph.", release.Blocks[2].Text);
        }

        [Fact]
        public void ABulletRunEndsWhenProseStartsWithoutABlankLineBetween()
        {
            PatchNotesDocument document = PatchNotesDocument.Parse(
                "## 2026-08-18 | Title\n- one\nprose\n- two");

            PatchNotesRelease release = document.Releases[0];
            Assert.Equal(3, release.Blocks.Count);
            Assert.Equal(new[] { "one" }, release.Blocks[0].Items);
            Assert.Equal("prose", release.Blocks[1].Text);
            Assert.Equal(new[] { "two" }, release.Blocks[2].Items);
        }

        [Fact]
        public void LinesAboveTheFirstReleaseAreThePagesOwnOpening()
        {
            PatchNotesDocument document = PatchNotesDocument.Parse(
                "First paragraph.\n\nSecond one.\n\n## 2026-08-18 | Title");

            Assert.Equal(new[] { "First paragraph.", "Second one." }, document.Intro);
            Assert.Single(document.Releases);
        }

        [Fact]
        public void ATitleWithoutADateIsATitle()
        {
            PatchNotesRelease release = PatchNotesDocument.Parse("## Coming soon").Releases[0];
            Assert.Equal("Coming soon", release.Title);
            Assert.Equal(string.Empty, release.Date);
            Assert.Equal(string.Empty, release.DisplayDate);
            Assert.Equal("coming-soon", release.Anchor);
        }

        [Fact]
        public void ADateWeCannotReadIsPrintedAsWritten()
        {
            PatchNotesRelease release = PatchNotesDocument.Parse("## Last Tuesday | Title").Releases[0];
            Assert.Equal(string.Empty, release.Date);
            Assert.Equal("Last Tuesday", release.DisplayDate);
        }

        [Fact]
        public void TwoReleasesOnOneDayDoNotShareAnAnchor()
        {
            PatchNotesDocument document = PatchNotesDocument.Parse(
                "## 2026-08-18 | Morning\n## 2026-08-18 | Evening");

            Assert.Equal("2026-08-18", document.Releases[0].Anchor);
            Assert.Equal("2026-08-18-2", document.Releases[1].Anchor);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   \n\n\t\n")]
        public void AnEmptySourceIsAnEmptyDocumentRatherThanAThrow(string? source)
        {
            PatchNotesDocument document = PatchNotesDocument.Parse(source);
            Assert.True(document.IsEmpty);
            Assert.Empty(document.Releases);
            Assert.Empty(document.Intro);
        }

        [Fact]
        public void WindowsLineEndingsParseTheSameAsUnixOnes()
        {
            // The source can be pasted into a textarea from anywhere.
            Assert.Equal(2, PatchNotesDocument
                .Parse("## 2026-08-18 | A\r\n- x\r\n## 2026-08-17 | B").Releases.Count);
        }
    }

    /// <summary>The page itself, and the notes that ship inside it.</summary>
    public class PatchNotesPageTests
    {
        private static string Shipped() => PatchNotesSource.Committed();

        [Fact]
        public void TheShippedNotesParseIntoDatedReleases()
        {
            // The committed file is the record of what shipped. A typo in its
            // first character would otherwise be discovered by a player.
            PatchNotesDocument document = PatchNotesDocument.Parse(Shipped());
            Assert.False(document.IsEmpty);
            Assert.NotEmpty(document.Intro);

            foreach (PatchNotesRelease release in document.Releases)
            {
                Assert.NotEqual(string.Empty, release.Title);
                Assert.NotEqual(string.Empty, release.DisplayDate);
                Assert.NotEmpty(release.Blocks);
            }
        }

        [Fact]
        public void TheNewestReleaseIsFirst()
        {
            // The page does not sort. The file's order IS the page's order, so
            // the file has to be right.
            DateTime previous = DateTime.MaxValue;
            foreach (PatchNotesRelease release in PatchNotesDocument.Parse(Shipped()).Releases)
            {
                DateTime at = DateTime.Parse(release.Date,
                    System.Globalization.CultureInfo.InvariantCulture);
                Assert.True(at <= previous,
                    "release '" + release.Title + "' is out of order in patch-notes.md");
                previous = at;
            }
        }

        [Fact]
        public void ThePageCarriesEveryReleaseAndItsIndex()
        {
            string html = PatchNotesPage.Html(Shipped());
            foreach (PatchNotesRelease release in PatchNotesDocument.Parse(Shipped()).Releases)
            {
                Assert.Contains("id=\"" + release.Anchor + "\"", html, StringComparison.Ordinal);
                Assert.Contains("href=\"#" + release.Anchor + "\"", html, StringComparison.Ordinal);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void APageWithNoNotesIsStillAPage(string? source)
        {
            // A fresh deployment, or an operator who cleared the box. Neither is
            // an error, and neither may produce a blank screen or an exception.
            string html = PatchNotesPage.Html(source);
            Assert.Contains("<title>Patch notes", html, StringComparison.Ordinal);
            Assert.Contains("No notes have been published yet", html, StringComparison.Ordinal);
            Assert.Contains("Nothing published yet", html, StringComparison.Ordinal);
            Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
        }

        [Fact]
        public void ASingleReleaseGetsNoContentsList()
        {
            // A table of contents with one entry is furniture, not navigation.
            // The element, not the class name: the stylesheet is in the page too
            // and always carries the rules for it.
            string html = PatchNotesPage.Html("## 2026-08-18 | Only one\n- a thing");
            Assert.DoesNotContain("<nav class=\"pn-index\"", html, StringComparison.Ordinal);
            Assert.Contains("Only one", html, StringComparison.Ordinal);
        }

        [Fact]
        public void ThePageIsSelfContained()
        {
            // It is public and it is the first thing most people read about the
            // server. A CDN font or a remote script would hand every reader to a
            // third party, and there is nothing here that needs one.
            string html = PatchNotesPage.Html(Shipped());
            foreach (string reach in new[] { "http://", "https://", "//cdn", "@import" })
            {
                Assert.False(html.Contains(reach, StringComparison.OrdinalIgnoreCase),
                    "the patch notes reach for an external host via '" + reach + "'");
            }

            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TheNotesCannotSmuggleMarkupOntoThePublicPage()
        {
            // The source is operator-editable, so this is the boundary that
            // matters: nothing stored can become a tag except the three markers.
            string html = PatchNotesPage.Html(
                "## 2026-08-18 | <img src=x onerror=alert(1)>\n- <script>alert(2)</script>");

            Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("&lt;img", html, StringComparison.Ordinal);
        }

        [Fact]
        public void ThePageIsStyledFromTheSameStylesheetAsTheConsoleAndTheMap()
        {
            // One site. If the console's palette moves, this page moves with it
            // rather than becoming the one screen that did not follow.
            Assert.Contains(WebAssets.Read("console.css"), PatchNotesPage.Style,
                StringComparison.Ordinal);
            Assert.Contains(WebAssets.Read("patchnotes.css"), PatchNotesPage.Style,
                StringComparison.Ordinal);
        }

        [Fact]
        public void TheOverrideKeyIsTheOneTheAdminPanelWrites()
        {
            // Two names for the same row would mean an operator editing a value
            // the page never reads.
            Assert.Equal("patch_notes", PatchNotesSource.ConfigKey);
            Assert.Contains("name=\"patchNotes\"", WebAssets.Read("admin-patchnotes.html"),
                StringComparison.Ordinal);
            Assert.Contains("action=\"/admin/patch-notes\"", WebAssets.Read("admin-patchnotes.html"),
                StringComparison.Ordinal);
            Assert.Contains("/patchnotes/source", WebAssets.Read("admin-patchnotes.js"),
                StringComparison.Ordinal);
        }

        [Fact]
        public void ABlankOverrideIsNotWorthStoring()
        {
            Assert.False(PatchNotesSource.IsStorable(null));
            Assert.False(PatchNotesSource.IsStorable("   \n "));
            Assert.True(PatchNotesSource.IsStorable("## 2026-08-18 | x"));
        }

        [Fact]
        public void ACommitRowSplitsIntoAShaAndASubject()
        {
            Assert.True(PatchNotesCommit.TryParse(
                "153728a Record the 2026.08.19-1 patcher release", out PatchNotesCommit commit));
            Assert.Equal("153728a", commit.Sha);
            Assert.Equal("Record the 2026.08.19-1 patcher release", commit.Subject);
        }

        [Fact]
        public void OnlySomethingShapedLikeAShaIsACommit()
        {
            // The "* " marker has to stay usable for anything else, so the sha
            // test is what decides - not the marker. A line that fails it must
            // fall through to prose rather than render as a commit whose sha
            // column contains a word.
            Assert.False(PatchNotesCommit.TryParse("nothex1 a subject", out _));
            Assert.False(PatchNotesCommit.TryParse("abc a subject", out _));   // too short
            Assert.False(PatchNotesCommit.TryParse("153728a", out _));         // no subject
            Assert.False(PatchNotesCommit.TryParse("153728a    ", out _));     // blank subject
            Assert.False(PatchNotesCommit.TryParse(null, out _));
        }

        [Fact]
        public void ARunOfCommitsBecomesOneCommitBlock()
        {
            PatchNotesDocument document = PatchNotesDocument.Parse(
                "## 2026-08-19 | 2 commits\n\n* 153728a First\n* 1621a28 Second\n");

            PatchNotesBlock block = Assert.Single(document.Releases[0].Blocks);
            Assert.Equal(PatchNotesBlockKind.Commits, block.Kind);
            Assert.Equal(2, block.Items.Count);
        }

        [Fact]
        public void ACommitLineThatIsNotOneDoesNotVanish()
        {
            // The failure this guards against is silent: a line the parser
            // refuses as a commit and then forgets to keep is a line missing
            // from a page that claims to list everything.
            PatchNotesDocument document = PatchNotesDocument.Parse(
                "## 2026-08-19 | x\n\n* not-a-sha but still a sentence\n");

            PatchNotesBlock block = Assert.Single(document.Releases[0].Blocks);
            Assert.Equal(PatchNotesBlockKind.Paragraph, block.Kind);
            Assert.Contains("still a sentence", block.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void TheShippedNotesAreACommitLog()
        {
            // The page's whole claim is that it lists real commits, so the
            // committed file has to actually be that and not prose that drifted
            // back in.
            PatchNotesDocument document = PatchNotesDocument.Parse(Shipped());

            int commits = 0;
            foreach (PatchNotesRelease release in document.Releases)
            {
                foreach (PatchNotesBlock block in release.Blocks)
                {
                    if (block.Kind == PatchNotesBlockKind.Commits)
                    {
                        commits += block.Items.Count;
                    }
                }
            }

            Assert.True(commits > 100, "the shipped notes carry only " + commits + " commit rows");

            // Every row must survive the round trip the renderer performs.
            foreach (PatchNotesRelease release in document.Releases)
            {
                foreach (PatchNotesBlock block in release.Blocks)
                {
                    if (block.Kind != PatchNotesBlockKind.Commits) continue;
                    foreach (string item in block.Items)
                    {
                        Assert.True(PatchNotesCommit.TryParse(item, out _),
                            "commit row does not parse: " + item);
                    }
                }
            }
        }

        [Fact]
        public void TheHeaderStripCountsCommitsNotDays()
        {
            // "14 releases" both misnames the calendar days and reports the less
            // interesting number.
            string html = PatchNotesPage.Html(Shipped());
            Assert.Contains(" commits", html, StringComparison.Ordinal);
            Assert.DoesNotContain(" releases &middot;", html, StringComparison.Ordinal);
        }

        [Fact]
        public void ACommitRendersAsAShaAndASubject()
        {
            string html = PatchNotesPage.Html(
                "## 2026-08-19 | 1 commit\n\n* 153728a Record the release\n");

            Assert.Contains("pn-commits", html, StringComparison.Ordinal);
            Assert.Contains("<code class=\"pn-sha\">153728a</code>", html, StringComparison.Ordinal);
            Assert.Contains("Record the release", html, StringComparison.Ordinal);
        }

        [Fact]
        public void TheContentsRailLeadsWithTheDate()
        {
            // The titles on a changelog are counts, and a rail of "23 commits /
            // 101 commits" is unnavigable. The date is what a reader scans for.
            string html = PatchNotesPage.Html(Shipped());
            int lead = html.IndexOf("<span class=\"pn-index-title\">19 August 2026</span>",
                StringComparison.Ordinal);
            Assert.True(lead > 0, "the rail does not lead with the newest date");
        }
    }
}

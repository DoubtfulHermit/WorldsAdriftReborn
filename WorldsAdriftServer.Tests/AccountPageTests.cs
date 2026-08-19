using WorldsAdriftServer.Emblems;
using WorldsAdriftServer.Portal;
using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// What the portal actually draws.
    ///
    /// The page takes a <see cref="PortalView"/> and asks nothing else, so these
    /// run with no database, no ledger and no clock. The assertions run in one
    /// direction throughout: a control that is NOT permitted must not be in the
    /// markup at all. Drawing a disabled control would be worse than drawing none
    /// - it tells a player the server might accept it - and drawing a live one
    /// would put a button on the page that always fails.
    /// </summary>
    public class AccountPageTests
    {
        private const string Csrf = "0123456789abcdef0123456789abcdef";

        private static readonly Guid AllianceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid MineUid = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid OtherUid = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid LeaderRankId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        private static readonly Guid MemberRankId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        private static CharacterSheet Sheet(string name = "Wrenna") => new CharacterSheet(
            MineUid, name, 0, DateTimeOffset.UnixEpoch,
            new SheetKnowledge(4, 11, 7,
                new[] { "sch_rope" },
                new[] { new SheetTally("node_iron", 3) }, 3, 2),
            new SheetInventory(10, 18, 2, 12, 1, 0, new[] { new SheetTally("iron_ore", 12) }),
            new SheetPosition(120, -3, 44, "Kestrel's Rest", true, DateTimeOffset.UnixEpoch));

        private static AllianceCard Alliance(AllianceRights rights, string name = "The Kestrels") =>
            new AllianceCard(
                AllianceId, MineUid, name, "We fly at dawn.", "Meet at the spire.",
                "Officer", new[] { "edit_group" }, false,
                new[]
                {
                    new AllianceMemberRow(MineUid, "Wrenna", "Officer", MemberRankId, false, true, false, false),
                    new AllianceMemberRow(OtherUid, "Halloran", "Member", MemberRankId, false, false,
                        rights.ManageMembers, rights.ManageMembers),
                },
                new[]
                {
                    new AllianceRankRow(LeaderRankId, "Leader", false, true, new[] { "edit_group" }),
                    new AllianceRankRow(MemberRankId, "Member", false, false, Array.Empty<string>()),
                },
                new[] { new RequestRow("invite:a", "Sesta", "Let me in", DateTimeOffset.UnixEpoch) },
                new[] { new RequestRow("invite:b", "Ovel", string.Empty, DateTimeOffset.UnixEpoch) },
                EmblemSpec.DefaultFor(AllianceId), false, null, rights);

        private static PortalView View(
            AllianceCard? alliance = null, CrewCard? crew = null,
            string? notice = null, bool isError = false, string username = "wrenna",
            string tab = PortalTabs.Account) =>
            new PortalView(
                username, username, DateTimeOffset.UnixEpoch, null, "2026.08.18", "6",
                new[] { new CharacterCard(Sheet(), crew, alliance) },
                Csrf, notice, isError, tab);

        private static string Html(PortalView view) => AccountPage.Render(view);

        /// <summary>The portal on one tab. Only the named tab's panel is rendered
        /// at all - see <see cref="PortalTabs"/> - so a test about the alliance
        /// has to ask for the alliance.</summary>
        private static string Html(PortalView view, string tab) =>
            AccountPage.Render(view with { Tab = tab });

        /// <summary>Every tab of one view, concatenated. For the assertions that
        /// are about the WHOLE page rather than about one panel: escaping, and not
        /// reaching off this host.</summary>
        private static string Everything(PortalView view)
        {
            System.Text.StringBuilder all = new System.Text.StringBuilder();
            foreach (PortalTab tab in PortalTabs.For(view))
            {
                all.Append(AccountPage.Render(view with { Tab = tab.Id }));
            }
            return all.ToString();
        }

        private static readonly string CharacterTab = PortalTabs.CharacterId(MineUid);



        // -------------------------------------------------------- the furniture

        [Fact]
        public void TheStylesheetAndTheScriptAreInThePageVerbatim()
        {
            string html = Html(View());

            Assert.Contains(WebAssets.Read("account.css"), html, StringComparison.Ordinal);

            // The script is the asset with its one placeholder filled, so what
            // ships is the file plus the server's emblem version and nothing else.
            string script = WebAssets.Read("account.js")
                .Replace("{{emblemVersion}}", EmblemSpec.Version.ToString(), StringComparison.Ordinal);
            Assert.Contains(script, html, StringComparison.Ordinal);
        }

        [Fact]
        public void ThePageCarriesNoUnfilledPlaceholder()
        {
            Assert.DoesNotContain("{{",
                Everything(View(Alliance(new AllianceRights(true, true, true, true)))),
                StringComparison.Ordinal);
        }

        /// <summary>
        /// Self-contained: a strict-CSP page with no external host at all. The
        /// only URLs it may name are its own routes.
        /// </summary>
        [Fact]
        public void NothingIsLoadedFromAnotherHost()
        {
            // The W3C SVG namespace is an IDENTIFIER, not an address: it is never
            // fetched, and createElementNS requires it verbatim. Same exemption
            // WebAssetCompositionTests makes for the console's own fragments.
            string html = Everything(View(Alliance(new AllianceRights(true, true, true, true))))
                .Replace("http://www.w3.org/2000/svg", "", StringComparison.Ordinal);

            Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("//cdn", html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TheAccountAndPatcherTabsCarryWhatTheyPromise()
        {
            string account = Html(View(), PortalTabs.Account);

            Assert.Contains("id=\"account\"", account, StringComparison.Ordinal);
            Assert.Contains("/account/password", account, StringComparison.Ordinal);
            Assert.Contains("/account/logout", account, StringComparison.Ordinal);

            string patcher = Html(View(), PortalTabs.Patcher);

            Assert.Contains("id=\"download\"", patcher, StringComparison.Ordinal);
            Assert.Contains("/download/WAPatch.exe", patcher, StringComparison.Ordinal);
            Assert.Contains("2026.08.18", patcher, StringComparison.Ordinal);
        }

        /// <summary>
        /// The strip is LINKS, one per tab, and the current one is marked for a
        /// reader as well as for an eye.
        /// </summary>
        [Fact]
        public void EveryTabIsALinkAndTheCurrentOneSaysSo()
        {
            PortalView view = View(Alliance(new AllianceRights(true, true, true, true)));
            string html = Html(view, PortalTabs.Alliance);

            foreach (PortalTab tab in PortalTabs.For(view))
            {
                Assert.Contains("href=\"/account?tab=" + tab.Id + "\"", html, StringComparison.Ordinal);
            }

            Assert.Contains("href=\"/account?tab=alliance\" class=\"on\" aria-current=\"page\"",
                html, StringComparison.Ordinal);

            // Exactly one. Two current tabs is a strip that has stopped telling
            // anybody where they are.
            Assert.Equal(1, Count(html, "aria-current=\"page\""));
        }

        /// <summary>
        /// An account whose characters are in no alliance gets NO alliance tab and
        /// NO emblem tab - rather than two tabs that exist to explain themselves.
        /// </summary>
        [Fact]
        public void TabsWithNothingBehindThemAreNotDrawn()
        {
            IReadOnlyList<PortalTab> without = PortalTabs.For(View());
            Assert.DoesNotContain(without, tab => tab.Id == PortalTabs.Alliance);
            Assert.DoesNotContain(without, tab => tab.Id == PortalTabs.Emblem);

            IReadOnlyList<PortalTab> with =
                PortalTabs.For(View(Alliance(new AllianceRights(true, true, true, true))));
            Assert.Contains(with, tab => tab.Id == PortalTabs.Alliance);
            Assert.Contains(with, tab => tab.Id == PortalTabs.Emblem);
        }

        [Fact]
        public void EveryFormCarriesTheCsrfToken()
        {
            string html = Html(View(Alliance(new AllianceRights(true, true, true, true))),
                PortalTabs.Alliance)
                + Html(View(Alliance(new AllianceRights(true, true, true, true))),
                    PortalTabs.Emblem)
                + Html(View(), PortalTabs.Account);

            int forms = Count(html, "<form ");
            int tokens = Count(html, "value=\"" + Csrf + "\"");

            Assert.True(forms > 0);
            Assert.Equal(forms, tokens);
        }

        [Fact]
        public void TheCharacterSheetShowsWhatWasBuiltForIt()
        {
            string html = Html(View(), CharacterTab);

            Assert.Contains("Wrenna", html, StringComparison.Ordinal);
            Assert.Contains("Kestrel&#39;s Rest", html, StringComparison.Ordinal);
            Assert.Contains("sch_rope", html, StringComparison.Ordinal);
            Assert.Contains("node_iron", html, StringComparison.Ordinal);
            Assert.Contains("iron_ore", html, StringComparison.Ordinal);
        }

        [Fact]
        public void AnAccountWithNoCharactersSaysSoRatherThanRenderingNothing()
        {
            PortalView empty = new PortalView(
                "wrenna", "wrenna", DateTimeOffset.UnixEpoch, null, "-", "-",
                Array.Empty<CharacterCard>(), Csrf, null, false);

            Assert.Contains("not made a character", Html(empty), StringComparison.Ordinal);
        }

        // ------------------------------------------------------- the permissions

        [Fact]
        public void WithNoPermissionsNoAllianceFormIsDrawnAtAll()
        {
            string html = Html(View(Alliance(new AllianceRights(false, false, false, false))), PortalTabs.Alliance);

            Assert.DoesNotContain("/account/alliance-details", html, StringComparison.Ordinal);
            Assert.DoesNotContain("/account/alliance-emblem", html, StringComparison.Ordinal);
            Assert.DoesNotContain("/account/alliance-member", html, StringComparison.Ordinal);
            Assert.DoesNotContain("/account/alliance-request", html, StringComparison.Ordinal);

            // And it says why rather than simply going quiet.
            Assert.Contains("no alliance permissions", html, StringComparison.Ordinal);
        }

        [Fact]
        public void TheDescriptionFormAppearsOnlyWithItsOwnPermission()
        {
            string without = Html(View(Alliance(new AllianceRights(false, true, true, true))), PortalTabs.Alliance);
            string with = Html(View(Alliance(new AllianceRights(true, false, false, false))), PortalTabs.Alliance);

            Assert.DoesNotContain("name=\"" + PortalFormPolicy.DescriptionField + "\"",
                without, StringComparison.Ordinal);
            Assert.Contains("name=\"" + PortalFormPolicy.DescriptionField + "\"",
                with, StringComparison.Ordinal);

            // Read-only still SHOWS the text - being unable to change it is not a
            // reason to hide what the alliance says about itself.
            Assert.Contains("We fly at dawn.", without, StringComparison.Ordinal);
        }

        [Fact]
        public void TheMotdFormAppearsOnlyWithItsOwnPermission()
        {
            string without = Html(View(Alliance(new AllianceRights(true, false, true, true))), PortalTabs.Alliance);
            string with = Html(View(Alliance(new AllianceRights(false, true, false, false))), PortalTabs.Alliance);

            Assert.DoesNotContain("name=\"" + PortalFormPolicy.MotdField + "\"",
                without, StringComparison.Ordinal);
            Assert.Contains("name=\"" + PortalFormPolicy.MotdField + "\"",
                with, StringComparison.Ordinal);
            Assert.Contains("Meet at the spire.", without, StringComparison.Ordinal);
        }

        /// <summary>
        /// The two text fields are edited under DIFFERENT permissions, so they
        /// must never share a form: one post carrying both would let somebody
        /// holding one of them overwrite the other field with their stale copy.
        /// </summary>
        [Fact]
        public void TheDescriptionAndTheMotdAreNeverInOneForm()
        {
            string html = Html(View(Alliance(new AllianceRights(true, true, true, true))), PortalTabs.Alliance);

            foreach (string form in Forms(html))
            {
                bool description = form.Contains("name=\"" + PortalFormPolicy.DescriptionField + "\"", StringComparison.Ordinal);
                bool motd = form.Contains("name=\"" + PortalFormPolicy.MotdField + "\"", StringComparison.Ordinal);

                Assert.False(description && motd, "one form posts both permission-separated fields");
            }
        }

        [Fact]
        public void TheEditorAppearsOnlyWithEditGroupAndIsOtherwiseJustThePicture()
        {
            string with = Html(View(Alliance(new AllianceRights(false, false, true, false))),
                PortalTabs.Emblem);
            string without = Html(View(Alliance(new AllianceRights(true, true, false, true))),
                PortalTabs.Emblem);

            Assert.Contains("/account/alliance-emblem", with, StringComparison.Ordinal);
            Assert.Contains("class=\"editor\" data-emblem>", with, StringComparison.Ordinal);

            Assert.DoesNotContain("/account/alliance-emblem", without, StringComparison.Ordinal);
            Assert.DoesNotContain("class=\"editor\" data-emblem>", without, StringComparison.Ordinal);

            // And the script is not shipped at all to somebody who cannot use it.
            Assert.DoesNotContain("embLimits", without, StringComparison.Ordinal);

            // The emblem is still SHOWN - a member who cannot change it can still
            // see what their alliance wears - and the page names the permission
            // that would unlock it, because this rank is short of only that one.
            Assert.Contains("class=\"preview\"", without, StringComparison.Ordinal);
            Assert.Contains("Changing the emblem needs", without, StringComparison.Ordinal);

            // A rank that is short of EVERYTHING gets the one summary note at the
            // foot of the alliance card instead of a per-control note as well.
            string nothing = Html(View(Alliance(new AllianceRights(false, false, false, false))),
                PortalTabs.Emblem);
            Assert.DoesNotContain("Changing the emblem needs", nothing, StringComparison.Ordinal);
            Assert.Contains("class=\"preview\"", nothing, StringComparison.Ordinal);

            // And the downloads are offered to BOTH, because saving a picture of
            // the crest is not editing it. A member who cannot change the emblem
            // can still want it for a Discord icon.
            foreach (string html in new[] { with, without, nothing })
            {
                Assert.Contains("Download as SVG", html, StringComparison.Ordinal);
                Assert.Contains("Download as PNG", html, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void TheSaveMenuOffersEverySizeTheRouteWillRenderAndNoOther()
        {
            string html = Html(View(Alliance(new AllianceRights(false, false, true, false))),
                PortalTabs.Emblem);

            EmblemArtwork crest = EmblemSpec.DefaultFor(AllianceId);

            foreach (int size in EmblemUrlPolicy.DownloadSizes)
            {
                // The exact href, not just the number: a link that 200s with the
                // wrong picture is the failure mode a download has, and the size
                // travels in the query rather than the path.
                // Encoded, because an href is attribute text: the ampersand before
                // the size is written &amp;, which is what makes it one URL with
                // two parameters rather than a URL and an entity.
                Assert.Contains(
                    "href=\"" + EmblemUrlPolicy.RasterUrl(AllianceId, crest, size)
                        .Replace("&", "&amp;", StringComparison.Ordinal) + "\"",
                    html, StringComparison.Ordinal);

                // The script re-points these at the design being edited, and reads
                // the size off the link rather than keeping its own list.
                Assert.Contains("data-savepng=\"" + size + "\"", html, StringComparison.Ordinal);
            }

            // A size the handler would refuse is not offered anywhere on the page.
            Assert.DoesNotContain("s=2048", html, StringComparison.Ordinal);
            Assert.DoesNotContain("s=4096", html, StringComparison.Ordinal);

            // `download` is what actually makes the browser save rather than
            // navigate - the route answers `inline`, deliberately, because the
            // same address is the game's crest and the editor's live preview.
            Assert.Contains("<a class=\"px\" download data-savepng=", html, StringComparison.Ordinal);
        }

        /// <summary>
        /// THE EDITOR POSTS ONE FIELD, and it is the design code.
        ///
        /// Twenty layers of eight numbers each is a design whose LAYER ORDER is
        /// data, and a form does not promise the order of its fields. It is also a
        /// textarea rather than a hidden input, so a browser with no script can
        /// still paste a design and save it.
        /// </summary>
        [Fact]
        public void TheEditorPostsTheDesignAsOneVisibleField()
        {
            string html = Html(View(Alliance(new AllianceRights(false, false, true, false))),
                PortalTabs.Emblem);

            Assert.Contains("<textarea name=\"" + EmblemFormPolicy.DesignField + "\"",
                html, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "type=\"hidden\" name=\"" + EmblemFormPolicy.DesignField + "\"",
                html, StringComparison.Ordinal);

            // And exactly the design the alliance is wearing, so opening the tab
            // and saving without touching anything is a no-op rather than a wipe.
            Assert.Contains(">" + EmblemSpec.DefaultFor(AllianceId).ToCode() + "</textarea>",
                html, StringComparison.Ordinal);
        }

        /// <summary>
        /// SYMMETRY AND THE GRID ARE BOTH REACHABLE AND BOTH SAY WHETHER THEY ARE
        /// ON.
        ///
        /// They are toggles rather than actions - a mirrored layer STAYS mirrored,
        /// and the grid stays on until it is turned off - so each carries
        /// aria-pressed. A toggle that looks like a button is the one a player
        /// presses twice and gives up on.
        /// </summary>
        [Fact]
        public void TheEditorOffersMirrorAndGridAsTogglesThatSayTheirState()
        {
            string html = Html(View(Alliance(new AllianceRights(false, false, true, false))),
                PortalTabs.Emblem);

            Assert.Contains("data-mirror aria-pressed=\"false\"", html, StringComparison.Ordinal);
            Assert.Contains("data-grid aria-pressed=\"false\"", html, StringComparison.Ordinal);

            // Neither is a form field. The mirror rides in the design code with
            // every other property of a layer, and the grid rides nowhere at all.
            Assert.DoesNotContain("name=\"mirror\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"grid\"", html, StringComparison.Ordinal);
        }

        /// <summary>
        /// THE GRID IS NOT PART OF THE DESIGN, and this is what says so.
        ///
        /// Snapping changes which values a player produces; it does not change what
        /// a value MEANS. So a crest laid out on the grid and the same crest laid
        /// out by eye must be the same string - which they are for free as long as
        /// nothing about the grid ever reaches the encoder. The limits the server
        /// stamps into the script are the place a future reader would be tempted to
        /// put a grid step, and this is the tripwire on that.
        /// </summary>
        [Fact]
        public void TheGridReachesNeitherTheEncodingNorTheServer()
        {
            Assert.DoesNotContain("grid", EmblemEditorData.LimitsJson(),
                StringComparison.OrdinalIgnoreCase);

            // And no field of the layer code is one the grid could have added.
            Assert.DoesNotContain("snap", EmblemEditorData.LimitsJson(),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The catalogue is fetched from THIS server, at a URL carrying its own
        /// revision, so it caches forever and a changed shape mints a new address.
        /// </summary>
        [Fact]
        public void TheEditorPointsAtTheServersOwnObjectCatalogue()
        {
            string html = Html(View(Alliance(new AllianceRights(false, false, true, false))),
                PortalTabs.Emblem);

            Assert.Contains(EmblemEditorData.CatalogueUrl, html, StringComparison.Ordinal);
            Assert.StartsWith("/alliance-emblem/", EmblemEditorData.CatalogueUrl, StringComparison.Ordinal);
        }

        /// <summary>
        /// The editor's script and stylesheet are served ONLY on the tab that uses
        /// them. Together they are the largest thing on the portal, and a visit
        /// about a password must not pay for them.
        /// </summary>
        [Fact]
        public void TheEditorsAssetsAreOnItsOwnTabAndNowhereElse()
        {
            PortalView view = View(Alliance(new AllianceRights(false, false, true, false)));

            Assert.Contains(WebAssets.Read("emblem-editor.css"),
                Html(view, PortalTabs.Emblem), StringComparison.Ordinal);

            foreach (string other in new[]
            {
                PortalTabs.Account, PortalTabs.Patcher, PortalTabs.Alliance, CharacterTab,
            })
            {
                string html = Html(view, other);
                Assert.DoesNotContain("emb-", html, StringComparison.Ordinal);
                Assert.DoesNotContain("embLimits", html, StringComparison.Ordinal);
                Assert.DoesNotContain(".editor .cols", html, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void MemberControlsAppearOnlyOnTheRowsThatPermitThem()
        {
            string html = Html(View(Alliance(new AllianceRights(false, false, false, true))), PortalTabs.Alliance);

            // Halloran's row permits both; the signed-in character's own does not.
            Assert.Contains(OtherUid.ToString("D"), html, StringComparison.Ordinal);
            Assert.Contains("/account/alliance-member", html, StringComparison.Ordinal);

            // The only member-form target is the row that allowed it - the actor's
            // own uid never appears as a TARGET.
            foreach (string form in Forms(html))
            {
                if (!form.Contains("/account/alliance-member", StringComparison.Ordinal)) continue;

                Assert.DoesNotContain(
                    "name=\"" + PortalFormPolicy.TargetField + "\" value=\"" + MineUid.ToString("D") + "\"",
                    form, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void WithNoMemberRightsTheRosterIsJustNames()
        {
            string html = Html(View(Alliance(new AllianceRights(true, true, true, false))), PortalTabs.Alliance);

            Assert.Contains("Halloran", html, StringComparison.Ordinal);
            Assert.DoesNotContain("/account/alliance-member", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<select class=\"rank\"", html, StringComparison.Ordinal);
        }

        /// <summary>
        /// The founder's rank is never a destination: leadership is two facts at
        /// once, and handing out the rank alone leaves the alliance disagreeing
        /// with itself about who leads it. The policy refuses it, so offering it
        /// would be offering a choice that always fails.
        /// </summary>
        [Fact]
        public void TheFoundersRankIsNeverOfferedInTheRankPicker()
        {
            string html = Html(View(Alliance(new AllianceRights(false, false, false, true))), PortalTabs.Alliance);

            Assert.Contains("<select class=\"rank\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "value=\"" + LeaderRankId.ToString("D") + "\"", html, StringComparison.Ordinal);
            Assert.Contains(
                "value=\"" + MemberRankId.ToString("D") + "\"", html, StringComparison.Ordinal);
        }

        /// <summary>
        /// A member holding a rank the picker does not offer - the founder, whose
        /// leader rank is never a destination - must be shown their rank as TEXT.
        /// A select with no matching option renders its FIRST option, so drawing
        /// one here would tell the page's reader the founder is a Deckhand.
        /// </summary>
        [Fact]
        public void AMemberOnARankThePickerCannotOfferIsShownTheirRankAsText()
        {
            AllianceRights rights = new AllianceRights(false, false, false, true);
            AllianceCard card = Alliance(rights) with
            {
                Members = new[]
                {
                    new AllianceMemberRow(OtherUid, "Halloran", "Wingleader", LeaderRankId,
                        true, false, false, true),
                },
            };

            string html = Html(View(card), PortalTabs.Alliance);

            Assert.DoesNotContain("<select class=\"rank\"", html, StringComparison.Ordinal);
            Assert.Contains("Wingleader", html, StringComparison.Ordinal);
        }

        /// <summary>
        /// The builder's own rules have to sit together with the breakpoint LAST.
        /// They did not once, and the button's <c>grid-column: 2</c> - declared
        /// after the media query - put an item in the second column of a grid the
        /// breakpoint had just made one column wide, which creates an IMPLICIT
        /// second column. The phone layout was two columns while the stylesheet
        /// said otherwise, and nothing in the CSS looked wrong.
        /// </summary>
        [Fact]
        public void NoEditorRuleFollowsTheNarrowScreenBreakpoint()
        {
            string css = WebAssets.Read("emblem-editor.css");

            int breakpoint = css.IndexOf("@media (max-width: 62rem)", StringComparison.Ordinal);
            Assert.True(breakpoint > 0, "the editor's breakpoint is gone");

            int closes = css.LastIndexOf("\n}", StringComparison.Ordinal);
            Assert.True(closes > breakpoint, "the breakpoint is not the last block in the file");

            Assert.DoesNotContain(".editor", css.Substring(closes), StringComparison.Ordinal);
        }

        [Fact]
        public void ApplicationsAndInvitationsAreListedButOnlyActionableWithEditMembers()
        {
            string with = Html(View(Alliance(new AllianceRights(false, false, false, true))), PortalTabs.Alliance);
            string without = Html(View(Alliance(new AllianceRights(true, true, true, false))), PortalTabs.Alliance);

            Assert.Contains("Sesta", with, StringComparison.Ordinal);
            Assert.Contains("Ovel", with, StringComparison.Ordinal);
            Assert.Contains("/account/alliance-request", with, StringComparison.Ordinal);

            Assert.Contains("Sesta", without, StringComparison.Ordinal);
            Assert.DoesNotContain("/account/alliance-request", without, StringComparison.Ordinal);
        }

        // ------------------------------------------------------------- the crew

        [Fact]
        public void TheCrewIsShownAndSaysItIsNotChangedHere()
        {
            CrewCard crew = new CrewCard("crew:1", "Halloran's crew", 4, new[]
            {
                new CrewMemberRow("Halloran", true, false, 0),
                new CrewMemberRow("Wrenna", false, true, null),
            });

            string html = Html(View(crew: crew), CharacterTab);

            Assert.Contains("Halloran&#39;s crew", html, StringComparison.Ordinal);
            Assert.Contains("captain", html, StringComparison.Ordinal);
            Assert.Contains("run from the Social panel", html, StringComparison.Ordinal);
        }

        // ------------------------------------------------------------ escaping

        [Fact]
        public void EveryStampedValueIsEscaped()
        {
            const string nasty = "</textarea><script>alert(1)</script>";

            PortalView view = new PortalView(
                nasty, nasty, DateTimeOffset.UnixEpoch, null, nasty, nasty,
                new[]
                {
                    new CharacterCard(
                        new CharacterSheet(MineUid, nasty, 0, DateTimeOffset.UnixEpoch,
                            new SheetKnowledge(1, 1, 0, new[] { nasty },
                                new[] { new SheetTally(nasty, 1) }, 1, 0),
                            new SheetInventory(1, 1, 1, 1, 0, 0, new[] { new SheetTally(nasty, 1) }),
                            new SheetPosition(0, 0, 0, nasty, true, DateTimeOffset.UnixEpoch)),
                        new CrewCard("crew:1", nasty, 1, new[] { new CrewMemberRow(nasty, true, true, 0) }),
                        Alliance(new AllianceRights(true, true, true, true), nasty)),
                },
                Csrf, nasty, true);

            string html = Everything(view);

            Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        }

        [Fact]
        public void ANoticeIsRenderedWithItsMood()
        {
            Assert.Contains("notice bad", Html(View(notice: "no", isError: true)), StringComparison.Ordinal);
            Assert.Contains("notice good", Html(View(notice: "yes", isError: false)), StringComparison.Ordinal);
            Assert.DoesNotContain("class=\"notice ", Html(View()), StringComparison.Ordinal);
        }

        // -------------------------------------------------------------- helpers

        private static int Count(string haystack, string needle)
        {
            int count = 0;
            int at = 0;
            while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }
            return count;
        }

        /// <summary>Every <c>&lt;form&gt;...&lt;/form&gt;</c> in the page.</summary>
        private static IEnumerable<string> Forms(string html)
        {
            int at = 0;
            while ((at = html.IndexOf("<form ", at, StringComparison.Ordinal)) >= 0)
            {
                int end = html.IndexOf("</form>", at, StringComparison.Ordinal);
                if (end < 0) yield break;

                yield return html.Substring(at, end - at);
                at = end + 7;
            }
        }
    }
}

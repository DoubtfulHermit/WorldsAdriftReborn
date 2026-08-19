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
            string? notice = null, bool isError = false, string username = "wrenna") =>
            new PortalView(
                username, username, DateTimeOffset.UnixEpoch, null, "2026.08.18", "6",
                new[] { new CharacterCard(Sheet(), crew, alliance) },
                Csrf, notice, isError);

        private static string Html(PortalView view) => AccountPage.Render(view);

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
            Assert.DoesNotContain("{{", Html(View()), StringComparison.Ordinal);
        }

        /// <summary>
        /// Self-contained: a strict-CSP page with no external host at all. The
        /// only URLs it may name are its own routes.
        /// </summary>
        [Fact]
        public void NothingIsLoadedFromAnotherHost()
        {
            string html = Html(View(Alliance(new AllianceRights(true, true, true, true))));

            Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("//cdn", html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TheAccountAndPatcherSectionsAreAlwaysThere()
        {
            string html = Html(View());

            Assert.Contains("id=\"account\"", html, StringComparison.Ordinal);
            Assert.Contains("id=\"download\"", html, StringComparison.Ordinal);
            Assert.Contains("/download/WAPatch.exe", html, StringComparison.Ordinal);
            Assert.Contains("2026.08.18", html, StringComparison.Ordinal);
            Assert.Contains("/account/password", html, StringComparison.Ordinal);
            Assert.Contains("/account/logout", html, StringComparison.Ordinal);
        }

        [Fact]
        public void EveryFormCarriesTheCsrfToken()
        {
            string html = Html(View(Alliance(new AllianceRights(true, true, true, true))));

            int forms = Count(html, "<form ");
            int tokens = Count(html, "value=\"" + Csrf + "\"");

            Assert.True(forms > 0);
            Assert.Equal(forms, tokens);
        }

        [Fact]
        public void TheCharacterSheetShowsWhatWasBuiltForIt()
        {
            string html = Html(View());

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
            string html = Html(View(Alliance(new AllianceRights(false, false, false, false))));

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
            string without = Html(View(Alliance(new AllianceRights(false, true, true, true))));
            string with = Html(View(Alliance(new AllianceRights(true, false, false, false))));

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
            string without = Html(View(Alliance(new AllianceRights(true, false, true, true))));
            string with = Html(View(Alliance(new AllianceRights(false, true, false, false))));

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
            string html = Html(View(Alliance(new AllianceRights(true, true, true, true))));

            foreach (string form in Forms(html))
            {
                bool description = form.Contains("name=\"" + PortalFormPolicy.DescriptionField + "\"", StringComparison.Ordinal);
                bool motd = form.Contains("name=\"" + PortalFormPolicy.MotdField + "\"", StringComparison.Ordinal);

                Assert.False(description && motd, "one form posts both permission-separated fields");
            }
        }

        [Fact]
        public void TheCrestBuilderAppearsOnlyWithEditGroupAndIsOtherwiseJustThePicture()
        {
            string with = Html(View(Alliance(new AllianceRights(false, false, true, false))));
            string without = Html(View(Alliance(new AllianceRights(true, true, false, true))));

            Assert.Contains("/account/alliance-emblem", with, StringComparison.Ordinal);
            Assert.Contains("class=\"builder\"", with, StringComparison.Ordinal);

            Assert.DoesNotContain("/account/alliance-emblem", without, StringComparison.Ordinal);
            Assert.DoesNotContain("class=\"builder\"", without, StringComparison.Ordinal);

            // The crest is still SHOWN - a member who cannot change it can still
            // see what their alliance wears - and the page names the permission
            // that would unlock it, because this rank is short of only that one.
            Assert.Contains("class=\"preview\"", without, StringComparison.Ordinal);
            Assert.Contains("Changing the crest needs", without, StringComparison.Ordinal);

            // A rank that is short of EVERYTHING gets the one summary note at the
            // foot of the card instead of a per-control note as well.
            string nothing = Html(View(Alliance(new AllianceRights(false, false, false, false))));
            Assert.DoesNotContain("Changing the crest needs", nothing, StringComparison.Ordinal);
            Assert.Contains("class=\"preview\"", nothing, StringComparison.Ordinal);
        }

        /// <summary>
        /// The preview is the REAL renderer's route, not a canvas. Two renderers
        /// of one picture drift silently.
        /// </summary>
        [Fact]
        public void ThePreviewPointsAtTheSameRouteTheGameDownloadsFrom()
        {
            string html = Html(View(Alliance(new AllianceRights(false, false, true, false))));

            Assert.Contains("/alliance-emblem/preview.png", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<canvas", html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MemberControlsAppearOnlyOnTheRowsThatPermitThem()
        {
            string html = Html(View(Alliance(new AllianceRights(false, false, false, true))));

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
            string html = Html(View(Alliance(new AllianceRights(true, true, true, false))));

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
            string html = Html(View(Alliance(new AllianceRights(false, false, false, true))));

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

            string html = Html(View(card));

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
        public void NoBuilderRuleFollowsTheNarrowScreenBreakpoint()
        {
            string css = WebAssets.Read("account.css");

            int breakpoint = css.IndexOf("@media (max-width: 36rem)", StringComparison.Ordinal);
            Assert.True(breakpoint > 0, "the builder's breakpoint is gone");

            int closes = css.IndexOf("\n}", breakpoint, StringComparison.Ordinal);
            Assert.True(closes > breakpoint);

            Assert.DoesNotContain(".builder", css.Substring(closes), StringComparison.Ordinal);
        }

        [Fact]
        public void ApplicationsAndInvitationsAreListedButOnlyActionableWithEditMembers()
        {
            string with = Html(View(Alliance(new AllianceRights(false, false, false, true))));
            string without = Html(View(Alliance(new AllianceRights(true, true, true, false))));

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

            string html = Html(View(crew: crew));

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

            string html = AccountPage.Render(view);

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

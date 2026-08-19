using WorldsAdriftServer.Emblems;
using WorldsAdriftServer.Portal;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// Which tab the portal is on.
    ///
    /// The interesting cases are all about NOT trusting the URL: a tab that does
    /// not exist, a tab that stopped existing when somebody left an alliance, and
    /// a query value that is not a tab id at all. None of them may produce an
    /// error page - the portal is where a player lands after signing in, and a
    /// stale bookmark should open it.
    /// </summary>
    public class PortalTabsTests
    {
        private static readonly Guid AllianceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid MineUid = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid OtherUid = Guid.Parse("33333333-3333-3333-3333-333333333333");

        private static CharacterSheet Sheet(Guid uid, string name) =>
            new CharacterSheet(uid, name, 0, DateTimeOffset.UnixEpoch, null, null, null);

        private static AllianceCard Alliance() => new AllianceCard(
            AllianceId, MineUid, "The Kestrels", string.Empty, string.Empty, "Officer",
            Array.Empty<string>(), false,
            Array.Empty<AllianceMemberRow>(), Array.Empty<AllianceRankRow>(),
            Array.Empty<RequestRow>(), Array.Empty<RequestRow>(),
            EmblemSpec.DefaultFor(AllianceId), false, null,
            new AllianceRights(true, true, true, true));

        private static PortalView View(params CharacterCard[] characters) =>
            new PortalView("wrenna", "wrenna", DateTimeOffset.UnixEpoch, null, "-", "-",
                characters, new string('a', 32), null, false);

        private static PortalView Alone(bool inAlliance) =>
            View(new CharacterCard(Sheet(MineUid, "Wrenna"), null, inAlliance ? Alliance() : null));

        // ------------------------------------------------------------ the strip

        [Fact]
        public void The_account_and_the_patcher_are_always_there_and_always_first()
        {
            IReadOnlyList<PortalTab> tabs = PortalTabs.For(Alone(false));

            Assert.Equal(PortalTabs.Account, tabs[0].Id);
            Assert.Equal(PortalTabs.Patcher, tabs[1].Id);
        }

        [Fact]
        public void Each_character_gets_a_tab_named_after_it()
        {
            PortalView view = View(
                new CharacterCard(Sheet(MineUid, "Wrenna"), null, null),
                new CharacterCard(Sheet(OtherUid, "Halloran"), null, null));

            IReadOnlyList<PortalTab> tabs = PortalTabs.For(view);

            Assert.Contains(tabs, tab => tab.Id == PortalTabs.CharacterId(MineUid) && tab.Label == "Wrenna");
            Assert.Contains(tabs, tab => tab.Id == PortalTabs.CharacterId(OtherUid) && tab.Label == "Halloran");
        }

        /// <summary>
        /// A tab id is a QUERY VALUE and an HTML id at once, so it is letters and
        /// digits and nothing else - there is nothing in it to escape at either
        /// end.
        /// </summary>
        [Fact]
        public void Every_tab_id_is_safe_in_a_url_and_in_markup()
        {
            foreach (PortalTab tab in PortalTabs.For(Alone(true)))
            {
                Assert.Equal(tab.Id, Uri.EscapeDataString(tab.Id));
                Assert.All(tab.Id, c => Assert.True(char.IsLetterOrDigit(c) && c < 128));
            }
        }

        [Fact]
        public void The_alliance_and_emblem_tabs_appear_only_when_there_is_an_alliance()
        {
            Assert.DoesNotContain(PortalTabs.For(Alone(false)), tab => tab.Id == PortalTabs.Alliance);
            Assert.DoesNotContain(PortalTabs.For(Alone(false)), tab => tab.Id == PortalTabs.Emblem);

            Assert.Contains(PortalTabs.For(Alone(true)), tab => tab.Id == PortalTabs.Alliance);
            Assert.Contains(PortalTabs.For(Alone(true)), tab => tab.Id == PortalTabs.Emblem);
        }

        /// <summary>
        /// One alliance tab, not one per character. Two characters of the same
        /// account can be in two alliances; the tab lists both.
        /// </summary>
        [Fact]
        public void Two_characters_in_alliances_still_share_one_alliance_tab()
        {
            PortalView view = View(
                new CharacterCard(Sheet(MineUid, "Wrenna"), null, Alliance()),
                new CharacterCard(Sheet(OtherUid, "Halloran"), null, Alliance()));

            Assert.Single(PortalTabs.For(view).Where(tab => tab.Id == PortalTabs.Alliance));
        }

        // -------------------------------------------------------- the resolution

        [Fact]
        public void A_tab_that_exists_is_the_one_returned()
        {
            IReadOnlyList<PortalTab> tabs = PortalTabs.For(Alone(true));

            Assert.Equal(PortalTabs.Emblem, PortalTabs.Resolve(PortalTabs.Emblem, tabs));
            Assert.Equal(PortalTabs.CharacterId(MineUid),
                PortalTabs.Resolve(PortalTabs.CharacterId(MineUid), tabs));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("nope")]
        [InlineData("emblem")]     // real, but this account is in no alliance
        [InlineData("c00000000000000000000000000000000")]
        public void A_tab_that_is_not_there_falls_back_to_the_first(string? asked)
        {
            Assert.Equal(PortalTabs.Account, PortalTabs.Resolve(asked, PortalTabs.For(Alone(false))));
        }

        [Fact]
        public void Resolving_against_no_tabs_at_all_still_answers()
        {
            Assert.Equal(PortalTabs.Account, PortalTabs.Resolve("emblem", Array.Empty<PortalTab>()));
            Assert.Equal(PortalTabs.Account, PortalTabs.Resolve(null, null!));
        }

        // ---------------------------------------------------------- reading a url

        [Theory]
        [InlineData("/account", null)]
        [InlineData("/account?tab=alliance", "alliance")]
        [InlineData("/account?m=crest-saved&tab=emblem", "emblem")]
        [InlineData("/account?tab=emblem&m=crest-saved", "emblem")]
        [InlineData("/account?tab=", null)]
        [InlineData("/account?tab=has-a-hyphen", null)]
        [InlineData("/account?tab=%3Cscript%3E", null)]
        [InlineData("/account?tab=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null)]
        [InlineData("/account?tabby=alliance", null)]
        public void Only_a_well_formed_tab_id_is_read_out_of_a_url(string url, string? expected)
        {
            Assert.Equal(expected, PortalTabs.Requested(url));
        }

        [Fact]
        public void The_last_value_of_the_key_wins_and_a_bad_one_poisons_it()
        {
            Assert.Equal("alliance", PortalTabs.Requested("/account?tab=emblem&tab=alliance"));

            // A second, malformed value is not ignored in favour of the first: a
            // URL that says two things must not be read as whichever one we liked.
            Assert.Null(PortalTabs.Requested("/account?tab=alliance&tab=not+a+tab"));
        }

        // ---------------------------------------------------------- after a post

        /// <summary>
        /// WHERE A SAVE COMES BACK TO. Derived from the route rather than from a
        /// hidden field, so no form can forget to carry it - a crest save that
        /// dumped the player on the Account tab is exactly the small wrongness that
        /// makes a tabbed page feel broken.
        /// </summary>
        [Theory]
        [InlineData("/account/alliance-emblem", "emblem")]
        [InlineData("/account/alliance-details", "alliance")]
        [InlineData("/account/alliance-member", "alliance")]
        [InlineData("/account/alliance-request", "alliance")]
        [InlineData("/account/password", "account")]
        [InlineData("/account/logout", "account")]
        [InlineData("/account", "account")]
        [InlineData("", "account")]
        [InlineData(null, "account")]
        public void A_post_returns_to_the_tab_it_was_made_in(string? path, string expected)
        {
            Assert.Equal(expected, PortalTabs.AfterPost(path));
        }

        [Fact]
        public void The_redirect_url_carries_the_tab_and_the_notice_together()
        {
            Assert.Equal("/account?tab=emblem", PortalTabs.Url("/account", PortalTabs.Emblem));
            Assert.Equal("/account?tab=emblem&m=crest-saved",
                PortalTabs.Url("/account", PortalTabs.Emblem, PortalNotices.CrestSaved));

            // And what comes back out is what went in - the redirect and the reader
            // are the two halves of one round trip.
            string url = PortalTabs.Url("/account", PortalTabs.Alliance, PortalNotices.Denied);

            Assert.Equal(PortalTabs.Alliance, PortalTabs.Requested(url));
            Assert.Equal(PortalNotices.Denied, PortalNotices.CodeFrom(url));
        }
    }
}

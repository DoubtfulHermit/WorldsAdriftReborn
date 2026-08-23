using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    public class PublicSiteChromeTests
    {
        [Fact]
        public void EveryAnonymousPageCarriesTheSharedNavigationAndTheme()
        {
            string[] pages =
            {
                LoginPage.Html,
                SignupPage.Html,
                DownloadPage.Render("pilot", "1", "2"),
                PatchNotesPage.Html("## 2026-08-23 | Test\n- one change"),
                PublicMapPage.Html("{}", "{}"),
            };

            foreach (string page in pages)
            {
                Assert.Contains(WebAssets.Read("site-shell.css"), page, StringComparison.Ordinal);
                Assert.Contains("class=\"wa-sitebar\"", page, StringComparison.Ordinal);
                Assert.Contains("href=\"/\"", page, StringComparison.Ordinal);
                Assert.Contains("href=\"/map\"", page, StringComparison.Ordinal);
                Assert.Contains("href=\"/patchnotes\"", page, StringComparison.Ordinal);
                Assert.Contains("href=\"/download\"", page, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void PlayerPagesShareTheHomepagePaletteWithoutTheLegacySky()
        {
            string[] pages =
            {
                LoginPage.Html,
                SignupPage.Html,
                DownloadPage.Render("pilot", "1", "2"),
            };

            foreach (string page in pages)
            {
                Assert.Contains(WebAssets.Read("site-player.css"), page, StringComparison.Ordinal);
                Assert.Contains("class=\"wa-player", page, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void SignedInNavigationPointsAtTheCrewPortalInsteadOfSignIn()
        {
            string signedIn = PublicSiteChrome.Header("account", true);
            Assert.Contains("href=\"/account\" aria-current=\"location\">Crew portal", signedIn,
                StringComparison.Ordinal);
            Assert.DoesNotContain("href=\"/login\"", signedIn, StringComparison.Ordinal);
            Assert.DoesNotContain("href=\"/signup\"", signedIn, StringComparison.Ordinal);
        }

        [Fact]
        public void MobileNavigationIsNativeAndNeedsNoAdditionalScript()
        {
            string header = PublicSiteChrome.Header("map", false);
            Assert.Contains("<details class=\"wa-mobile-nav\">", header, StringComparison.Ordinal);
            Assert.Equal(2, Occurrences(header, "href=\"/map\" aria-current=\"location\""));
            Assert.DoesNotContain("onclick", header, StringComparison.OrdinalIgnoreCase);
        }

        private static int Occurrences(string value, string needle)
        {
            int count = 0;
            int at = 0;
            while ((at = value.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }
            return count;
        }
    }
}

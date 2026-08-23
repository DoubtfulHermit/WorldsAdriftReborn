using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Handlers.PublicSite;
using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    public class HomePageTests
    {
        [Fact]
        public void HomeComposesTheEmbeddedAssetsThatShip()
        {
            Assert.Contains(WebAssets.Read("home.css"), HomePage.Html, StringComparison.Ordinal);
            Assert.Contains(WebAssets.Read("home-body.html"), HomePage.Html, StringComparison.Ordinal);
            Assert.Contains(WebAssets.Read("home.js"), HomePage.Html, StringComparison.Ordinal);
        }

        [Fact]
        public void HomeOwnsOnlyTheExactRoot()
        {
            Assert.True(HomeHandler.Owns("/"));
            Assert.True(HomeHandler.Owns("/?from=discord"));
            Assert.False(HomeHandler.Owns("/signup"));
            Assert.False(HomeHandler.Owns("//signup"));
            Assert.False(HomeHandler.Owns("/admin"));
            Assert.False(HomeHandler.Owns("/map"));
            Assert.False(HomeHandler.Owns(null));
        }

        [Theory]
        [InlineData("/signup")]
        [InlineData("/login")]
        [InlineData("/account")]
        [InlineData("/download")]
        [InlineData("/map")]
        [InlineData("/patchnotes")]
        public void HomePreservesEveryPublicRouteContract(string route)
        {
            Assert.Contains("href=\"" + route + "\"", HomePage.Html, StringComparison.Ordinal);
        }

        [Fact]
        public void AllianceAndSocialEntryRemainsBehindTheAccountPortal()
        {
            Assert.Contains("href=\"/account\"", HomePage.Html, StringComparison.Ordinal);
            Assert.Contains("Account and crew portal", HomePage.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("/alliance", HomePage.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("/memberships", HomePage.Html, StringComparison.Ordinal);
        }

        [Fact]
        public void PublicPageDoesNotEmbedOperatorDataOrRelaxAdminCookieScope()
        {
            Assert.DoesNotContain("/admin/api", HomePage.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("admin-console.js", HomePage.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("admin-wiring.js", HomePage.Html, StringComparison.Ordinal);
            Assert.Equal("/admin", AdminAuthPolicy.CookiePath);
        }

        [Fact]
        public void ClaimsDistinguishCurrentRuntimeFromFutureDistribution()
        {
            Assert.Contains("One process hosts everything", HomePage.Html,
                StringComparison.Ordinal);
            Assert.Contains("Not live yet:</b> remote workers", HomePage.Html,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("vector, collision and docking systems advance through shadow evidence",
                HomePage.Html, StringComparison.Ordinal);
            Assert.Contains("A domain is an ownership unit today—not a separate server",
                HomePage.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("invitation", HomePage.Html,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RoadmapCountsAreDerivedFromTheObjectivesItShows()
        {
            string body = WebAssets.Read("home-body.html");
            Assert.Equal(20, Occurrences(body, "data-state=\"done\""));
            Assert.Equal(5, Occurrences(body, "data-state=\"flight\""));
            Assert.Equal(2, Occurrences(body, "data-state=\"gate\""));
            Assert.Equal(5, Occurrences(body, "data-state=\"open\""));
            Assert.Contains("These totals count the 32 objectives represented above",
                HomePage.Html, StringComparison.Ordinal);
            Assert.Contains("They are not a percentage of the whole game",
                HomePage.Html, StringComparison.Ordinal);
        }

        [Fact]
        public void RoadmapHasExplicitStatusesAndAccessibleProgress()
        {
            Assert.Equal(6, Occurrences(HomePage.Html, "role=\"progressbar\""));
            Assert.Contains("aria-label=\"Roadmap status legend\"", HomePage.Html,
                StringComparison.Ordinal);
            Assert.Contains("Latest verified milestone", HomePage.Html,
                StringComparison.Ordinal);
            Assert.Contains("Mission status", HomePage.Html, StringComparison.Ordinal);
        }

        [Fact]
        public void HeroContainsNoPlaceholderIslandOrShipIllustration()
        {
            Assert.DoesNotContain("<svg", HomePage.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hero-world", HomePage.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("skyship approaching", HomePage.Html,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PageHasNoExternalRuntimeAssets()
        {
            Assert.DoesNotContain("<img", HomePage.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<script src=", HomePage.Html,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("@import", HomePage.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("url(http", HomePage.Html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PageHasBasicAccessibleNavigationAndMotionFallback()
        {
            Assert.Contains("href=\"#main\"", HomePage.Html, StringComparison.Ordinal);
            Assert.Contains("aria-label=\"Primary\"", HomePage.Html, StringComparison.Ordinal);
            Assert.Contains("aria-expanded=\"false\"", HomePage.Html, StringComparison.Ordinal);
            Assert.Contains("prefers-reduced-motion", HomePage.Html, StringComparison.Ordinal);
            Assert.Contains("<h1", HomePage.Html, StringComparison.Ordinal);
        }

        [Fact]
        public void UnclaimedRequestsHaveADeterministicTerminalResponse()
        {
            Assert.Equal(404, NotFoundHandler.StatusCode);
            Assert.Equal("Not found.", NotFoundHandler.Body);
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

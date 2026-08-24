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
            Assert.Contains("One machine runs the world", HomePage.Html,
                StringComparison.Ordinal);
            Assert.Contains("Design target, not a live claim", HomePage.Html,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Vector force/torque is a live pure-shadow observer",
                HomePage.Html, StringComparison.Ordinal);
            Assert.Contains("lift, collision and docking policies are compiled but default-off",
                HomePage.Html, StringComparison.Ordinal);
            Assert.Contains("vector force and torque are shadow-only",
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
            Assert.Equal(21, Occurrences(body, "data-state=\"done\""));
            Assert.Equal(5, Occurrences(body, "data-state=\"flight\""));
            Assert.Equal(2, Occurrences(body, "data-state=\"gate\""));
            Assert.Equal(5, Occurrences(body, "data-state=\"open\""));
            Assert.Contains("These totals count the 33 objectives represented above",
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
        public void PageUsesOneThemeWithoutTheOldSpectrumDivider()
        {
            Assert.DoesNotContain("class=\"spectrum", HomePage.Html,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("linear-gradient(90deg, #744d8c", HomePage.Html,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Fuelled engines fly. The hull now has one pose authority",
                HomePage.Html, StringComparison.Ordinal);
            Assert.Contains("One familiar address", HomePage.Html, StringComparison.Ordinal);
        }

        [Fact]
        public void CurrentFlightClaimSeparatesLivePassesFromThePendingRetest()
        {
            Assert.Contains("data-game-status-through=\"93ab672\"", HomePage.Html,
                StringComparison.Ordinal);
            Assert.Contains("Engine and generator-fuel path", HomePage.Html,
                StringComparison.Ordinal);
            Assert.Contains("visual spin, authoritative burn, empty cutoff and partial-fuel restart passed live checks",
                HomePage.Html, StringComparison.Ordinal);
            Assert.Contains("turn and low-speed settling need the final visual retest",
                HomePage.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("5 ships", HomePage.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("3,788", HomePage.Html, StringComparison.Ordinal);
        }

        [Fact]
        public void HomeCanBeWrittenForDesktopAndMobileVisualReview()
        {
            string? path = Environment.GetEnvironmentVariable("WAREBORN_HOME_DUMP");
            if (string.IsNullOrWhiteSpace(path)) return;

            string? parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(path, HomePage.Html);
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

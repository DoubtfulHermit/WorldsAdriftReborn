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
            Assert.Contains("Form a crew", HomePage.Html, StringComparison.Ordinal);
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
            Assert.Contains("runs as one authoritative process today", HomePage.Html,
                StringComparison.Ordinal);
            Assert.Contains("remote workers not yet enabled", HomePage.Html,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("active parity testing", HomePage.Html,
                StringComparison.Ordinal);
            Assert.Contains("vector flight and collision models remain observation-only",
                HomePage.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("invitation", HomePage.Html,
                StringComparison.OrdinalIgnoreCase);
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
    }
}

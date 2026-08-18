using WorldsAdriftServer.PublicMap;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The public map's routing table and its load-shedding cache.
    ///
    /// The routing matters because it is the list of what is reachable with
    /// NO authentication: exactly four GET routes, and everything else under
    /// /map is claimed-and-refused rather than left to fall through to some
    /// other handler. The cache matters because the endpoint is public: it is
    /// what keeps N viewers from becoming N stats-file reads per poll.
    /// </summary>
    public class PublicMapRoutesAndCacheTests
    {
        // ---- routing --------------------------------------------------------

        // The route enum is internal (rightly), so the theory rows name routes
        // as strings and compare against the enum's own name.
        [Theory]
        [InlineData("GET", "/map", "Page")]
        [InlineData("GET", "/map/", "Page")]
        [InlineData("GET", "/map?embed=1", "Page")]
        [InlineData("GET", "/map/data", "LiveData")]
        [InlineData("GET", "/map/data?ts=123", "LiveData")]
        [InlineData("GET", "/map/world", "WorldData")]
        [InlineData("HEAD", "/map/data", "LiveData")]
        [InlineData("GET", "/map/viewers", "Viewers")]
        [InlineData("HEAD", "/map/viewers", "Viewers")]
        // The viewer heartbeat rides the live poll, so a tokened URL must still
        // route as the live feed and not as something new.
        [InlineData("GET", "/map/data?v=0123456789abcdef", "LiveData")]
        public void KnownRoutesMatch(string method, string url, string expected)
        {
            Assert.Equal(expected, PublicMapRoutes.Match(method, url).ToString());
        }

        [Theory]
        [InlineData("GET", "/")]
        [InlineData("GET", "/maps")]                  // prefix must not over-match
        [InlineData("GET", "/mapdata")]
        [InlineData("GET", "/admin")]
        [InlineData("GET", "/admin/api/stats")]
        public void ForeignUrlsAreNotOurs(string method, string url)
        {
            Assert.Equal(PublicMapRoute.None, PublicMapRoutes.Match(method, url));
        }

        [Theory]
        [InlineData("GET", "/map/anything")]          // unknown path: claimed, 404
        [InlineData("GET", "/map/data/extra")]
        [InlineData("GET", "/map/../admin")]          // no traversal into other routes
        [InlineData("POST", "/map/data")]             // no verbs but GET/HEAD
        [InlineData("POST", "/map")]
        [InlineData("DELETE", "/map/world")]
        public void EverythingElseUnderMapIsClaimedAndRefused(string method, string url)
        {
            Assert.Equal(PublicMapRoute.NotFound, PublicMapRoutes.Match(method, url));
        }

        // ---- cache ----------------------------------------------------------

        private static readonly DateTimeOffset T0 =
            DateTimeOffset.FromUnixTimeMilliseconds(1_723_200_000_000);

        [Fact]
        public void FreshnessWindowIsTheTtl()
        {
            Assert.True(PublicMapCache.IsFresh(T0, T0));
            Assert.True(PublicMapCache.IsFresh(T0, T0 + PublicMapCache.Ttl - TimeSpan.FromMilliseconds(1)));
            Assert.False(PublicMapCache.IsFresh(T0, T0 + PublicMapCache.Ttl));
            // A build stamped in the future (clock stepped back) is not fresh:
            // better one redundant rebuild than a payload pinned until the
            // clock catches up.
            Assert.False(PublicMapCache.IsFresh(T0, T0 - TimeSpan.FromMilliseconds(1)));
        }

        [Fact]
        public void CacheServesWithinTtlAndExpiresAfter()
        {
            PublicMapCache cache = new PublicMapCache();
            Assert.False(cache.TryGet(T0, out _));

            cache.Store("payload-one", T0);
            Assert.True(cache.TryGet(T0 + TimeSpan.FromSeconds(1), out string hit));
            Assert.Equal("payload-one", hit);

            Assert.False(cache.TryGet(T0 + PublicMapCache.Ttl, out _));

            cache.Store("payload-two", T0 + PublicMapCache.Ttl);
            Assert.True(cache.TryGet(T0 + PublicMapCache.Ttl, out hit));
            Assert.Equal("payload-two", hit);
        }

        [Fact]
        public void TtlSitsUnderTheWritersCadence()
        {
            // The game server rewrites the stats file every ~3 s; the public
            // TTL must stay under that so no viewer ever waits out two writes.
            Assert.True(PublicMapCache.Ttl < TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void ACacheMayBeGivenItsOwnWindowForASlowerSource()
        {
            // The viewer trend's rows only change once a minute, so caching it on
            // the live payload's two seconds would mean sixty database round trips
            // to produce sixty identical answers.
            PublicMapCache slow = new PublicMapCache(TimeSpan.FromMinutes(1));
            slow.Store("trend", T0);

            Assert.True(slow.TryGet(T0 + TimeSpan.FromSeconds(59), out string hit));
            Assert.Equal("trend", hit);
            Assert.False(slow.TryGet(T0 + TimeSpan.FromSeconds(60), out _));

            // And the default is unchanged for everybody who does not ask.
            PublicMapCache normal = new PublicMapCache();
            normal.Store("live", T0);
            Assert.False(normal.TryGet(T0 + PublicMapCache.Ttl, out _));

            Assert.Throws<ArgumentOutOfRangeException>(() => new PublicMapCache(TimeSpan.Zero));
        }
    }
}

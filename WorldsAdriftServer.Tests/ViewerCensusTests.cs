using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Admin;
using WorldsAdriftServer.PublicMap;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The live viewer count, and the reasons it is structurally unable to say
    /// who anybody is.
    ///
    /// These tests are written the way <see cref="PublicMapProjectionTests"/> is:
    /// not "does it count", which is easy, but "can it be made to remember a
    /// person", which is the property that has to survive future edits. So the
    /// assertions go at the door (what shapes are even accepted), at the store
    /// (what is held, and for how long) and at the wire (what the payload can
    /// contain).
    /// </summary>
    public class ViewerCensusTests
    {
        private static readonly DateTimeOffset Now =
            DateTimeOffset.FromUnixTimeMilliseconds(1_723_200_123_000);

        private static byte[] FilledSalt(byte value)
        {
            byte[] salt = new byte[32];
            Array.Fill(salt, value);
            return salt;
        }

        private static readonly byte[] SaltA = FilledSalt(0x11);
        private static readonly byte[] SaltB = FilledSalt(0x22);

        private static ViewerCensus Census() => new ViewerCensus(SaltA);

        // ---- the door -----------------------------------------------------------

        [Theory]
        [InlineData("0123456789abcdef0123456789abcdef")]  // what the page really sends
        [InlineData("abcdefgh")]                          // the shortest allowed
        [InlineData("ABCDEFGH12345678")]
        public void AWellFormedTokenIsAccepted(string token)
        {
            Assert.True(ViewerToken.IsWellFormed(token));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("short7")]                            // under the minimum
        [InlineData("203.0.113.9")]                       // an address, plainly
        [InlineData("2001:db8::1")]                       // and the other kind
        [InlineData("someone@example.com")]               // a person
        [InlineData("Mozilla/5.0 (X11; Linux)")]          // a user agent
        [InlineData("%32%30%33%2e%30%2e%31%31%33")]       // an address, percent-encoded
        [InlineData("token with spaces")]
        [InlineData("token-with-dashes")]
        [InlineData("../../etc/passwd")]
        public void AnythingThatCouldCarryIdentityIsRefusedAtTheDoor(string? token)
        {
            // The census never sees these at all, so there is no salted hash of
            // an address held even for a moment. Note the percent-encoded case in
            // particular: the value is never decoded, so a client cannot dress an
            // address up as something acceptable.
            Assert.False(ViewerToken.IsWellFormed(token));

            ViewerCensus census = Census();
            Assert.False(census.Beat(token, Now));
            Assert.Equal(0, census.Count(Now));
        }

        [Fact]
        public void AnAbsurdlyLongTokenIsRefusedRatherThanHashed()
        {
            Assert.False(ViewerToken.IsWellFormed(new string('a', ViewerToken.MaxLength + 1)));
            Assert.True(ViewerToken.IsWellFormed(new string('a', ViewerToken.MaxLength)));
        }

        [Theory]
        [InlineData("/map/data?v=0123456789abcdef", "0123456789abcdef")]
        [InlineData("/map/data?other=1&v=0123456789abcdef", "0123456789abcdef")]
        [InlineData("/map/data?v=0123456789abcdef&other=1", "0123456789abcdef")]
        [InlineData("/map/data?v=0123456789abcdef#frag", "0123456789abcdef")]
        public void TheTokenIsReadOffTheQueryString(string url, string expected)
        {
            Assert.Equal(expected, ViewerToken.FromUrl(url));
        }

        [Theory]
        [InlineData("/map/data")]
        [InlineData("/map/data?")]
        [InlineData("/map/data?v=")]
        [InlineData("/map/data?v=203.0.113.9")]
        [InlineData("/map/data?vv=0123456789abcdef")]
        [InlineData(null)]
        public void ARequestWithNoUsableTokenSimplyIsNotCounted(string? url)
        {
            // Not an error: a third party embedding the open feed, a crawler or
            // curl all land here, and all of them still get the data.
            Assert.Null(ViewerToken.FromUrl(url));
        }

        // ---- the store ----------------------------------------------------------

        [Fact]
        public void EachDistinctTabCountsOnceHoweverOftenItPolls()
        {
            ViewerCensus census = Census();

            for (int i = 0; i < 20; i++)
            {
                census.Beat("aaaaaaaaaaaaaaaa", Now + TimeSpan.FromSeconds(i));
                census.Beat("bbbbbbbbbbbbbbbb", Now + TimeSpan.FromSeconds(i));
            }

            Assert.Equal(2, census.Count(Now + TimeSpan.FromSeconds(19)));
        }

        [Fact]
        public void TwoTabsCountTwiceBecauseNothingLinksThemAndNothingShould()
        {
            // The documented consequence of minting a token per page load rather
            // than per person: linking two tabs to one viewer would mean
            // recognising a viewer, which is the thing this must not do.
            ViewerCensus census = Census();
            census.Beat("tab1aaaaaaaaaaaa", Now);
            census.Beat("tab2aaaaaaaaaaaa", Now);
            Assert.Equal(2, census.Count(Now));
        }

        [Fact]
        public void AClosedTabDecaysOutOfTheCountRatherThanVanishing()
        {
            ViewerCensus census = Census();
            census.Beat("aaaaaaaaaaaaaaaa", Now);

            // Still counted right up to the TTL...
            Assert.Equal(1, census.Count(Now + ViewerCensus.Ttl - TimeSpan.FromMilliseconds(1)));

            // ...and gone at it.
            Assert.Equal(0, census.Count(Now + ViewerCensus.Ttl));
        }

        [Fact]
        public void TheTtlIsShorterThanABrowsersBackgroundThrottleSoAHiddenTabLeaves()
        {
            // A backgrounded tab is throttled to roughly one timer a minute, so a
            // TTL under that minute is what makes the number mean "looking at it"
            // rather than "has it open somewhere". It must also be several polls
            // long, or a single dropped request would make the count flicker.
            Assert.True(ViewerCensus.Ttl < TimeSpan.FromSeconds(60));
            Assert.True(ViewerCensus.Ttl >= TimeSpan.FromSeconds(15));
        }

        [Fact]
        public void NothingSurvivesItsTtlEvenWhenNobodyAsksForTheCount()
        {
            // Pruning happens on every write as well as every read, so a census
            // nobody is reading is not a census quietly accumulating fingerprints.
            ViewerCensus census = Census();
            for (int i = 0; i < 50; i++)
            {
                census.Beat("tab" + i.ToString("D13"), Now + TimeSpan.FromSeconds(i));
            }

            // At t+49s only the beats from t+19s onwards are inside a 30 s TTL.
            Assert.Equal(30, census.Count(Now + TimeSpan.FromSeconds(49)));
        }

        [Fact]
        public void AFloodOfTokensSaturatesRatherThanGrowingWithoutBound()
        {
            // The endpoint is unauthenticated, so "allocates per distinct token"
            // would otherwise be a way to spend the server's memory from the
            // internet. The count stops rising; the server keeps working.
            ViewerCensus census = Census();
            for (int i = 0; i < ViewerCensus.MaxTracked + 500; i++)
            {
                census.Beat("flood" + i.ToString("D11"), Now);
            }

            Assert.Equal(ViewerCensus.MaxTracked, census.Count(Now));

            // And an existing viewer can still beat, so a flood cannot lock the
            // people who are already there out of the count.
            census.Beat("flood" + 0.ToString("D11"), Now + TimeSpan.FromSeconds(1));
            Assert.Equal(ViewerCensus.MaxTracked, census.Count(Now + TimeSpan.FromSeconds(1)));
        }

        // ---- what is actually held ----------------------------------------------

        [Fact]
        public void WhatIsHeldIsNotWhatTheClientSent()
        {
            // The fingerprint is a salted digest, so even a client that
            // deliberately picks an identifying string leaves nothing readable in
            // memory. The salt is generated at boot and never written anywhere.
            string identifying = "203011309deadbeef";
            string held = ViewerCensus.Fingerprint(identifying, SaltA);

            Assert.DoesNotContain(identifying, held, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("2030113", held, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(64, held.Length);
        }

        [Fact]
        public void AFingerprintIsStableWithinASaltAndUnlinkableAcrossRestarts()
        {
            Assert.Equal(
                ViewerCensus.Fingerprint("aaaaaaaaaaaaaaaa", SaltA),
                ViewerCensus.Fingerprint("aaaaaaaaaaaaaaaa", SaltA));

            // A new salt every boot is what stops a viewer being recognised across
            // days - the same rule the map markers already follow.
            Assert.NotEqual(
                ViewerCensus.Fingerprint("aaaaaaaaaaaaaaaa", SaltA),
                ViewerCensus.Fingerprint("aaaaaaaaaaaaaaaa", SaltB));
        }

        [Fact]
        public void AViewerAndAMapMarkerCannotBeJoinedToEachOther()
        {
            // The two anonymised populations share a salt, so they must not share
            // a key space: a viewer token that happened to equal an entity id must
            // not produce the marker's token.
            Assert.NotEqual(
                ViewerCensus.Fingerprint("987654321", SaltA),
                PublicMapProjection.AnonymousId("player", 987654321, SaltA));
        }

        [Fact]
        public void TheSharedCensusRidesTheSameRotatingSaltTheMarkersDo()
        {
            Assert.Equal(
                ViewerCensus.Fingerprint("aaaaaaaaaaaaaaaa", PublicMapProjection.ProcessSalt),
                ViewerCensus.Fingerprint("aaaaaaaaaaaaaaaa", PublicMapProjection.ProcessSalt));
            Assert.Equal(32, PublicMapProjection.ProcessSalt.Length);
        }

        // ---- the wire -----------------------------------------------------------

        [Fact]
        public void TheCountOnTheFeedComesFromTheCensusAndNeverFromTheStatsFile()
        {
            // The corpus in PublicMapProjectionTests seeds a viewers OBJECT with
            // addresses in it. This is the other half of that: the published
            // "viewers" key is bound to the parameter, so a stats file cannot fill
            // it however it is shaped.
            JObject o = PublicMapProjection.Project(GameStatsResult.Missing(), SaltA, 7);
            Assert.Equal(7, (int?)o["viewers"]);

            JObject none = PublicMapProjection.Project(GameStatsResult.Missing(), SaltA);
            Assert.Equal(0, (int?)none["viewers"]);
        }

        [Fact]
        public void TheCountSurvivesTheGameServerBeingDown()
        {
            // People are still looking at the page during an outage, and dropping
            // the field would leave the page unable to tell "nobody is here" from
            // "we stopped counting".
            JObject o = PublicMapProjection.Project(GameStatsResult.Missing(), SaltA, 4);
            Assert.False((bool?)o["reporting"]);
            Assert.Equal(4, (int?)o["viewers"]);
        }

        [Fact]
        public void ANegativeCountIsClampedRatherThanPublished()
        {
            Assert.Equal(0, (int?)PublicMapProjection.Project(GameStatsResult.Missing(), SaltA, -3)["viewers"]);
        }
    }
}

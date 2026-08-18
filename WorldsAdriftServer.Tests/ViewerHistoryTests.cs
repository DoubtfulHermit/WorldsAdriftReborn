using Newtonsoft.Json.Linq;
using WorldsAdriftServer.PublicMap;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The recorded viewer series: the bucketing that turns minutes into a
    /// drawable line, the sampler that writes it, and the shape of what goes on
    /// the wire.
    ///
    /// The shape assertions matter as much as the maths ones. The payload is the
    /// only place the recorded history is published, so pinning its keys is what
    /// makes "the recorded data is aggregate-only" a checked fact rather than a
    /// claim in a comment.
    /// </summary>
    public class ViewerHistoryTests
    {
        private static readonly DateTimeOffset Origin =
            new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        private static (DateTimeOffset, int) At(int minutes, int count) =>
            (Origin + TimeSpan.FromMinutes(minutes), count);

        // ---- bucketing ----------------------------------------------------------

        [Fact]
        public void BucketEdgesAreAbsoluteSoTheLineDoesNotShuffleBetweenPolls()
        {
            // Aligned to the epoch rather than to "now": two viewers asking a few
            // seconds apart must get the same buckets, or the drawn line slides
            // sideways every refresh.
            DateTimeOffset odd = new DateTimeOffset(2026, 8, 17, 12, 37, 41, TimeSpan.Zero);

            Assert.Equal(new DateTimeOffset(2026, 8, 17, 12, 30, 0, TimeSpan.Zero),
                ViewerHistory.FloorTo(odd, TimeSpan.FromMinutes(10)));
            Assert.Equal(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero),
                ViewerHistory.FloorTo(odd, TimeSpan.FromHours(1)));

            // Already on an edge stays put.
            Assert.Equal(Origin, ViewerHistory.FloorTo(Origin, TimeSpan.FromMinutes(10)));
        }

        [Fact]
        public void ABucketTakesTheBusiestMinuteInIt()
        {
            // The peak rather than the mean: these are small integers, and an
            // average turns "three people for four minutes" into 1.2 and flattens
            // the line into nothing.
            int[] series = ViewerHistory.Bucket(
                new[] { At(0, 1), At(3, 5), At(7, 2), At(12, 9) },
                Origin, TimeSpan.FromMinutes(10), 3);

            Assert.Equal(new[] { 5, 9, 0 }, series);
        }

        [Fact]
        public void AMinuteTheServerWasDownIsADipRatherThanAnInterpolation()
        {
            // The sampler runs whether or not anybody is watching, so a gap means
            // the server was not running - which is a thing an operator wants to
            // see, not smooth over.
            int[] series = ViewerHistory.Bucket(
                new[] { At(0, 4), At(30, 4) },
                Origin, TimeSpan.FromMinutes(10), 4);

            Assert.Equal(new[] { 4, 0, 0, 4 }, series);
        }

        [Fact]
        public void SamplesOutsideTheWindowAreDroppedRatherThanFoldedIntoTheEdges()
        {
            int[] series = ViewerHistory.Bucket(
                new[] { At(-30, 99), At(5, 3), At(500, 99) },
                Origin, TimeSpan.FromMinutes(10), 3);

            Assert.Equal(new[] { 3, 0, 0 }, series);
        }

        [Fact]
        public void AnEmptyOrMissingSeriesIsAFlatLineRatherThanAThrow()
        {
            Assert.Equal(new[] { 0, 0, 0 },
                ViewerHistory.Bucket(Array.Empty<(DateTimeOffset, int)>(), Origin, TimeSpan.FromMinutes(10), 3));
            Assert.Equal(new[] { 0, 0, 0 },
                ViewerHistory.Bucket(null!, Origin, TimeSpan.FromMinutes(10), 3));
            Assert.Empty(ViewerHistory.Bucket(null!, Origin, TimeSpan.FromMinutes(10), 0));
        }

        [Fact]
        public void ANonPositiveStepIsLoudRatherThanADivideByZero()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ViewerHistory.Bucket(null!, Origin, TimeSpan.Zero, 3));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ViewerHistory.FloorTo(Origin, TimeSpan.Zero));
        }

        [Fact]
        public void ThePublicWindowIsADayAndTheOperatorsIsAMonth()
        {
            // The console is authenticated and may legitimately show more; the
            // public page shows the modest version. Both are the same aggregate
            // rows, just more or fewer of them.
            Assert.Equal(TimeSpan.FromDays(1),
                ViewerHistory.PublicStep * ViewerHistory.PublicBuckets);
            Assert.Equal(TimeSpan.FromDays(30),
                ViewerHistory.AdminStep * ViewerHistory.AdminBuckets);
            Assert.True(ViewerHistory.AdminStep > ViewerHistory.PublicStep);
        }

        // ---- the payload --------------------------------------------------------

        [Fact]
        public void ThePayloadIsCountsAndNothingElse()
        {
            // The tripwire for the recorded half of this feature. Every key here
            // is a number or an array of numbers; there is nowhere for a visitor
            // to be, and a change that wanted one would have to edit this list.
            JObject payload = ViewerHistory.Payload(
                3, new[] { At(0, 2), At(11, 5) }, Origin, TimeSpan.FromMinutes(10), 3);

            Assert.Equal(new[] { "now", "peak", "fromUnixMs", "stepSeconds", "points" },
                payload.Properties().Select(p => p.Name).ToArray());

            Assert.Equal(3, (int?)payload["now"]);
            Assert.Equal(5, (int?)payload["peak"]);
            Assert.Equal(Origin.ToUnixTimeMilliseconds(), (long?)payload["fromUnixMs"]);
            Assert.Equal(600, (int?)payload["stepSeconds"]);

            JArray points = (JArray)payload["points"]!;
            Assert.Equal(3, points.Count);
            foreach (JToken point in points)
            {
                Assert.Equal(JTokenType.Integer, point.Type);
            }
        }

        [Fact]
        public void ThePeakNeverReadsLowerThanTheLiveCount()
        {
            // The recorded series is a minute behind, so a fresh spike would
            // otherwise print "5 now, 2 peak", which reads as a bug.
            JObject payload = ViewerHistory.Payload(
                9, new[] { At(0, 2) }, Origin, TimeSpan.FromMinutes(10), 2);

            Assert.Equal(9, (int?)payload["peak"]);
        }

        [Fact]
        public void PeakOfNothingIsZeroRatherThanAThrow()
        {
            Assert.Equal(0, ViewerHistory.Peak(Array.Empty<(DateTimeOffset, int)>()));
            Assert.Equal(0, ViewerHistory.Peak(null!));
        }

        // ---- the sampler --------------------------------------------------------

        [Fact]
        public void TheSamplerWritesTheCountItWasGivenAtTheInstantItWasGiven()
        {
            List<(DateTimeOffset At, int Count)> written = new List<(DateTimeOffset, int)>();
            using ViewerSampler sampler = new ViewerSampler(
                _ => 4, (at, n) => written.Add((at, n)), () => Origin, _ => { }, started: false);

            sampler.Tick();

            Assert.Equal((Origin, 4), Assert.Single(written));
        }

        [Fact]
        public void ADatabaseHiccupDoesNotTakeTheLoginServerDownWithIt()
        {
            // This runs on a thread-pool timer, where an escaping exception ends
            // the process. A number on a map must not be able to do that.
            List<string> logged = new List<string>();
            using ViewerSampler sampler = new ViewerSampler(
                _ => 1,
                (_, _) => throw new InvalidOperationException("the database is asleep"),
                () => Origin,
                logged.Add,
                started: false);

            for (int i = 0; i < 5; i++)
            {
                sampler.Tick();
            }

            Assert.Equal(5, sampler.ConsecutiveFailures);

            // Logged once, not once per tick: sixty lines an hour is how an
            // operator learns to ignore the log.
            Assert.Single(logged);
            Assert.Contains("viewer count", logged[0], StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TheFailureCounterResetsOnceItWorksAgain()
        {
            bool broken = true;
            using ViewerSampler sampler = new ViewerSampler(
                _ => 1,
                (_, _) => { if (broken) throw new InvalidOperationException("nope"); },
                () => Origin,
                _ => { },
                started: false);

            sampler.Tick();
            Assert.Equal(1, sampler.ConsecutiveFailures);

            broken = false;
            sampler.Tick();
            Assert.Equal(0, sampler.ConsecutiveFailures);
        }

        [Fact]
        public void TheSamplingIntervalIsAMinuteRatherThanPerRequest()
        {
            // The grain IS a privacy property, not a performance one: a row per
            // request would make the series' own density a visit log, legible in
            // where the rows start and stop even with no identifying column.
            Assert.Equal(TimeSpan.FromMinutes(1), ViewerSampler.Interval);
        }
    }
}

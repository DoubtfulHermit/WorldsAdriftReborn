using Newtonsoft.Json.Linq;

namespace WorldsAdriftServer.PublicMap
{
    /// <summary>
    /// Turns the recorded <c>(timestamp, count)</c> series into something a
    /// sparkline can draw, and nothing more.
    ///
    /// Pure: it takes a list of pairs and returns numbers. No clock, no database,
    /// no request - so the bucketing rules can be tested on their own, and so the
    /// only thing that can possibly come out the far end is an array of counts.
    /// There is deliberately no shape here that could carry a per-viewer row even
    /// if the table one day grew one: the input type is a pair of a time and an
    /// integer, and a leak would have to change this signature to happen.
    ///
    /// The buckets are aligned to the step rather than to "now", so two viewers
    /// asking a few seconds apart get the same buckets and the drawn line does not
    /// shuffle sideways between polls.
    /// </summary>
    internal static class ViewerHistory
    {
        /// <summary>
        /// The public page's window: a day, in ten-minute buckets. 144 points is
        /// plenty of shape for a line a couple of hundred pixels wide, and it is
        /// coarse enough that the series says "the map was busy last night"
        /// without saying anything about a single visit.
        /// </summary>
        internal static readonly TimeSpan PublicStep = TimeSpan.FromMinutes(10);

        internal const int PublicBuckets = 144;

        /// <summary>
        /// The operator's window: a month, in hourly buckets. Longer because the
        /// console is authenticated and an operator is the person who wants to
        /// know whether last week's post actually brought anybody - still the same
        /// aggregate rows, just more of them.
        /// </summary>
        internal static readonly TimeSpan AdminStep = TimeSpan.FromHours(1);

        internal const int AdminBuckets = 720;

        /// <summary>
        /// Rounds an instant DOWN to a multiple of <paramref name="step"/> since
        /// the Unix epoch, so bucket edges are absolute rather than relative to
        /// whoever asked.
        /// </summary>
        internal static DateTimeOffset FloorTo(DateTimeOffset at, TimeSpan step)
        {
            if (step <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(step), step, "A bucket step must be positive.");
            }

            long ticks = at.UtcTicks;
            return new DateTimeOffset(ticks - ticks % step.Ticks, TimeSpan.Zero);
        }

        /// <summary>
        /// Buckets <paramref name="samples"/> into <paramref name="buckets"/> slots
        /// of <paramref name="step"/> starting at <paramref name="from"/>, taking
        /// the HIGHEST sample in each slot.
        ///
        /// Highest rather than average on purpose. The numbers here are small
        /// integers, so an average turns "three people were watching for four
        /// minutes" into 1.2 and the line flattens into nothing; the peak keeps the
        /// shape a person is actually looking for. It is also the honest match for
        /// the "peak" readout printed beside it.
        ///
        /// A slot with no sample in it is 0, which is the truth: the sampler runs
        /// every minute regardless of traffic, so a gap means the server was down,
        /// and drawing that as a dip rather than interpolating over it is the
        /// behaviour an operator would want.
        /// </summary>
        internal static int[] Bucket(
            IReadOnlyList<(DateTimeOffset At, int Count)> samples,
            DateTimeOffset from,
            TimeSpan step,
            int buckets)
        {
            if (buckets < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(buckets), buckets, "A series cannot have a negative length.");
            }

            if (step <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(step), step, "A bucket step must be positive.");
            }

            int[] series = new int[buckets];
            if (samples == null)
            {
                return series;
            }

            foreach ((DateTimeOffset at, int count) in samples)
            {
                long offset = at.UtcTicks - from.UtcTicks;
                if (offset < 0)
                {
                    continue;
                }

                long index = offset / step.Ticks;
                if (index >= buckets)
                {
                    continue;
                }

                int slot = (int)index;
                if (count > series[slot])
                {
                    series[slot] = count;
                }
            }

            return series;
        }

        /// <summary>The highest count in a series, or 0 for an empty one.</summary>
        internal static int Peak(IReadOnlyList<(DateTimeOffset At, int Count)> samples)
        {
            int peak = 0;
            if (samples == null)
            {
                return peak;
            }

            foreach ((DateTimeOffset _, int count) in samples)
            {
                if (count > peak)
                {
                    peak = count;
                }
            }

            return peak;
        }

        /// <summary>
        /// The wire payload for a trend readout.
        ///
        /// Every field is an aggregate by construction: a live count, a peak, the
        /// window's start and step so the browser can put the line on a time axis,
        /// and a flat array of integers. There is no room in this shape for a
        /// visitor, which is the point - a future change that wanted to publish one
        /// would have to add a field here and to the tests that pin this shape.
        /// </summary>
        internal static JObject Payload(
            int now,
            IReadOnlyList<(DateTimeOffset At, int Count)> samples,
            DateTimeOffset from,
            TimeSpan step,
            int buckets)
        {
            int[] series = Bucket(samples, from, step, buckets);

            JArray points = new JArray();
            foreach (int value in series)
            {
                points.Add(value);
            }

            return new JObject
            {
                ["now"] = now,
                ["peak"] = Math.Max(Peak(samples), now),
                ["fromUnixMs"] = from.ToUnixTimeMilliseconds(),
                ["stepSeconds"] = (int)step.TotalSeconds,
                ["points"] = points,
            };
        }
    }
}

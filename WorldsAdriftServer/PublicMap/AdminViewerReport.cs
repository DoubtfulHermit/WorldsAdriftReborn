using Newtonsoft.Json.Linq;

namespace WorldsAdriftServer.PublicMap
{
    /// <summary>
    /// The operator console's view of the map's audience.
    ///
    /// The console is authenticated, so it may legitimately show more than the
    /// public page - and the point of this class is exactly HOW MUCH more, stated
    /// once: a longer window (a month of hourly buckets against a day of
    /// ten-minute ones), the all-time peak, and how many minutes have been
    /// recorded. That is all. There is no per-viewer detail behind the operator
    /// login because none exists anywhere to put there: this reads the same
    /// two-column aggregate table the public feed reads.
    ///
    /// Worth saying plainly, because "the admin console can see more" is normally
    /// how a privacy boundary erodes. Here the authentication is buying LENGTH,
    /// not RESOLUTION. If a future change wants the console to answer "who", it
    /// has to add a column to <c>map_viewer_samples</c> first, and that is a
    /// migration an operator watches go past.
    ///
    /// Degrades rather than throws: an unreachable database costs the console its
    /// history, not its dashboard.
    /// </summary>
    internal static class AdminViewerReport
    {
        internal static string Json(DateTimeOffset now)
        {
            int live = ViewerCensus.Shared.Count(now);

            DateTimeOffset to = ViewerHistory.FloorTo(now, ViewerHistory.AdminStep)
                + ViewerHistory.AdminStep;
            DateTimeOffset from = to - ViewerHistory.AdminStep * ViewerHistory.AdminBuckets;

            IReadOnlyList<(DateTimeOffset At, int Count)> samples;
            int peakAllTime;
            long recordedMinutes;
            bool recording;

            try
            {
                samples = Persistence.Accounts.ViewerSamples.Between(from, to);
                peakAllTime = Persistence.Accounts.ViewerSamples.PeakAllTime();
                recordedMinutes = Persistence.Accounts.ViewerSamples.Count();
                recording = true;
            }
            catch (Exception)
            {
                samples = Array.Empty<(DateTimeOffset, int)>();
                peakAllTime = 0;
                recordedMinutes = 0;
                recording = false;
            }

            JObject payload = ViewerHistory.Payload(
                live, samples, from, ViewerHistory.AdminStep, ViewerHistory.AdminBuckets);

            payload["peakAllTime"] = Math.Max(peakAllTime, live);
            payload["recordedMinutes"] = recordedMinutes;
            payload["recording"] = recording;
            payload["ttlSeconds"] = (int)ViewerCensus.Ttl.TotalSeconds;

            return PublicMapProjection.Serialize(payload);
        }
    }
}

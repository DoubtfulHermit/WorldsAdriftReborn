using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// Pure policy for the CLIENT's island asset-bundle loads.
    ///
    /// WHY THIS EXISTS. Island terrain is the only prefab the client still loads
    /// from an on-disk AssetBundle: every other entity prefab is fetched from
    /// resources.assets by <c>ResourcesGameObjectLoader</c>, which uses an async
    /// <c>Resources.LoadAsync</c> coroutine. Names containing "@Island" are
    /// routed instead to the bundle loader, whose LOCAL strategy calls
    /// <c>AssetBundle.LoadFromFile</c> + <c>LoadAsset&lt;GameObject&gt;</c>
    /// SYNCHRONOUSLY, on the main thread, from inside the SpatialOS op pump. The
    /// island bundles in this build average 8 MiB and reach 46 MiB, so one
    /// checkout blocks the frame for hundreds of milliseconds.
    ///
    /// Doing that load asynchronously needs exactly one piece of decidable
    /// policy, and it is here rather than in the mod so it can be unit-tested
    /// natively: WHICH names take the bundle path, and WHETHER a second request
    /// for a bundle already being loaded must start a second load (it must not -
    /// two concurrent <c>LoadFromFileAsync</c> calls on one file fail in Unity)
    /// or simply join the first one's waiters.
    ///
    /// Keep this file net35/C# 7.3 compatible: the client mod links this exact
    /// source, while the native tests compile it in the multiplayer assembly.
    /// </summary>
    public static class IslandBundleLoadPolicy
    {
        /// <summary>
        /// The marker the retail loader itself keys on (ResourcesGameObjectLoader
        /// hands "@Island" names to the bundle loader and everything else to
        /// Resources). Matched case-insensitively because the on-disk bundle file
        /// names are lower-cased while the prefab name is not.
        /// </summary>
        public const string IslandBundleMarker = "@Island";

        /// <summary>
        /// A load that has not reported back after this long is presumed
        /// abandoned - the only way that happens is a destroyed coroutine host -
        /// and a fresh load is allowed. Deliberately longer than the server's
        /// 30 s asset-ack fallback, so this NEVER fires for a load that is merely
        /// slow; by the time it can fire the server has already given up waiting
        /// and this is a rescue, not a retry.
        /// </summary>
        public const double DefaultStaleLoadSeconds = 60.0;

        /// <summary>Whether this prefab name is served by the island bundle loader.</summary>
        public static bool IsIslandBundle(string prefabName)
        {
            if (prefabName == null || prefabName.Length == 0) return false;
            return prefabName.IndexOf(IslandBundleMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// The bundle's file name, exactly as LocalAssetBundleLoader derives it
        /// (<c>prefabName.ToLower()</c>). Reproduced rather than reflected so the
        /// mod never has to guess at the naming rule.
        /// </summary>
        public static string BundleFileName(string prefabName)
        {
            if (prefabName == null) return null;
            return prefabName.ToLowerInvariant();
        }
    }

    /// <summary>
    /// The in-flight ledger for asynchronous island bundle loads: one load per
    /// bundle name, every request for that name gets the result.
    ///
    /// The synchronous loader could not have this problem - it returned before
    /// the next request could arrive. Once the load spans frames, the server's
    /// 5 s asset-request retry (and any second peer-driven checkout of the same
    /// island) can legally arrive mid-load, and starting a second
    /// <c>AssetBundle.LoadFromFileAsync</c> for a file already being loaded is a
    /// Unity error, not a duplicate.
    ///
    /// Not thread-safe and does not need to be: every caller is a Unity main
    /// thread dispatch callback or coroutine step.
    /// </summary>
    public sealed class IslandBundleLoadLedger
    {
        private sealed class Entry
        {
            public double StartedAt;
            public readonly List<object> Waiters = new List<object>();
        }

        private readonly Dictionary<string, Entry> _inFlight = new Dictionary<string, Entry>();

        /// <summary>Bundle names with a load currently in flight.</summary>
        public int InFlightCount { get { return _inFlight.Count; } }

        public bool IsInFlight(string prefabName)
        {
            if (prefabName == null || prefabName.Length == 0) return false;
            return _inFlight.ContainsKey(prefabName);
        }

        /// <summary>
        /// Registers <paramref name="waiter"/> for this bundle and reports
        /// whether the CALLER must start the load.
        ///
        /// True on the first request for a name, and again if the recorded load
        /// went stale (see <see cref="IslandBundleLoadPolicy.DefaultStaleLoadSeconds"/>);
        /// false when an existing load will deliver the result. Waiters already
        /// registered are never dropped by a stale restart - they are still owed
        /// a callback and the restarted load is what will pay them.
        ///
        /// A null/empty name is not tracked at all and always returns true, so a
        /// caller that somehow has no name degrades to the old one-load-per-call
        /// behaviour rather than to a dropped callback.
        /// </summary>
        public bool BeginOrJoin(string prefabName, object waiter, double nowSeconds,
            double staleAfterSeconds)
        {
            if (prefabName == null || prefabName.Length == 0) return true;

            Entry entry;
            if (!_inFlight.TryGetValue(prefabName, out entry))
            {
                entry = new Entry { StartedAt = nowSeconds };
                _inFlight[prefabName] = entry;
                if (waiter != null) entry.Waiters.Add(waiter);
                return true;
            }

            if (waiter != null) entry.Waiters.Add(waiter);

            bool stale = staleAfterSeconds > 0.0
                && nowSeconds - entry.StartedAt >= staleAfterSeconds;
            if (stale)
            {
                entry.StartedAt = nowSeconds;
                return true;
            }
            return false;
        }

        public bool BeginOrJoin(string prefabName, object waiter, double nowSeconds)
        {
            return BeginOrJoin(prefabName, waiter, nowSeconds,
                IslandBundleLoadPolicy.DefaultStaleLoadSeconds);
        }

        /// <summary>
        /// Ends the load for this bundle and hands back every waiter, in arrival
        /// order, so the caller can deliver one result to each. Always returns a
        /// list (empty for an unknown or already-completed name), because a
        /// duplicate completion must be a no-op, never a crash inside a
        /// coroutine where the exception would be swallowed.
        /// </summary>
        public IList<object> TakeWaiters(string prefabName)
        {
            Entry entry;
            if (prefabName == null || prefabName.Length == 0
                || !_inFlight.TryGetValue(prefabName, out entry))
            {
                return new List<object>();
            }
            _inFlight.Remove(prefabName);
            return entry.Waiters;
        }
    }
}

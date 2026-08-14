using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    public enum ResourceStreamActionKind { Add, Remove }

    public readonly record struct ResourceStreamAction(ResourceStreamActionKind Kind, long EntityId);

    /// <summary>Pure geometry, classification and hysteresis for roaming resource checkout.</summary>
    public static class ResourceInterestPolicy
    {
        public const string UnloadRadiusEnvVar = "WAREBORN_INTEREST_UNLOAD_RADIUS_M";
        public const string SettleDelayEnvVar = "WAREBORN_INTEREST_SETTLE_MS";
        public const double DefaultUnloadMarginMetres = 35.0;
        public const int DefaultSettleDelayMs = 5000;
        public const int MinSettleDelayMs = 1000;
        public const int MaxSettleDelayMs = 30000;

        /// <summary>
        /// Post-connect quiet period before continuous resource checkout starts.
        /// The retail client creates entities synchronously on its main thread; this
        /// lets activation, movement and visualizer initialization settle before the
        /// paced roaming stream begins. It cannot be disabled accidentally.
        /// </summary>
        public static TimeSpan SettleDelayFrom(string? env)
        {
            if (!int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                value = DefaultSettleDelayMs;
            }
            value = System.Math.Clamp(value, MinSettleDelayMs, MaxSettleDelayMs);
            return TimeSpan.FromMilliseconds(value);
        }

        public static bool IsStreamedResourceKey(string? key) =>
            HasPrefix(key, "deposit-") || HasPrefix(key, "atlas-shard-")
            || HasPrefix(key, IslandResourceLedger.KeyPrefix)
            || HasPrefix(key, "fuel-pod-") || HasPrefix(key, "tree-")
            || HasPrefix(key, "databank-") || HasPrefix(key, "metal-");

        /// <summary>
        /// Runtime registrations use the spatial service only when interest is
        /// enabled and the key is a resource family. Global biome data and every
        /// other essential entity retain the immediate broadcast path.
        /// </summary>
        public static bool StreamsRuntimeRegistration(string? key, bool interestEnabled) =>
            interestEnabled && IsStreamedResourceKey(key);

        /// <summary>
        /// Whether a component-interest request may seed an entity. Only a spatially
        /// streamed resource under enabled interest needs an active per-peer checkout;
        /// disabled interest and essential/non-resource entities always pass.
        /// </summary>
        public static bool MayServeComponents(bool interestEnabled, bool isStreamedResource, bool loadedForPeer) =>
            !interestEnabled || !isStreamedResource || loadedForPeer;

        /// <summary>
        /// Revalidates queued lifecycle work at the final send boundary. The
        /// connect-time spawn plan and the roaming-interest queue share the same
        /// loaded set but advance independently: while an Add waits behind its asset
        /// request, the spawn plan may add that entity first. Sending the stale Add
        /// corrupts the retail client's entity map, so queued work is never trusted
        /// without checking the current checkout state again.
        /// </summary>
        public static bool ShouldExecute(
            bool connectPlanComplete,
            ResourceStreamAction action,
            ISet<long> loaded) =>
            connectPlanComplete && (action.Kind == ResourceStreamActionKind.Add
                ? !loaded.Contains(action.EntityId)
                : loaded.Contains(action.EntityId));

        public static double UnloadRadiusFrom(string? env, double loadRadius)
        {
            if (loadRadius <= 0) return 0;
            if (!double.TryParse(env, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.IsNaN(value) || value <= loadRadius)
            {
                return System.Math.Min(InterestPolicy.MaxRadiusMetres, loadRadius + DefaultUnloadMarginMetres);
            }
            return System.Math.Min(value, InterestPolicy.MaxRadiusMetres);
        }

        public static IReadOnlyList<ResourceStreamAction> Reconcile(
            FixedPointPosition center,
            IEnumerable<(long Id, FixedPointPosition Position)> resources,
            ISet<long> loaded,
            double loadRadius,
            double unloadRadius)
        {
            List<(long Id, double DistanceSquared)> adds = new();
            List<(long Id, double DistanceSquared)> removes = new();
            double load2 = loadRadius * loadRadius;
            double unload2 = unloadRadius * unloadRadius;
            foreach ((long id, FixedPointPosition position) in resources)
            {
                double dx = center.MetresX - position.MetresX;
                double dy = center.MetresY - position.MetresY;
                double dz = center.MetresZ - position.MetresZ;
                double d2 = dx * dx + dy * dy + dz * dz;
                if (loaded.Contains(id))
                {
                    if (d2 > unload2) removes.Add((id, d2));
                }
                else if (d2 <= load2)
                {
                    adds.Add((id, d2));
                }
            }
            adds.Sort((a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));
            removes.Sort((a, b) => b.DistanceSquared.CompareTo(a.DistanceSquared));
            List<ResourceStreamAction> result = new(adds.Count + removes.Count);
            result.AddRange(removes.Select(x => new ResourceStreamAction(ResourceStreamActionKind.Remove, x.Id)));
            result.AddRange(adds.Select(x => new ResourceStreamAction(ResourceStreamActionKind.Add, x.Id)));
            return result;
        }

        /// <summary>
        /// Replaces, rather than appends to, a peer's pending lifecycle work. A
        /// roaming/flying player may cross the whole load radius before a large queue
        /// drains; retaining stale adds would instantiate the place they already left.
        /// </summary>
        public static void ReplacePending(
            Queue<ResourceStreamAction> pending,
            IEnumerable<ResourceStreamAction> current,
            int maximum)
        {
            if (pending == null) throw new ArgumentNullException(nameof(pending));
            if (current == null) throw new ArgumentNullException(nameof(current));
            pending.Clear();
            if (maximum <= 0) return;
            foreach (ResourceStreamAction action in current.Take(maximum)) pending.Enqueue(action);
        }

        private static bool HasPrefix(string? key, string prefix) =>
            key != null && key.StartsWith(prefix, StringComparison.Ordinal);
    }
}

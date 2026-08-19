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
            || HasPrefix(key, "databank-") || HasPrefix(key, "metal-")
            // Loot containers. Note the ordering trap: "loot-" must be tested
            // AFTER nothing and BEFORE nothing in particular, but it must be here
            // AT ALL. A resource key outside this allowlist is broadcast eagerly
            // instead of spatially streamed AND is skipped by
            // ActivateBoundResources, so a container left off this line would
            // render on every client at once and open into nothing.
            || HasPrefix(key, LootContainers.KeyPrefix);

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

        /// <summary>Squared metres between two world positions. The one spelling of it.</summary>
        public static double DistanceSquared(FixedPointPosition a, FixedPointPosition b)
        {
            double dx = a.MetresX - b.MetresX;
            double dy = a.MetresY - b.MetresY;
            double dz = a.MetresZ - b.MetresZ;
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>
        /// Turns "which resources should this peer hold" into the ordered lifecycle
        /// work that gets it there. The MEMBERSHIP question is the caller's -
        /// <paramref name="resources"/> arrives already carrying each entity's
        /// Desired flag - and this owns only the ORDERING, which is geometry:
        /// removals farthest-first, then additions NEAREST-FIRST, so a peer arriving
        /// at an island sees the nodes at its feet before the ones across the ridge.
        ///
        /// Splitting membership from ordering is what lets island-keyed checkout
        /// (<see cref="Islands.IslandResourceCheckoutPolicy"/>) reuse this instead of
        /// growing a second, eventually-divergent copy of the same sort.
        /// </summary>
        public static IReadOnlyList<ResourceStreamAction> Reconcile(
            FixedPointPosition center,
            IEnumerable<(long Id, FixedPointPosition Position, bool Desired)> resources,
            ISet<long> loaded)
        {
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            if (loaded == null) throw new ArgumentNullException(nameof(loaded));

            List<(long Id, double DistanceSquared)> adds = new();
            List<(long Id, double DistanceSquared)> removes = new();
            foreach ((long id, FixedPointPosition position, bool desired) in resources)
            {
                double d2 = DistanceSquared(center, position);
                if (loaded.Contains(id))
                {
                    if (!desired) removes.Add((id, d2));
                }
                else if (desired)
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
        /// The player-centred sphere: a resource is desired while it is inside
        /// <paramref name="loadRadius"/>, and once held it stays desired out to
        /// <paramref name="unloadRadius"/>. Expressing the hysteresis as a Desired
        /// flag makes it exactly a special case of the reconcile above, which is why
        /// there is only one sort in this file.
        ///
        /// NOT USED FOR ISLAND RESOURCE CHECKOUT ANY MORE. A release island is up to
        /// 735 m across and this bubble is 240 m across, so it holds a fraction of an
        /// island and empties it as the player walks - see
        /// <see cref="Islands.IslandInterestAdmissionPolicy"/>. It remains the right
        /// answer for anything genuinely centred on the player.
        /// </summary>
        public static IReadOnlyList<ResourceStreamAction> Reconcile(
            FixedPointPosition center,
            IEnumerable<(long Id, FixedPointPosition Position)> resources,
            ISet<long> loaded,
            double loadRadius,
            double unloadRadius)
        {
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            if (loaded == null) throw new ArgumentNullException(nameof(loaded));

            double load2 = loadRadius * loadRadius;
            double unload2 = unloadRadius * unloadRadius;
            return Reconcile(center, resources.Select(resource =>
            {
                double d2 = DistanceSquared(center, resource.Position);
                bool desired = loaded.Contains(resource.Id) ? d2 <= unload2 : d2 <= load2;
                return (resource.Id, resource.Position, desired);
            }), loaded);
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

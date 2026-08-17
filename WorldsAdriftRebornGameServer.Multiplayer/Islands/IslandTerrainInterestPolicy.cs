using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    public enum TerrainStreamActionKind { Add, Remove }

    public readonly record struct TerrainStreamCandidate(
        long EntityId,
        IslandDefinition Island,
        IslandTerrainEnvelope Envelope);

    public readonly record struct TerrainStreamAction(
        TerrainStreamActionKind Kind,
        long EntityId,
        IslandId IslandId);

    /// <summary>Pure configuration, geometry and ordering for island terrain checkout.</summary>
    public static class IslandTerrainInterestPolicy
    {
        public const string EnabledEnvVar = "WAREBORN_TERRAIN_INTEREST_ENABLED";
        public const string LoadRadiusEnvVar = "WAREBORN_TERRAIN_LOAD_RADIUS_M";
        public const string UnloadRadiusEnvVar = "WAREBORN_TERRAIN_UNLOAD_RADIUS_M";
        public const string AssetAckTimeoutEnvVar = "WAREBORN_TERRAIN_ASSET_ACK_TIMEOUT_MS";
        public const double DefaultLoadRadiusMetres = 1200.0;
        public const double DefaultUnloadRadiusMetres = 1600.0;
        public const int DefaultAssetAckTimeoutMs = 30000;
        public const int MinAssetAckTimeoutMs = 10000;
        public const int MaxAssetAckTimeoutMs = 120000;
        public static readonly TimeSpan AssetRequestRetryInterval = TimeSpan.FromSeconds(5);

        public static bool EnabledFrom(string? value) =>
            string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The server observes the completed spawn-plan sentinel every poll. The
        /// continuous-interest settle boundary must therefore be armed once only.
        /// </summary>
        public static bool ShouldArmContinuous(bool alreadyComplete) => !alreadyComplete;

        public static double LoadRadiusFrom(string? value) =>
            RadiusFrom(value, DefaultLoadRadiusMetres, minimum: 100.0);

        public static double UnloadRadiusFrom(string? value, double loadRadius)
        {
            double result = RadiusFrom(value, DefaultUnloadRadiusMetres, minimum: loadRadius + 1.0);
            return result > loadRadius ? result : System.Math.Min(
                InterestPolicy.MaxRadiusMetres, loadRadius + 400.0);
        }

        public static TimeSpan AssetAckTimeoutFrom(string? value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms))
                ms = DefaultAssetAckTimeoutMs;
            return TimeSpan.FromMilliseconds(System.Math.Clamp(ms,
                MinAssetAckTimeoutMs, MaxAssetAckTimeoutMs));
        }

        /// <summary>
        /// Global movement only chooses proximity. Confirmed ground is supplied
        /// separately from relative-to/collision evidence and is never guessed here.
        /// Adds are always ordered before removes. A previous ground terrain remains
        /// protected until a destination is actually checked out.
        /// </summary>
        public static IReadOnlyList<TerrainStreamAction> Reconcile(
            FixedPointPosition center,
            IEnumerable<TerrainStreamCandidate> candidates,
            ISet<long> loaded,
            IslandId? confirmedGround,
            IslandId? requestedDestination,
            double loadRadius,
            double unloadRadius,
            Func<IslandId, bool>? mayRemove = null)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (loaded == null) throw new ArgumentNullException(nameof(loaded));
            mayRemove ??= _ => true;

            TerrainStreamCandidate[] all = candidates.OrderBy(x => x.Island.Id).ToArray();
            double load2 = loadRadius * loadRadius;
            double unload2 = unloadRadius * unloadRadius;
            var adds = new List<(TerrainStreamAction Action, double Distance)>();
            var removes = new List<(TerrainStreamAction Action, double Distance)>();
            bool destinationReady = requestedDestination == null
                || all.Any(x => x.Island.Id == requestedDestination.Value && loaded.Contains(x.EntityId));

            foreach (TerrainStreamCandidate candidate in all)
            {
                double distance = candidate.Envelope.DistanceSquaredTo(center, candidate.Island);
                bool isLoaded = loaded.Contains(candidate.EntityId);
                bool forced = candidate.Island.Id == requestedDestination;
                bool protectedGround = candidate.Island.Id == confirmedGround;
                if (!isLoaded && (forced || distance <= load2))
                {
                    adds.Add((new TerrainStreamAction(TerrainStreamActionKind.Add,
                        candidate.EntityId, candidate.Island.Id), distance));
                }
                else if (isLoaded && !forced && !protectedGround && destinationReady
                    && distance > unload2 && mayRemove(candidate.Island.Id))
                {
                    removes.Add((new TerrainStreamAction(TerrainStreamActionKind.Remove,
                        candidate.EntityId, candidate.Island.Id), distance));
                }
            }

            adds.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            removes.Sort((a, b) => b.Distance.CompareTo(a.Distance));
            return adds.Select(x => x.Action).Concat(removes.Select(x => x.Action)).ToArray();
        }

        public static bool ExactAssetAck(
            ulong expectedPeerId, string expectedAssetType, string expectedName, string expectedContext,
            ulong actualPeerId, string actualAssetType, string actualName, string actualContext) =>
            expectedPeerId == actualPeerId
            && string.Equals(expectedAssetType, actualAssetType, StringComparison.Ordinal)
            && string.Equals(expectedName, actualName, StringComparison.Ordinal)
            && string.Equals(expectedContext, actualContext, StringComparison.Ordinal);

        public static bool AssetFallbackDue(TimeSpan requestedAt, TimeSpan now, TimeSpan timeout) =>
            now >= requestedAt && now - requestedAt >= timeout;

        public static bool AssetRetryDue(TimeSpan lastRequestAt, TimeSpan now) =>
            now >= lastRequestAt && now - lastRequestAt >= AssetRequestRetryInterval;

        /// <summary>
        /// Terrain re-entry is enabled only after BOTH transport and correlated
        /// asset lifecycle support were demonstrated. Legacy opaque-ack clients
        /// retain visited terrain even when their ENet channel count includes 5.
        /// </summary>
        public static bool MayRemove(bool removeChannelSupported, bool correlatedAckObserved) =>
            removeChannelSupported && correlatedAckObserved;

        private static double RadiusFrom(string? value, double fallback, double minimum)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
                || double.IsNaN(result) || double.IsInfinity(result) || result < minimum)
                result = fallback;
            return System.Math.Min(result, InterestPolicy.MaxRadiusMetres);
        }
    }
}

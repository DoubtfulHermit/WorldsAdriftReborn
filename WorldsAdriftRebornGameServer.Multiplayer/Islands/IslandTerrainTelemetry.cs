using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// The semantic lifecycle position of ONE island's terrain for ONE peer. These
    /// are the labels the operator console renders verbatim, so they are part of
    /// the cross-process contract and are never derived twice with different rules.
    /// </summary>
    public enum TerrainCheckoutState
    {
        /// <summary>Not checked out and not wanted right now.</summary>
        Absent,

        /// <summary>Queued for load; no asset request is in flight yet.</summary>
        Requesting,

        /// <summary>An asset load request is in flight, awaiting the exact correlated ack.</summary>
        WaitingAck,

        /// <summary>Checked out and confirmed on the wire.</summary>
        Ready,

        /// <summary>Removal is queued but this island's resources have not drained yet.</summary>
        Draining,

        /// <summary>Removal is queued and clear to send.</summary>
        Unloading,

        /// <summary>
        /// Loaded, and this client proved it cannot safely receive a terrain
        /// RemoveEntity. The checkout is retained for the life of the session.
        /// </summary>
        RetainedLegacy,

        /// <summary>A lifecycle step failed for this pairing.</summary>
        Error,
    }

    /// <summary>What the paced queue is currently trying to do for a peer.</summary>
    public enum TerrainPendingActionKind { None, Load, Remove, ResourceDrain }

    /// <summary>
    /// Whether terrain checkout is off, requested-but-held-back by its
    /// resource-interest prerequisite, or actually running. The distinction
    /// matters: an operator who set the env var must not read "off" and conclude
    /// the setting did not take.
    /// </summary>
    public enum TerrainRuntimeMode { Off, PrerequisiteDisabled, On }

    /// <summary>
    /// The bounded set of lifecycle transitions the ring buffer records. A closed
    /// enum rather than free text is what keeps packet payloads, paths and pointers
    /// structurally unable to reach the stats file.
    /// </summary>
    public enum TerrainEventKind
    {
        Requested,
        AssetAcknowledged,
        AssetRetried,
        AssetFallback,
        AddSucceeded,
        AddFailed,
        DrainWaiting,
        RemoveSucceeded,
        RemoveFailed,
        TeleportWaiting,
        TeleportReady,
        TeleportRefused,
    }

    /// <summary>Stable wire labels for the telemetry enums.</summary>
    public static class TerrainTelemetryLabels
    {
        public static string Of(TerrainCheckoutState state) => state switch
        {
            TerrainCheckoutState.Absent => "absent",
            TerrainCheckoutState.Requesting => "requesting",
            TerrainCheckoutState.WaitingAck => "waiting-ack",
            TerrainCheckoutState.Ready => "ready",
            TerrainCheckoutState.Draining => "draining",
            TerrainCheckoutState.Unloading => "unloading",
            TerrainCheckoutState.RetainedLegacy => "retained-legacy",
            _ => "error",
        };

        public static string Of(TerrainPendingActionKind kind) => kind switch
        {
            TerrainPendingActionKind.Load => "load",
            TerrainPendingActionKind.Remove => "remove",
            TerrainPendingActionKind.ResourceDrain => "resource-drain",
            _ => "none",
        };

        public static string Of(TerrainRuntimeMode mode) => mode switch
        {
            TerrainRuntimeMode.On => "on",
            TerrainRuntimeMode.PrerequisiteDisabled => "prerequisite-disabled",
            _ => "off",
        };

        public static string Of(TerrainEventKind kind) => kind switch
        {
            TerrainEventKind.Requested => "request",
            TerrainEventKind.AssetAcknowledged => "asset-ack",
            TerrainEventKind.AssetRetried => "asset-retry",
            TerrainEventKind.AssetFallback => "asset-fallback",
            TerrainEventKind.AddSucceeded => "add-ok",
            TerrainEventKind.AddFailed => "add-failed",
            TerrainEventKind.DrainWaiting => "drain-wait",
            TerrainEventKind.RemoveSucceeded => "remove-ok",
            TerrainEventKind.RemoveFailed => "remove-failed",
            TerrainEventKind.TeleportWaiting => "teleport-wait",
            TerrainEventKind.TeleportReady => "teleport-ready",
            _ => "teleport-refused",
        };

        /// <summary>All checkout states in display order, for count projections.</summary>
        public static readonly IReadOnlyList<TerrainCheckoutState> AllStates =
            Array.AsReadOnly(new[]
            {
                TerrainCheckoutState.Absent,
                TerrainCheckoutState.Requesting,
                TerrainCheckoutState.WaitingAck,
                TerrainCheckoutState.Ready,
                TerrainCheckoutState.Draining,
                TerrainCheckoutState.Unloading,
                TerrainCheckoutState.RetainedLegacy,
                TerrainCheckoutState.Error,
            });
    }

    /// <summary>
    /// Pure derivation of every state and warning the operator console shows. The
    /// rules live here, once, so the game server, the JSON contract and the tests
    /// cannot disagree about what "RETAINED (LEGACY)" means.
    /// </summary>
    public static class IslandTerrainStatePolicy
    {
        /// <summary>
        /// Retry and stall thresholds are telemetry judgement, not wire behaviour:
        /// crossing them changes what the dashboard says, never what is sent.
        /// </summary>
        public const int NoisyRetryCount = 2;

        public static TerrainRuntimeMode ModeOf(bool requested, bool enabled) =>
            enabled ? TerrainRuntimeMode.On
                : requested ? TerrainRuntimeMode.PrerequisiteDisabled
                : TerrainRuntimeMode.Off;

        /// <summary>
        /// One peer/island cell. Order matters: a queued removal describes the
        /// peer's intent more truthfully than the checkout it is undoing, and a
        /// loaded island a client cannot unload is retained rather than merely
        /// ready.
        /// </summary>
        public static TerrainCheckoutState CellState(
            bool loaded,
            bool mayRemove,
            bool pendingAdd,
            bool pendingRemove,
            bool drainWaiting,
            bool assetInFlight,
            bool assetAcknowledged,
            bool failed)
        {
            if (failed) return TerrainCheckoutState.Error;
            if (pendingRemove)
                return drainWaiting ? TerrainCheckoutState.Draining : TerrainCheckoutState.Unloading;
            if (loaded)
                return mayRemove ? TerrainCheckoutState.Ready : TerrainCheckoutState.RetainedLegacy;
            if (pendingAdd)
                return assetInFlight && !assetAcknowledged
                    ? TerrainCheckoutState.WaitingAck
                    : TerrainCheckoutState.Requesting;
            return TerrainCheckoutState.Absent;
        }

        /// <summary>
        /// Whether a peer is in legacy retain mode: it has terrain checked out that
        /// it will never be asked to unload. Merely not having acked yet is NOT
        /// this state - a patched client acks before its first add.
        /// </summary>
        public static bool IsLegacyRetaining(bool anyLoaded, bool mayRemove) =>
            anyLoaded && !mayRemove;

        /// <summary>
        /// The single most useful sentence about a peer, or empty. Ordered by what
        /// an operator should act on first.
        /// </summary>
        public static string WarningFor(
            bool assetTimedOut,
            int assetRetryCount,
            bool legacyRetaining,
            bool destinationWaiting)
        {
            if (assetTimedOut)
                return "asset acknowledgement timed out; bounded fallback add pending";
            if (assetRetryCount >= NoisyRetryCount)
                return "asset request retried " + assetRetryCount.ToString(CultureInfo.InvariantCulture)
                    + " times without an exact acknowledgement";
            if (legacyRetaining)
                return "legacy client: visited terrain is retained for this session";
            if (destinationWaiting)
                return "waiting for requested destination terrain";
            return string.Empty;
        }

        /// <summary>
        /// Counts of every state across a peer/island matrix, indexed in the
        /// <see cref="TerrainTelemetryLabels.AllStates"/> display order (which is
        /// the enum's own order, so the cast is the index).
        /// </summary>
        public static IReadOnlyList<int> CountByState(IEnumerable<TerrainCheckoutState> states)
        {
            if (states == null) throw new ArgumentNullException(nameof(states));
            int[] counts = new int[TerrainTelemetryLabels.AllStates.Count];
            foreach (TerrainCheckoutState state in states)
            {
                int index = (int)state;
                if (index >= 0 && index < counts.Length) counts[index]++;
            }
            return Array.AsReadOnly(counts);
        }
    }

    /// <summary>
    /// Defensive copies for the operator snapshot. A telemetry value must not be
    /// a live window onto the runtime's own collections: the producer keeps its
    /// list, the snapshot keeps a frozen copy, and neither can surprise the other
    /// half way through a serialization.
    /// </summary>
    public static class TerrainSnapshotList
    {
        public static IReadOnlyList<T> Immutable<T>(IReadOnlyList<T>? source) =>
            source == null || source.Count == 0
                ? Array.Empty<T>()
                : Array.AsReadOnly(source.ToArray());
    }

    /// <summary>
    /// The cold-asset request currently in flight for a peer. Absent when the peer
    /// is idle; never fabricated, because "no request" and "a request with zero age"
    /// are different facts.
    /// </summary>
    public readonly struct TerrainAssetFlightStat
    {
        public string IslandId { get; }
        public string AssetName { get; }
        public long RequestAgeMs { get; }
        public long LastRetryAgeMs { get; }
        public int RetryCount { get; }
        public bool Acknowledged { get; }
        public bool FallbackDue { get; }

        public TerrainAssetFlightStat(string islandId, string assetName, long requestAgeMs,
            long lastRetryAgeMs, int retryCount, bool acknowledged, bool fallbackDue)
        {
            IslandId = islandId ?? string.Empty;
            AssetName = assetName ?? string.Empty;
            RequestAgeMs = Math.Max(0, requestAgeMs);
            LastRetryAgeMs = Math.Max(0, lastRetryAgeMs);
            RetryCount = Math.Max(0, retryCount);
            Acknowledged = acknowledged;
            FallbackDue = fallbackDue;
        }
    }

    /// <summary>One cell of the player x island lifecycle matrix.</summary>
    public readonly struct TerrainPeerIslandStat
    {
        public string IslandId { get; }
        public TerrainCheckoutState State { get; }

        public TerrainPeerIslandStat(string islandId, TerrainCheckoutState state)
        {
            IslandId = islandId ?? string.Empty;
            State = state;
        }
    }

    /// <summary>
    /// One tracked peer's terrain lifecycle. Identified by the player entity id it
    /// controls; <see cref="Slot"/> is a process-local ordinal that exists only so a
    /// peer which has not spawned an entity yet still has a stable row. No ENet
    /// pointer or handle is exported.
    /// </summary>
    public readonly struct TerrainPlayerStat
    {
        public long PlayerEntityId { get; }
        public int Slot { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public string? ConfirmedGroundIslandId { get; }
        public string? RequestedDestinationIslandId { get; }
        public TerrainPendingActionKind PendingAction { get; }
        public string? PendingIslandId { get; }
        public TerrainAssetFlightStat? Asset { get; }
        public bool CorrelatedAckObserved { get; }
        public bool RemoveSupported { get; }
        public bool ConnectPlanComplete { get; }
        public bool SettleWaiting { get; }
        public IReadOnlyList<TerrainPeerIslandStat> Islands { get; }

        public TerrainPlayerStat(
            long playerEntityId,
            int slot,
            double x, double y, double z,
            string? confirmedGroundIslandId,
            string? requestedDestinationIslandId,
            TerrainPendingActionKind pendingAction,
            string? pendingIslandId,
            TerrainAssetFlightStat? asset,
            bool correlatedAckObserved,
            bool removeSupported,
            bool connectPlanComplete,
            bool settleWaiting,
            IReadOnlyList<TerrainPeerIslandStat>? islands)
        {
            PlayerEntityId = playerEntityId;
            Slot = slot;
            X = x; Y = y; Z = z;
            ConfirmedGroundIslandId = confirmedGroundIslandId;
            RequestedDestinationIslandId = requestedDestinationIslandId;
            PendingAction = pendingAction;
            PendingIslandId = pendingIslandId;
            Asset = asset;
            CorrelatedAckObserved = correlatedAckObserved;
            RemoveSupported = removeSupported;
            ConnectPlanComplete = connectPlanComplete;
            SettleWaiting = settleWaiting;
            Islands = TerrainSnapshotList.Immutable(islands);
        }

        /// <summary>Terrain re-entry needs BOTH transport and correlated-ack proof.</summary>
        public bool MayRemove =>
            IslandTerrainInterestPolicy.MayRemove(RemoveSupported, CorrelatedAckObserved);

        public int ReadyCount
        {
            get
            {
                int n = 0;
                foreach (TerrainPeerIslandStat cell in Islands)
                    if (cell.State == TerrainCheckoutState.Ready
                        || cell.State == TerrainCheckoutState.RetainedLegacy) n++;
                return n;
            }
        }

        public bool AnyLoaded => ReadyCount > 0;

        public bool LegacyRetaining =>
            IslandTerrainStatePolicy.IsLegacyRetaining(AnyLoaded, MayRemove);

        public bool DestinationWaiting
        {
            get
            {
                if (RequestedDestinationIslandId == null) return false;
                foreach (TerrainPeerIslandStat cell in Islands)
                    if (cell.IslandId == RequestedDestinationIslandId)
                        return cell.State != TerrainCheckoutState.Ready
                            && cell.State != TerrainCheckoutState.RetainedLegacy;
                return true;
            }
        }

        public string Warning => IslandTerrainStatePolicy.WarningFor(
            Asset.HasValue && Asset.Value.FallbackDue && !Asset.Value.Acknowledged,
            Asset?.RetryCount ?? 0,
            LegacyRetaining,
            DestinationWaiting);
    }

    /// <summary>
    /// One optional island as terrain checkout sees it. Registration and ownership
    /// are reported as they actually are: an island without an extracted envelope,
    /// a directory entry, a bound entity id or local ownership is listed as NOT
    /// managed rather than silently omitted.
    /// </summary>
    public readonly struct TerrainIslandStat
    {
        public string IslandId { get; }
        public string DisplayName { get; }
        public long TerrainEntityId { get; }
        public bool Registered { get; }
        public bool LocallyOwned { get; }
        public bool HasEnvelope { get; }
        public bool Managed { get; }
        public bool Unconditional { get; }
        public double MinX { get; }
        public double MinY { get; }
        public double MinZ { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double MaxZ { get; }
        public int ReadyPeerCount { get; }
        public int LoadingPeerCount { get; }
        public int DrainingPeerCount { get; }
        public int UnloadingPeerCount { get; }
        public int RetainedLegacyPeerCount { get; }
        public int ErrorPeerCount { get; }

        /// <summary>Registered resource nodes on this island, or -1 when unknown.</summary>
        public int ResourceNodeCount { get; }

        /// <summary>Resource checkouts still outstanding across peers, or -1 when unknown.</summary>
        public int CheckedOutResourceCount { get; }

        /// <summary>Whether an ordered resource drain is actually wired for this island.</summary>
        public bool ResourceDrainWired { get; }

        public TerrainIslandStat(
            string islandId, string displayName, long terrainEntityId,
            bool registered, bool locallyOwned, bool hasEnvelope, bool managed, bool unconditional,
            double minX, double minY, double minZ, double maxX, double maxY, double maxZ,
            int readyPeerCount, int loadingPeerCount, int drainingPeerCount,
            int unloadingPeerCount, int retainedLegacyPeerCount, int errorPeerCount,
            int resourceNodeCount, int checkedOutResourceCount, bool resourceDrainWired)
        {
            IslandId = islandId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            TerrainEntityId = terrainEntityId;
            Registered = registered;
            LocallyOwned = locallyOwned;
            HasEnvelope = hasEnvelope;
            Managed = managed;
            Unconditional = unconditional;
            MinX = minX; MinY = minY; MinZ = minZ;
            MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
            ReadyPeerCount = readyPeerCount;
            LoadingPeerCount = loadingPeerCount;
            DrainingPeerCount = drainingPeerCount;
            UnloadingPeerCount = unloadingPeerCount;
            RetainedLegacyPeerCount = retainedLegacyPeerCount;
            ErrorPeerCount = errorPeerCount;
            ResourceNodeCount = resourceNodeCount;
            CheckedOutResourceCount = checkedOutResourceCount;
            ResourceDrainWired = resourceDrainWired;
        }

        /// <summary>Envelope span in metres per axis; zero when no envelope is evidenced.</summary>
        public double SpanX => HasEnvelope ? MaxX - MinX : 0.0;
        public double SpanY => HasEnvelope ? MaxY - MinY : 0.0;
        public double SpanZ => HasEnvelope ? MaxZ - MinZ : 0.0;
    }

    /// <summary>One bounded, operator-safe lifecycle event.</summary>
    public readonly struct TerrainEventStat
    {
        public long AgeMs { get; }
        public TerrainEventKind Kind { get; }
        public string IslandId { get; }
        public long PlayerEntityId { get; }
        public int Slot { get; }
        public bool Success { get; }

        public TerrainEventStat(long ageMs, TerrainEventKind kind, string islandId,
            long playerEntityId, int slot, bool success)
        {
            AgeMs = Math.Max(0, ageMs);
            Kind = kind;
            IslandId = islandId ?? string.Empty;
            PlayerEntityId = playerEntityId;
            Slot = slot;
            Success = success;
        }
    }

    /// <summary>
    /// The whole process-local terrain lifecycle as exported to the operator. This
    /// describes ONE authoritative game loop on one host; it says nothing about
    /// remote workers and does not change island domain authority.
    /// </summary>
    public readonly struct TerrainRuntimeStat
    {
        /// <summary>The env var asked for terrain checkout.</summary>
        public bool Requested { get; }

        /// <summary>Terrain checkout is actually running.</summary>
        public bool Enabled { get; }

        public double LoadRadiusMetres { get; }
        public double UnloadRadiusMetres { get; }
        public long AssetAckTimeoutMs { get; }
        public long SettleDelayMs { get; }
        public int CandidateCount { get; }
        public int TrackedPeerCount { get; }
        public IReadOnlyList<TerrainPlayerStat> Players { get; }
        public IReadOnlyList<TerrainIslandStat> Islands { get; }
        public IReadOnlyList<TerrainEventStat> Events { get; }

        public TerrainRuntimeStat(
            bool requested, bool enabled,
            double loadRadiusMetres, double unloadRadiusMetres,
            long assetAckTimeoutMs, long settleDelayMs,
            int candidateCount, int trackedPeerCount,
            IReadOnlyList<TerrainPlayerStat>? players,
            IReadOnlyList<TerrainIslandStat>? islands,
            IReadOnlyList<TerrainEventStat>? events)
        {
            Requested = requested;
            Enabled = enabled;
            LoadRadiusMetres = loadRadiusMetres;
            UnloadRadiusMetres = unloadRadiusMetres;
            AssetAckTimeoutMs = assetAckTimeoutMs;
            SettleDelayMs = settleDelayMs;
            CandidateCount = Math.Max(0, candidateCount);
            TrackedPeerCount = Math.Max(0, trackedPeerCount);
            Players = TerrainSnapshotList.Immutable(players);
            Islands = TerrainSnapshotList.Immutable(islands);
            Events = TerrainSnapshotList.Immutable(events);
        }

        /// <summary>
        /// The truthful "this process has no terrain lifecycle" value. Used
        /// instead of <c>default</c> so every collection is a real empty list and
        /// no consumer has to defend against a null inside a struct.
        /// </summary>
        public static readonly TerrainRuntimeStat Off =
            new TerrainRuntimeStat(false, false, 0, 0, 0, 0, 0, 0, null, null, null);

        public TerrainRuntimeMode Mode => IslandTerrainStatePolicy.ModeOf(Requested, Enabled);

        /// <summary>Every matrix cell's state, in row-major player/island order.</summary>
        public IReadOnlyList<int> StateCounts
        {
            get
            {
                List<TerrainCheckoutState> states = new();
                foreach (TerrainPlayerStat player in Players)
                    foreach (TerrainPeerIslandStat cell in player.Islands)
                        states.Add(cell.State);
                return IslandTerrainStatePolicy.CountByState(states);
            }
        }

        public int ReadyCount
        {
            get
            {
                int n = 0;
                foreach (TerrainPlayerStat player in Players) n += player.ReadyCount;
                return n;
            }
        }

        public int WarningCount
        {
            get
            {
                int n = 0;
                foreach (TerrainPlayerStat player in Players)
                    if (player.Warning.Length > 0) n++;
                return n;
            }
        }

        public int ErrorCount
        {
            get
            {
                int n = 0;
                foreach (TerrainIslandStat island in Islands) n += island.ErrorPeerCount;
                return n;
            }
        }
    }

    /// <summary>
    /// A bounded ring of recent lifecycle transitions. Bounded on purpose: this is
    /// a diagnostic window an operator reads at a glance, not a log, so it can
    /// never grow with uptime and never becomes a memory or a privacy problem.
    ///
    /// Records carry a clock reading rather than a wall time so the pure assembly
    /// stays free of ambient time, and a peer ORDINAL rather than a peer handle so
    /// no pointer can reach the stats file.
    /// </summary>
    public sealed class TerrainEventLog
    {
        /// <summary>
        /// Enough to hold a full connect/travel/return acceptance run for several
        /// peers, small enough to serialize into a 3-second snapshot unnoticed.
        /// </summary>
        public const int Capacity = 64;

        private readonly struct Entry
        {
            public readonly TimeSpan At;
            public readonly TerrainEventKind Kind;
            public readonly string IslandId;
            public readonly int Slot;
            public readonly bool Success;

            public Entry(TimeSpan at, TerrainEventKind kind, string islandId, int slot, bool success)
            {
                At = at; Kind = kind; IslandId = islandId; Slot = slot; Success = success;
            }
        }

        private readonly Entry[] _entries = new Entry[Capacity];
        private int _next;
        private int _count;

        public int Count => _count;

        public void Record(TimeSpan at, TerrainEventKind kind, IslandId islandId, int slot, bool success)
        {
            _entries[_next] = new Entry(at, kind, islandId.Value ?? string.Empty, slot, success);
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        /// <summary>
        /// An immutable newest-first copy, aged against the supplied clock reading.
        /// Callers get a snapshot array they cannot use to reach back into the log.
        /// </summary>
        public IReadOnlyList<TerrainEventStat> Snapshot(TimeSpan now, Func<int, long> playerEntityIdOfSlot)
        {
            if (playerEntityIdOfSlot == null) throw new ArgumentNullException(nameof(playerEntityIdOfSlot));
            TerrainEventStat[] result = new TerrainEventStat[_count];
            for (int i = 0; i < _count; i++)
            {
                // Walk backwards from the most recently written slot.
                int index = ((_next - 1 - i) % Capacity + Capacity) % Capacity;
                Entry entry = _entries[index];
                result[i] = new TerrainEventStat(
                    (long)Math.Max(0, (now - entry.At).TotalMilliseconds),
                    entry.Kind, entry.IslandId,
                    playerEntityIdOfSlot(entry.Slot), entry.Slot, entry.Success);
            }
            return Array.AsReadOnly(result);
        }

        public void Clear()
        {
            Array.Clear(_entries, 0, _entries.Length);
            _next = 0;
            _count = 0;
        }
    }
}

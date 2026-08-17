using System.Globalization;
using System.Text;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The judgement call around wire health, kept pure so the threshold is
    /// testable and named in exactly one place.
    /// </summary>
    public static class StatsSnapshotPolicy
    {
        /// <summary>
        /// The RTT above which a peer is treated as spiralling. The 73-second
        /// silent drop that motivated all of this began with RTT blowing out as
        /// reliable relay traffic outran a peer's ACKs; half a second of round
        /// trip on a game that ticks at 20 Hz means ten frames of queue, which is
        /// already visibly wrong and heading for a timeout. Surfaced as a loud
        /// dashboard warning so the operator SEES the spiral forming instead of
        /// discovering the drop after the fact.
        /// </summary>
        public const uint SpiralRttMs = 500;

        /// <summary>Whether a peer's round-trip time is in spiral territory.</summary>
        public static bool IsSpiralRtt(uint roundTripTimeMs)
        {
            return roundTripTimeMs > SpiralRttMs;
        }
    }

    /// <summary>Pure warning rules for the read-only local simulation inspector.</summary>
    public static class ShipDomainStatPolicy
    {
        public static bool IsDeliveryStale(bool liveCadenceExpected, long deliveryAgeMs,
            int cadenceMs)
        {
            if (!liveCadenceExpected) return false;
            if (deliveryAgeMs < 0) return true;
            return deliveryAgeMs > Math.Max(1000, cadenceMs * 4);
        }

        public static bool HasAboardCheckoutGap(int aboardPlayers, int subscribers) =>
            aboardPlayers > subscribers;
    }

    /// <summary>
    /// One truthful in-process whole-ship domain as exported to the operator UI.
    /// This is observation only: no worker, migration or authority-control fields
    /// are implied beyond the local domain generation the runtime already owns.
    /// </summary>
    public readonly struct ShipDomainStat
    {
        public string DomainId { get; }
        public long HullEntityId { get; }
        public long AuthorityGeneration { get; }
        public long ReplicationSequence { get; }
        public int CadenceMs { get; }
        public long DeliveryAgeMs { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public bool Active { get; }
        public bool Piloted { get; }
        public bool LiveCadenceExpected { get; }
        public long? PilotPlayerEntityId { get; }
        public IReadOnlyList<long> AboardPlayerEntityIds { get; }
        public int DeckCount { get; }
        public int MountedPartCount { get; }
        public int SubscriberCount { get; }

        public ShipDomainStat(string domainId, long hullEntityId, long authorityGeneration,
            long replicationSequence, int cadenceMs, long deliveryAgeMs,
            double x, double y, double z, bool active, bool piloted,
            bool liveCadenceExpected, long? pilotPlayerEntityId,
            IReadOnlyList<long> aboardPlayerEntityIds, int deckCount,
            int mountedPartCount, int subscriberCount)
        {
            DomainId = domainId ?? string.Empty;
            HullEntityId = hullEntityId;
            AuthorityGeneration = authorityGeneration;
            ReplicationSequence = replicationSequence;
            CadenceMs = cadenceMs;
            DeliveryAgeMs = deliveryAgeMs;
            X = x; Y = y; Z = z;
            Active = active;
            Piloted = piloted;
            LiveCadenceExpected = liveCadenceExpected;
            PilotPlayerEntityId = pilotPlayerEntityId;
            AboardPlayerEntityIds = aboardPlayerEntityIds ?? Array.Empty<long>();
            DeckCount = deckCount;
            MountedPartCount = mountedPartCount;
            SubscriberCount = subscriberCount;
        }

        public bool StaleDelivery => ShipDomainStatPolicy.IsDeliveryStale(
            LiveCadenceExpected, DeliveryAgeMs, CadenceMs);
        public bool AboardCheckoutWarning => ShipDomainStatPolicy.HasAboardCheckoutGap(
            AboardPlayerEntityIds.Count, SubscriberCount);
    }

    /// <summary>
    /// One node in the operator-facing local runtime topology. This is deliberately
    /// smaller than a gameplay snapshot: it describes ownership, placement and
    /// health without inventing remote workers or migration state.
    /// </summary>
    public readonly struct RuntimeDomainStat
    {
        public string DomainId { get; }
        public string Kind { get; }
        public string Label { get; }
        public string HostId { get; }
        public string? AffinityDomainId { get; }
        public int EntityCount { get; }
        public bool Active { get; }
        public int WarningCount { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public RuntimeDomainStat(string domainId, string kind, string label,
            string hostId, string? affinityDomainId, int entityCount, bool active,
            int warningCount, double x, double y, double z)
        {
            DomainId = domainId ?? string.Empty;
            Kind = kind ?? string.Empty;
            Label = label ?? string.Empty;
            HostId = hostId ?? string.Empty;
            AffinityDomainId = affinityDomainId;
            EntityCount = entityCount;
            Active = active;
            WarningCount = warningCount;
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// One live player as the dashboard sees them: the entity they control, the
    /// peer they are, when they connected, and - when ENet's counters are
    /// readable - their wire health. Health is optional because
    /// <see cref="EnetPeerHealth"/> can fail its layout sanity check, in which
    /// case the dashboard must say "unreadable", never show zeros.
    /// </summary>
    public readonly struct PlayerStat
    {
        public long EntityId { get; }
        public ulong PeerId { get; }
        public long ConnectedAtUnixMs { get; }
        public EnetPeerHealth? Health { get; }
        public FixedPointPosition? Position { get; }

        public PlayerStat(long entityId, ulong peerId, long connectedAtUnixMs,
            EnetPeerHealth? health, FixedPointPosition? position = null)
        {
            EntityId = entityId;
            PeerId = peerId;
            ConnectedAtUnixMs = connectedAtUnixMs;
            Health = health;
            Position = position;
        }

        /// <summary>Whether this player's RTT is spiralling. False when health is unreadable.</summary>
        public bool IsSpiralling =>
            Health.HasValue && StatsSnapshotPolicy.IsSpiralRtt(Health.Value.RoundTripTimeMs);
    }

    /// <summary>
    /// The whole snapshot the game server writes and the login server reads. A
    /// value type with a hand-built <see cref="ToJson"/> rather than a serialized
    /// object graph, on purpose: the pure Multiplayer assembly has NO
    /// dependencies (that is what lets it be tested on Linux without Wine), so it
    /// cannot pull in a JSON library, and the file is a cross-process CONTRACT
    /// whose exact shape a test should be able to pin. Hand-building it here is
    /// what makes that shape assertable.
    ///
    /// The reader is Newtonsoft on the login side; the field names below are the
    /// contract between the two.
    /// </summary>
    public readonly struct StatsSnapshot
    {
        /// <summary>
        /// The schema version of THIS file format. Bumped if a field's meaning
        /// changes, so a login server reading an older game server's file can
        /// tell rather than mis-parse. Independent of the database schema
        /// version.
        /// </summary>
        public const int SchemaVersion = 6;

        public long BootTimeUnixMs { get; }
        public long GeneratedAtUnixMs { get; }
        public long UptimeSeconds { get; }

        /// <summary>"v2@20Hz" or "raw" - the relay emitter's current mode.</summary>
        public string RelayMode { get; }
        public int RelayHz { get; }

        /// <summary>Build or commit marker, or "unknown". Free-form; escaped on write.</summary>
        public string Build { get; }

        public long TotalConnects { get; }
        public long TotalDisconnects { get; }
        public int CurrentOnline { get; }
        public int PeakOnline { get; }

        /// <summary>
        /// Actual boot-registry readiness for the first distinct production
        /// island. The admin page uses this fact instead of guessing from an
        /// environment variable owned by another process.
        /// </summary>
        public bool SecondIslandRegistered { get; }

        /// <summary>
        /// Number of tier-1 B3 terrain candidates actually registered this boot.
        /// This is registry truth, not a copy of the environment setting.
        /// </summary>
        public int FirstRegionTerrainCount { get; }

        /// <summary>
        /// The process-local optional-terrain lifecycle. Always a fully built
        /// value: a server with no terrain service reports
        /// <see cref="TerrainRuntimeStat.Off"/> rather than an absent section, so
        /// the operator sees "off", not "unknown".
        /// </summary>
        public TerrainRuntimeStat Terrain { get; }

        public IReadOnlyList<PlayerStat> Players { get; }
        public IReadOnlyList<ShipDomainStat> ShipDomains { get; }
        public IReadOnlyList<RuntimeDomainStat> RuntimeDomains { get; }
        public int RuntimeOwnedEntityCount { get; }
        public int RuntimeGlobalEntityCount { get; }
        public int RuntimeUnownedEntityCount { get; }
        public int RuntimeOwnershipIssueCount { get; }

        public StatsSnapshot(
            long bootTimeUnixMs,
            long generatedAtUnixMs,
            long uptimeSeconds,
            string relayMode,
            int relayHz,
            string build,
            long totalConnects,
            long totalDisconnects,
            int currentOnline,
            int peakOnline,
            IReadOnlyList<PlayerStat> players,
            bool secondIslandRegistered = false,
            IReadOnlyList<ShipDomainStat>? shipDomains = null,
            IReadOnlyList<RuntimeDomainStat>? runtimeDomains = null,
            int runtimeOwnedEntityCount = 0,
            int runtimeGlobalEntityCount = 0,
            int runtimeUnownedEntityCount = 0,
            int runtimeOwnershipIssueCount = 0,
            int firstRegionTerrainCount = 0,
            TerrainRuntimeStat? terrain = null)
        {
            BootTimeUnixMs = bootTimeUnixMs;
            GeneratedAtUnixMs = generatedAtUnixMs;
            UptimeSeconds = uptimeSeconds;
            RelayMode = relayMode;
            RelayHz = relayHz;
            Build = build;
            TotalConnects = totalConnects;
            TotalDisconnects = totalDisconnects;
            CurrentOnline = currentOnline;
            PeakOnline = peakOnline;
            Players = players ?? Array.Empty<PlayerStat>();
            SecondIslandRegistered = secondIslandRegistered;
            ShipDomains = shipDomains ?? Array.Empty<ShipDomainStat>();
            RuntimeDomains = runtimeDomains ?? Array.Empty<RuntimeDomainStat>();
            RuntimeOwnedEntityCount = runtimeOwnedEntityCount;
            RuntimeGlobalEntityCount = runtimeGlobalEntityCount;
            RuntimeUnownedEntityCount = runtimeUnownedEntityCount;
            RuntimeOwnershipIssueCount = runtimeOwnershipIssueCount;
            FirstRegionTerrainCount = Math.Max(0, firstRegionTerrainCount);
            Terrain = terrain ?? TerrainRuntimeStat.Off;
        }

        /// <summary>
        /// Whether ANY connected player's RTT is spiralling. The single flag the
        /// operator has to see: one peer in trouble is the whole session in
        /// trouble, because the reliable relay backlog that spirals one peer is
        /// the same traffic every peer is being sent.
        /// </summary>
        public bool WireHealthWarning
        {
            get
            {
                foreach (PlayerStat p in Players)
                {
                    if (p.IsSpiralling)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// The snapshot as a single JSON object. Deterministic field order so a
        /// test can assert on it and so a diff of two snapshots is readable.
        /// </summary>
        public string ToJson()
        {
            StringBuilder b = new StringBuilder(256 + Players.Count * 160);
            b.Append('{');

            Num(b, "schemaVersion", SchemaVersion); b.Append(',');
            Num(b, "bootTimeUnixMs", BootTimeUnixMs); b.Append(',');
            Num(b, "generatedAtUnixMs", GeneratedAtUnixMs); b.Append(',');
            Num(b, "uptimeSeconds", UptimeSeconds); b.Append(',');
            Str(b, "relayMode", RelayMode); b.Append(',');
            Num(b, "relayHz", RelayHz); b.Append(',');
            Str(b, "build", Build); b.Append(',');
            Num(b, "totalConnects", TotalConnects); b.Append(',');
            Num(b, "totalDisconnects", TotalDisconnects); b.Append(',');
            Num(b, "currentOnline", CurrentOnline); b.Append(',');
            Num(b, "peakOnline", PeakOnline); b.Append(',');
            Bool(b, "wireHealthWarning", WireHealthWarning); b.Append(',');
            Bool(b, "secondIslandRegistered", SecondIslandRegistered); b.Append(',');
            Num(b, "firstRegionTerrainCount", FirstRegionTerrainCount); b.Append(',');

            Key(b, "players");
            b.Append('[');
            for (int i = 0; i < Players.Count; i++)
            {
                if (i > 0)
                {
                    b.Append(',');
                }
                AppendPlayer(b, Players[i]);
            }
            b.Append(']'); b.Append(',');

            Key(b, "runtime");
            b.Append('{');
            Str(b, "hostMode", "local-single-process"); b.Append(',');
            Str(b, "hostId", "local:primary"); b.Append(',');
            Num(b, "ownedEntityCount", RuntimeOwnedEntityCount); b.Append(',');
            Num(b, "globalEntityCount", RuntimeGlobalEntityCount); b.Append(',');
            Num(b, "unownedEntityCount", RuntimeUnownedEntityCount); b.Append(',');
            Num(b, "ownershipIssueCount", RuntimeOwnershipIssueCount); b.Append(',');
            Key(b, "domains"); b.Append('[');
            for (int i = 0; i < RuntimeDomains.Count; i++)
            {
                if (i > 0) b.Append(',');
                AppendRuntimeDomain(b, RuntimeDomains[i]);
            }
            b.Append(']'); b.Append(',');
            Key(b, "shipDomains"); b.Append('[');
            for (int i = 0; i < ShipDomains.Count; i++)
            {
                if (i > 0) b.Append(',');
                AppendShipDomain(b, ShipDomains[i]);
            }
            b.Append(']');
            b.Append('}');
            b.Append(',');

            AppendTerrain(b, Terrain);

            b.Append('}');
            return b.ToString();
        }

        /// <summary>
        /// The optional-terrain lifecycle section. Written unconditionally so a
        /// reader can always distinguish "off", "requested but held back by its
        /// prerequisite" and "on" without inferring anything from absence.
        ///
        /// Every string here is either a fixed label or an island id / asset name
        /// the server itself registered. No path, packet payload, peer pointer or
        /// operator secret has a route into this object.
        /// </summary>
        private static void AppendTerrain(StringBuilder b, TerrainRuntimeStat t)
        {
            Key(b, "terrain");
            b.Append('{');
            Bool(b, "requested", t.Requested); b.Append(',');
            Bool(b, "enabled", t.Enabled); b.Append(',');
            Str(b, "mode", TerrainTelemetryLabels.Of(t.Mode)); b.Append(',');
            // The same single authoritative loop the runtime section describes.
            // Terrain checkout does not move island authority and says so here.
            Str(b, "hostId", "local:primary"); b.Append(',');
            Str(b, "authority", "process-local-poll-loop"); b.Append(',');
            Num(b, "loadRadiusMetres", t.LoadRadiusMetres); b.Append(',');
            Num(b, "unloadRadiusMetres", t.UnloadRadiusMetres); b.Append(',');
            Num(b, "assetAckTimeoutMs", t.AssetAckTimeoutMs); b.Append(',');
            Num(b, "settleDelayMs", t.SettleDelayMs); b.Append(',');
            Num(b, "candidateCount", t.CandidateCount); b.Append(',');
            Num(b, "trackedPeerCount", t.TrackedPeerCount); b.Append(',');
            Num(b, "readyCount", t.ReadyCount); b.Append(',');
            Num(b, "warningCount", t.WarningCount); b.Append(',');
            Num(b, "errorCount", t.ErrorCount); b.Append(',');
            Num(b, "eventCapacity", TerrainEventLog.Capacity); b.Append(',');

            Key(b, "stateCounts");
            b.Append('{');
            IReadOnlyList<int> counts = t.StateCounts;
            for (int i = 0; i < TerrainTelemetryLabels.AllStates.Count; i++)
            {
                if (i > 0) b.Append(',');
                Num(b, TerrainTelemetryLabels.Of(TerrainTelemetryLabels.AllStates[i]), counts[i]);
            }
            b.Append('}'); b.Append(',');

            Key(b, "players"); b.Append('[');
            for (int i = 0; i < t.Players.Count; i++)
            {
                if (i > 0) b.Append(',');
                AppendTerrainPlayer(b, t.Players[i]);
            }
            b.Append(']'); b.Append(',');

            Key(b, "islands"); b.Append('[');
            for (int i = 0; i < t.Islands.Count; i++)
            {
                if (i > 0) b.Append(',');
                AppendTerrainIsland(b, t.Islands[i]);
            }
            b.Append(']'); b.Append(',');

            Key(b, "events"); b.Append('[');
            for (int i = 0; i < t.Events.Count; i++)
            {
                if (i > 0) b.Append(',');
                TerrainEventStat e = t.Events[i];
                b.Append('{');
                Num(b, "ageMs", e.AgeMs); b.Append(',');
                Str(b, "kind", TerrainTelemetryLabels.Of(e.Kind)); b.Append(',');
                Str(b, "islandId", e.IslandId); b.Append(',');
                Num(b, "playerEntityId", e.PlayerEntityId); b.Append(',');
                Num(b, "slot", e.Slot); b.Append(',');
                Bool(b, "success", e.Success);
                b.Append('}');
            }
            b.Append(']');
            b.Append('}');
        }

        private static void AppendTerrainPlayer(StringBuilder b, TerrainPlayerStat p)
        {
            b.Append('{');
            // Entity id first: a peer is identified by the player it controls.
            // Slot is a process-local ordinal so a peer that has not spawned an
            // entity yet still has a stable row; it is not a peer handle.
            Num(b, "playerEntityId", p.PlayerEntityId); b.Append(',');
            Num(b, "slot", p.Slot); b.Append(',');
            Num(b, "x", p.X); b.Append(','); Num(b, "y", p.Y); b.Append(','); Num(b, "z", p.Z); b.Append(',');
            NullableStr(b, "confirmedGroundIslandId", p.ConfirmedGroundIslandId); b.Append(',');
            NullableStr(b, "requestedDestinationIslandId", p.RequestedDestinationIslandId); b.Append(',');
            Str(b, "pendingAction", TerrainTelemetryLabels.Of(p.PendingAction)); b.Append(',');
            NullableStr(b, "pendingIslandId", p.PendingIslandId); b.Append(',');
            Bool(b, "correlatedAckObserved", p.CorrelatedAckObserved); b.Append(',');
            Bool(b, "removeSupported", p.RemoveSupported); b.Append(',');
            Bool(b, "mayRemove", p.MayRemove); b.Append(',');
            Bool(b, "legacyRetaining", p.LegacyRetaining); b.Append(',');
            Bool(b, "connectPlanComplete", p.ConnectPlanComplete); b.Append(',');
            Bool(b, "settleWaiting", p.SettleWaiting); b.Append(',');
            Bool(b, "destinationWaiting", p.DestinationWaiting); b.Append(',');
            Num(b, "readyCount", p.ReadyCount); b.Append(',');
            Str(b, "warning", p.Warning); b.Append(',');

            Key(b, "asset");
            if (p.Asset.HasValue)
            {
                TerrainAssetFlightStat a = p.Asset.Value;
                b.Append('{');
                Str(b, "islandId", a.IslandId); b.Append(',');
                Str(b, "assetName", a.AssetName); b.Append(',');
                Num(b, "requestAgeMs", a.RequestAgeMs); b.Append(',');
                Num(b, "lastRetryAgeMs", a.LastRetryAgeMs); b.Append(',');
                Num(b, "retryCount", a.RetryCount); b.Append(',');
                Bool(b, "acknowledged", a.Acknowledged); b.Append(',');
                Bool(b, "fallbackDue", a.FallbackDue);
                b.Append('}');
            }
            else
            {
                // null, not a zero-age request: "idle" and "asked one millisecond
                // ago" are different facts about a peer.
                b.Append("null");
            }
            b.Append(',');

            Key(b, "islands"); b.Append('[');
            for (int i = 0; i < p.Islands.Count; i++)
            {
                if (i > 0) b.Append(',');
                b.Append('{');
                Str(b, "islandId", p.Islands[i].IslandId); b.Append(',');
                Str(b, "state", TerrainTelemetryLabels.Of(p.Islands[i].State));
                b.Append('}');
            }
            b.Append(']');
            b.Append('}');
        }

        private static void AppendTerrainIsland(StringBuilder b, TerrainIslandStat i)
        {
            b.Append('{');
            Str(b, "islandId", i.IslandId); b.Append(',');
            Str(b, "displayName", i.DisplayName); b.Append(',');
            Num(b, "terrainEntityId", i.TerrainEntityId); b.Append(',');
            Bool(b, "registered", i.Registered); b.Append(',');
            Bool(b, "locallyOwned", i.LocallyOwned); b.Append(',');
            Bool(b, "hasEnvelope", i.HasEnvelope); b.Append(',');
            Bool(b, "managed", i.Managed); b.Append(',');
            Bool(b, "unconditional", i.Unconditional); b.Append(',');
            Num(b, "readyPeerCount", i.ReadyPeerCount); b.Append(',');
            Num(b, "loadingPeerCount", i.LoadingPeerCount); b.Append(',');
            Num(b, "drainingPeerCount", i.DrainingPeerCount); b.Append(',');
            Num(b, "unloadingPeerCount", i.UnloadingPeerCount); b.Append(',');
            Num(b, "retainedLegacyPeerCount", i.RetainedLegacyPeerCount); b.Append(',');
            Num(b, "errorPeerCount", i.ErrorPeerCount); b.Append(',');
            Num(b, "resourceNodeCount", i.ResourceNodeCount); b.Append(',');
            Num(b, "checkedOutResourceCount", i.CheckedOutResourceCount); b.Append(',');
            Bool(b, "resourceDrainWired", i.ResourceDrainWired); b.Append(',');

            // Geometry is reported only where an extracted envelope evidences it.
            // An island without one is null here rather than a fabricated box.
            Key(b, "envelope");
            if (i.HasEnvelope)
            {
                b.Append('{');
                Num(b, "minX", i.MinX); b.Append(','); Num(b, "minY", i.MinY); b.Append(',');
                Num(b, "minZ", i.MinZ); b.Append(','); Num(b, "maxX", i.MaxX); b.Append(',');
                Num(b, "maxY", i.MaxY); b.Append(','); Num(b, "maxZ", i.MaxZ); b.Append(',');
                Num(b, "spanX", i.SpanX); b.Append(','); Num(b, "spanY", i.SpanY); b.Append(',');
                Num(b, "spanZ", i.SpanZ);
                b.Append('}');
            }
            else
            {
                b.Append("null");
            }
            b.Append('}');
        }

        private static void NullableStr(StringBuilder b, string name, string? value)
        {
            Key(b, name);
            if (value == null) b.Append("null");
            else AppendJsonString(b, value);
        }

        private static void AppendRuntimeDomain(StringBuilder b, RuntimeDomainStat d)
        {
            b.Append('{');
            Str(b, "domainId", d.DomainId); b.Append(',');
            Str(b, "kind", d.Kind); b.Append(',');
            Str(b, "label", d.Label); b.Append(',');
            Str(b, "hostId", d.HostId); b.Append(',');
            Key(b, "affinityDomainId");
            if (d.AffinityDomainId == null) b.Append("null");
            else AppendJsonString(b, d.AffinityDomainId);
            b.Append(',');
            Num(b, "entityCount", d.EntityCount); b.Append(',');
            Bool(b, "active", d.Active); b.Append(',');
            Num(b, "warningCount", d.WarningCount); b.Append(',');
            Num(b, "x", d.X); b.Append(',');
            Num(b, "y", d.Y); b.Append(',');
            Num(b, "z", d.Z);
            b.Append('}');
        }

        private static void AppendShipDomain(StringBuilder b, ShipDomainStat d)
        {
            b.Append('{');
            Str(b, "domainId", d.DomainId); b.Append(',');
            Num(b, "hullEntityId", d.HullEntityId); b.Append(',');
            Num(b, "authorityGeneration", d.AuthorityGeneration); b.Append(',');
            Num(b, "replicationSequence", d.ReplicationSequence); b.Append(',');
            Num(b, "cadenceMs", d.CadenceMs); b.Append(',');
            Num(b, "deliveryAgeMs", d.DeliveryAgeMs); b.Append(',');
            Num(b, "x", d.X); b.Append(','); Num(b, "y", d.Y); b.Append(','); Num(b, "z", d.Z); b.Append(',');
            Bool(b, "active", d.Active); b.Append(','); Bool(b, "piloted", d.Piloted); b.Append(',');
            Bool(b, "liveCadenceExpected", d.LiveCadenceExpected); b.Append(',');
            Key(b, "pilotPlayerEntityId");
            if (d.PilotPlayerEntityId.HasValue) b.Append(d.PilotPlayerEntityId.Value.ToString(CultureInfo.InvariantCulture));
            else b.Append("null");
            b.Append(',');
            Key(b, "aboardPlayerEntityIds"); b.Append('[');
            for (int i = 0; i < d.AboardPlayerEntityIds.Count; i++)
            {
                if (i > 0) b.Append(',');
                b.Append(d.AboardPlayerEntityIds[i].ToString(CultureInfo.InvariantCulture));
            }
            b.Append(']'); b.Append(',');
            Num(b, "deckCount", d.DeckCount); b.Append(',');
            Num(b, "mountedPartCount", d.MountedPartCount); b.Append(',');
            Num(b, "subscriberCount", d.SubscriberCount); b.Append(',');
            Bool(b, "staleDelivery", d.StaleDelivery); b.Append(',');
            Bool(b, "aboardCheckoutWarning", d.AboardCheckoutWarning);
            b.Append('}');
        }

        private static void AppendPlayer(StringBuilder b, PlayerStat p)
        {
            b.Append('{');
            Num(b, "entityId", p.EntityId); b.Append(',');
            // Hex string to match the "peer 0x..." identity the server logs use,
            // and because a 64-bit pointer value is not safely a JSON number.
            Str(b, "peerId", "0x" + p.PeerId.ToString("x")); b.Append(',');
            Num(b, "connectedAtUnixMs", p.ConnectedAtUnixMs); b.Append(',');

            Key(b, "position");
            if (p.Position.HasValue)
            {
                FixedPointPosition position = p.Position.Value;
                b.Append('{');
                Num(b, "x", position.MetresX); b.Append(',');
                Num(b, "y", position.MetresY); b.Append(',');
                Num(b, "z", position.MetresZ);
                b.Append('}');
            }
            else
            {
                b.Append("null");
            }
            b.Append(',');

            Key(b, "health");
            if (p.Health.HasValue)
            {
                EnetPeerHealth h = p.Health.Value;
                b.Append('{');
                Num(b, "rttMs", h.RoundTripTimeMs); b.Append(',');
                Num(b, "rttVarianceMs", h.RoundTripTimeVarianceMs); b.Append(',');
                Num(b, "packetsLost", h.PacketsLost); b.Append(',');
                Num(b, "packetsSent", h.PacketsSent); b.Append(',');
                Num(b, "inFlightBytes", h.ReliableDataInTransit); b.Append(',');
                Bool(b, "spiral", p.IsSpiralling);
                b.Append('}');
            }
            else
            {
                // null, not zeros: an unreadable ENet layout must not masquerade
                // as a perfectly healthy peer.
                b.Append("null");
            }
            b.Append('}');
        }

        private static void Key(StringBuilder b, string name)
        {
            AppendJsonString(b, name);
            b.Append(':');
        }

        private static void Num(StringBuilder b, string name, long value)
        {
            Key(b, name);
            b.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Num(StringBuilder b, string name, double value)
        {
            Key(b, name);
            b.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void Bool(StringBuilder b, string name, bool value)
        {
            Key(b, name);
            b.Append(value ? "true" : "false");
        }

        private static void Str(StringBuilder b, string name, string? value)
        {
            Key(b, name);
            AppendJsonString(b, value ?? string.Empty);
        }

        /// <summary>
        /// Appends a JSON string literal, escaped per RFC 8259. Only the
        /// operator-controlled RelayMode/Build strings and fixed field names pass
        /// through here, but a server name in a future field, or a build tag with
        /// a quote in it, must not be able to break the file the other process
        /// parses.
        /// </summary>
        private static void AppendJsonString(StringBuilder b, string value)
        {
            b.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\b': b.Append("\\b"); break;
                    case '\f': b.Append("\\f"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            b.Append(c);
                        }
                        break;
                }
            }
            b.Append('"');
        }
    }
}

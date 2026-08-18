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
    /// WHAT ONE SHIP IS, as opposed to where it is: the shape the player built, the
    /// dimensions of it, who owns it and what it is made of.
    ///
    /// This is STATIC for the life of a hull. It rides the same snapshot as the
    /// pose because the snapshot is the only bridge the two processes have, but a
    /// reader is expected to treat the outline as immutable per
    /// hull and rebuild nothing while <see cref="Present"/> and the hull id are
    /// unchanged - a ring is a few dozen numbers on the wire and an SVG path
    /// rebuild sixty times a second in the browser, so the cost that matters is
    /// the second one.
    ///
    /// PROVENANCE. Everything here is RECOVERED from the player's own hull bytes or
    /// from the ledger the build wrote - nothing is inferred and nothing is tuned.
    /// A hull whose bytes are missing or undecodable reports
    /// <see cref="Present"/> false rather than a plausible substitute shape.
    /// </summary>
    public readonly struct ShipHullStat
    {
        private readonly Ship.ShipMapSilhouette? _silhouette;
        private readonly string? _ownerCharacterUid;
        private readonly Materials.HullMaterials? _materials;

        /// <summary>The hull whose bytes could not be found or decoded: no shape, no dimensions.</summary>
        public static ShipHullStat Unavailable => default;

        public ShipHullStat(
            Ship.ShipMapSilhouette? silhouette,
            string? ownerCharacterUid,
            bool docked,
            Materials.HullMaterials? materials)
        {
            _silhouette = silhouette;
            _ownerCharacterUid = ownerCharacterUid;
            _materials = materials;
            Docked = docked;
        }

        /// <summary>Whether a real outline and real dimensions are carried.</summary>
        public bool Present => _silhouette != null && !_silhouette.IsEmpty;

        /// <summary>The derived plan-view ring and the measured hull behind it.</summary>
        public Ship.ShipMapSilhouette Silhouette => _silhouette ?? Ship.ShipMapSilhouette.Empty;

        /// <summary>The owner's character uid, or empty for an unowned hull.</summary>
        public string OwnerCharacterUid => _ownerCharacterUid ?? string.Empty;

        /// <summary>Whether the hull is sitting in a shipyard.</summary>
        public bool Docked { get; }

        /// <summary>The dominant wood and metal, with their qualities.</summary>
        public Materials.HullMaterials Materials => _materials ?? Multiplayer.Materials.HullMaterials.Legacy;
    }

    /// <summary>
    /// THE NUMBERS A SECOND EVALUATOR OF SHIP MOTION NEEDS, read off the running
    /// server's own flight tuning rather than restated.
    ///
    /// The operator console draws a ship somewhere, and "somewhere" is a
    /// measurement several seconds old carried forward along the velocity the
    /// server reported. How far it may be carried is not a taste call: it is
    /// solved from the acceleration limit the flight integrator is ACTUALLY
    /// configured with, which is env-tunable per deployment. Publishing the live
    /// value - instead of letting the console hard-code the default - is what stops
    /// a server tuned to accelerate harder from being drawn with a window that is
    /// too generous for it.
    ///
    /// Every field is a direct read of <see cref="Ship.Flight.FlightTuning"/> or of
    /// <see cref="Ship.ShipMapMotion"/>'s own arithmetic. Nothing is a literal.
    /// </summary>
    public readonly struct ShipMapRuntimeStat
    {
        /// <summary>A server that reports no ship-motion model at all.</summary>
        public static ShipMapRuntimeStat Off => default;

        public ShipMapRuntimeStat(double accelMps2, double maxSpeedMps)
        {
            Present = true;
            AccelMps2 = accelMps2;
            MaxSpeedMps = maxSpeedMps;
        }

        public bool Present { get; }

        /// <summary>The live <c>WAREBORN_FLIGHT_ACCEL</c>, m/s^2.</summary>
        public double AccelMps2 { get; }

        /// <summary>The live <c>WAREBORN_FLIGHT_MAX_SPEED</c>, m/s.</summary>
        public double MaxSpeedMps { get; }

        /// <summary>How long a reader may dead-reckon, solved from the acceleration above.</summary>
        public double WindowSeconds => Ship.ShipMapMotion.WindowSecondsFor(AccelMps2);

        /// <summary>The metres of error that window is bought at.</summary>
        public double ToleratedErrorMetres => Ship.ShipMapMotion.ToleratedErrorMetres;

        /// <summary>
        /// The hard ceiling on any window, published so a second evaluator does
        /// not have to restate it. It is the one guard that has nothing to do
        /// with the error budget: a very gentle acceleration would otherwise
        /// permit a minute of reckoning, and a minute-old pose is not a position
        /// however tight its bound is - the helm can be released, the ship
        /// recalled, the domain torn down.
        /// </summary>
        public double MaxWindowSeconds => Ship.ShipMapMotion.MaxWindowSeconds;
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

        /// <summary>
        /// Heading about world +Y, radians; 0 faces +Z, positive turns the nose
        /// toward +X. A ship's facing is REAL DATA, unlike a creature's, so a
        /// console that draws hulls draws them turned.
        /// </summary>
        public double YawRadians { get; }

        /// <summary>The current turn rate, rad/s - the derivative a reader needs to carry the heading forward.</summary>
        public double YawRateRadPerSec { get; }

        /// <summary>
        /// The hull's own velocity in global axes, m/s: the exact derivative of the
        /// position steps, which is what the flight state reports to the client's
        /// spline as well. Carried so a reader seconds behind the measurement can
        /// dead-reckon with the SERVER's number instead of guessing one.
        /// </summary>
        public double VxMps { get; }
        public double VyMps { get; }
        public double VzMps { get; }

        /// <summary>The shape, dimensions, owner and materials of this hull.</summary>
        public ShipHullStat Hull { get; }

        /// <summary>
        /// The character uid this hull belongs to, or "" when the owner is not
        /// known to this boot. The operator surface answers "the ship this player
        /// owns" from it.
        ///
        /// DERIVED, not stored. It reads straight through <see cref="Hull"/>, which
        /// is where the owner actually lives, so the two CANNOT disagree - there is
        /// one field and this is a second spelling of it. It exists as a top-level
        /// name because ownership and SHAPE are different facts with different
        /// availability: a hull whose bytes are missing or will not decode has no
        /// silhouette (<c>Present</c> false) but still has an owner, and a resolver
        /// that had to reach through a "hull" it was told is not present would read
        /// as though it were asking about the shape.
        /// </summary>
        public string OwnerCharacterUid => Hull.OwnerCharacterUid;

        public ShipDomainStat(string domainId, long hullEntityId, long authorityGeneration,
            long replicationSequence, int cadenceMs, long deliveryAgeMs,
            double x, double y, double z, bool active, bool piloted,
            bool liveCadenceExpected, long? pilotPlayerEntityId,
            IReadOnlyList<long> aboardPlayerEntityIds, int deckCount,
            int mountedPartCount, int subscriberCount,
            double yawRadians = 0, double yawRateRadPerSec = 0,
            double vxMps = 0, double vyMps = 0, double vzMps = 0,
            ShipHullStat hull = default)
        {
            YawRadians = yawRadians;
            YawRateRadPerSec = yawRateRadPerSec;
            VxMps = vxMps;
            VyMps = vyMps;
            VzMps = vzMps;
            Hull = hull;
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

        /// <summary>
        /// The DURABLE identity behind this row, as the canonical uid string, or ""
        /// when no character uid has arrived for this entity yet.
        ///
        /// Everything else in this row is per-session: an entity id and a peer
        /// handle are both recycled, so a dashboard that lets an operator act on a
        /// row is letting them act on an identifier that may already mean somebody
        /// else by the time the click lands. This field is what an operator command
        /// can be addressed to instead - it is the same key ship ownership, the
        /// shipyard registration and the stored position all join on.
        ///
        /// "" rather than null, and read as "no identity yet" rather than as an
        /// identity: two rows with "" are two unknowns, not one player. See
        /// OperatorTargetPolicy, which refuses on both zero and multiple matches.
        /// </summary>
        public string CharacterUid { get; }

        public PlayerStat(long entityId, ulong peerId, long connectedAtUnixMs,
            EnetPeerHealth? health, FixedPointPosition? position = null,
            string? characterUid = null)
        {
            EntityId = entityId;
            PeerId = peerId;
            ConnectedAtUnixMs = connectedAtUnixMs;
            Health = health;
            Position = position;
            CharacterUid = characterUid ?? string.Empty;
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
        ///
        /// v10 adds the interest section. v9 is deliberately SKIPPED here: it
        /// was claimed by the concurrent ecology work, and two branches both
        /// writing "9" with different shapes would be exactly the mis-parse
        /// this number exists to prevent. The reader keys on field presence,
        /// not on this value, so either order of merge stays readable.
        /// </summary>
        // v9: the fauna section gained an `ecology` object (capacity, expressed,
        // quiet factor, groups with their (behaviour, epoch) pair, and bloom
        // parameters).
        // v10: adds the `interest` section (radii, budgets, gates and per-peer
        // holdings). Both landed on concurrent branches; v9 and v10 mean what
        // they say and the reader is presence-keyed, so a v7/v8/v9 file still
        // parses - GameStats projects any missing section to an explicit ABSENT
        // rather than a default, so "never said" is distinguishable from "false".
        // v11: adds the `skyWhale` section (the one animal per region, the clock
        // its circuit is a function of, and where each whale's current call is
        // coming from).
        public const int SchemaVersion = 11;

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

        /// <summary>
        /// The live island fauna and, critically, the clock that places it
        /// (schema v7+). Always a fully built value: a server with fauna off
        /// reports <see cref="FaunaRuntimeStat.Off"/> rather than an absent
        /// section, so a reader distinguishes "off" from "older server".
        /// </summary>
        public FaunaRuntimeStat Fauna { get; }

        /// <summary>
        /// The flight tuning a reader needs to draw ships between snapshots
        /// (schema v8+). Always a fully built value on a server that has ships;
        /// <see cref="ShipMapRuntimeStat.Present"/> false means the game server
        /// predates it, which a reader must be able to tell from "no ships".
        /// </summary>
        public ShipMapRuntimeStat ShipModel { get; }

        /// <summary>
        /// The interest picture: radii, budgets, gates and per-peer holdings
        /// (schema v10+). <see cref="InterestRuntimeStat.Present"/> false means
        /// the game server predates it, which a reader renders as "not
        /// reported" rather than as any number.
        /// </summary>
        public InterestRuntimeStat Interest { get; }

        /// <summary>
        /// The sky whale and, critically, the clock that places it (schema v11+).
        /// Always a fully built value: a server with the whale off reports
        /// <see cref="SkyWhaleRuntimeStat.Off"/> rather than an absent section, so
        /// a reader distinguishes "off" from "older server".
        /// </summary>
        public SkyWhaleRuntimeStat SkyWhale { get; }

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
            TerrainRuntimeStat? terrain = null,
            FaunaRuntimeStat? fauna = null,
            ShipMapRuntimeStat shipModel = default,
            InterestRuntimeStat interest = default,
            SkyWhaleRuntimeStat? skyWhale = null)
        {
            ShipModel = shipModel;
            Interest = interest;
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
            Fauna = fauna ?? FaunaRuntimeStat.Off;
            SkyWhale = skyWhale ?? SkyWhaleRuntimeStat.Off;
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
            b.Append(',');
            AppendFauna(b, Fauna);
            b.Append(',');
            AppendShipModel(b, ShipModel);
            b.Append(',');
            AppendInterest(b, Interest);
            b.Append(',');
            AppendSkyWhale(b, SkyWhale);

            b.Append('}');
            return b.ToString();
        }

        /// <summary>
        /// The interest section: the streaming radii, budgets and gates this
        /// boot decides with, and what each peer currently holds. Written
        /// unconditionally with an explicit <c>present</c>, for the reason every
        /// other section here is: absence must read as "an older game server",
        /// never as "nothing is streamed".
        /// </summary>
        private static void AppendInterest(StringBuilder b, InterestRuntimeStat s)
        {
            Key(b, "interest");
            b.Append('{');
            Bool(b, "present", s.Present); b.Append(',');

            Key(b, "resources");
            b.Append('{');
            Bool(b, "enabled", s.ResourcesEnabled); b.Append(',');
            Num(b, "loadRadiusMetres", s.ResourceLoadRadiusMetres); b.Append(',');
            Num(b, "unloadRadiusMetres", s.ResourceUnloadRadiusMetres); b.Append(',');
            Num(b, "perPeerBudget", s.ResourcePerPeerBudget); b.Append(',');
            Num(b, "connectRadiusMetres", s.ResourceConnectRadiusMetres);
            b.Append('}'); b.Append(',');

            Key(b, "fauna");
            b.Append('{');
            Bool(b, "enabled", s.FaunaEnabled); b.Append(',');
            Num(b, "loadRadiusMetres", s.FaunaLoadRadiusMetres); b.Append(',');
            Num(b, "unloadRadiusMetres", s.FaunaUnloadRadiusMetres);
            b.Append('}'); b.Append(',');

            Key(b, "ship");
            b.Append('{');
            Num(b, "loadRadiusMetres", s.ShipLoadRadiusMetres); b.Append(',');
            Num(b, "unloadRadiusMetres", s.ShipUnloadRadiusMetres); b.Append(',');
            // The ship step of the connect plan IS the load radius; a second
            // name would be a second value to let drift.
            Num(b, "connectRadiusMetres", s.ShipLoadRadiusMetres);
            b.Append('}'); b.Append(',');

            Num(b, "terrainConnectRadiusMetres", s.TerrainConnectRadiusMetres); b.Append(',');

            Key(b, "gates");
            b.Append('{');
            Bool(b, "loadBarrier", s.LoadBarrier); b.Append(',');
            Num(b, "spawnPaceMs", s.SpawnPaceMs);
            b.Append('}'); b.Append(',');

            Key(b, "peers"); b.Append('[');
            for (int i = 0; i < s.Peers.Count; i++)
            {
                if (i > 0) b.Append(',');
                InterestPeerStat peer = s.Peers[i];
                b.Append('{');
                Num(b, "playerEntityId", peer.PlayerEntityId); b.Append(',');
                Num(b, "resourceCheckedOut", peer.ResourceCheckedOut); b.Append(',');
                Num(b, "faunaCheckedOut", peer.FaunaCheckedOut); b.Append(',');
                Key(b, "resourceIslands"); b.Append('[');
                for (int j = 0; j < peer.ResourceIslands.Count; j++)
                {
                    if (j > 0) b.Append(',');
                    b.Append('{');
                    Str(b, "islandId", peer.ResourceIslands[j].IslandId); b.Append(',');
                    Num(b, "checkedOut", peer.ResourceIslands[j].CheckedOut);
                    b.Append('}');
                }
                b.Append(']'); b.Append(',');
                Key(b, "shipDomainIds"); b.Append('[');
                for (int j = 0; j < peer.ShipDomainIds.Count; j++)
                {
                    if (j > 0) b.Append(',');
                    AppendJsonString(b, peer.ShipDomainIds[j]);
                }
                b.Append(']');
                b.Append('}');
            }
            b.Append(']');
            b.Append('}');
        }

        /// <summary>
        /// The ship-motion model section. Unconditional and self-describing, like
        /// the terrain and fauna sections: a reader must distinguish a server that
        /// predates it from a world with no ships in it, because the first means
        /// "draw nothing and say why" and the second means "the sea is empty".
        /// </summary>
        private static void AppendShipModel(StringBuilder b, ShipMapRuntimeStat s)
        {
            Key(b, "shipModel");
            b.Append('{');
            Bool(b, "present", s.Present); b.Append(',');
            Num(b, "accelMps2", s.AccelMps2); b.Append(',');
            Num(b, "maxSpeedMps", s.MaxSpeedMps); b.Append(',');
            Num(b, "windowSeconds", s.WindowSeconds); b.Append(',');
            Num(b, "maxWindowSeconds", s.MaxWindowSeconds); b.Append(',');
            Num(b, "toleratedErrorMetres", s.ToleratedErrorMetres);
            b.Append('}');
        }

        /// <summary>
        /// The island-fauna section. Written unconditionally for the same reason
        /// the terrain section is: absence must mean "older game server", never
        /// "no wildlife".
        ///
        /// The island list is COUNTS BY SPECIES and nothing else. It is small - one
        /// short id and two integers per populated island, about two kilobytes for
        /// the whole tier-1 world - because the expensive half of the answer, WHERE
        /// each creature is, is a function of <c>clockSeconds</c> that the reader
        /// evaluates for itself rather than a payload anyone has to ship at 4 Hz.
        /// </summary>
        private static void AppendFauna(StringBuilder b, FaunaRuntimeStat f)
        {
            Key(b, "fauna");
            b.Append('{');
            Bool(b, "enabled", f.Enabled); b.Append(',');
            Num(b, "clockSeconds", f.ClockSeconds); b.Append(',');
            Num(b, "liveCount", f.LiveCount); b.Append(',');
            Num(b, "budget", f.Budget); b.Append(',');
            Num(b, "demand", f.Demand); b.Append(',');
            Num(b, "perPeerBudget", f.PerPeerBudget); b.Append(',');
            Num(b, "poseIntervalMs", f.PoseIntervalMs); b.Append(',');
            Key(b, "islands"); b.Append('[');
            for (int i = 0; i < f.Islands.Count; i++)
            {
                if (i > 0) b.Append(',');
                FaunaIslandStat island = f.Islands[i];
                b.Append('{');
                Str(b, "islandId", island.IslandId); b.Append(',');
                Num(b, "mantaRays", island.MantaRays); b.Append(',');
                Num(b, "jellyFish", island.JellyFish);
                b.Append('}');
            }
            b.Append(']'); b.Append(',');
            AppendFaunaEcology(b, f.Ecology);
            b.Append('}');
        }

        /// <summary>
        /// The sky whale section (schema v11).
        ///
        /// Written unconditionally with an explicit enabled flag, like every other
        /// section here: absence must mean "an older game server", never "no
        /// whale". One row per region, and the rows are TINY - an id, an entity
        /// pair and one call station each - because the expensive half of the
        /// answer, WHERE the animal is, is a function of <c>clockSeconds</c> that
        /// the reader evaluates for itself.
        ///
        /// The call station IS carried, unlike any creature position, because a
        /// call is a discrete event pinned to one place for two minutes rather than
        /// a pose that moves between snapshots. See
        /// <see cref="SkyWhaleRegionStat"/>.
        /// </summary>
        private static void AppendSkyWhale(StringBuilder b, SkyWhaleRuntimeStat w)
        {
            Key(b, "skyWhale");
            b.Append('{');
            Bool(b, "enabled", w.Enabled); b.Append(',');
            Num(b, "clockSeconds", w.ClockSeconds); b.Append(',');
            Num(b, "whaleCount", w.WhaleCount); b.Append(',');
            Num(b, "loadRadiusMetres", w.LoadRadiusMetres); b.Append(',');
            Num(b, "callRadiusMetres", w.CallRadiusMetres); b.Append(',');
            Num(b, "poseIntervalMs", w.PoseIntervalMs); b.Append(',');
            Num(b, "callIntervalSeconds", w.CallIntervalSeconds); b.Append(',');
            Key(b, "regions"); b.Append('[');
            for (int i = 0; i < w.Regions.Count; i++)
            {
                if (i > 0) b.Append(',');
                SkyWhaleRegionStat region = w.Regions[i];
                b.Append('{');
                Str(b, "regionId", region.RegionId); b.Append(',');
                Num(b, "entityId", region.EntityId); b.Append(',');
                Num(b, "callEntityId", region.CallEntityId); b.Append(',');
                Num(b, "callIndex", region.CallIndex); b.Append(',');
                Num(b, "callX", region.CallX); b.Append(',');
                Num(b, "callY", region.CallY); b.Append(',');
                Num(b, "callZ", region.CallZ);
                b.Append('}');
            }
            b.Append(']');
            b.Append('}');
        }

        /// <summary>
        /// The ecology sub-section (schema v9). Written unconditionally with an
        /// explicit enabled flag, like every other section: absence must mean
        /// "older game server", never "no ecology". Bloom parameters are the
        /// SAME numbers the pose function derives from - published rather than
        /// re-derived, so the two admin/public map consumers evaluate exactly
        /// what the wire carries. Everything here is geometry and counts; no
        /// identity of any kind has a route in.
        /// </summary>
        private static void AppendFaunaEcology(StringBuilder b, FaunaEcologyStat e)
        {
            Key(b, "ecology");
            b.Append('{');
            Bool(b, "enabled", e.Enabled); b.Append(',');
            Num(b, "worldSeed", e.WorldSeed); b.Append(',');
            Key(b, "islands"); b.Append('[');
            for (int i = 0; i < e.Islands.Count; i++)
            {
                if (i > 0) b.Append(',');
                FaunaEcologyIslandStat island = e.Islands[i];
                b.Append('{');
                Str(b, "islandId", island.IslandId); b.Append(',');
                Num(b, "quietFactor", island.QuietFactor); b.Append(',');
                Num(b, "mantaCapacity", island.MantaCapacity); b.Append(',');
                Num(b, "jellyCapacity", island.JellyCapacity); b.Append(',');
                Num(b, "mantaExpressed", island.MantaExpressed); b.Append(',');
                Num(b, "jellyExpressed", island.JellyExpressed); b.Append(',');
                Str(b, "mantaPhase", island.MantaPhase); b.Append(',');
                Num(b, "mantaPhaseFraction", island.MantaPhaseFraction); b.Append(',');
                Str(b, "jellyPhase", island.JellyPhase); b.Append(',');
                Num(b, "jellyPhaseFraction", island.JellyPhaseFraction); b.Append(',');
                Key(b, "groups"); b.Append('[');
                for (int g = 0; g < island.Groups.Count; g++)
                {
                    if (g > 0) b.Append(',');
                    FaunaGroupStat group = island.Groups[g];
                    b.Append('{');
                    Str(b, "species", group.Species); b.Append(',');
                    Num(b, "index", group.Index); b.Append(',');
                    Num(b, "bloom", group.BloomIndex); b.Append(',');
                    Num(b, "members", group.Members); b.Append(',');
                    Str(b, "behaviour", group.Behaviour); b.Append(',');
                    Num(b, "epochSeconds", group.EpochSeconds); b.Append(',');
                    Num(b, "durationSeconds", group.DurationSeconds); b.Append(',');
                    Num(b, "toBloom", group.ToBloom);
                    b.Append('}');
                }
                b.Append(']'); b.Append(',');
                Key(b, "blooms"); b.Append('[');
                for (int k = 0; k < island.Blooms.Count; k++)
                {
                    if (k > 0) b.Append(',');
                    FaunaBloomStat bloom = island.Blooms[k];
                    b.Append('{');
                    Str(b, "species", bloom.Species); b.Append(',');
                    Num(b, "index", bloom.Index); b.Append(',');
                    Num(b, "amplitude", bloom.Amplitude); b.Append(',');
                    Num(b, "sigma", bloom.SigmaMetres); b.Append(',');
                    Num(b, "annulusRadius", bloom.AnnulusRadiusMetres); b.Append(',');
                    Num(b, "radialDrift", bloom.RadialDriftMetres); b.Append(',');
                    Num(b, "angularDrift", bloom.AngularDriftRadians); b.Append(',');
                    // Frequencies and phases are NOT rounded, for the recorded
                    // mantaLapSeconds reason: they multiply ELAPSED SECONDS, so a
                    // trimmed digit becomes a position error that grows with
                    // uptime.
                    Num(b, "omegaRadial", bloom.OmegaRadial); b.Append(',');
                    Num(b, "omegaAngular", bloom.OmegaAngular); b.Append(',');
                    Num(b, "omegaMigration", bloom.OmegaMigration); b.Append(',');
                    Num(b, "phaseRadial", bloom.PhaseRadial); b.Append(',');
                    Num(b, "phaseAngular", bloom.PhaseAngular); b.Append(',');
                    Num(b, "baseAngle", bloom.BaseAngleRadians);
                    b.Append('}');
                }
                b.Append(']');
                b.Append('}');
            }
            b.Append(']');
            b.Append('}');
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
            // Whose ship this is, as the same canonical character uid the player
            // rows carry. Read-only information: nothing in the operator surface
            // writes it, and moving a hull never changes it.
            Str(b, "ownerCharacterUid", d.OwnerCharacterUid); b.Append(',');
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
            Bool(b, "aboardCheckoutWarning", d.AboardCheckoutWarning); b.Append(',');

            // The heading and the two derivatives. yawRadians was sitting unused in
            // the same flight state the position is read from; without it a console
            // can only draw a hull as a dot, and a ship's facing is real data.
            Num(b, "yawRadians", Trim(d.YawRadians)); b.Append(',');
            Num(b, "yawRateRadPerSec", Trim(d.YawRateRadPerSec)); b.Append(',');
            Num(b, "vxMps", Trim(d.VxMps)); b.Append(',');
            Num(b, "vyMps", Trim(d.VyMps)); b.Append(',');
            Num(b, "vzMps", Trim(d.VzMps)); b.Append(',');

            AppendHull(b, d.Hull);
            b.Append('}');
        }

        /// <summary>
        /// The hull's shape, size, owner and materials.
        ///
        /// The ring is written as a FLAT array of alternating x and z in hull-local
        /// metres, trimmed to the centimetre - the same encoding and the same
        /// reasoning as the preserved island coastlines, which are flat and trimmed
        /// to the decimetre. Flat costs about a third of what pairs of objects
        /// would, and a centimetre is far below one screen pixel at every zoom the
        /// console offers, on a hull an order of magnitude smaller than an island.
        ///
        /// Written unconditionally with an explicit <c>present</c>, for the reason
        /// every other section here is: absence must read as "an older game server",
        /// never as "this ship has no shape".
        /// </summary>
        private static void AppendHull(StringBuilder b, ShipHullStat h)
        {
            Key(b, "hull");
            b.Append('{');
            Bool(b, "present", h.Present); b.Append(',');
            Str(b, "ownerCharacterUid", h.OwnerCharacterUid); b.Append(',');
            Bool(b, "docked", h.Docked); b.Append(',');

            Ship.ShipHullMetrics m = h.Silhouette.Metrics;
            Num(b, "beamMetres", Trim(m.BeamMetres)); b.Append(',');
            Num(b, "keelMetres", Trim(m.KeelMetres)); b.Append(',');
            Num(b, "deckPlaneMetres", Trim(m.DeckPlaneMetres)); b.Append(',');
            Num(b, "bowLocalZMetres", Trim(m.BowLocalZMetres)); b.Append(',');
            Num(b, "sternLocalZMetres", Trim(m.SternLocalZMetres)); b.Append(',');
            Num(b, "cellCount", m.CellCount); b.Append(',');
            Num(b, "hullDeckCount", m.DeckCount); b.Append(',');
            Num(b, "sectionCount", h.Silhouette.SectionCount); b.Append(',');
            Bool(b, "keelIsLongestAxis", m.KeelIsLongestAxis); b.Append(',');

            Materials.HullMaterials materials = h.Materials;
            Str(b, "woodId", materials.WoodId ?? string.Empty); b.Append(',');
            Num(b, "woodQuality", materials.WoodQuality); b.Append(',');
            Str(b, "metalId", materials.MetalId ?? string.Empty); b.Append(',');
            Num(b, "metalQuality", materials.MetalQuality); b.Append(',');

            Key(b, "outline"); b.Append('[');
            IReadOnlyList<Ship.ShipMapPoint> ring = h.Silhouette.Outline;
            for (int i = 0; i < ring.Count; i++)
            {
                if (i > 0) b.Append(',');
                b.Append(Trim(ring[i].X).ToString("R", CultureInfo.InvariantCulture));
                b.Append(',');
                b.Append(Trim(ring[i].Z).ToString("R", CultureInfo.InvariantCulture));
            }
            b.Append(']');
            b.Append('}');
        }

        /// <summary>
        /// Centimetres. Every hull number on this wire is trimmed the same way, so
        /// a reader that draws the ring and a reader that prints the beam are
        /// looking at the same rounding.
        /// </summary>
        private static double Trim(double metres) => Math.Round(metres, 2, MidpointRounding.AwayFromZero);

        private static void AppendPlayer(StringBuilder b, PlayerStat p)
        {
            b.Append('{');
            Num(b, "entityId", p.EntityId); b.Append(',');
            // Hex string to match the "peer 0x..." identity the server logs use,
            // and because a 64-bit pointer value is not safely a JSON number.
            Str(b, "peerId", "0x" + p.PeerId.ToString("x")); b.Append(',');
            Num(b, "connectedAtUnixMs", p.ConnectedAtUnixMs); b.Append(',');
            // The durable identity an operator command can be addressed to. Always
            // written, "" when unknown, so an older reader sees a field it can
            // ignore and a newer one can tell "not published" from "not known".
            Str(b, "characterUid", p.CharacterUid); b.Append(',');

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

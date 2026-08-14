using System.Globalization;
using System.Text;

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

        public PlayerStat(long entityId, ulong peerId, long connectedAtUnixMs, EnetPeerHealth? health)
        {
            EntityId = entityId;
            PeerId = peerId;
            ConnectedAtUnixMs = connectedAtUnixMs;
            Health = health;
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
        public const int SchemaVersion = 1;

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

        public IReadOnlyList<PlayerStat> Players { get; }

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
            bool secondIslandRegistered = false)
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
            b.Append(']');

            b.Append('}');
            return b.ToString();
        }

        private static void AppendPlayer(StringBuilder b, PlayerStat p)
        {
            b.Append('{');
            Num(b, "entityId", p.EntityId); b.Append(',');
            // Hex string to match the "peer 0x..." identity the server logs use,
            // and because a 64-bit pointer value is not safely a JSON number.
            Str(b, "peerId", "0x" + p.PeerId.ToString("x")); b.Append(',');
            Num(b, "connectedAtUnixMs", p.ConnectedAtUnixMs); b.Append(',');

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

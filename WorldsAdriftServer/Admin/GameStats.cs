using Newtonsoft.Json.Linq;

namespace WorldsAdriftServer.Admin
{
    /// <summary>
    /// The login-server half of the cross-process bridge: it READS the snapshot
    /// the game server writes to a file, because the two run as separate
    /// processes (the game server under Wine cannot reach Postgres; this one
    /// serves HTTP and can). The game server writes
    /// <c>/tmp/wareborn-stats.json</c> atomically every few seconds; this parses
    /// whatever is there and reports how old it is, so the dashboard can show a
    /// "as of N seconds ago" note and a "not reporting" state rather than
    /// pretending stale numbers are live.
    ///
    /// Nothing here throws: a missing file is the normal state when the game
    /// server is down, and a half-written one (never, given the atomic rename,
    /// but belt and braces) must not take the panel down with it.
    /// </summary>
    internal static class GameStats
    {
        /// <summary>
        /// Default path, matched to the game server's own default and mapped
        /// identically under Wine. Overridable with WAREBORN_STATS_FILE on both
        /// sides so a non-default deployment keeps the two in step.
        /// </summary>
        private const string DefaultStatsFile = "/tmp/wareborn-stats.json";

        /// <summary>
        /// How old a snapshot may be before the dashboard calls it stale. The
        /// writer's cadence is 3-5 s; twelve seconds is several missed writes, so
        /// crossing it means the game server has stopped, hung, or lost the disk -
        /// all of which the operator wants flagged, not smoothed over.
        /// </summary>
        internal static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(12);

        internal static string StatsFilePath
        {
            get
            {
                string? configured = Environment.GetEnvironmentVariable("WAREBORN_STATS_FILE");
                return string.IsNullOrWhiteSpace(configured) ? DefaultStatsFile : configured!.Trim();
            }
        }

        /// <summary>Pure staleness call, so the threshold is testable in one place.</summary>
        internal static bool IsStale(TimeSpan age) => age >= StaleAfter;

        /// <summary>
        /// Reads and parses the current snapshot. Returns a result whose State
        /// distinguishes "no file" (game server down) from "file unreadable"
        /// (something wrote garbage) from "ok", so the dashboard can say which.
        /// </summary>
        internal static GameStatsResult Read(DateTimeOffset now)
        {
            return ReadFrom(StatsFilePath, now);
        }

        /// <summary>
        /// The same read against an explicit path. Split out so a test can drive
        /// it against a temp file without mutating a process-wide env var.
        /// </summary>
        internal static GameStatsResult ReadFrom(string path, DateTimeOffset now)
        {
            string raw;
            try
            {
                if (!File.Exists(path))
                {
                    return GameStatsResult.Missing();
                }

                raw = File.ReadAllText(path);
            }
            catch (Exception)
            {
                return GameStatsResult.Unreadable();
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                return GameStatsResult.Unreadable();
            }

            try
            {
                GameStatsSnapshot snapshot = GameStatsSnapshot.Parse(JObject.Parse(raw));
                TimeSpan age = now - snapshot.GeneratedAt;
                if (age < TimeSpan.Zero)
                {
                    age = TimeSpan.Zero;
                }

                return GameStatsResult.Ok(snapshot, age, IsStale(age));
            }
            catch (Exception)
            {
                return GameStatsResult.Unreadable();
            }
        }
    }

    internal enum GameStatsState
    {
        /// <summary>No file: the game server is not running or has never written.</summary>
        Missing,

        /// <summary>A file exists but could not be read or parsed.</summary>
        Unreadable,

        /// <summary>A snapshot was read.</summary>
        Ok,
    }

    internal sealed class GameStatsResult
    {
        public GameStatsState State { get; private init; }
        public GameStatsSnapshot? Snapshot { get; private init; }
        public TimeSpan Age { get; private init; }
        public bool Stale { get; private init; }

        public static GameStatsResult Missing() => new GameStatsResult { State = GameStatsState.Missing };
        public static GameStatsResult Unreadable() => new GameStatsResult { State = GameStatsState.Unreadable };

        public static GameStatsResult Ok(GameStatsSnapshot snapshot, TimeSpan age, bool stale) =>
            new GameStatsResult { State = GameStatsState.Ok, Snapshot = snapshot, Age = age, Stale = stale };
    }

    /// <summary>
    /// A typed view of the game server's snapshot file. Field names mirror the
    /// game side's <c>StatsSnapshot</c> JSON contract; missing fields default
    /// rather than throw, so an older game server writing fewer fields still
    /// renders.
    /// </summary>
    internal sealed class GameStatsSnapshot
    {
        public int SchemaVersion { get; private init; }
        public DateTimeOffset BootTime { get; private init; }
        public DateTimeOffset GeneratedAt { get; private init; }
        public long UptimeSeconds { get; private init; }
        public string RelayMode { get; private init; } = "unknown";
        public int RelayHz { get; private init; }
        public string Build { get; private init; } = "unknown";
        public long TotalConnects { get; private init; }
        public long TotalDisconnects { get; private init; }
        public int CurrentOnline { get; private init; }
        public int PeakOnline { get; private init; }
        public bool WireHealthWarning { get; private init; }
        public bool SecondIslandRegistered { get; private init; }
        public int FirstRegionTerrainCount { get; private init; }
        public IReadOnlyList<GamePlayerStat> Players { get; private init; } = Array.Empty<GamePlayerStat>();
        public string RuntimeHostMode { get; private init; } = "unknown";
        public string RuntimeHostId { get; private init; } = "unknown";
        public int RuntimeOwnedEntityCount { get; private init; }
        public int RuntimeGlobalEntityCount { get; private init; }
        public int RuntimeUnownedEntityCount { get; private init; }
        public int RuntimeOwnershipIssueCount { get; private init; }
        public IReadOnlyList<GameRuntimeDomainStat> RuntimeDomains { get; private init; } = Array.Empty<GameRuntimeDomainStat>();
        public IReadOnlyList<GameShipDomainStat> ShipDomains { get; private init; } = Array.Empty<GameShipDomainStat>();

        public static GameStatsSnapshot Parse(JObject o)
        {
            List<GamePlayerStat> players = new List<GamePlayerStat>();
            if (o["players"] is JArray arr)
            {
                foreach (JToken t in arr)
                {
                    if (t is JObject p)
                    {
                        players.Add(GamePlayerStat.Parse(p));
                    }
                }
            }
            List<GameShipDomainStat> domains = new List<GameShipDomainStat>();
            JObject? runtime = o["runtime"] as JObject;
            if (runtime?["shipDomains"] is JArray domainArray)
            {
                foreach (JToken t in domainArray)
                    if (t is JObject d) domains.Add(GameShipDomainStat.Parse(d));
            }
            List<GameRuntimeDomainStat> runtimeDomains = new List<GameRuntimeDomainStat>();
            if (runtime?["domains"] is JArray runtimeDomainArray)
            {
                foreach (JToken t in runtimeDomainArray)
                    if (t is JObject d) runtimeDomains.Add(GameRuntimeDomainStat.Parse(d));
            }

            return new GameStatsSnapshot
            {
                SchemaVersion = (int?)o["schemaVersion"] ?? 0,
                BootTime = FromUnixMs((long?)o["bootTimeUnixMs"] ?? 0),
                GeneratedAt = FromUnixMs((long?)o["generatedAtUnixMs"] ?? 0),
                UptimeSeconds = (long?)o["uptimeSeconds"] ?? 0,
                RelayMode = (string?)o["relayMode"] ?? "unknown",
                RelayHz = (int?)o["relayHz"] ?? 0,
                Build = (string?)o["build"] ?? "unknown",
                TotalConnects = (long?)o["totalConnects"] ?? 0,
                TotalDisconnects = (long?)o["totalDisconnects"] ?? 0,
                CurrentOnline = (int?)o["currentOnline"] ?? 0,
                PeakOnline = (int?)o["peakOnline"] ?? 0,
                WireHealthWarning = (bool?)o["wireHealthWarning"] ?? false,
                SecondIslandRegistered = (bool?)o["secondIslandRegistered"] ?? false,
                FirstRegionTerrainCount = Math.Max(0, (int?)o["firstRegionTerrainCount"] ?? 0),
                Players = players,
                RuntimeHostMode = (string?)(runtime?["hostMode"]) ?? "unknown",
                RuntimeHostId = (string?)(runtime?["hostId"]) ?? "unknown",
                RuntimeOwnedEntityCount = (int?)(runtime?["ownedEntityCount"]) ?? 0,
                RuntimeGlobalEntityCount = (int?)(runtime?["globalEntityCount"]) ?? 0,
                RuntimeUnownedEntityCount = (int?)(runtime?["unownedEntityCount"]) ?? 0,
                RuntimeOwnershipIssueCount = (int?)(runtime?["ownershipIssueCount"]) ?? 0,
                RuntimeDomains = runtimeDomains,
                ShipDomains = domains,
            };
        }

        internal static DateTimeOffset FromUnixMs(long ms) =>
            DateTimeOffset.FromUnixTimeMilliseconds(ms);
    }

    internal sealed class GameRuntimeDomainStat
    {
        public JObject Json { get; private init; } = new JObject();

        public static GameRuntimeDomainStat Parse(JObject d) => new GameRuntimeDomainStat
        {
            Json = new JObject
            {
                ["domainId"] = (string?)d["domainId"] ?? "",
                ["kind"] = (string?)d["kind"] ?? "unknown",
                ["label"] = (string?)d["label"] ?? "Unnamed domain",
                ["hostId"] = (string?)d["hostId"] ?? "unknown",
                ["affinityDomainId"] = d["affinityDomainId"]?.Type == JTokenType.String
                    ? (string?)d["affinityDomainId"] : null,
                ["entityCount"] = (int?)d["entityCount"] ?? 0,
                ["active"] = (bool?)d["active"] ?? false,
                ["warningCount"] = (int?)d["warningCount"] ?? 0,
                ["x"] = (double?)d["x"] ?? 0,
                ["y"] = (double?)d["y"] ?? 0,
                ["z"] = (double?)d["z"] ?? 0,
            }
        };
    }

    internal sealed class GameShipDomainStat
    {
        public JObject Json { get; private init; } = new JObject();

        public static GameShipDomainStat Parse(JObject d)
        {
            // Rebuild an allowlisted object: the API never blindly forwards a file
            // object into authenticated HTML/JSON output.
            JArray aboard = new JArray();
            if (d["aboardPlayerEntityIds"] is JArray ids)
                foreach (JToken id in ids) aboard.Add((long?)id ?? 0);
            return new GameShipDomainStat { Json = new JObject
            {
                ["domainId"] = (string?)d["domainId"] ?? "",
                ["hullEntityId"] = (long?)d["hullEntityId"] ?? 0,
                ["authorityGeneration"] = (long?)d["authorityGeneration"] ?? 0,
                ["replicationSequence"] = (long?)d["replicationSequence"] ?? 0,
                ["cadenceMs"] = (int?)d["cadenceMs"] ?? 0,
                ["deliveryAgeMs"] = (long?)d["deliveryAgeMs"] ?? -1,
                ["x"] = (double?)d["x"] ?? 0, ["y"] = (double?)d["y"] ?? 0,
                ["z"] = (double?)d["z"] ?? 0,
                ["active"] = (bool?)d["active"] ?? false,
                ["piloted"] = (bool?)d["piloted"] ?? false,
                ["liveCadenceExpected"] = (bool?)d["liveCadenceExpected"] ?? false,
                ["pilotPlayerEntityId"] = d["pilotPlayerEntityId"]?.Type == JTokenType.Integer
                    ? (long?)d["pilotPlayerEntityId"] : null,
                ["aboardPlayerEntityIds"] = aboard,
                ["deckCount"] = (int?)d["deckCount"] ?? 0,
                ["mountedPartCount"] = (int?)d["mountedPartCount"] ?? 0,
                ["subscriberCount"] = (int?)d["subscriberCount"] ?? 0,
                ["staleDelivery"] = (bool?)d["staleDelivery"] ?? false,
                ["aboardCheckoutWarning"] = (bool?)d["aboardCheckoutWarning"] ?? false,
            }};
        }
    }

    internal sealed class GamePlayerStat
    {
        public long EntityId { get; private init; }
        public string PeerId { get; private init; } = "";
        public DateTimeOffset ConnectedAt { get; private init; }
        public bool HasHealth { get; private init; }
        public uint RttMs { get; private init; }
        public uint RttVarianceMs { get; private init; }
        public uint PacketsLost { get; private init; }
        public uint PacketsSent { get; private init; }
        public uint InFlightBytes { get; private init; }
        public bool Spiral { get; private init; }

        public static GamePlayerStat Parse(JObject p)
        {
            JObject? h = p["health"] as JObject;

            return new GamePlayerStat
            {
                EntityId = (long?)p["entityId"] ?? 0,
                PeerId = (string?)p["peerId"] ?? "",
                ConnectedAt = GameStatsSnapshot.FromUnixMs((long?)p["connectedAtUnixMs"] ?? 0),
                HasHealth = h != null,
                RttMs = (uint?)(h?["rttMs"]) ?? 0,
                RttVarianceMs = (uint?)(h?["rttVarianceMs"]) ?? 0,
                PacketsLost = (uint?)(h?["packetsLost"]) ?? 0,
                PacketsSent = (uint?)(h?["packetsSent"]) ?? 0,
                InFlightBytes = (uint?)(h?["inFlightBytes"]) ?? 0,
                Spiral = (bool?)(h?["spiral"]) ?? false,
            };
        }
    }
}

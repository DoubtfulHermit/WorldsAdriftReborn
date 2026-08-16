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

        /// <summary>
        /// The optional-terrain lifecycle section (schema v5+). Never null: an
        /// older game server that never writes it parses to an ABSENT projection,
        /// which the dashboard renders as "this server predates terrain
        /// telemetry" rather than as "terrain is off".
        /// </summary>
        public GameTerrainStat Terrain { get; private init; } = GameTerrainStat.Absent();

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
                Terrain = GameTerrainStat.Parse(o["terrain"] as JObject),
            };
        }

        internal static DateTimeOffset FromUnixMs(long ms) =>
            DateTimeOffset.FromUnixTimeMilliseconds(ms);
    }

    /// <summary>
    /// The login server's view of the game server's terrain lifecycle section.
    ///
    /// Like <see cref="GameShipDomainStat"/> this REBUILDS an allowlisted object
    /// rather than forwarding whatever the file contained: the dashboard is
    /// authenticated output, and a field the writer never promised must not reach
    /// it just because something wrote it. Every value defaults, so a v4 file
    /// (no terrain section at all) and a v5 file with a truncated section both
    /// render as a defined state instead of throwing.
    /// </summary>
    internal sealed class GameTerrainStat
    {
        /// <summary>
        /// The three modes the game server can report. Anything else - including
        /// a section written by a future schema - reads as "unknown" rather than
        /// being echoed into the console verbatim.
        /// </summary>
        private static readonly string[] KnownModes = { "on", "off", "prerequisite-disabled" };

        /// <summary>The per-cell states the console knows how to label.</summary>
        private static readonly string[] KnownStates =
        {
            "absent", "requesting", "waiting-ack", "ready",
            "draining", "unloading", "retained-legacy", "error",
        };

        private static readonly string[] KnownActions = { "none", "load", "remove", "resource-drain" };

        private static readonly string[] KnownEventKinds =
        {
            "request", "asset-ack", "asset-retry", "asset-fallback",
            "add-ok", "add-failed", "drain-wait", "remove-ok", "remove-failed",
            "teleport-wait", "teleport-ready", "teleport-refused",
        };

        public bool Present { get; private init; }
        public string Mode { get; private init; } = "unknown";
        public JObject Json { get; private init; } = new JObject();

        /// <summary>The projection for a game server whose schema has no terrain section.</summary>
        public static GameTerrainStat Absent() => new GameTerrainStat
        {
            Present = false,
            Mode = "unknown",
            Json = Build(null),
        };

        public static GameTerrainStat Parse(JObject? t)
        {
            if (t == null) return Absent();
            return new GameTerrainStat
            {
                Present = true,
                Mode = Allowed((string?)t["mode"], KnownModes),
                Json = Build(t),
            };
        }

        private static JObject Build(JObject? t)
        {
            JObject stateCounts = new JObject();
            JObject? counts = t?["stateCounts"] as JObject;
            foreach (string state in KnownStates)
                stateCounts[state] = Math.Max(0, (int?)counts?[state] ?? 0);

            JArray players = new JArray();
            if (t?["players"] is JArray playerArray)
                foreach (JToken token in playerArray)
                    if (token is JObject p) players.Add(BuildPlayer(p));

            JArray islands = new JArray();
            if (t?["islands"] is JArray islandArray)
                foreach (JToken token in islandArray)
                    if (token is JObject i) islands.Add(BuildIsland(i));

            JArray events = new JArray();
            if (t?["events"] is JArray eventArray)
                foreach (JToken token in eventArray)
                    if (token is JObject e) events.Add(BuildEvent(e));

            return new JObject
            {
                ["present"] = t != null,
                ["requested"] = (bool?)t?["requested"] ?? false,
                ["enabled"] = (bool?)t?["enabled"] ?? false,
                ["mode"] = Allowed((string?)t?["mode"], KnownModes),
                ["hostId"] = (string?)t?["hostId"] ?? "unknown",
                ["authority"] = (string?)t?["authority"] ?? "unknown",
                ["loadRadiusMetres"] = (double?)t?["loadRadiusMetres"] ?? 0,
                ["unloadRadiusMetres"] = (double?)t?["unloadRadiusMetres"] ?? 0,
                ["assetAckTimeoutMs"] = (long?)t?["assetAckTimeoutMs"] ?? 0,
                ["settleDelayMs"] = (long?)t?["settleDelayMs"] ?? 0,
                ["candidateCount"] = Math.Max(0, (int?)t?["candidateCount"] ?? 0),
                ["trackedPeerCount"] = Math.Max(0, (int?)t?["trackedPeerCount"] ?? 0),
                ["readyCount"] = Math.Max(0, (int?)t?["readyCount"] ?? 0),
                ["warningCount"] = Math.Max(0, (int?)t?["warningCount"] ?? 0),
                ["errorCount"] = Math.Max(0, (int?)t?["errorCount"] ?? 0),
                ["eventCapacity"] = Math.Max(0, (int?)t?["eventCapacity"] ?? 0),
                ["stateCounts"] = stateCounts,
                ["players"] = players,
                ["islands"] = islands,
                ["events"] = events,
            };
        }

        private static JObject BuildPlayer(JObject p)
        {
            JArray islands = new JArray();
            if (p["islands"] is JArray cells)
            {
                foreach (JToken token in cells)
                {
                    if (token is not JObject cell) continue;
                    islands.Add(new JObject
                    {
                        ["islandId"] = (string?)cell["islandId"] ?? "",
                        ["state"] = Allowed((string?)cell["state"], KnownStates),
                    });
                }
            }

            JObject? asset = p["asset"] as JObject;
            return new JObject
            {
                ["playerEntityId"] = (long?)p["playerEntityId"] ?? 0,
                ["slot"] = (int?)p["slot"] ?? 0,
                ["x"] = (double?)p["x"] ?? 0,
                ["y"] = (double?)p["y"] ?? 0,
                ["z"] = (double?)p["z"] ?? 0,
                ["confirmedGroundIslandId"] = p["confirmedGroundIslandId"]?.Type == JTokenType.String
                    ? (string?)p["confirmedGroundIslandId"] : null,
                ["requestedDestinationIslandId"] = p["requestedDestinationIslandId"]?.Type == JTokenType.String
                    ? (string?)p["requestedDestinationIslandId"] : null,
                ["pendingAction"] = Allowed((string?)p["pendingAction"], KnownActions),
                ["pendingIslandId"] = p["pendingIslandId"]?.Type == JTokenType.String
                    ? (string?)p["pendingIslandId"] : null,
                ["correlatedAckObserved"] = (bool?)p["correlatedAckObserved"] ?? false,
                ["removeSupported"] = (bool?)p["removeSupported"] ?? false,
                ["mayRemove"] = (bool?)p["mayRemove"] ?? false,
                ["legacyRetaining"] = (bool?)p["legacyRetaining"] ?? false,
                ["connectPlanComplete"] = (bool?)p["connectPlanComplete"] ?? false,
                ["settleWaiting"] = (bool?)p["settleWaiting"] ?? false,
                ["destinationWaiting"] = (bool?)p["destinationWaiting"] ?? false,
                ["readyCount"] = Math.Max(0, (int?)p["readyCount"] ?? 0),
                ["warning"] = (string?)p["warning"] ?? "",
                ["asset"] = asset == null ? null : new JObject
                {
                    ["islandId"] = (string?)asset["islandId"] ?? "",
                    ["assetName"] = (string?)asset["assetName"] ?? "",
                    ["requestAgeMs"] = (long?)asset["requestAgeMs"] ?? 0,
                    ["lastRetryAgeMs"] = (long?)asset["lastRetryAgeMs"] ?? 0,
                    ["retryCount"] = Math.Max(0, (int?)asset["retryCount"] ?? 0),
                    ["acknowledged"] = (bool?)asset["acknowledged"] ?? false,
                    ["fallbackDue"] = (bool?)asset["fallbackDue"] ?? false,
                },
                ["islands"] = islands,
            };
        }

        private static JObject BuildIsland(JObject i)
        {
            JObject? envelope = i["envelope"] as JObject;
            return new JObject
            {
                ["islandId"] = (string?)i["islandId"] ?? "",
                ["displayName"] = (string?)i["displayName"] ?? "Unnamed island",
                ["terrainEntityId"] = (long?)i["terrainEntityId"] ?? 0,
                ["registered"] = (bool?)i["registered"] ?? false,
                ["locallyOwned"] = (bool?)i["locallyOwned"] ?? false,
                ["hasEnvelope"] = (bool?)i["hasEnvelope"] ?? false,
                ["managed"] = (bool?)i["managed"] ?? false,
                ["unconditional"] = (bool?)i["unconditional"] ?? false,
                ["readyPeerCount"] = Math.Max(0, (int?)i["readyPeerCount"] ?? 0),
                ["loadingPeerCount"] = Math.Max(0, (int?)i["loadingPeerCount"] ?? 0),
                ["drainingPeerCount"] = Math.Max(0, (int?)i["drainingPeerCount"] ?? 0),
                ["unloadingPeerCount"] = Math.Max(0, (int?)i["unloadingPeerCount"] ?? 0),
                ["retainedLegacyPeerCount"] = Math.Max(0, (int?)i["retainedLegacyPeerCount"] ?? 0),
                ["errorPeerCount"] = Math.Max(0, (int?)i["errorPeerCount"] ?? 0),
                // -1 is the writer's "unknown", and stays -1: a resource count the
                // game server could not supply must not read as "drained".
                ["resourceNodeCount"] = (int?)i["resourceNodeCount"] ?? -1,
                ["checkedOutResourceCount"] = (int?)i["checkedOutResourceCount"] ?? -1,
                ["resourceDrainWired"] = (bool?)i["resourceDrainWired"] ?? false,
                ["envelope"] = envelope == null ? null : new JObject
                {
                    ["spanX"] = (double?)envelope["spanX"] ?? 0,
                    ["spanY"] = (double?)envelope["spanY"] ?? 0,
                    ["spanZ"] = (double?)envelope["spanZ"] ?? 0,
                },
            };
        }

        private static JObject BuildEvent(JObject e) => new JObject
        {
            ["ageMs"] = Math.Max(0, (long?)e["ageMs"] ?? 0),
            ["kind"] = Allowed((string?)e["kind"], KnownEventKinds),
            ["islandId"] = (string?)e["islandId"] ?? "",
            ["playerEntityId"] = (long?)e["playerEntityId"] ?? 0,
            ["slot"] = (int?)e["slot"] ?? 0,
            ["success"] = (bool?)e["success"] ?? false,
        };

        private static string Allowed(string? value, string[] known) =>
            value != null && Array.IndexOf(known, value) >= 0 ? value : "unknown";
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

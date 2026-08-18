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

        /// <summary>
        /// The island-fauna section (schema v7+). Never null: an older game server
        /// that never writes it parses to an ABSENT projection, which the console
        /// renders as "this server predates fauna telemetry" - and, crucially,
        /// draws no wildlife rather than drawing it at a guessed clock.
        /// </summary>
        public GameFaunaStat Fauna { get; private init; } = GameFaunaStat.Absent();

        /// <summary>
        /// The ship-motion model (schema v8+). Never null: an older game server
        /// projects to an ABSENT model, which the console renders as "this server
        /// predates ship geometry" and, crucially, draws no reckoned position from
        /// - rather than as a fleet standing still.
        /// </summary>
        public GameShipModelStat ShipModel { get; private init; } = GameShipModelStat.Absent();

        /// <summary>
        /// The interest section (schema v10+). Never null: an older game server -
        /// including the v8 and v9 files still in the field - projects to an
        /// ABSENT section, which the streaming view renders as "not reported"
        /// rather than as radii of zero.
        /// </summary>
        public GameInterestStat Interest { get; private init; } = GameInterestStat.Absent();

        /// <summary>
        /// The sky whale section (schema v11+, reshaped in v12). Never null: an older game server
        /// projects to an ABSENT section, which the maps render as "this server
        /// predates the sky whale" and, crucially, draw NO animal from - rather
        /// than one flying at a guessed clock.
        /// </summary>
        public GameSkyWhaleStat SkyWhale { get; private init; } = GameSkyWhaleStat.Absent();

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
                Fauna = GameFaunaStat.Parse(o["fauna"] as JObject),
                ShipModel = GameShipModelStat.Parse(o["shipModel"] as JObject),
                Interest = GameInterestStat.Parse(o["interest"] as JObject),
                SkyWhale = GameSkyWhaleStat.Parse(o["skyWhale"] as JObject),
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

    /// <summary>
    /// The login server's view of the game server's island-fauna section.
    ///
    /// Like <see cref="GameTerrainStat"/> this REBUILDS an allowlisted object
    /// rather than forwarding what the file contained, and it CLAMPS every number
    /// it passes on. That matters more here than elsewhere: the console feeds these
    /// values into an animation loop, and a negative lap count or a nonsense
    /// creature count would come out as a browser hang rather than as a wrong
    /// number on a page.
    ///
    /// A v6 file (no fauna section at all) and a v7 file with a truncated one both
    /// parse to a defined, absent state instead of throwing.
    /// </summary>
    internal sealed class GameFaunaStat
    {
        /// <summary>
        /// The most islands whose roster is passed through. The release catalogue
        /// is 254 islands and the tier-1 world is 46; a file naming thousands is
        /// not a world this console has to draw, and the cap keeps a malformed
        /// snapshot from becoming an unbounded DOM.
        /// </summary>
        private const int MaxIslands = 512;

        /// <summary>Per-island creature counts are clamped to this. See the type remarks.</summary>
        private const int MaxCreaturesPerIsland = 4096;

        public bool Present { get; private init; }
        public bool Enabled { get; private init; }
        public int LiveCount { get; private init; }
        public JObject Json { get; private init; } = new JObject();

        /// <summary>The projection for a game server whose schema has no fauna section.</summary>
        public static GameFaunaStat Absent() => new GameFaunaStat
        {
            Present = false,
            Enabled = false,
            LiveCount = 0,
            Json = Build(null),
        };

        public static GameFaunaStat Parse(JObject? f)
        {
            if (f == null) return Absent();
            return new GameFaunaStat
            {
                Present = true,
                Enabled = (bool?)f["enabled"] ?? false,
                LiveCount = Clamp((int?)f["liveCount"] ?? 0, MaxIslands * MaxCreaturesPerIsland),
                Json = Build(f),
            };
        }

        private static JObject Build(JObject? f)
        {
            JArray islands = new JArray();
            if (f?["islands"] is JArray islandArray)
            {
                foreach (JToken token in islandArray)
                {
                    if (islands.Count >= MaxIslands) break;
                    if (token is not JObject island) continue;
                    string id = (string?)island["islandId"] ?? "";
                    if (id.Length == 0) continue;
                    islands.Add(new JObject
                    {
                        ["islandId"] = id,
                        ["mantaRays"] = Clamp((int?)island["mantaRays"] ?? 0, MaxCreaturesPerIsland),
                        ["jellyFish"] = Clamp((int?)island["jellyFish"] ?? 0, MaxCreaturesPerIsland),
                    });
                }
            }

            // clockSeconds is the one field a reader MUST NOT invent: without it
            // the console cannot place a creature, and placing one at zero would
            // draw every animal on this server at the pose it held the instant the
            // process booted. Absent stays 0 and `present:false` is what gates the
            // drawing, never the number itself.
            double clock = (double?)f?["clockSeconds"] ?? 0;
            if (double.IsNaN(clock) || double.IsInfinity(clock) || clock < 0) clock = 0;

            return new JObject
            {
                ["present"] = f != null,
                ["enabled"] = (bool?)f?["enabled"] ?? false,
                ["clockSeconds"] = clock,
                ["liveCount"] = Clamp((int?)f?["liveCount"] ?? 0, MaxIslands * MaxCreaturesPerIsland),
                ["budget"] = Clamp((int?)f?["budget"] ?? 0, int.MaxValue),
                ["demand"] = Clamp((int?)f?["demand"] ?? 0, int.MaxValue),
                ["perPeerBudget"] = Clamp((int?)f?["perPeerBudget"] ?? 0, int.MaxValue),
                ["poseIntervalMs"] = Clamp((int?)f?["poseIntervalMs"] ?? 0, 3_600_000),
                ["islands"] = islands,
                ["ecology"] = BuildEcology(f?["ecology"] as JObject),
            };
        }

        /// <summary>
        /// The v9 ecology block, rebuilt allowlisted and clamped like everything
        /// else here. A v7/v8 file has no such object and parses to
        /// enabled:false with empty islands - the same defined-absent shape the
        /// fauna section itself takes on a v6 file. Every number feeds the map's
        /// animation loop, so NaN and infinity are floored rather than passed:
        /// an omega of NaN is a creature that silently stops being drawn.
        /// </summary>
        private static JObject BuildEcology(JObject? e)
        {
            const int MaxGroupsPerIsland = 16;
            const int MaxBloomsPerIsland = 8;

            JArray islands = new JArray();
            if (e?["islands"] is JArray rows)
            {
                foreach (JToken token in rows)
                {
                    if (islands.Count >= MaxIslands) break;
                    if (token is not JObject island) continue;
                    string id = (string?)island["islandId"] ?? "";
                    if (id.Length == 0) continue;

                    JArray groups = new JArray();
                    if (island["groups"] is JArray groupRows)
                    {
                        foreach (JToken groupToken in groupRows)
                        {
                            if (groups.Count >= MaxGroupsPerIsland) break;
                            if (groupToken is not JObject group) continue;
                            // The family pairing (Phase 5). Both fields are
                            // member indices inside one group, so the group's own
                            // ceiling bounds them; a malformed row is dropped
                            // rather than passed, because a mother index the
                            // mirror cannot resolve draws a calf at the origin.
                            JArray calves = new JArray();
                            if (group["calves"] is JArray calfRows)
                            {
                                foreach (JToken calfToken in calfRows)
                                {
                                    if (calves.Count >= MaxCreaturesPerIsland) break;
                                    if (calfToken is not JObject calf) continue;
                                    int member = (int?)calf["member"] ?? -1;
                                    int mother = (int?)calf["mother"] ?? -1;
                                    if (member < 0 || mother < 0
                                        || member > MaxCreaturesPerIsland
                                        || mother > MaxCreaturesPerIsland) continue;
                                    calves.Add(new JObject
                                    {
                                        ["member"] = member,
                                        ["mother"] = mother,
                                    });
                                }
                            }
                            groups.Add(new JObject
                            {
                                ["species"] = Species((string?)group["species"]),
                                ["index"] = Clamp((int?)group["index"] ?? 0, MaxGroupsPerIsland),
                                ["bloom"] = Clamp((int?)group["bloom"] ?? 0, MaxBloomsPerIsland),
                                ["members"] = Clamp((int?)group["members"] ?? 0, MaxCreaturesPerIsland),
                                ["behaviour"] = Label((string?)group["behaviour"]),
                                ["epochSeconds"] = Finite((double?)group["epochSeconds"] ?? 0),
                                ["durationSeconds"] = Finite((double?)group["durationSeconds"] ?? 0),
                                ["toBloom"] = Clamp((int?)group["toBloom"] ?? 0, MaxBloomsPerIsland),
                                ["calves"] = calves,
                            });
                        }
                    }

                    JArray blooms = new JArray();
                    if (island["blooms"] is JArray bloomRows)
                    {
                        foreach (JToken bloomToken in bloomRows)
                        {
                            if (blooms.Count >= MaxBloomsPerIsland) break;
                            if (bloomToken is not JObject bloom) continue;
                            JObject rebuilt = new JObject
                            {
                                ["species"] = Species((string?)bloom["species"]),
                                ["index"] = Clamp((int?)bloom["index"] ?? 0, MaxBloomsPerIsland),
                            };
                            foreach (string field in new[]
                            {
                                "amplitude", "sigma", "annulusRadius", "radialDrift",
                                "angularDrift", "omegaRadial", "omegaAngular",
                                "omegaMigration", "phaseRadial", "phaseAngular", "baseAngle",
                            })
                            {
                                rebuilt[field] = Finite((double?)bloom[field] ?? 0);
                            }
                            blooms.Add(rebuilt);
                        }
                    }

                    double quiet = Finite((double?)island["quietFactor"] ?? 0);
                    islands.Add(new JObject
                    {
                        ["islandId"] = id,
                        ["quietFactor"] = quiet < 0 ? 0 : quiet > 1 ? 1 : quiet,
                        ["mantaCapacity"] = Clamp((int?)island["mantaCapacity"] ?? 0, MaxCreaturesPerIsland),
                        ["jellyCapacity"] = Clamp((int?)island["jellyCapacity"] ?? 0, MaxCreaturesPerIsland),
                        ["mantaExpressed"] = Clamp((int?)island["mantaExpressed"] ?? 0, MaxCreaturesPerIsland),
                        ["jellyExpressed"] = Clamp((int?)island["jellyExpressed"] ?? 0, MaxCreaturesPerIsland),
                        // The population rhythm's state (Phase 3). Unknown or
                        // malformed labels default to Bloom - the reading that
                        // matches a pre-rhythm world where everything was
                        // always fully expressed.
                        ["mantaPhase"] = Label((string?)island["mantaPhase"], "Bloom"),
                        ["mantaPhaseFraction"] = Fraction01(
                            Finite((double?)island["mantaPhaseFraction"] ?? 0)),
                        ["jellyPhase"] = Label((string?)island["jellyPhase"], "Bloom"),
                        ["jellyPhaseFraction"] = Fraction01(
                            Finite((double?)island["jellyPhaseFraction"] ?? 0)),
                        ["groups"] = groups,
                        ["blooms"] = blooms,
                    });
                }
            }

            return new JObject
            {
                ["enabled"] = (bool?)e?["enabled"] ?? false,
                // Display-only: the browser derives nothing from the seed (the
                // blooms arrive as published numbers), so any integer is honest.
                ["worldSeed"] = (int?)e?["worldSeed"] ?? 0,
                ["islands"] = islands,
            };
        }

        /// <summary>Only the two species labels the renderer knows may pass.</summary>
        private static string Species(string? value) =>
            value == "jelly" ? "jelly" : "manta";

        /// <summary>
        /// A short alphanumeric label, or the given default. Behaviour and phase
        /// vocabularies will grow; the map treats an unknown label as its
        /// default, so passing a well-formed new one through is forward-safe
        /// while a malformed one cannot reach the DOM.
        /// </summary>
        private static string Label(string? value, string fallback = "Cruise") =>
            value != null && value.Length is > 0 and <= 24
                && value.All(char.IsLetterOrDigit) ? value : fallback;

        private static double Fraction01(double value) =>
            value < 0 ? 0 : value > 1 ? 1 : value;

        private static double Finite(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;

        private static int Clamp(int value, int maximum) =>
            value < 0 ? 0 : value > maximum ? maximum : value;
    }

    /// <summary>
    /// The login server's view of the game server's SKY WHALE section (schema
    /// v11+).
    ///
    /// Like every other projection in this file it REBUILDS an allowlisted object
    /// rather than forwarding what it was handed, and clamps what it passes on:
    /// this JSON reaches a browser that draws with it, so a corrupt or hostile
    /// value would come out as a mark somewhere absurd or as a NaN that silently
    /// stops the whole animation loop.
    ///
    /// THE DISTINCTION THAT MATTERS is the same one <see cref="GameFaunaStat"/>
    /// makes: <c>present</c> false means "this game server predates the feature",
    /// <c>enabled</c> false means "it has the feature and it is switched off".
    /// A map that draws nothing must still be able to say which it is looking at,
    /// and neither is allowed to masquerade as the other.
    ///
    /// <c>clockSeconds</c> is the one field a reader MUST NOT invent. Without it
    /// the maps cannot place the animal, and placing it at zero would draw every
    /// whale at the pose it held the instant the process booted. Absent stays 0 and
    /// <c>present</c> is what gates the drawing, never the number itself.
    /// </summary>
    internal sealed class GameSkyWhaleStat
    {
        /// <summary>
        /// The most whale rows that are passed through. The world carries ONE
        /// migrating whale, so this is a corruption guard rather than a budget: a
        /// file naming hundreds is not a world this console has to draw, and the cap
        /// keeps a malformed snapshot from becoming an unbounded DOM. It is not
        /// tightened to one, because "the server says four" should be visible as a
        /// wrong number rather than silently truncated to the number this console
        /// expected.
        /// </summary>
        private const int MaxWhales = 64;

        /// <summary>
        /// The largest world coordinate a call station may claim, in metres. The
        /// release world edge is about 40 km; anything past this is a corrupt file,
        /// and a mark at 1e300 would stretch the map's view to nothing.
        /// </summary>
        private const double MaxWorldMetres = 1_000_000.0;

        public bool Present { get; private init; }
        public bool Enabled { get; private init; }
        public int WhaleCount { get; private init; }
        public JObject Json { get; private init; } = new JObject();

        /// <summary>The projection for a game server whose schema has no whale section.</summary>
        public static GameSkyWhaleStat Absent() => new GameSkyWhaleStat
        {
            Present = false,
            Enabled = false,
            WhaleCount = 0,
            Json = Build(null),
        };

        public static GameSkyWhaleStat Parse(JObject? w)
        {
            if (w == null) return Absent();
            return new GameSkyWhaleStat
            {
                Present = true,
                Enabled = (bool?)w["enabled"] ?? false,
                WhaleCount = Clamp((int?)w["whaleCount"] ?? 0, MaxWhales),
                Json = Build(w),
            };
        }

        private static JObject Build(JObject? w)
        {
            JArray whales = new JArray();
            if (w?["whales"] is JArray rows)
            {
                foreach (JToken token in rows)
                {
                    if (whales.Count >= MaxWhales) break;
                    if (token is not JObject whale) continue;
                    string id = (string?)whale["routeId"] ?? "";
                    if (id.Length == 0) continue;
                    whales.Add(new JObject
                    {
                        ["routeId"] = id,
                        ["entityId"] = (long?)whale["entityId"] ?? 0,
                        ["callEntityId"] = (long?)whale["callEntityId"] ?? 0,
                        ["callIndex"] = (long?)whale["callIndex"] ?? 0,
                        ["callX"] = Metres((double?)whale["callX"] ?? 0),
                        ["callY"] = Metres((double?)whale["callY"] ?? 0),
                        ["callZ"] = Metres((double?)whale["callZ"] ?? 0),
                        // EMPTY regionId is a REAL answer - the animal is between
                        // zones - so it is passed through rather than defaulted to
                        // the next zone, which would put a marker in the wrong cell.
                        ["regionId"] = Name((string?)whale["regionId"]),
                        ["nextRegionId"] = Name((string?)whale["nextRegionId"]),
                        ["nextRegionIslandId"] = Name((string?)whale["nextRegionIslandId"]),
                        ["nextRegionSeconds"] = Seconds((double?)whale["nextRegionSeconds"] ?? 0),
                        ["nextIslandId"] = Name((string?)whale["nextIslandId"]),
                        ["nextIslandSeconds"] = Seconds((double?)whale["nextIslandSeconds"] ?? 0),
                    });
                }
            }

            double clock = (double?)w?["clockSeconds"] ?? 0;
            if (double.IsNaN(clock) || double.IsInfinity(clock) || clock < 0) clock = 0;

            return new JObject
            {
                ["present"] = w != null,
                ["enabled"] = (bool?)w?["enabled"] ?? false,
                ["clockSeconds"] = clock,
                ["whaleCount"] = Clamp((int?)w?["whaleCount"] ?? 0, MaxWhales),
                ["loadRadiusMetres"] = Metres((double?)w?["loadRadiusMetres"] ?? 0),
                ["callRadiusMetres"] = Metres((double?)w?["callRadiusMetres"] ?? 0),
                ["poseIntervalMs"] = Clamp((int?)w?["poseIntervalMs"] ?? 0, 3_600_000),
                ["callIntervalSeconds"] = Metres((double?)w?["callIntervalSeconds"] ?? 0),
                ["whales"] = whales,
            };
        }

        /// <summary>
        /// A zone or island name from a snapshot, bounded. Names reach the DOM, so a
        /// megabyte of them from a corrupt file would be a megabyte of DOM; and an
        /// absent name is EMPTY rather than a placeholder, because empty already
        /// means something here ("between zones") and inventing a word for it would
        /// make a real state indistinguishable from a parse failure.
        /// </summary>
        private static string Name(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= 96 ? value : value.Substring(0, 96);
        }

        /// <summary>A countdown that is finite and not negative. A NaN would render as a NaN.</summary>
        private static double Seconds(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) return 0;
            return value > 86_400.0 ? 86_400.0 : value;
        }

        /// <summary>A world coordinate that is finite and inside the world. See the type remarks.</summary>
        private static double Metres(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            return value < -MaxWorldMetres ? -MaxWorldMetres
                : value > MaxWorldMetres ? MaxWorldMetres : value;
        }

        private static int Clamp(int value, int maximum) =>
            value < 0 ? 0 : value > maximum ? maximum : value;
    }

    /// <summary>
    /// The login server's view of the game server's interest section (schema v10+).
    ///
    /// Like every other projection here it REBUILDS an allowlisted object and
    /// clamps what it passes on. The radii it forwards are drawn as circles in
    /// world metres on the operator map, so a corrupt or hostile value would come
    /// out as a ring the size of the world stretching the view - the magnitude
    /// clamp is load-bearing, not belt-and-braces. A v8 or v9 file (no interest
    /// section) parses to a defined absent state instead of throwing.
    /// </summary>
    internal sealed class GameInterestStat
    {
        /// <summary>
        /// Metres. The widest configurable interest radius is bounded by the game
        /// side's own InterestPolicy ceiling; anything past the world edge in a
        /// file is not a radius, it is corruption.
        /// </summary>
        private const double MaxRadiusMetres = 100_000.0;

        /// <summary>The most peers whose holdings are passed through.</summary>
        private const int MaxPeers = 256;

        /// <summary>Per-peer list caps, so a malformed file cannot become an unbounded DOM.</summary>
        private const int MaxIslandsPerPeer = 512;
        private const int MaxShipDomainsPerPeer = 256;

        public bool Present { get; private init; }
        public JObject Json { get; private init; } = new JObject();

        /// <summary>The projection for a game server whose schema has no interest section.</summary>
        public static GameInterestStat Absent() => new GameInterestStat
        {
            Present = false,
            Json = Build(null),
        };

        public static GameInterestStat Parse(JObject? i)
        {
            if (i == null) return Absent();
            return new GameInterestStat
            {
                Present = (bool?)i["present"] ?? false,
                Json = Build(i),
            };
        }

        private static JObject Build(JObject? i)
        {
            JObject? resources = i?["resources"] as JObject;
            JObject? fauna = i?["fauna"] as JObject;
            JObject? ship = i?["ship"] as JObject;
            JObject? gates = i?["gates"] as JObject;

            JArray peers = new JArray();
            if (i?["peers"] is JArray peerArray)
            {
                foreach (JToken token in peerArray)
                {
                    if (peers.Count >= MaxPeers) break;
                    if (token is not JObject p) continue;
                    peers.Add(BuildPeer(p));
                }
            }

            return new JObject
            {
                ["present"] = i != null && ((bool?)i["present"] ?? false),
                ["resources"] = new JObject
                {
                    ["enabled"] = (bool?)resources?["enabled"] ?? false,
                    ["loadRadiusMetres"] = Radius((double?)resources?["loadRadiusMetres"] ?? 0),
                    ["unloadRadiusMetres"] = Radius((double?)resources?["unloadRadiusMetres"] ?? 0),
                    ["perPeerBudget"] = Count((int?)resources?["perPeerBudget"] ?? 0),
                    ["connectRadiusMetres"] = Radius((double?)resources?["connectRadiusMetres"] ?? 0),
                },
                ["fauna"] = new JObject
                {
                    ["enabled"] = (bool?)fauna?["enabled"] ?? false,
                    ["loadRadiusMetres"] = Radius((double?)fauna?["loadRadiusMetres"] ?? 0),
                    ["unloadRadiusMetres"] = Radius((double?)fauna?["unloadRadiusMetres"] ?? 0),
                },
                ["ship"] = new JObject
                {
                    ["loadRadiusMetres"] = Radius((double?)ship?["loadRadiusMetres"] ?? 0),
                    ["unloadRadiusMetres"] = Radius((double?)ship?["unloadRadiusMetres"] ?? 0),
                    ["connectRadiusMetres"] = Radius((double?)ship?["connectRadiusMetres"] ?? 0),
                },
                ["terrainConnectRadiusMetres"] = Radius((double?)i?["terrainConnectRadiusMetres"] ?? 0),
                ["gates"] = new JObject
                {
                    // Present as an explicit null when the section is absent:
                    // "the barrier is off" and "this server never said" are
                    // different operator answers, and the second must not
                    // masquerade as the first.
                    ["loadBarrier"] = gates?["loadBarrier"]?.Type == JTokenType.Boolean
                        ? (bool?)gates["loadBarrier"] : null,
                    ["spawnPaceMs"] = Math.Min(3_600_000, Count((int?)gates?["spawnPaceMs"] ?? 0)),
                },
                ["peers"] = peers,
            };
        }

        private static JObject BuildPeer(JObject p)
        {
            JArray islands = new JArray();
            if (p["resourceIslands"] is JArray islandArray)
            {
                foreach (JToken token in islandArray)
                {
                    if (islands.Count >= MaxIslandsPerPeer) break;
                    if (token is not JObject island) continue;
                    string id = Text((string?)island["islandId"]);
                    if (id.Length == 0) continue;
                    islands.Add(new JObject
                    {
                        ["islandId"] = id,
                        ["checkedOut"] = Count((int?)island["checkedOut"] ?? 0),
                    });
                }
            }

            JArray shipDomains = new JArray();
            if (p["shipDomainIds"] is JArray shipArray)
            {
                foreach (JToken token in shipArray)
                {
                    if (shipDomains.Count >= MaxShipDomainsPerPeer) break;
                    string id = Text((string?)token);
                    if (id.Length > 0) shipDomains.Add(id);
                }
            }

            return new JObject
            {
                ["playerEntityId"] = (long?)p["playerEntityId"] ?? 0,
                ["resourceCheckedOut"] = Count((int?)p["resourceCheckedOut"] ?? 0),
                ["faunaCheckedOut"] = Count((int?)p["faunaCheckedOut"] ?? 0),
                ["resourceIslands"] = islands,
                ["shipDomainIds"] = shipDomains,
            };
        }

        private static double Radius(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) return 0;
            return value > MaxRadiusMetres ? MaxRadiusMetres : value;
        }

        private static int Count(int value) => value < 0 ? 0 : value;

        private static string Text(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value!.Length <= 96 ? value : value.Substring(0, 96);
        }
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

    /// <summary>
    /// The login server's view of the game server's ship-motion model (schema v8+).
    ///
    /// Like every other projection here it REBUILDS an allowlisted object and
    /// clamps what it passes on, and here that is load-bearing rather than
    /// belt-and-braces: the console feeds these numbers into a per-frame
    /// extrapolation, so a negative acceleration or a nonsense window would come
    /// out as hulls flying off the map rather than as a wrong figure in a table.
    /// </summary>
    internal sealed class GameShipModelStat
    {
        /// <summary>
        /// A ceiling on the published flight numbers. The game server's own
        /// tuning clamps max speed at 60 m/s; anything past that in a file is not
        /// a tuning, it is a corrupt or hostile snapshot.
        /// </summary>
        private const double MaxFlightNumber = 1000.0;

        public bool Present { get; private init; }
        public JObject Json { get; private init; } = new JObject();

        /// <summary>The projection for a game server whose schema has no ship model.</summary>
        public static GameShipModelStat Absent() => new GameShipModelStat
        {
            Present = false,
            Json = Build(null),
        };

        public static GameShipModelStat Parse(JObject? s)
        {
            if (s == null) return Absent();
            return new GameShipModelStat { Present = true, Json = Build(s) };
        }

        private static JObject Build(JObject? s) => new JObject
        {
            ["present"] = s != null && ((bool?)s["present"] ?? false),
            ["accelMps2"] = Finite((double?)s?["accelMps2"] ?? 0),
            ["maxSpeedMps"] = Finite((double?)s?["maxSpeedMps"] ?? 0),
            // The window is what actually bounds the console's arithmetic, so a
            // zero or absent one must mean "reckon nothing", never "reckon
            // forever". Zero is the safe reading of a missing value here: it
            // draws the measured pose, which is always defensible.
            ["windowSeconds"] = Finite((double?)s?["windowSeconds"] ?? 0),
            // The hard ceiling, forwarded rather than restated here. The console
            // caps the window with it, so a snapshot claiming a ten-minute window
            // still only ever gets the game server's own maximum.
            ["maxWindowSeconds"] = Finite((double?)s?["maxWindowSeconds"] ?? 0),
            ["toleratedErrorMetres"] = Finite((double?)s?["toleratedErrorMetres"] ?? 0),
        };

        private static double Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) return 0;
            return value > MaxFlightNumber ? MaxFlightNumber : value;
        }
    }

    internal sealed class GameShipDomainStat
    {
        /// <summary>
        /// The most outline points a hull may contribute. A ShipPlan's sections
        /// are two points each and the editor cannot build anything near this, so
        /// the cap only ever fires on a malformed file - where its job is to keep
        /// one bad snapshot from becoming an unbounded SVG path.
        /// </summary>
        private const int MaxOutlinePoints = 512;

        /// <summary>
        /// Metres. A hull is tens of metres; the world is thousands. A coordinate
        /// past this is not a hull, and letting one through would stretch the map's
        /// own view box around a ship that does not exist.
        /// </summary>
        private const double MaxHullMetres = 2000.0;

        /// <summary>
        /// The most profile points a hull may contribute, and the most parts. Same
        /// job as <see cref="MaxOutlinePoints"/>: the cap only ever fires on a
        /// malformed file, where it keeps one bad snapshot from becoming an
        /// unbounded SVG path or an unbounded list of marks.
        /// </summary>
        private const int MaxProfilePoints = 512;
        private const int MaxParts = 256;

        public JObject Json { get; private init; } = new JObject();

        /// <summary>
        /// The hull's STATIC geometry - side elevation, decks, mounted parts - kept
        /// OUT of <see cref="Json"/> on purpose.
        ///
        /// This is the half of a hull that does not change from one snapshot to the
        /// next, and the live poll is read every 1.5 s by an operator and every 3 s
        /// by every public viewer. Islands solved the same problem the same way:
        /// their coastlines are served once from their own document rather than
        /// re-sent with every position. So the geometry is parsed here, held here,
        /// and served from its own per-hull endpoint - and a reader that already has
        /// this hull's <see cref="GeometryRevision"/> never asks again.
        /// </summary>
        public JObject Geometry { get; private init; } = new JObject();

        /// <summary>
        /// Which version of <see cref="Geometry"/> this ship is on. Rides the live
        /// poll (it is one integer) so a browser can tell that a part was mounted
        /// and refetch, without the geometry itself riding the poll. Zero means the
        /// game server published none.
        /// </summary>
        public long GeometryRevision { get; private init; }

        public static GameShipDomainStat Parse(JObject d)
        {
            // Rebuild an allowlisted object: the API never blindly forwards a file
            // object into authenticated HTML/JSON output.
            JArray aboard = new JArray();
            if (d["aboardPlayerEntityIds"] is JArray ids)
                foreach (JToken id in ids) aboard.Add((long?)id ?? 0);
            JObject? hull = d["hull"] as JObject;
            long revision = Revision((long?)hull?["geometryRevision"] ?? 0);
            return new GameShipDomainStat
            {
                Geometry = BuildGeometry(hull?["geometry"] as JObject),
                GeometryRevision = revision,
                Json = new JObject
            {
                ["domainId"] = (string?)d["domainId"] ?? "",
                ["hullEntityId"] = (long?)d["hullEntityId"] ?? 0,
                // "" on an older game server that does not publish it. The operator
                // surface reads that as "owner unknown", never as "owned by nobody".
                ["ownerCharacterUid"] = (string?)d["ownerCharacterUid"] ?? "",
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
                ["yawRadians"] = Finite((double?)d["yawRadians"] ?? 0, Math.PI * 4),
                ["yawRateRadPerSec"] = Finite((double?)d["yawRateRadPerSec"] ?? 0, Math.PI * 4),
                ["vxMps"] = Finite((double?)d["vxMps"] ?? 0, MaxHullMetres),
                ["vyMps"] = Finite((double?)d["vyMps"] ?? 0, MaxHullMetres),
                ["vzMps"] = Finite((double?)d["vzMps"] ?? 0, MaxHullMetres),
                ["hull"] = Hull(hull, revision),
            }};
        }

        /// <summary>
        /// The hull's static geometry, rebuilt field by field like everything else
        /// here, and clamped the same way: the profile ring reaches an SVG path and
        /// the part marks reach SVG transforms, so lengths are capped, coordinates
        /// are bounded, an odd-length ring is truncated rather than read past its
        /// end, and a NaN never reaches an attribute (where it silently blanks the
        /// element it was drawn into).
        ///
        /// An ABSENT block is an absent block: <c>present</c> false and empty
        /// arrays, which the card reads as "this game server does not publish an
        /// elevation" - never as "this ship has no decks".
        /// </summary>
        private static JObject BuildGeometry(JObject? g)
        {
            JArray profile = new JArray();
            if (g?["profile"] is JArray ring)
            {
                int usable = Math.Min(ring.Count - (ring.Count % 2), MaxProfilePoints * 2);
                for (int i = 0; i < usable; i++)
                {
                    profile.Add(Finite((double?)ring[i] ?? 0, MaxHullMetres));
                }
            }

            JArray decks = new JArray();
            if (g?["decks"] is JArray deckRows)
            {
                foreach (JToken token in deckRows)
                {
                    if (token is not JObject deck) continue;
                    decks.Add(new JObject
                    {
                        ["deckNumber"] = (int?)deck["deckNumber"] ?? 0,
                        ["floorMetres"] = Finite((double?)deck["floorMetres"] ?? 0, MaxHullMetres),
                        ["planeMetres"] = Finite((double?)deck["planeMetres"] ?? 0, MaxHullMetres),
                        ["sternZMetres"] = Finite((double?)deck["sternZMetres"] ?? 0, MaxHullMetres),
                        ["bowZMetres"] = Finite((double?)deck["bowZMetres"] ?? 0, MaxHullMetres),
                    });
                    if (decks.Count >= MaxParts) break;
                }
            }

            JArray parts = new JArray();
            if (g?["parts"] is JArray partRows)
            {
                foreach (JToken token in partRows)
                {
                    if (token is not JObject part) continue;
                    parts.Add(new JObject
                    {
                        ["kind"] = Text((string?)part["kind"]),
                        ["title"] = Text((string?)part["title"]),
                        ["x"] = Finite((double?)part["x"] ?? 0, MaxHullMetres),
                        ["y"] = Finite((double?)part["y"] ?? 0, MaxHullMetres),
                        ["z"] = Finite((double?)part["z"] ?? 0, MaxHullMetres),
                    });
                    if (parts.Count >= MaxParts) break;
                }
            }

            return new JObject
            {
                // ANDed with a drawable ring rather than trusted alone, exactly as
                // the outline's own `present` is: a file claiming an elevation it
                // did not send must draw nothing, not a degenerate line.
                ["present"] = (g != null) && ((bool?)g["present"] ?? false) && profile.Count >= 6,
                ["floorMetres"] = Finite((double?)g?["floorMetres"] ?? 0, MaxHullMetres),
                ["headMetres"] = Finite((double?)g?["headMetres"] ?? 0, MaxHullMetres),
                ["heightMetres"] = Finite((double?)g?["heightMetres"] ?? 0, MaxHullMetres),
                ["sectionCount"] = Count((int?)g?["sectionCount"] ?? 0),
                ["profile"] = profile,
                ["decks"] = decks,
                ["parts"] = parts,
            };
        }

        /// <summary>A revision is an opaque non-negative token; anything else is "none".</summary>
        private static long Revision(long value) => value < 0 || value > int.MaxValue ? 0 : value;

        /// <summary>
        /// The hull's shape and description, rebuilt field by field.
        ///
        /// The outline is the one array here that reaches an SVG path, so it is
        /// capped in LENGTH and every coordinate is clamped in MAGNITUDE, and an
        /// odd-length array is truncated rather than read past its end - a flat
        /// x,z encoding with a trailing x has no z, and a NaN in a path attribute
        /// silently blanks the element it was drawn into.
        /// </summary>
        private static JObject Hull(JObject? h, long geometryRevision)
        {
            JArray outline = new JArray();
            if (h?["outline"] is JArray ring)
            {
                int usable = Math.Min(ring.Count - (ring.Count % 2), MaxOutlinePoints * 2);
                for (int i = 0; i < usable; i++)
                {
                    outline.Add(Finite((double?)ring[i] ?? 0, MaxHullMetres));
                }
            }

            return new JObject
            {
                // present is the console's gate on drawing a real shape at all. It
                // is ANDed with a non-empty ring rather than trusted alone, so a
                // file claiming a shape it did not send draws the plain mark.
                ["present"] = (h != null) && ((bool?)h["present"] ?? false) && outline.Count >= 6,
                ["ownerCharacterUid"] = Text((string?)h?["ownerCharacterUid"]),
                ["docked"] = (bool?)h?["docked"] ?? false,
                ["beamMetres"] = Finite((double?)h?["beamMetres"] ?? 0, MaxHullMetres),
                ["keelMetres"] = Finite((double?)h?["keelMetres"] ?? 0, MaxHullMetres),
                ["deckPlaneMetres"] = Finite((double?)h?["deckPlaneMetres"] ?? 0, MaxHullMetres),
                ["bowLocalZMetres"] = Finite((double?)h?["bowLocalZMetres"] ?? 0, MaxHullMetres),
                ["sternLocalZMetres"] = Finite((double?)h?["sternLocalZMetres"] ?? 0, MaxHullMetres),
                ["cellCount"] = Count((int?)h?["cellCount"] ?? 0),
                ["hullDeckCount"] = Count((int?)h?["hullDeckCount"] ?? 0),
                ["sectionCount"] = Count((int?)h?["sectionCount"] ?? 0),
                ["keelIsLongestAxis"] = (bool?)h?["keelIsLongestAxis"] ?? false,
                ["woodId"] = Text((string?)h?["woodId"]),
                ["woodQuality"] = Count((int?)h?["woodQuality"] ?? 0),
                ["metalId"] = Text((string?)h?["metalId"]),
                ["metalQuality"] = Count((int?)h?["metalQuality"] ?? 0),
                ["outline"] = outline,
                // The geometry itself is NOT here - see GameShipDomainStat.Geometry
                // for why. What rides the poll is its revision, so a card that
                // already drew this hull knows whether the drawing has changed. Zero
                // means the game server publishes no geometry at all, which the card
                // must report rather than paper over.
                ["geometryRevision"] = geometryRevision,
            };
        }

        /// <summary>
        /// A signed number that is finite and inside a stated magnitude. Signed,
        /// unlike the counts: a port coordinate, a westward velocity and a
        /// left-hand turn are all legitimately negative.
        /// </summary>
        private static double Finite(double value, double magnitude)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            if (value > magnitude) return magnitude;
            if (value < -magnitude) return -magnitude;
            return value;
        }

        private static int Count(int value) => value < 0 ? 0 : value > 4096 ? 4096 : value;

        /// <summary>
        /// A short identifier bounded in length. These are printed into the panel;
        /// the page escapes on write, and this stops a pathological file from
        /// pushing a megabyte of text through it.
        /// </summary>
        private static string Text(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value!.Length <= 96 ? value : value.Substring(0, 96);
        }
    }

    internal sealed class GamePlayerStat
    {
        public long EntityId { get; private init; }
        public string PeerId { get; private init; } = "";

        /// <summary>
        /// The durable character behind this row, or "" on a game server that does
        /// not publish it (schema &lt; 8) or before the uid has arrived. It is the
        /// only identifier here that survives a reconnect, which is why the
        /// operator surface prefers it.
        /// </summary>
        public string CharacterUid { get; private init; } = "";
        public DateTimeOffset ConnectedAt { get; private init; }
        public bool HasHealth { get; private init; }
        public uint RttMs { get; private init; }
        public uint RttVarianceMs { get; private init; }
        public uint PacketsLost { get; private init; }
        public uint PacketsSent { get; private init; }
        public uint InFlightBytes { get; private init; }
        public bool Spiral { get; private init; }
        public bool HasPosition { get; private init; }
        public double X { get; private init; }
        public double Y { get; private init; }
        public double Z { get; private init; }

        public static GamePlayerStat Parse(JObject p)
        {
            JObject? h = p["health"] as JObject;
            JObject? position = p["position"] as JObject;

            return new GamePlayerStat
            {
                EntityId = (long?)p["entityId"] ?? 0,
                PeerId = (string?)p["peerId"] ?? "",
                CharacterUid = (string?)p["characterUid"] ?? "",
                ConnectedAt = GameStatsSnapshot.FromUnixMs((long?)p["connectedAtUnixMs"] ?? 0),
                HasHealth = h != null,
                RttMs = (uint?)(h?["rttMs"]) ?? 0,
                RttVarianceMs = (uint?)(h?["rttVarianceMs"]) ?? 0,
                PacketsLost = (uint?)(h?["packetsLost"]) ?? 0,
                PacketsSent = (uint?)(h?["packetsSent"]) ?? 0,
                InFlightBytes = (uint?)(h?["inFlightBytes"]) ?? 0,
                Spiral = (bool?)(h?["spiral"]) ?? false,
                HasPosition = position != null,
                X = (double?)position?["x"] ?? 0,
                Y = (double?)position?["y"] ?? 0,
                Z = (double?)position?["z"] ?? 0,
            };
        }
    }
}

using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The terrain half of the cross-process file contract, parsed with the same
    /// library the login server reads it with. What the operator console shows
    /// about a peer's terrain lifecycle is only as truthful as these field names
    /// and values, so the shape is pinned here.
    /// </summary>
    public class TerrainStatsSnapshotTests
    {
        private static StatsSnapshot Snapshot(TerrainRuntimeStat? terrain) =>
            new StatsSnapshot(
                bootTimeUnixMs: 1_723_200_000_000,
                generatedAtUnixMs: 1_723_200_123_000,
                uptimeSeconds: 123,
                relayMode: "v2@20Hz",
                relayHz: 20,
                build: "abc1234",
                totalConnects: 1,
                totalDisconnects: 0,
                currentOnline: 1,
                peakOnline: 1,
                players: Array.Empty<PlayerStat>(),
                terrain: terrain);

        private static JObject Terrain(TerrainRuntimeStat? terrain) =>
            (JObject)JObject.Parse(Snapshot(terrain).ToJson())["terrain"]!;

        private static TerrainPlayerStat Player(
            long entityId, int slot,
            (string Island, TerrainCheckoutState State)[] cells,
            TerrainPendingActionKind pending = TerrainPendingActionKind.None,
            string? pendingIsland = null,
            string? destination = null,
            TerrainAssetFlightStat? asset = null,
            bool removeSupported = true, bool correlatedAck = true) =>
            new TerrainPlayerStat(entityId, slot, 10.5, -2.25, 300.75,
                "haven", destination, pending, pendingIsland, asset, correlatedAck,
                removeSupported, connectPlanComplete: true, settleWaiting: false,
                cells.Select(c => new TerrainPeerIslandStat(c.Island, c.State)).ToArray());

        private static TerrainIslandStat Island(
            string id, string name, long entityId, bool managed,
            int ready = 0, int loading = 0, int draining = 0, int unloading = 0,
            int retained = 0, int errors = 0, bool hasEnvelope = true,
            bool unconditional = false, int nodes = -1, int checkedOut = -1) =>
            new TerrainIslandStat(id, name, entityId,
                registered: entityId != 0, locallyOwned: entityId != 0,
                hasEnvelope: hasEnvelope, managed: managed, unconditional: unconditional,
                -100, -50, -25, 100, 50, 25,
                ready, loading, draining, unloading, retained, errors,
                nodes, checkedOut, resourceDrainWired: true);

        [Fact]
        public void The_schema_version_records_that_terrain_telemetry_exists()
        {
            Assert.Equal(5, StatsSnapshot.SchemaVersion);
            Assert.Equal(5, (int)JObject.Parse(Snapshot(null).ToJson())["schemaVersion"]!);
        }

        [Fact]
        public void A_server_with_no_terrain_service_reports_off_rather_than_an_absent_section()
        {
            JObject t = Terrain(null);

            Assert.False((bool)t["requested"]!);
            Assert.False((bool)t["enabled"]!);
            Assert.Equal("off", (string)t["mode"]!);
            Assert.Empty((JArray)t["players"]!);
            Assert.Empty((JArray)t["islands"]!);
            Assert.Empty((JArray)t["events"]!);
            Assert.Equal(0, (int)t["candidateCount"]!);
        }

        [Fact]
        public void Requested_but_prerequisite_disabled_is_distinguishable_from_off()
        {
            JObject held = Terrain(new TerrainRuntimeStat(
                requested: true, enabled: false, 1200, 1600, 30000, 3000, 0, 0, null, null, null));

            Assert.True((bool)held["requested"]!);
            Assert.False((bool)held["enabled"]!);
            Assert.Equal("prerequisite-disabled", (string)held["mode"]!);
        }

        [Fact]
        public void The_terrain_section_names_the_single_local_authority_it_describes()
        {
            JObject t = Terrain(new TerrainRuntimeStat(
                true, true, 1200, 1600, 30000, 3000, 1, 1, null, null, null));

            Assert.Equal("local:primary", (string)t["hostId"]!);
            Assert.Equal("process-local-poll-loop", (string)t["authority"]!);
            Assert.Equal(1200.0, (double)t["loadRadiusMetres"]!);
            Assert.Equal(1600.0, (double)t["unloadRadiusMetres"]!);
            Assert.Equal(30000, (long)t["assetAckTimeoutMs"]!);
            Assert.Equal(3000, (long)t["settleDelayMs"]!);
            Assert.Equal(TerrainEventLog.Capacity, (int)t["eventCapacity"]!);
        }

        [Fact]
        public void Every_declared_state_has_a_count_key_even_when_it_is_zero()
        {
            JObject counts = (JObject)Terrain(new TerrainRuntimeStat(
                true, true, 1200, 1600, 30000, 3000, 0, 0, null, null, null))["stateCounts"]!;

            foreach (TerrainCheckoutState state in TerrainTelemetryLabels.AllStates)
                Assert.Equal(0, (int)counts[TerrainTelemetryLabels.Of(state)]!);
        }

        [Fact]
        public void Multiple_players_across_multiple_islands_keep_their_own_lifecycle()
        {
            JObject t = Terrain(new TerrainRuntimeStat(
                requested: true, enabled: true, 1200, 1600, 30000, 3000,
                candidateCount: 2, trackedPeerCount: 2,
                new[]
                {
                    Player(11, 1, new[]
                    {
                        ("mental-facility", TerrainCheckoutState.Ready),
                        ("highlands-hills", TerrainCheckoutState.Absent),
                    }),
                    Player(22, 2, new[]
                    {
                        ("mental-facility", TerrainCheckoutState.Draining),
                        ("highlands-hills", TerrainCheckoutState.RetainedLegacy),
                    }, pending: TerrainPendingActionKind.ResourceDrain,
                       pendingIsland: "mental-facility",
                       removeSupported: true, correlatedAck: false),
                },
                new[]
                {
                    Island("mental-facility", "Mental Facility", 900, managed: true,
                        ready: 1, draining: 1, nodes: 12, checkedOut: 3),
                    Island("highlands-hills", "Highlands Hills", 901, managed: true,
                        retained: 1),
                },
                Array.Empty<TerrainEventStat>()));

            JArray players = (JArray)t["players"]!;
            Assert.Equal(2, players.Count);

            Assert.Equal(11, (long)players[0]["playerEntityId"]!);
            Assert.Equal("ready", (string)players[0]["islands"]![0]!["state"]!);
            Assert.Equal("absent", (string)players[0]["islands"]![1]!["state"]!);
            Assert.True((bool)players[0]["mayRemove"]!);
            Assert.False((bool)players[0]["legacyRetaining"]!);
            Assert.Equal("none", (string)players[0]["pendingAction"]!);
            Assert.Null(((JValue)players[0]["pendingIslandId"]!).Value);

            Assert.Equal(22, (long)players[1]["playerEntityId"]!);
            Assert.Equal("draining", (string)players[1]["islands"]![0]!["state"]!);
            Assert.Equal("retained-legacy", (string)players[1]["islands"]![1]!["state"]!);
            Assert.False((bool)players[1]["mayRemove"]!);
            Assert.True((bool)players[1]["legacyRetaining"]!);
            Assert.Equal("resource-drain", (string)players[1]["pendingAction"]!);
            Assert.Equal("mental-facility", (string)players[1]["pendingIslandId"]!);
            Assert.Contains("legacy client", (string)players[1]["warning"]!);

            JObject counts = (JObject)t["stateCounts"]!;
            Assert.Equal(1, (int)counts["ready"]!);
            Assert.Equal(1, (int)counts["draining"]!);
            Assert.Equal(1, (int)counts["retained-legacy"]!);
            Assert.Equal(1, (int)counts["absent"]!);
        }

        [Fact]
        public void An_island_reports_its_peer_counts_registration_and_resource_truth()
        {
            JArray islands = (JArray)Terrain(new TerrainRuntimeStat(
                true, true, 1200, 1600, 30000, 3000, 1, 0, null,
                new[]
                {
                    Island("mental-facility", "Mental Facility", 900, managed: true,
                        ready: 2, loading: 1, draining: 1, unloading: 1, retained: 1, errors: 1,
                        nodes: 12, checkedOut: 3),
                    Island("haven", "Haven", 100, managed: false, unconditional: true),
                    Island("land-man-forgot", "The Land Man Forgot", 0, managed: false,
                        hasEnvelope: false),
                },
                null))["islands"]!;

            Assert.Equal(2, (int)islands[0]["readyPeerCount"]!);
            Assert.Equal(1, (int)islands[0]["loadingPeerCount"]!);
            Assert.Equal(1, (int)islands[0]["drainingPeerCount"]!);
            Assert.Equal(1, (int)islands[0]["unloadingPeerCount"]!);
            Assert.Equal(1, (int)islands[0]["retainedLegacyPeerCount"]!);
            Assert.Equal(1, (int)islands[0]["errorPeerCount"]!);
            Assert.Equal(12, (int)islands[0]["resourceNodeCount"]!);
            Assert.Equal(3, (int)islands[0]["checkedOutResourceCount"]!);
            Assert.True((bool)islands[0]["managed"]!);
            Assert.Equal(200.0, (double)islands[0]["envelope"]!["spanX"]!);

            Assert.True((bool)islands[1]["unconditional"]!);
            Assert.False((bool)islands[1]["managed"]!);

            // No extracted envelope: geometry is null rather than a guessed box,
            // and an unknown resource count stays -1 rather than reading "drained".
            Assert.False((bool)islands[2]["registered"]!);
            Assert.Equal(JTokenType.Null, islands[2]["envelope"]!.Type);
            Assert.Equal(-1, (int)islands[2]["resourceNodeCount"]!);
        }

        [Fact]
        public void A_cold_asset_flight_is_reported_in_full_and_absence_is_null()
        {
            JArray players = (JArray)Terrain(new TerrainRuntimeStat(
                true, true, 1200, 1600, 30000, 3000, 1, 2,
                new[]
                {
                    Player(11, 1, new[] { ("mental-facility", TerrainCheckoutState.WaitingAck) },
                        asset: new TerrainAssetFlightStat("mental-facility",
                            "TerrainAsset_MentalFacility", 4200, 1200, 2,
                            acknowledged: false, fallbackDue: false)),
                    Player(22, 2, new[] { ("mental-facility", TerrainCheckoutState.Ready) }),
                },
                null, null))["players"]!;

            JObject asset = (JObject)players[0]["asset"]!;
            Assert.Equal("mental-facility", (string)asset["islandId"]!);
            Assert.Equal("TerrainAsset_MentalFacility", (string)asset["assetName"]!);
            Assert.Equal(4200, (long)asset["requestAgeMs"]!);
            Assert.Equal(1200, (long)asset["lastRetryAgeMs"]!);
            Assert.Equal(2, (int)asset["retryCount"]!);
            Assert.False((bool)asset["acknowledged"]!);
            Assert.False((bool)asset["fallbackDue"]!);

            Assert.Equal(JTokenType.Null, players[1]["asset"]!.Type);
        }

        [Fact]
        public void Recent_events_are_bounded_newest_first_and_carry_no_peer_handle()
        {
            TerrainEventLog log = new TerrainEventLog();
            for (int i = 0; i < TerrainEventLog.Capacity + 10; i++)
                log.Record(TimeSpan.FromSeconds(i), TerrainEventKind.AddSucceeded,
                    new IslandId("island-" + i), slot: 1, success: true);
            log.Record(TimeSpan.FromSeconds(500), TerrainEventKind.RemoveFailed,
                new IslandId("mental-facility"), slot: 1, success: false);

            JArray events = (JArray)Terrain(new TerrainRuntimeStat(
                true, true, 1200, 1600, 30000, 3000, 1, 1, null, null,
                log.Snapshot(TimeSpan.FromSeconds(505), _ => 11)))["events"]!;

            Assert.Equal(TerrainEventLog.Capacity, events.Count);
            Assert.Equal("remove-failed", (string)events[0]["kind"]!);
            Assert.Equal("mental-facility", (string)events[0]["islandId"]!);
            Assert.Equal(5000, (long)events[0]["ageMs"]!);
            Assert.False((bool)events[0]["success"]!);
            Assert.Equal(11, (long)events[0]["playerEntityId"]!);
            foreach (JToken e in events)
                Assert.DoesNotContain("0x", ((JObject)e).ToString());
        }

        [Fact]
        public void A_hostile_island_id_or_asset_name_cannot_break_the_file_the_reader_parses()
        {
            // A quote, a backslash, a newline and a raw control character: the four
            // ways a hostile or merely odd name could break the file the login
            // server parses. (HTML escaping is the READER's job, on the admin
            // response; this layer's contract is that the JSON still parses.)
            string nasty = "he\"llo\\\n</script>\u0007";

            string json = Snapshot(new TerrainRuntimeStat(
                true, true, 1200, 1600, 30000, 3000, 1, 1,
                new[]
                {
                    Player(11, 1, new[] { (nasty, TerrainCheckoutState.Ready) },
                        asset: new TerrainAssetFlightStat(nasty, nasty, 1, 1, 0, false, false)),
                },
                new[] { Island(nasty, nasty, 900, managed: true) },
                null)).ToJson();

            JObject parsed = JObject.Parse(json);
            JObject t = (JObject)parsed["terrain"]!;
            Assert.Equal(nasty, (string)t["players"]![0]!["islands"]![0]!["islandId"]!);
            Assert.Equal(nasty, (string)t["players"]![0]!["asset"]!["assetName"]!);
            Assert.Equal(nasty, (string)t["islands"]![0]!["displayName"]!);
            Assert.Contains("\\u0007", json);
            Assert.Contains("\\n", json);
            Assert.DoesNotContain("\n", json);
        }

        [Fact]
        public void Negative_and_absent_inputs_are_normalised_rather_than_written_raw()
        {
            JObject t = Terrain(new TerrainRuntimeStat(
                true, true, 1200, 1600, 30000, 3000,
                candidateCount: -5, trackedPeerCount: -1, null, null,
                new[] { new TerrainEventStat(-99, TerrainEventKind.Requested, null!, 0, 0, true) }));

            Assert.Equal(0, (int)t["candidateCount"]!);
            Assert.Equal(0, (int)t["trackedPeerCount"]!);
            Assert.Equal(0, (long)t["events"]![0]!["ageMs"]!);
            Assert.Equal(string.Empty, (string)t["events"]![0]!["islandId"]!);
        }

        [Fact]
        public void The_existing_snapshot_contract_is_unchanged_alongside_the_new_section()
        {
            JObject o = JObject.Parse(Snapshot(null).ToJson());

            Assert.Equal("v2@20Hz", (string)o["relayMode"]!);
            Assert.NotNull(o["players"]);
            Assert.NotNull(o["runtime"]!["shipDomains"]);
            Assert.Equal("local:primary", (string)o["runtime"]!["hostId"]!);
            Assert.NotNull(o["terrain"]);
        }
    }
}

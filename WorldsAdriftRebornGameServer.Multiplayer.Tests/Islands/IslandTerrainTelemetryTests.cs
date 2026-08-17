using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// The rules the operator console renders verbatim. They live in one pure
    /// place so the game server, the JSON contract and the dashboard cannot
    /// disagree about what "RETAINED (LEGACY)" or "DRAINING" mean.
    /// </summary>
    public class IslandTerrainTelemetryTests
    {
        private static TerrainCheckoutState Cell(
            bool loaded = false, bool mayRemove = true, bool pendingAdd = false,
            bool pendingRemove = false, bool drainWaiting = false,
            bool assetInFlight = false, bool assetAcknowledged = false, bool failed = false) =>
            IslandTerrainStatePolicy.CellState(loaded, mayRemove, pendingAdd, pendingRemove,
                drainWaiting, assetInFlight, assetAcknowledged, failed);

        [Fact]
        public void Nothing_wanted_and_nothing_loaded_is_absent()
        {
            Assert.Equal(TerrainCheckoutState.Absent, Cell());
        }

        [Fact]
        public void A_queued_add_is_requesting_until_its_asset_request_is_in_flight()
        {
            Assert.Equal(TerrainCheckoutState.Requesting, Cell(pendingAdd: true));
            Assert.Equal(TerrainCheckoutState.WaitingAck,
                Cell(pendingAdd: true, assetInFlight: true));
            Assert.Equal(TerrainCheckoutState.Requesting,
                Cell(pendingAdd: true, assetInFlight: true, assetAcknowledged: true));
        }

        [Fact]
        public void A_loaded_island_is_ready_only_when_the_client_could_be_asked_to_unload_it()
        {
            Assert.Equal(TerrainCheckoutState.Ready, Cell(loaded: true));
            Assert.Equal(TerrainCheckoutState.RetainedLegacy,
                Cell(loaded: true, mayRemove: false));
        }

        [Fact]
        public void A_queued_removal_reports_the_removal_even_though_the_island_is_still_loaded()
        {
            Assert.Equal(TerrainCheckoutState.Unloading, Cell(loaded: true, pendingRemove: true));
            Assert.Equal(TerrainCheckoutState.Draining,
                Cell(loaded: true, pendingRemove: true, drainWaiting: true));
        }

        [Fact]
        public void A_failed_step_outranks_every_other_reading()
        {
            Assert.Equal(TerrainCheckoutState.Error,
                Cell(loaded: true, pendingRemove: true, drainWaiting: true, failed: true));
        }

        [Fact]
        public void Legacy_retain_needs_a_loaded_island_not_merely_a_missing_capability()
        {
            Assert.True(IslandTerrainStatePolicy.IsLegacyRetaining(anyLoaded: true, mayRemove: false));
            Assert.False(IslandTerrainStatePolicy.IsLegacyRetaining(anyLoaded: false, mayRemove: false));
            Assert.False(IslandTerrainStatePolicy.IsLegacyRetaining(anyLoaded: true, mayRemove: true));
        }

        [Fact]
        public void Requested_but_disabled_is_a_distinct_mode_from_off()
        {
            Assert.Equal(TerrainRuntimeMode.On,
                IslandTerrainStatePolicy.ModeOf(requested: true, enabled: true));
            Assert.Equal(TerrainRuntimeMode.PrerequisiteDisabled,
                IslandTerrainStatePolicy.ModeOf(requested: true, enabled: false));
            Assert.Equal(TerrainRuntimeMode.Off,
                IslandTerrainStatePolicy.ModeOf(requested: false, enabled: false));
            // Even a forced-on service reads as running, never as "off".
            Assert.Equal(TerrainRuntimeMode.On,
                IslandTerrainStatePolicy.ModeOf(requested: false, enabled: true));
        }

        [Fact]
        public void The_warning_an_operator_should_act_on_first_is_the_one_reported()
        {
            Assert.Contains("timed out", IslandTerrainStatePolicy.WarningFor(
                assetTimedOut: true, assetRetryCount: 9, legacyRetaining: true,
                destinationWaiting: true));
            Assert.Contains("retried 3 times", IslandTerrainStatePolicy.WarningFor(
                assetTimedOut: false, assetRetryCount: 3, legacyRetaining: true,
                destinationWaiting: true));
            Assert.Contains("legacy client", IslandTerrainStatePolicy.WarningFor(
                assetTimedOut: false, assetRetryCount: 0, legacyRetaining: true,
                destinationWaiting: true));
            Assert.Contains("requested destination", IslandTerrainStatePolicy.WarningFor(
                assetTimedOut: false, assetRetryCount: 0, legacyRetaining: false,
                destinationWaiting: true));
            Assert.Equal(string.Empty, IslandTerrainStatePolicy.WarningFor(
                assetTimedOut: false, assetRetryCount: 1, legacyRetaining: false,
                destinationWaiting: false));
        }

        [Fact]
        public void State_counts_are_indexed_in_the_declared_display_order()
        {
            IReadOnlyList<int> counts = IslandTerrainStatePolicy.CountByState(new[]
            {
                TerrainCheckoutState.Ready,
                TerrainCheckoutState.Ready,
                TerrainCheckoutState.Error,
            });

            Assert.Equal(TerrainTelemetryLabels.AllStates.Count, counts.Count);
            Assert.Equal(2, counts[(int)TerrainCheckoutState.Ready]);
            Assert.Equal(1, counts[(int)TerrainCheckoutState.Error]);
            Assert.Equal(0, counts[(int)TerrainCheckoutState.Absent]);
        }

        [Fact]
        public void Every_state_has_a_distinct_stable_wire_label()
        {
            HashSet<string> labels = new HashSet<string>();
            foreach (TerrainCheckoutState state in TerrainTelemetryLabels.AllStates)
                Assert.True(labels.Add(TerrainTelemetryLabels.Of(state)));
            Assert.Equal(8, labels.Count);
            Assert.Contains("retained-legacy", labels);
        }

        // ---- bounded event ring ------------------------------------------

        private static TerrainEventLog LogWith(int events)
        {
            TerrainEventLog log = new TerrainEventLog();
            for (int i = 0; i < events; i++)
                log.Record(TimeSpan.FromSeconds(i), TerrainEventKind.Requested,
                    new IslandId("island-" + i), slot: 1, success: true);
            return log;
        }

        [Fact]
        public void The_event_ring_never_grows_past_its_capacity()
        {
            TerrainEventLog log = LogWith(TerrainEventLog.Capacity * 3);

            Assert.Equal(TerrainEventLog.Capacity, log.Count);
            Assert.Equal(TerrainEventLog.Capacity,
                log.Snapshot(TimeSpan.FromSeconds(1000), _ => 7).Count);
        }

        [Fact]
        public void The_event_ring_reports_newest_first_with_ages_against_the_supplied_clock()
        {
            TerrainEventLog log = LogWith(3);

            IReadOnlyList<TerrainEventStat> events =
                log.Snapshot(TimeSpan.FromSeconds(10), _ => 42);

            Assert.Equal(new[] { "island-2", "island-1", "island-0" },
                events.Select(e => e.IslandId).ToArray());
            Assert.Equal(8000, events[0].AgeMs);
            Assert.Equal(10000, events[2].AgeMs);
            Assert.All(events, e => Assert.Equal(42, e.PlayerEntityId));
        }

        [Fact]
        public void An_event_snapshot_is_a_copy_the_log_can_no_longer_reach()
        {
            TerrainEventLog log = LogWith(2);
            IReadOnlyList<TerrainEventStat> before = log.Snapshot(TimeSpan.FromSeconds(5), _ => 1);

            log.Record(TimeSpan.FromSeconds(6), TerrainEventKind.RemoveFailed,
                new IslandId("later"), slot: 2, success: false);
            log.Clear();

            Assert.Equal(2, before.Count);
            Assert.Equal("island-1", before[0].IslandId);
            Assert.Equal(0, log.Count);
        }

        [Fact]
        public void A_peer_that_departed_leaves_its_events_with_no_entity_rather_than_a_handle()
        {
            TerrainEventLog log = LogWith(1);

            TerrainEventStat only = log.Snapshot(TimeSpan.FromSeconds(1), _ => 0)[0];

            Assert.Equal(0, only.PlayerEntityId);
            Assert.Equal(1, only.Slot);
        }

        // ---- derived per-peer and whole-runtime facts ----------------------

        private static TerrainPlayerStat Player(
            long entityId, int slot,
            (string Island, TerrainCheckoutState State)[] cells,
            bool removeSupported = true, bool correlatedAck = true,
            string? destination = null, TerrainAssetFlightStat? asset = null) =>
            new TerrainPlayerStat(entityId, slot, 1, 2, 3, "haven", destination,
                TerrainPendingActionKind.None, null, asset, correlatedAck, removeSupported,
                connectPlanComplete: true, settleWaiting: false,
                cells.Select(c => new TerrainPeerIslandStat(c.Island, c.State)).ToArray());

        [Fact]
        public void A_peers_ready_count_includes_terrain_it_is_retaining()
        {
            TerrainPlayerStat player = Player(5, 1, new[]
            {
                ("a", TerrainCheckoutState.Ready),
                ("b", TerrainCheckoutState.RetainedLegacy),
                ("c", TerrainCheckoutState.Requesting),
            });

            Assert.Equal(2, player.ReadyCount);
            Assert.True(player.AnyLoaded);
        }

        [Fact]
        public void A_legacy_client_with_terrain_loaded_is_reported_as_retaining_it()
        {
            TerrainPlayerStat legacy = Player(5, 1,
                new[] { ("a", TerrainCheckoutState.RetainedLegacy) },
                removeSupported: true, correlatedAck: false);

            Assert.False(legacy.MayRemove);
            Assert.True(legacy.LegacyRetaining);
            Assert.Contains("legacy client", legacy.Warning);
        }

        [Fact]
        public void A_destination_is_waiting_until_that_island_is_actually_checked_out()
        {
            Assert.True(Player(5, 1, new[] { ("dest", TerrainCheckoutState.WaitingAck) },
                destination: "dest").DestinationWaiting);
            Assert.False(Player(5, 1, new[] { ("dest", TerrainCheckoutState.Ready) },
                destination: "dest").DestinationWaiting);
            // An island the peer has no cell for at all is not secretly ready.
            Assert.True(Player(5, 1, new[] { ("other", TerrainCheckoutState.Ready) },
                destination: "dest").DestinationWaiting);
            Assert.False(Player(5, 1, new[] { ("other", TerrainCheckoutState.Ready) })
                .DestinationWaiting);
        }

        [Fact]
        public void A_timed_out_asset_outranks_the_retry_count_in_the_peer_warning()
        {
            TerrainPlayerStat player = Player(5, 1,
                new[] { ("a", TerrainCheckoutState.WaitingAck) },
                asset: new TerrainAssetFlightStat("a", "TerrainAsset", 45000, 5000, 4,
                    acknowledged: false, fallbackDue: true));

            Assert.Contains("timed out", player.Warning);
        }

        [Fact]
        public void Two_players_on_the_same_islands_do_not_share_state()
        {
            TerrainRuntimeStat runtime = new TerrainRuntimeStat(
                requested: true, enabled: true, 1200, 1600, 30000, 3000,
                candidateCount: 2, trackedPeerCount: 2,
                new[]
                {
                    Player(11, 1, new[]
                    {
                        ("mental-facility", TerrainCheckoutState.Ready),
                        ("highlands", TerrainCheckoutState.Absent),
                    }),
                    Player(22, 2, new[]
                    {
                        ("mental-facility", TerrainCheckoutState.WaitingAck),
                        ("highlands", TerrainCheckoutState.Ready),
                    }, removeSupported: false, correlatedAck: false),
                },
                Array.Empty<TerrainIslandStat>(),
                Array.Empty<TerrainEventStat>());

            Assert.Equal(1, runtime.Players[0].ReadyCount);
            Assert.Equal(1, runtime.Players[1].ReadyCount);
            Assert.False(runtime.Players[0].LegacyRetaining);
            Assert.True(runtime.Players[1].LegacyRetaining);
            Assert.Equal(1, runtime.WarningCount);
            Assert.Equal(2, runtime.ReadyCount);

            IReadOnlyList<int> counts = runtime.StateCounts;
            Assert.Equal(2, counts[(int)TerrainCheckoutState.Ready]);
            Assert.Equal(1, counts[(int)TerrainCheckoutState.WaitingAck]);
            Assert.Equal(1, counts[(int)TerrainCheckoutState.Absent]);
        }

        [Fact]
        public void The_off_runtime_is_a_real_value_with_empty_collections()
        {
            Assert.Equal(TerrainRuntimeMode.Off, TerrainRuntimeStat.Off.Mode);
            Assert.Empty(TerrainRuntimeStat.Off.Players);
            Assert.Empty(TerrainRuntimeStat.Off.Islands);
            Assert.Empty(TerrainRuntimeStat.Off.Events);
            Assert.Equal(0, TerrainRuntimeStat.Off.ReadyCount);
            Assert.Equal(0, TerrainRuntimeStat.Off.ErrorCount);
        }

        [Fact]
        public void A_runtime_snapshot_cannot_be_mutated_through_the_list_it_was_built_from()
        {
            List<TerrainEventStat> mutable = new List<TerrainEventStat>
            {
                new TerrainEventStat(1, TerrainEventKind.AddSucceeded, "a", 5, 1, true),
            };
            TerrainRuntimeStat runtime = new TerrainRuntimeStat(
                true, true, 1, 2, 3, 4, 1, 1, Array.Empty<TerrainPlayerStat>(),
                Array.Empty<TerrainIslandStat>(), mutable);

            mutable.Clear();

            Assert.Single(runtime.Events);
            Assert.Equal("a", runtime.Events[0].IslandId);
        }

        [Fact]
        public void Island_extents_are_only_reported_where_an_envelope_evidences_them()
        {
            TerrainIslandStat evidenced = new TerrainIslandStat(
                "a", "A", 7, true, true, hasEnvelope: true, managed: true, unconditional: false,
                -10, -5, -2, 10, 5, 2, 1, 0, 0, 0, 0, 0, 12, 3, true);
            TerrainIslandStat unevidenced = new TerrainIslandStat(
                "b", "B", 0, false, false, hasEnvelope: false, managed: false, unconditional: false,
                -10, -5, -2, 10, 5, 2, 0, 0, 0, 0, 0, 0, -1, -1, false);

            Assert.Equal(20.0, evidenced.SpanX);
            Assert.Equal(0.0, unevidenced.SpanX);
            Assert.Equal(-1, unevidenced.ResourceNodeCount);
        }
    }
}

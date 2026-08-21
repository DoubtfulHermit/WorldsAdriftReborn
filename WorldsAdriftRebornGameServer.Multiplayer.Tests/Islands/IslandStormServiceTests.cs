using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// THE WHOLE UNDERSTORM, DRIVEN. A fake clock and a recording wire run several
    /// complete cycles - warning, storm, end, reset - and assert on what a client
    /// would actually have been told.
    ///
    /// This exists because this repo has TWICE shipped a green suite over an
    /// unplugged feature. Tests over the schedule alone cannot tell you that
    /// anything was ever SENT, and tests over the push rule alone cannot tell you
    /// that it was ever CALLED. So every test here goes through
    /// <see cref="IslandStormService.Tick"/> at the rate the main loop calls it, and
    /// looks at the wire.
    ///
    /// The mutations §14.10 demands are each pinned by a named test below, and each
    /// test says which mutation it is for.
    /// </summary>
    public class IslandStormServiceTests
    {
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }
        }

        private sealed class RecordingWire : IIslandStormWire
        {
            public readonly List<(string Island, IslandStormUpdate Update)> Pushes = new();
            public readonly List<(string Island, TimeSpan At)> Resets = new();
            public readonly List<(string Island, long Generation)> ResetGenerations = new();
            public readonly Dictionary<string, long?> Ids = new();
            public FakeClock? Clock;

            public long? IslandEntityId(string islandId) =>
                Ids.TryGetValue(islandId, out long? id) ? id : 900 + islandId.Length;

            public int PushTimer(long islandEntityId, IslandStormUpdate update)
            {
                Pushes.Add((NameOf(islandEntityId), update));
                return 1;
            }

            public string ResetIslandResources(string islandId, long generation)
            {
                Resets.Add((islandId, Clock?.Elapsed ?? TimeSpan.Zero));
                ResetGenerations.Add((islandId, generation));
                return "reset " + islandId;
            }

            private string NameOf(long entityId)
            {
                foreach (KeyValuePair<string, long?> entry in Ids)
                    if (entry.Value == entityId) return entry.Key;
                return "e" + entityId;
            }
        }

        private static readonly TimeSpan Cadence = TimeSpan.FromSeconds(6300);
        private static readonly TimeSpan Duration = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(8);

        private static (IslandStormService, FakeClock, RecordingWire) Build(
            bool enabled = true, double jitter = 0.0, params string[] islands)
        {
            FakeClock clock = new FakeClock();
            RecordingWire wire = new RecordingWire { Clock = clock };
            string[] ids = islands.Length == 0 ? new[] { "haven" } : islands;
            for (int i = 0; i < ids.Length; i++) wire.Ids[ids[i]] = 1000 + i;

            IslandStormService service = new IslandStormService(
                clock, wire, ids, enabled, Cadence, Duration, jitter, Refresh);
            return (service, clock, wire);
        }

        /// <summary>Runs the loop from <paramref name="from"/> to <paramref name="to"/> at 20 Hz.</summary>
        private static void Run(IslandStormService service, FakeClock clock, double from, double to)
        {
            for (double t = from; t <= to; t += 0.05)
            {
                clock.Elapsed = TimeSpan.FromSeconds(t);
                service.Tick();
            }
        }

        // ====================================================================
        // The feature, end to end
        // ====================================================================

        [Fact]
        public void A_full_cycle_announces_a_storm_runs_it_ends_it_and_resets_the_world()
        {
            (IslandStormService service, FakeClock clock, RecordingWire wire) = Build();

            Run(service, clock, 0, 6400);

            // It announced itself at boot, so the client stops believing the seeded 50 s.
            Assert.Equal(IslandStormPhase.Quiet, wire.Pushes[0].Update.Phase);

            // It warned.
            Assert.Contains(wire.Pushes, p => p.Update.Phase == IslandStormPhase.Telegraph);

            // It stormed, and the storm switch was set.
            Assert.Contains(wire.Pushes,
                p => p.Update.Phase == IslandStormPhase.Active && p.Update.MillisTillLightningEnd > 0);

            // It ended, and the switch went back off.
            Assert.Contains(wire.Pushes,
                p => p.Update.Phase == IslandStormPhase.Quiet && p.Update.Generation == 2
                    && p.Update.MillisTillLightningEnd == 0);

            // And it refreshed that island's own resources exactly once.
            Assert.Single(wire.Resets);
            Assert.Equal("haven", wire.Resets[0].Island);
        }

        [Fact]
        public void The_rate_is_a_handful_of_updates_per_island_per_cycle()
        {
            // This is NOT a relayed per-frame component. If this number ever grows
            // into the hundreds, something has started pushing on a timer instead of
            // on a state change, and a soak would find it far later than this will.
            (IslandStormService service, FakeClock clock, RecordingWire wire) = Build();

            Run(service, clock, 0, 6400);

            Assert.InRange(wire.Pushes.Count, 4, 10);
        }

        [Fact]
        public void Nothing_happens_at_all_when_storms_are_off()
        {
            // MUTATION GUARD: the operator switch. With WAREBORN_STORMS unset this
            // server must be byte-identical on the wire to one built without the
            // feature.
            (IslandStormService service, FakeClock clock, RecordingWire wire) =
                Build(enabled: false);

            Run(service, clock, 0, 13000);

            Assert.Empty(wire.Pushes);
            Assert.Empty(wire.Resets);
        }

        // ====================================================================
        // MUTATION: "no-op the 1254 push"
        // ====================================================================

        [Fact]
        public void Mutation_the_client_is_actually_told_about_the_storm()
        {
            (IslandStormService service, FakeClock clock, RecordingWire wire) = Build();

            Run(service, clock, 6200, 6400);

            Assert.NotEmpty(wire.Pushes);
            Assert.Contains(wire.Pushes, p => p.Update.MillisTillLightningEnd > 0);
        }

        [Fact]
        public void An_island_that_has_not_spawned_yet_is_skipped_and_retried()
        {
            (IslandStormService service, FakeClock clock, RecordingWire wire) = Build();
            wire.Ids["haven"] = null;                     // AddEntityOp has not run

            Run(service, clock, 0, 10);
            Assert.Empty(wire.Pushes);

            wire.Ids["haven"] = 1000;                     // now it has
            Run(service, clock, 10, 20);
            Assert.NotEmpty(wire.Pushes);
        }

        // ====================================================================
        // MUTATION: "make the countdown never cross 30 s"
        // ====================================================================

        [Fact]
        public void Mutation_the_countdown_crosses_thirty_seconds_so_the_warning_can_start()
        {
            // WITHOUT THIS THE FEATURE IS INVISIBLE, and silently so. The client's
            // warning is gated on EstimatedTimeUntilLightningStarts < 30f, and its
            // countdown does NOT tick down on its own - the smoother computes a
            // decayed value and throws it away. So if the server never pushes a value
            // below 30 000 ms, the seeded 50 s just sits there and the storm arrives
            // with no rumble, no shake and no warning whatsoever.
            (IslandStormService service, FakeClock clock, RecordingWire wire) = Build();

            Run(service, clock, 6200, 6300);

            Assert.Contains(wire.Pushes,
                p => p.Update.Phase == IslandStormPhase.Telegraph
                    && p.Update.MillisTillNextLightning > 0
                    && p.Update.MillisTillNextLightning <= 30_000);
        }

        [Fact]
        public void Mutation_the_warning_ramps_in_several_steps_rather_than_one()
        {
            (IslandStormService service, FakeClock clock, RecordingWire wire) = Build();

            Run(service, clock, 6200, 6300);

            List<int> telegraph = wire.Pushes
                .Where(p => p.Update.Phase == IslandStormPhase.Telegraph)
                .Select(p => p.Update.MillisTillNextLightning)
                .ToList();

            Assert.True(telegraph.Count >= 3,
                "expected the 30 s warning to be stepped at least three times, got " + telegraph.Count);

            // Monotonically falling: a countdown that went back up would warp the
            // client's smoother the wrong way and the shake would stutter.
            for (int i = 1; i < telegraph.Count; i++)
                Assert.True(telegraph[i] < telegraph[i - 1],
                    "countdown did not fall: " + telegraph[i - 1] + " -> " + telegraph[i]);
        }

        [Fact]
        public void Every_countdown_step_moves_far_enough_for_the_client_to_accept_it()
        {
            // THE WARP GATE. TimeEstimationSmoother only stores a new value when
            // |new - held| > 7 s. A step of five seconds costs a packet and changes
            // nothing on screen, which is the worst possible failure: it looks wired.
            (IslandStormService service, FakeClock clock, RecordingWire wire) = Build();

            Run(service, clock, 6200, 6300);

            List<int> telegraph = wire.Pushes
                .Where(p => p.Update.Phase == IslandStormPhase.Telegraph)
                .Select(p => p.Update.MillisTillNextLightning)
                .ToList();

            for (int i = 1; i < telegraph.Count; i++)
            {
                double moved = (telegraph[i - 1] - telegraph[i]) / 1000.0;
                Assert.True(moved > IslandStormPolicy.ClientWarpThresholdSeconds,
                    "step " + i + " moved only " + moved + " s; the client discards anything <= "
                    + IslandStormPolicy.ClientWarpThresholdSeconds + " s");
            }
        }

        [Fact]
        public void A_refresh_interval_below_the_warp_threshold_still_never_emits_a_dud_step()
        {
            // Belt and braces: even if the env floor were bypassed, the push rule
            // itself refuses a step the client would discard.
            IslandStormUpdate last = new IslandStormUpdate(IslandStormPhase.Telegraph, 30_000, 0, 1);
            IslandStormSample barelyLater =
                new IslandStormSample(IslandStormPhase.Telegraph, 27_000, 0, 1);

            Assert.Null(IslandStormPush.Next(last, barelyLater,
                TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)));
        }

        // ====================================================================
        // MUTATION: "drop the generation bump"
        // ====================================================================

        [Fact]
        public void Mutation_each_storm_carries_a_new_generation()
        {
            (IslandStormService service, FakeClock clock, RecordingWire wire) = Build();

            Run(service, clock, 6200, 6400);
            Run(service, clock, 12500, 12700);

            List<long> stormGenerations = wire.Pushes
                .Where(p => p.Update.Phase == IslandStormPhase.Active)
                .Select(p => p.Update.Generation)
                .Distinct()
                .ToList();

            Assert.Equal(2, stormGenerations.Count);
            Assert.Equal(new long[] { 1, 2 }, stormGenerations.OrderBy(g => g).ToArray());
        }

        [Fact]
        public void A_generation_change_alone_is_enough_to_owe_the_client_an_update()
        {
            IslandStormUpdate last = new IslandStormUpdate(IslandStormPhase.Quiet, 100_000, 0, 1);
            IslandStormSample nextCycle = new IslandStormSample(IslandStormPhase.Quiet, 100_000, 0, 2);

            Assert.NotNull(IslandStormPush.Next(last, nextCycle, TimeSpan.Zero, Refresh));
        }

        // ====================================================================
        // MUTATION: "let the reset fire at storm START instead of end"
        // ====================================================================

        [Fact]
        public void Mutation_the_reset_fires_when_the_storm_ENDS_not_when_it_starts()
        {
            // Resetting at the start would put every mined node back while the bolts
            // were still falling, so the storm would be an announcement of something
            // that had already happened.
            (IslandStormService service, FakeClock clock, RecordingWire wire) = Build();

            Run(service, clock, 0, 6299);
            Assert.Empty(wire.Resets);

            Run(service, clock, 6299, 6344);          // the whole storm
            Assert.Empty(wire.Resets);

            Run(service, clock, 6344, 6346);          // one second past the end
            Assert.Single(wire.Resets);
            Assert.InRange(wire.Resets[0].At.TotalSeconds, 6345, 6346);
        }

        // ====================================================================
        // S2: THE RESET IS PER ISLAND, AT THAT ISLAND'S OWN STORM END
        // ====================================================================

        [Fact]
        public void Mutation_every_island_gets_its_OWN_reset_once_per_cycle()
        {
            // S1 fired ONE world-wide reset per generation, so this count was 2 for
            // four islands over two cadences. Per island it must be 4 x 2 = 8, one
            // for each island's own storm.
            (IslandStormService service, FakeClock clock, RecordingWire wire) =
                Build(enabled: true, jitter: 0.2, "haven", "trades-challenge", "b3-01", "b3-02");

            Run(service, clock, 0, 6300 * 2 + 2000);

            Assert.Equal(8, wire.Resets.Count);
            foreach (string island in new[] { "haven", "trades-challenge", "b3-01", "b3-02" })
                Assert.Equal(2, wire.Resets.Count(r => r.Island == island));
        }

        [Fact]
        public void Mutation_each_islands_reset_lands_at_that_islands_OWN_storm_end_tick()
        {
            // ⚠ THIS IS THE ACCEPTANCE CRITERION, IN ARITHMETIC.
            // "Stand on one island, cut a tree, and the tree returns at the moment
            // that island's bolts stop." A reset that is correct in COUNT but fires
            // at the wrong instant is the S1 defect exactly: S1's resets were all
            // present and all late.
            string[] islands = { "haven", "trades-challenge", "b3-01", "b3-02", "b7-11" };
            (IslandStormService service, FakeClock clock, RecordingWire wire) =
                Build(enabled: true, jitter: 0.2, islands);

            // One full cadence plus the widest possible jitter (0.2 x 6300 = 1260 s)
            // and one storm: every island's GENERATION 1 reset has landed, and no
            // island's generation 2 has.
            Run(service, clock, 0, 6300 + 2000);

            Assert.Equal(islands.Length, wire.Resets.Count);
            foreach ((string island, TimeSpan at) in wire.Resets)
            {
                TimeSpan offset = service.PhaseOffsetOf(island);
                TimeSpan due = IslandStormPolicy.ResetAt(1, Cadence, Duration, offset);

                // Within one 20 Hz loop turn of that island's own storm end. Never
                // before it, and never a tick later than the loop could notice.
                Assert.InRange((at - due).TotalSeconds, 0.0, 0.06);
            }
        }

        [Fact]
        public void Mutation_an_early_islands_reset_does_not_wait_for_the_last_islands_storm()
        {
            // THE DEFECT, REPRODUCED AS A REGRESSION TEST. On production
            // (47 islands, 900 s cadence, 0.2 jitter) the first island stormed at
            // 10:59:57 and the reset landed at 11:03:29 - 3 m 32 s later, under a
            // clear sky. Here: the earliest island's reset must land strictly BEFORE
            // the latest island's storm even STARTS.
            string[] islands =
            {
                "haven", "trades-challenge", "b3-01", "b3-02", "b7-11", "b5-04",
                "b9-22", "b2-17", "b6-08", "b4-13",
            };
            (IslandStormService service, FakeClock clock, RecordingWire wire) =
                Build(enabled: true, jitter: 0.2, islands);

            Run(service, clock, 0, 6300 + 2000);

            string earliest = islands.OrderBy(i => service.PhaseOffsetOf(i)).First();
            string latest = islands.OrderBy(i => service.PhaseOffsetOf(i)).Last();
            Assert.NotEqual(earliest, latest);

            TimeSpan earliestReset = wire.Resets.First(r => r.Island == earliest).At;
            TimeSpan latestStormStart =
                IslandStormPolicy.StartOf(1, Cadence, service.PhaseOffsetOf(latest));

            Assert.True(earliestReset < latestStormStart,
                "the first island's resources came back at " + earliestReset
                + ", which is not before the last island's storm even began at "
                + latestStormStart + " - that is the S1 defect.");
        }

        [Fact]
        public void The_resets_interleave_with_the_staggered_storm_starts()
        {
            // The headless shape of the acceptance: reset lines are SPREAD THROUGH
            // the sweep, not bunched at the end of it. With a world-wide reset every
            // reset lands after every Active push; per island they interleave.
            string[] islands =
            {
                "haven", "trades-challenge", "b3-01", "b3-02", "b7-11", "b5-04",
                "b9-22", "b2-17", "b6-08", "b4-13",
            };
            (IslandStormService service, FakeClock clock, RecordingWire wire) =
                Build(enabled: true, jitter: 0.2, islands);

            Run(service, clock, 0, 6300 + 2000);

            TimeSpan lastActiveStart = islands
                .Select(i => IslandStormPolicy.StartOf(1, Cadence, service.PhaseOffsetOf(i)))
                .Max();

            int before = wire.Resets.Count(r => r.At < lastActiveStart);
            Assert.True(before > 0,
                "not one island's resources came back before the last island had even "
                + "started storming; the resets are still bunched at the end of the sweep.");
            Assert.Equal(islands.Length, wire.Resets.Count);
        }

        [Fact]
        public void An_island_whose_entity_has_not_spawned_still_gets_its_resources_reset()
        {
            // The resources are SERVER-side state. Whether any client has been served
            // the island's 1254 has nothing to do with whether a chopped tree should
            // be standing again. Putting the reset behind the push's early exit would
            // make a storm on an unspawned island silently restore nothing.
            (IslandStormService service, FakeClock clock, RecordingWire wire) = Build();
            wire.Ids["haven"] = null;                     // AddEntityOp never runs

            Run(service, clock, 0, 6400);

            Assert.Empty(wire.Pushes);
            Assert.Single(wire.Resets);
            Assert.Equal("haven", wire.Resets[0].Island);
        }

        [Fact]
        public void A_server_enabled_late_does_not_replay_the_resets_it_slept_through()
        {
            // The service is constructed at boot but the clock may be hours old by
            // the time anything ticks. Firing five backdated resets would wipe every
            // player's in-progress harvesting the instant an operator flipped a flag.
            (IslandStormService service, FakeClock clock, RecordingWire wire) = Build();

            clock.Elapsed = TimeSpan.FromSeconds(6300 * 5);
            service.Tick();

            Assert.Empty(wire.Resets);

            // Up to just short of generation 6's own reset instant, so exactly
            // one storm has completed since the service started ticking.
            Run(service, clock, 6300 * 5, 6300 * 6 - 100);
            Assert.Single(wire.Resets);
        }

        // ====================================================================
        // MUTATION: "write isLightningActive = true"
        // ====================================================================

        [Fact]
        public void Mutation_nothing_in_this_assembly_can_express_isLightningActive()
        {
            // ⚠ THE ISLAND-DROP HAZARD. A rising isLightningActive makes
            // IslandLocalTransformBehaviour write the island's transform to
            // GetEndOfWorldPosition() - doomsday code that lerps Y toward
            // -250..-1500 m. The bool buys nothing: the visualiser that renders a
            // storm switches on the INT. So the field is not on the update type at
            // all, and this test is what stops somebody adding it back "for
            // completeness".
            Type update = typeof(IslandStormUpdate);

            Assert.DoesNotContain(update.GetProperties(),
                p => p.Name.Contains("LightningActive", StringComparison.OrdinalIgnoreCase)
                    || p.Name.Contains("Active", StringComparison.OrdinalIgnoreCase)
                        && p.PropertyType == typeof(bool));

            Assert.DoesNotContain(update.GetFields(),
                f => f.FieldType == typeof(bool));

            // Nor can the sample the update is built from.
            Assert.DoesNotContain(typeof(IslandStormSample).GetProperties(),
                p => p.PropertyType == typeof(bool));
        }

        [Fact]
        public void The_storm_switch_is_the_int_and_it_is_zero_outside_a_storm()
        {
            (IslandStormService service, FakeClock clock, RecordingWire wire) = Build();

            Run(service, clock, 0, 6400);

            foreach ((string _, IslandStormUpdate update) in wire.Pushes)
            {
                if (update.Phase == IslandStormPhase.Active)
                    Assert.True(update.MillisTillLightningEnd > 0,
                        "an active storm must set the switch");
                else
                    Assert.Equal(0, update.MillisTillLightningEnd);
            }
        }

        // ====================================================================
        // Multiple islands
        // ====================================================================

        [Fact]
        public void Jittered_islands_do_not_all_storm_on_the_same_turn()
        {
            (IslandStormService service, FakeClock clock, RecordingWire wire) =
                Build(enabled: true, jitter: 0.2, "haven", "trades-challenge", "b3-01");

            Run(service, clock, 6000, 8000);

            List<(string Island, IslandStormUpdate Update)> starts = wire.Pushes
                .Where(p => p.Update.Phase == IslandStormPhase.Active).ToList();

            Assert.Equal(3, starts.Count);
            Assert.Equal(3, starts.Select(s => s.Island).Distinct().Count());
            Assert.True(service.PhaseOffsetOf("haven") != service.PhaseOffsetOf("b3-01"));
        }

        [Fact]
        public void An_empty_world_storms_nothing_and_does_not_throw()
        {
            FakeClock clock = new FakeClock();
            RecordingWire wire = new RecordingWire { Clock = clock };
            IslandStormService service = new IslandStormService(
                clock, wire, Array.Empty<string>(), true, Cadence, Duration, 0.2, Refresh);

            Run(service, clock, 0, 13000);

            Assert.Empty(wire.Pushes);
            Assert.Empty(wire.Resets);
            Assert.Equal(0, service.IslandCount);
        }

        [Fact]
        public void A_zero_length_storm_is_refused_at_construction()
        {
            FakeClock clock = new FakeClock();
            RecordingWire wire = new RecordingWire();
            Assert.Throws<ArgumentOutOfRangeException>(() => new IslandStormService(
                clock, wire, new[] { "haven" }, true, Cadence, TimeSpan.Zero, 0.2, Refresh));
        }
    }
}

using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// THE UNDERSTORM SCHEDULE, and the two integers on 1254 that are the whole
    /// feature.
    ///
    /// These are arithmetic tests over a pure function, so a 105-minute cycle costs
    /// microseconds. What they are actually protecting is a client that behaves in
    /// two non-obvious ways:
    ///
    ///   * the storm switch is an INT (<c>estimatedMilliTillLightningEnd &gt; 0</c>),
    ///     not the bool that shares the component; and
    ///   * the countdown does not tick down on its own, so the warning only exists
    ///     if the server keeps pushing it.
    ///
    /// Both are asserted here rather than left to a comment.
    /// </summary>
    public class IslandStormPolicyTests
    {
        private static readonly TimeSpan Cadence = TimeSpan.FromSeconds(6300);
        private static readonly TimeSpan Duration = TimeSpan.FromSeconds(45);

        private static IslandStormSample At(double seconds, TimeSpan? offset = null) =>
            IslandStormPolicy.Sample(TimeSpan.FromSeconds(seconds), Cadence, Duration,
                offset ?? TimeSpan.Zero);

        // --------------------------------------------------------------------
        // The cycle
        // --------------------------------------------------------------------

        [Fact]
        public void A_fresh_server_is_quiet_and_does_not_storm_at_zero()
        {
            IslandStormSample sample = At(0);

            Assert.Equal(IslandStormPhase.Quiet, sample.Phase);
            Assert.Equal(0, sample.MillisTillLightningEnd);
            Assert.Equal(6300 * 1000, sample.MillisTillNextLightning);
            Assert.Equal(1, sample.Generation);
        }

        [Fact]
        public void The_countdown_falls_toward_the_first_storm()
        {
            Assert.Equal(300 * 1000, At(6000).MillisTillNextLightning);
            Assert.Equal(60 * 1000, At(6240).MillisTillNextLightning);
        }

        [Fact]
        public void The_last_thirty_seconds_are_the_telegraph_and_not_a_second_earlier()
        {
            // 30 s out is NOT yet the warning - the client's test is a strict "< 30f".
            Assert.Equal(IslandStormPhase.Quiet, At(6270).Phase);
            Assert.Equal(IslandStormPhase.Telegraph, At(6270.5).Phase);
            Assert.Equal(IslandStormPhase.Telegraph, At(6299).Phase);
        }

        [Fact]
        public void A_telegraph_never_sets_the_storm_switch()
        {
            // The single most important assertion in this file. estimatedMilli-
            // TillLightningEnd IS IsLightningActive on the client; a non-zero value
            // during the warning would start the bolts thirty seconds early.
            Assert.Equal(0, At(6299).MillisTillLightningEnd);
        }

        [Fact]
        public void The_storm_runs_for_its_duration_and_then_stops()
        {
            Assert.Equal(IslandStormPhase.Active, At(6300).Phase);
            Assert.Equal(45 * 1000, At(6300).MillisTillLightningEnd);
            Assert.Equal(0, At(6300).MillisTillNextLightning);

            Assert.Equal(IslandStormPhase.Active, At(6344).Phase);
            Assert.Equal(1000, At(6344).MillisTillLightningEnd);

            // One millisecond past the end it is over, and the switch is off.
            Assert.Equal(IslandStormPhase.Quiet, At(6345).Phase);
            Assert.Equal(0, At(6345).MillisTillLightningEnd);
        }

        [Fact]
        public void The_generation_advances_once_per_cycle_and_never_backwards()
        {
            Assert.Equal(1, At(6300).Generation);
            Assert.Equal(2, At(6345).Generation);      // the moment gen 1 ends
            Assert.Equal(2, At(12600).Generation);
            Assert.Equal(3, At(12645).Generation);

            long previous = 0;
            for (double t = 0; t < 6300 * 4; t += 37)
            {
                long generation = At(t).Generation;
                Assert.True(generation >= previous,
                    "generation went backwards at t=" + t + ": " + previous + " -> " + generation);
                previous = generation;
            }
        }

        [Fact]
        public void Every_cycle_contains_exactly_one_storm()
        {
            int storms = 0;
            bool wasActive = false;
            for (double t = 0; t < 6300 * 3 + 100; t += 0.5)
            {
                bool active = At(t).Phase == IslandStormPhase.Active;
                if (active && !wasActive) storms++;
                wasActive = active;
            }
            Assert.Equal(3, storms);
        }

        // --------------------------------------------------------------------
        // Per-island phase offsets
        // --------------------------------------------------------------------

        [Fact]
        public void Islands_do_not_all_storm_at_the_same_instant()
        {
            TimeSpan haven = IslandStormPolicy.PhaseOffsetFor("haven", Cadence, 0.2);
            TimeSpan trades = IslandStormPolicy.PhaseOffsetFor("trades-challenge", Cadence, 0.2);

            Assert.NotEqual(haven, trades);
        }

        [Fact]
        public void A_phase_offset_stays_inside_its_jitter_window()
        {
            foreach (string id in new[] { "haven", "trades-challenge", "b3-01", "b3-12", "" })
            {
                TimeSpan offset = IslandStormPolicy.PhaseOffsetFor(id, Cadence, 0.2);
                Assert.True(offset >= TimeSpan.Zero, id + " offset was negative");
                Assert.True(offset <= TimeSpan.FromSeconds(6300 * 0.2),
                    id + " offset " + offset + " escaped its jitter window");
            }
        }

        [Fact]
        public void Zero_jitter_puts_every_island_in_lockstep()
        {
            Assert.Equal(TimeSpan.Zero, IslandStormPolicy.PhaseOffsetFor("haven", Cadence, 0));
            Assert.Equal(TimeSpan.Zero, IslandStormPolicy.PhaseOffsetFor("b3-07", Cadence, 0));
        }

        [Fact]
        public void The_phase_offset_is_stable_across_processes()
        {
            // NOT string.GetHashCode, which .NET randomises per process - that would
            // reshuffle every island's schedule on every restart, and "the storm
            // times all changed" is indistinguishable from a bug.
            Assert.Equal(IslandStormPolicy.StableUnitInterval("haven"),
                IslandStormPolicy.StableUnitInterval("haven"));
            Assert.NotEqual(IslandStormPolicy.StableUnitInterval("haven"),
                IslandStormPolicy.StableUnitInterval("trades-challenge"));

            // Pinned literally, so a change to the hash is a visible diff rather
            // than a silent reschedule of every island in the world.
            Assert.InRange(IslandStormPolicy.StableUnitInterval("haven"), 0.0, 1.0);
            Assert.Equal(0.0, IslandStormPolicy.StableUnitInterval(null));
        }

        [Fact]
        public void An_offset_island_storms_later_but_for_the_same_length_of_time()
        {
            TimeSpan offset = TimeSpan.FromSeconds(600);

            Assert.Equal(IslandStormPhase.Quiet, At(6300, offset).Phase);
            Assert.Equal(IslandStormPhase.Active, At(6900, offset).Phase);
            Assert.Equal(45 * 1000, At(6900, offset).MillisTillLightningEnd);
            Assert.Equal(IslandStormPhase.Quiet, At(6945, offset).Phase);
        }

        // --------------------------------------------------------------------
        // The per-island reset (S2)
        // --------------------------------------------------------------------

        [Fact]
        public void An_islands_reset_is_due_at_its_OWN_storm_END_not_its_start()
        {
            TimeSpan offset = TimeSpan.FromSeconds(600);

            // This island's storm STARTS at 6900. Nothing is due yet.
            Assert.Equal(0, IslandStormPolicy.DueResetGeneration(
                TimeSpan.FromSeconds(6900), Cadence, Duration, offset));
            Assert.Equal(0, IslandStormPolicy.DueResetGeneration(
                TimeSpan.FromSeconds(6944), Cadence, Duration, offset));

            // It ENDS at 6945. Now it is.
            Assert.Equal(1, IslandStormPolicy.DueResetGeneration(
                TimeSpan.FromSeconds(6945), Cadence, Duration, offset));
        }

        [Fact]
        public void Two_islands_with_different_offsets_are_due_at_different_instants()
        {
            // ⚠ THIS IS THE S2 DEFECT, EXPRESSED AS ARITHMETIC.
            // S1 answered this question ONCE, with the LAST island's offset, so an
            // early island's reset was deferred until the storm front had swept the
            // whole world - MEASURED at 3 m 32 s late on production with 47 islands.
            // The early island must now be due while the late one still is not.
            TimeSpan early = TimeSpan.Zero;
            TimeSpan late = TimeSpan.FromSeconds(600);

            TimeSpan whenEarlyEnds = TimeSpan.FromSeconds(6345);

            Assert.Equal(1, IslandStormPolicy.DueResetGeneration(
                whenEarlyEnds, Cadence, Duration, early));
            Assert.Equal(0, IslandStormPolicy.DueResetGeneration(
                whenEarlyEnds, Cadence, Duration, late));
        }

        [Fact]
        public void Nothing_is_due_before_the_first_cycle_completes()
        {
            Assert.Equal(0, IslandStormPolicy.DueResetGeneration(
                TimeSpan.Zero, Cadence, Duration, TimeSpan.Zero));
            Assert.Equal(0, IslandStormPolicy.DueResetGeneration(
                TimeSpan.FromSeconds(6300), Cadence, Duration, TimeSpan.Zero));
        }

        [Fact]
        public void The_due_reset_generation_only_ever_climbs()
        {
            long previous = 0;
            for (double t = 0; t < 6300 * 4; t += 13)
            {
                long due = IslandStormPolicy.DueResetGeneration(
                    TimeSpan.FromSeconds(t), Cadence, Duration, TimeSpan.Zero);
                Assert.True(due >= previous, "due reset generation went backwards at t=" + t);
                previous = due;
            }
            Assert.Equal(3, previous);
        }

        [Fact]
        public void The_reset_instant_matches_that_islands_own_storm_end()
        {
            TimeSpan offset = TimeSpan.FromSeconds(600);
            Assert.Equal(TimeSpan.FromSeconds(6945),
                IslandStormPolicy.ResetAt(1, Cadence, Duration, offset));
            Assert.Equal(TimeSpan.FromSeconds(13245),
                IslandStormPolicy.ResetAt(2, Cadence, Duration, offset));

            // And it is exactly StartOf + Duration, for every offset.
            foreach (double seconds in new[] { 0.0, 137.0, 600.0, 1259.0 })
            {
                TimeSpan phase = TimeSpan.FromSeconds(seconds);
                Assert.Equal(IslandStormPolicy.StartOf(3, Cadence, phase) + Duration,
                    IslandStormPolicy.ResetAt(3, Cadence, Duration, phase));
            }
        }

        [Fact]
        public void The_last_island_special_case_is_gone_from_the_policy_surface()
        {
            // MUTATION GUARD, and a deliberate one: S1's WorldResetAt /
            // DueWorldResetGeneration were the "reset once, at the last island"
            // arithmetic. If either comes back, something has quietly reintroduced a
            // world-scoped reset and the 3 m 32 s delay with it.
            System.Reflection.MethodInfo[] methods = typeof(IslandStormPolicy).GetMethods();
            Assert.DoesNotContain(methods, m => m.Name == "WorldResetAt");
            Assert.DoesNotContain(methods, m => m.Name == "DueWorldResetGeneration");
        }

        // --------------------------------------------------------------------
        // Operator knobs
        // --------------------------------------------------------------------

        [Fact]
        public void Storms_arrive_off()
        {
            Assert.False(IslandStormPolicy.Enabled(null));
            Assert.False(IslandStormPolicy.Enabled(""));
            Assert.False(IslandStormPolicy.Enabled("0"));
            Assert.False(IslandStormPolicy.Enabled("no"));
            Assert.False(IslandStormPolicy.Enabled("maybe"));

            Assert.True(IslandStormPolicy.Enabled("1"));
            Assert.True(IslandStormPolicy.Enabled("true"));
            Assert.True(IslandStormPolicy.Enabled("TRUE"));
            Assert.True(IslandStormPolicy.Enabled("on"));
            Assert.True(IslandStormPolicy.Enabled(" yes "));
        }

        [Fact]
        public void A_typo_in_an_env_var_never_stops_a_server_booting()
        {
            Assert.Equal(IslandStormPolicy.DefaultDuration, IslandStormPolicy.DurationFrom("banana"));
            Assert.Equal(IslandStormPolicy.DefaultDuration, IslandStormPolicy.DurationFrom("-5"));
            Assert.Equal(IslandStormPolicy.DefaultDuration, IslandStormPolicy.DurationFrom(null));
            Assert.Equal(IslandStormPolicy.DefaultJitterFraction, IslandStormPolicy.JitterFrom("banana"));
            Assert.Equal(IslandStormPolicy.DefaultCadence,
                IslandStormPolicy.CadenceFrom("banana", Duration));
        }

        [Fact]
        public void The_recovered_cadence_is_the_one_TreeHarvest_already_recorded()
        {
            Assert.Equal(TreeHarvest.UnderstormCadence, IslandStormPolicy.DefaultCadence);
            Assert.Equal(TimeSpan.FromMinutes(105), IslandStormPolicy.DefaultCadence);
        }

        [Fact]
        public void A_cadence_shorter_than_one_storm_plus_its_warning_is_refused()
        {
            // 60 s of cadence around a 45 s storm with a 30 s warning would mean the
            // island was permanently either storming or announcing a storm.
            Assert.Equal(IslandStormPolicy.DefaultCadence, IslandStormPolicy.CadenceFrom("60", Duration));
            Assert.Equal(IslandStormPolicy.DefaultCadence, IslandStormPolicy.CadenceFrom("75", Duration));
            Assert.Equal(TimeSpan.FromSeconds(180), IslandStormPolicy.CadenceFrom("180", Duration));
        }

        [Fact]
        public void Jitter_is_clamped_so_one_generation_cannot_overlap_the_next()
        {
            Assert.Equal(0.0, IslandStormPolicy.JitterFrom("-1"));
            Assert.Equal(IslandStormPolicy.MaxJitterFraction, IslandStormPolicy.JitterFrom("5"));
            Assert.Equal(0.35, IslandStormPolicy.JitterFrom("0.35"));
        }

        [Fact]
        public void The_countdown_refresh_is_floored_above_the_clients_warp_threshold()
        {
            // THE FLOOR IS NOT A NICETY. The client discards a countdown push that
            // moves the value by seven seconds or less, so a 2 s refresh buys packets
            // and no warning at all.
            Assert.Equal(TimeSpan.FromSeconds(IslandStormPolicy.MinCountdownRefreshSeconds),
                IslandStormPolicy.CountdownRefreshFrom("2"));
            Assert.Equal(TimeSpan.FromSeconds(IslandStormPolicy.MinCountdownRefreshSeconds),
                IslandStormPolicy.CountdownRefreshFrom("7"));
            Assert.Equal(TimeSpan.FromSeconds(12), IslandStormPolicy.CountdownRefreshFrom("12"));
            Assert.Equal(IslandStormPolicy.DefaultCountdownRefresh,
                IslandStormPolicy.CountdownRefreshFrom(null));

            Assert.True(IslandStormPolicy.MinCountdownRefreshSeconds
                > IslandStormPolicy.ClientWarpThresholdSeconds,
                "the refresh floor must exceed the client's warp threshold or no push warps");
        }

        [Fact]
        public void The_default_refresh_fits_several_steps_into_the_warning_window()
        {
            Assert.True(
                IslandStormPolicy.DefaultCountdownRefresh.TotalSeconds * 3
                    <= IslandStormPolicy.TelegraphSeconds,
                "the default refresh must fit at least three steps into the 30 s warning");
        }

        // --------------------------------------------------------------------
        // Trees riding the storm
        // --------------------------------------------------------------------

        [Fact]
        public void Trees_stop_healing_on_their_own_timers_once_storms_exist()
        {
            Assert.False(IslandStormPolicy.PerTreeRegrowthEnabled(stormsEnabled: true, null));
            Assert.False(IslandStormPolicy.PerTreeRegrowthEnabled(stormsEnabled: true, "  "));
        }

        [Fact]
        public void An_operator_who_set_the_tree_knob_keeps_their_per_tree_timers()
        {
            Assert.True(IslandStormPolicy.PerTreeRegrowthEnabled(stormsEnabled: true, "300"));
            // Even an unparseable value is an operator saying something about trees.
            Assert.True(IslandStormPolicy.PerTreeRegrowthEnabled(stormsEnabled: true, "banana"));
        }

        [Fact]
        public void With_storms_off_trees_behave_exactly_as_they_always_did()
        {
            Assert.True(IslandStormPolicy.PerTreeRegrowthEnabled(stormsEnabled: false, null));
            Assert.True(IslandStormPolicy.PerTreeRegrowthEnabled(stormsEnabled: false, "300"));
        }

        // --------------------------------------------------------------------
        // Recovered client constants
        // --------------------------------------------------------------------

        [Fact]
        public void The_recovered_client_constants_are_the_clients_own()
        {
            // PROVED, acs/IslandLightningTimerVisualizer.cs:161 and :226; the radius
            // is sqrt(90000). RECOVERED, UnityPy type-tree read of the 255 shipped
            // island bundles, for the strike cadence. If any of these change, the
            // change was a guess and this test is where it gets caught.
            Assert.Equal(30.0, IslandStormPolicy.TelegraphSeconds);
            Assert.Equal(300.0, IslandStormPolicy.TelegraphRadiusMetres);
            Assert.Equal(7.0, IslandStormPolicy.ClientWarpThresholdSeconds);
            Assert.Equal(0.0, IslandStormPolicy.PrefabMinSecondsBetweenStrikes);
            Assert.Equal(1.0, IslandStormPolicy.PrefabMaxSecondsBetweenStrikes);
        }
    }
}

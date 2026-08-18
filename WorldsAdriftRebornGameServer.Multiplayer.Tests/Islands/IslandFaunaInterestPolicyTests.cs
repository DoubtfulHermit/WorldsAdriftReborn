using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// THE DESPAWN, AND WHY IT CANNOT COME BACK.
    ///
    /// The reported bug: "i have seen some manta rays here and there but they kinda
    /// despawn". The cause was that fauna checked out against each CREATURE's live
    /// position at the global resource radius. A deposit never moves, so that works;
    /// a manta orbits, so its distance to a standing player oscillates by hundreds of
    /// metres every lap, and each crossing of the radius produced a RemoveEntity
    /// followed later by a fresh AddEntity. Measured against the release catalogue's
    /// own AABBs with the player at the island's landing point, a manta spent 0% of
    /// its lap inside the production 120 m radius on THIRTY of the forty-six tier-1
    /// islands.
    ///
    /// The fix is structural rather than numeric: interest is keyed on the ISLAND,
    /// whose distance to a standing player is constant, so no movement change can
    /// ever reintroduce the churn. These tests pin the four properties that make that
    /// true, because each of them is a way the bug could be rebuilt by accident:
    ///
    /// A CREATURE'S POSITION IS NOT AN INPUT. Nothing in this type takes one.
    /// STANDING STILL NEVER CHANGES ANYTHING - the same peer position produces the
    /// same admission set forever.
    /// A HELD ISLAND IS NEVER EVICTED BY A NEWCOMER, so approaching a second island
    /// cannot unload the one under your feet.
    /// THE BUDGET IS SPENT IN WHOLE ISLANDS, so a school is never half-admitted and
    /// a cap boundary can never make members flicker in and out individually.
    ///
    /// And the safety property the multiplayer rule is actually about: the worst-case
    /// per-peer update rate is bounded by the per-peer budget ALONE, independent of
    /// how large the world's population is.
    /// </summary>
    public sealed class IslandFaunaInterestPolicyTests
    {
        private const double Load = IslandFaunaInterestPolicy.DefaultLoadRadiusMetres;
        private static readonly double Unload =
            IslandFaunaInterestPolicy.UnloadRadiusFor(Load);

        // --- Configuration

        [Fact]
        public void The_radius_falls_back_rather_than_throwing_on_anything_unparseable()
        {
            foreach (string? bad in new string?[] { null, "", "   ", "wide", "NaN", "1,5" })
            {
                Assert.Equal(IslandFaunaInterestPolicy.DefaultLoadRadiusMetres,
                    IslandFaunaInterestPolicy.LoadRadiusFrom(bad));
            }

            Assert.Equal(800.0, IslandFaunaInterestPolicy.LoadRadiusFrom("800"));
            Assert.Equal(250.5, IslandFaunaInterestPolicy.LoadRadiusFrom("250.5"));

            // Zero and negative are a kill switch, not an error: an operator who wants
            // the wildlife to stop being streamed must be able to say so.
            Assert.Equal(0.0, IslandFaunaInterestPolicy.LoadRadiusFrom("0"));
            Assert.Equal(0.0, IslandFaunaInterestPolicy.LoadRadiusFrom("-40"));

            // Never past the server-wide ceiling, whatever an operator types.
            Assert.Equal(InterestPolicy.MaxRadiusMetres,
                IslandFaunaInterestPolicy.LoadRadiusFrom("99999999"));
        }

        [Fact]
        public void The_unload_radius_is_always_wider_than_the_load_radius_or_also_off()
        {
            foreach (double load in new[] { 1.0, 120.0, 600.0, 2000.0 })
            {
                Assert.True(IslandFaunaInterestPolicy.UnloadRadiusFor(load) > load,
                    "hysteresis with no margin is not hysteresis");
            }
            Assert.Equal(0.0, IslandFaunaInterestPolicy.UnloadRadiusFor(0.0));
        }

        [Fact]
        public void The_worst_case_rate_depends_on_the_peer_budget_and_nothing_else()
        {
            TimeSpan cadence = IslandFaunaRegistry.DefaultPoseInterval;

            // The number the soak gate has already measured FLAT: 24 creatures at
            // 250 ms is a 96 update/s ceiling.
            Assert.Equal(96.0, IslandFaunaInterestPolicy.WorstCaseUpdatesPerSecond(
                IslandFaunaInterestPolicy.DefaultPerPeerCreatures, cadence), 9);

            Assert.Equal(0.0, IslandFaunaInterestPolicy.WorstCaseUpdatesPerSecond(0, cadence));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                IslandFaunaInterestPolicy.WorstCaseUpdatesPerSecond(24, TimeSpan.Zero));
        }

        // --- Admission

        [Fact]
        public void An_island_under_the_players_feet_is_always_admitted()
        {
            IReadOnlyList<IslandId> admitted = Admit(
                Held(), Candidate("home", 0.0, 10), Candidate("far", 9000.0, 10));

            Assert.Equal(new[] { new IslandId("home") }, admitted);
        }

        [Fact]
        public void Standing_still_produces_the_same_answer_forever()
        {
            // The bug in one sentence: the old code sampled creature positions, so the
            // answer changed while the player did not move. This is the assertion that
            // says that can no longer happen - there is no time input at all.
            FaunaIslandCandidate[] world =
            {
                Candidate("a", 10.0, 10), Candidate("b", 700.0, 10), Candidate("c", 300.0, 4),
            };

            HashSet<IslandId> held = new HashSet<IslandId>();
            IReadOnlyList<IslandId> first = Admit(held, world);
            foreach (IslandId id in first) held.Add(id);

            for (int turn = 0; turn < 50; turn++)
            {
                IReadOnlyList<IslandId> again = Admit(held, world);
                Assert.Equal(first, again);
            }
        }

        [Fact]
        public void A_held_island_is_retained_out_to_the_unload_radius_and_dropped_past_it()
        {
            IslandId id = new IslandId("drifting");

            // Between load and unload: an unheld island is not taken, a held one is kept.
            double between = (Load + Unload) / 2.0;
            Assert.Empty(Admit(Held(), Candidate(id.Value, between, 10)));
            Assert.Equal(new[] { id }, Admit(Held(id), Candidate(id.Value, between, 10)));

            // Past unload: dropped even though it was held. Otherwise a player who flew
            // away would keep an island's wildlife for the rest of the session.
            Assert.Empty(Admit(Held(id), Candidate(id.Value, Unload + 1.0, 10)));
        }

        [Fact]
        public void A_newly_approached_island_never_evicts_the_one_already_held()
        {
            // Without retention priority, flying toward a nearer island would unload
            // the wildlife on the island under your feet - the loudest possible version
            // of the reported bug.
            IslandId standing = new IslandId("standing-on");
            IReadOnlyList<IslandId> admitted = Admit(
                Held(standing),
                budget: 10,
                Candidate("standing-on", 400.0, 10),
                Candidate("approaching", 5.0, 10));

            Assert.Equal(new[] { standing }, admitted);
        }

        [Fact]
        public void Islands_are_admitted_whole_so_a_school_is_never_half_streamed()
        {
            // A budget of 12 fits one ten-creature island and cannot fit a second, so
            // the second is skipped entirely rather than contributing two members.
            IReadOnlyList<IslandId> admitted = Admit(
                Held(), budget: 12,
                Candidate("near", 10.0, 10),
                Candidate("next", 20.0, 10));

            Assert.Equal(new[] { new IslandId("near") }, admitted);
        }

        [Fact]
        public void A_smaller_population_may_still_fit_after_a_larger_one_is_skipped()
        {
            IReadOnlyList<IslandId> admitted = Admit(
                Held(), budget: 14,
                Candidate("near", 10.0, 10),
                Candidate("big", 20.0, 19),
                Candidate("small", 30.0, 4));

            Assert.Equal(new[] { new IslandId("near"), new IslandId("small") }, admitted);
        }

        [Fact]
        public void The_admitted_population_never_exceeds_the_per_peer_budget()
        {
            // The safety property, checked over a lot of shapes rather than one.
            FaunaIslandCandidate[] world = Enumerable.Range(0, 40)
                .Select(i => Candidate("island-" + i, i * 5.0, 4 + (i % 16)))
                .ToArray();

            foreach (int budget in new[] { 1, 4, 10, 24, 60, 500 })
            {
                IReadOnlyList<IslandId> admitted = IslandFaunaInterestPolicy.Admit(
                    world, Held(), Load, Unload, budget);
                int spent = admitted
                    .Select(id => world.First(c => c.IslandId == id).Population).Sum();
                Assert.True(spent <= budget,
                    "admitted " + spent + " creature(s) against a budget of " + budget);
            }
        }

        [Fact]
        public void A_zero_budget_or_a_zero_radius_admits_nothing()
        {
            Assert.Empty(IslandFaunaInterestPolicy.Admit(
                new[] { Candidate("home", 0.0, 10) }, Held(), Load, Unload, 0));
            Assert.Empty(IslandFaunaInterestPolicy.Admit(
                new[] { Candidate("home", 0.0, 10) }, Held(), 0.0, 0.0, 24));
        }

        [Fact]
        public void An_empty_island_is_never_admitted_and_never_spends_budget()
        {
            IReadOnlyList<IslandId> admitted = Admit(
                Held(), budget: 10,
                Candidate("empty", 1.0, 0),
                Candidate("real", 2.0, 10));

            Assert.Equal(new[] { new IslandId("real") }, admitted);
        }

        [Fact]
        public void Admission_does_not_depend_on_the_order_candidates_arrive_in()
        {
            FaunaIslandCandidate[] world =
            {
                Candidate("a", 100.0, 6), Candidate("b", 50.0, 6),
                Candidate("c", 50.0, 6), Candidate("d", 10.0, 6),
            };

            IReadOnlyList<IslandId> expected = IslandFaunaInterestPolicy.Admit(
                world, Held(), Load, Unload, 18);

            Assert.Equal(expected, IslandFaunaInterestPolicy.Admit(
                world.Reverse(), Held(), Load, Unload, 18));
            Assert.Equal(expected, IslandFaunaInterestPolicy.Admit(
                new[] { world[2], world[0], world[3], world[1] }, Held(), Load, Unload, 18));
        }

        [Fact]
        public void Null_arguments_are_programming_errors_not_empty_worlds()
        {
            Assert.Throws<ArgumentNullException>(() =>
                IslandFaunaInterestPolicy.Admit(null!, Held(), Load, Unload, 24));
            Assert.Throws<ArgumentNullException>(() =>
                IslandFaunaInterestPolicy.Admit(Array.Empty<FaunaIslandCandidate>(),
                    null!, Load, Unload, 24));
            Assert.Throws<ArgumentNullException>(() =>
                IslandFaunaInterestPolicy.Reconcile(null!, new HashSet<long>()));
            Assert.Throws<ArgumentNullException>(() =>
                IslandFaunaInterestPolicy.Reconcile(Array.Empty<long>(), null!));
        }

        // --- Turning an admission set into wire work

        [Fact]
        public void Reconcile_removes_before_it_adds()
        {
            HashSet<long> loaded = new HashSet<long> { 10L, 11L };
            IReadOnlyList<ResourceStreamAction> actions =
                IslandFaunaInterestPolicy.Reconcile(new[] { 11L, 20L, 21L }, loaded);

            Assert.Equal(ResourceStreamActionKind.Remove, actions[0].Kind);
            Assert.Equal(10L, actions[0].EntityId);
            Assert.Equal(new[] { 20L, 21L },
                actions.Where(a => a.Kind == ResourceStreamActionKind.Add)
                    .Select(a => a.EntityId).ToArray());
        }

        [Fact]
        public void Reconcile_asks_for_nothing_when_the_peer_already_holds_exactly_the_right_set()
        {
            HashSet<long> loaded = new HashSet<long> { 1L, 2L, 3L };
            Assert.Empty(IslandFaunaInterestPolicy.Reconcile(new[] { 3L, 1L, 2L }, loaded));
        }

        [Fact]
        public void A_school_arrives_as_a_contiguous_run_rather_than_interleaved()
        {
            // PopulationFor gives a school consecutive ids, and ordering additions by
            // id is what turns that into "the school shows up together".
            IReadOnlyList<ResourceStreamAction> actions = IslandFaunaInterestPolicy.Reconcile(
                new[] { 205L, 101L, 203L, 100L, 204L, 102L }, new HashSet<long>());

            Assert.Equal(new[] { 100L, 101L, 102L, 203L, 204L, 205L },
                actions.Select(a => a.EntityId).ToArray());
        }

        private static IReadOnlyList<IslandId> Admit(
            ISet<IslandId> held, params FaunaIslandCandidate[] candidates) =>
            IslandFaunaInterestPolicy.Admit(candidates, held, Load, Unload,
                IslandFaunaInterestPolicy.DefaultPerPeerCreatures);

        private static IReadOnlyList<IslandId> Admit(
            ISet<IslandId> held, int budget, params FaunaIslandCandidate[] candidates) =>
            IslandFaunaInterestPolicy.Admit(candidates, held, Load, Unload, budget);

        private static HashSet<IslandId> Held(params IslandId[] ids) => new HashSet<IslandId>(ids);

        /// <summary>A candidate stated in METRES, squared here so the tests read in metres.</summary>
        private static FaunaIslandCandidate Candidate(string id, double metres, int population) =>
            new FaunaIslandCandidate(new IslandId(id), metres * metres, population);
    }
}

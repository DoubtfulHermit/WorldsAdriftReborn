using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// The island-keyed resource checkout that replaced the 120 m player-centred
    /// bubble. The bug it fixes is a player standing ON an island holding 2 of its
    /// 19 nodes, so the tests that matter most are the ones about a peer that has
    /// not moved and an island that is wider than the old radius.
    /// </summary>
    public class IslandResourceCheckoutPolicyTests
    {
        private static readonly IslandId A = new("island-a");
        private static readonly IslandId B = new("island-b");
        private static readonly IslandId C = new("island-c");

        [Fact]
        public void Radius_defaults_match_fauna_so_the_two_features_load_an_island_together()
        {
            Assert.Equal(IslandFaunaInterestPolicy.DefaultLoadRadiusMetres,
                IslandResourceCheckoutPolicy.DefaultLoadRadiusMetres);
            Assert.Equal(IslandFaunaInterestPolicy.UnloadMarginMetres,
                IslandResourceCheckoutPolicy.UnloadMarginMetres);
            Assert.Equal(800.0, IslandResourceCheckoutPolicy.UnloadRadiusFor(
                IslandResourceCheckoutPolicy.DefaultLoadRadiusMetres));
        }

        [Theory]
        [InlineData(null, 600.0)]
        [InlineData("", 600.0)]
        [InlineData("not-a-number", 600.0)]
        [InlineData("250", 250.0)]
        [InlineData("250.5", 250.5)]
        [InlineData("-1", 0.0)]
        [InlineData("0", 0.0)]
        public void An_environment_typo_falls_back_instead_of_stopping_a_boot(
            string? raw, double expected)
        {
            Assert.Equal(expected, IslandResourceCheckoutPolicy.LoadRadiusFrom(raw));
        }

        [Fact]
        public void A_zero_radius_is_the_kill_switch_and_stays_killed_through_unload()
        {
            Assert.Equal(0.0, IslandResourceCheckoutPolicy.UnloadRadiusFor(0.0));
            Assert.Empty(IslandResourceCheckoutPolicy.Admit(
                new[] { new IslandInterestCandidate(A, 0.0, 5) },
                new HashSet<IslandId>(), loadRadius: 0.0, unloadRadius: 0.0,
                perPeerBudget: 512));
        }

        [Theory]
        [InlineData(null, 512)]
        [InlineData("junk", 512)]
        [InlineData("-3", 512)]
        [InlineData("0", 0)]
        [InlineData("64", 64)]
        public void A_budget_from_the_environment_parses_or_falls_back(string? raw, int expected)
        {
            Assert.Equal(expected, IslandResourceCheckoutPolicy.PerPeerBudgetFrom(raw));
        }

        [Fact]
        public void The_default_budget_clears_the_measured_worst_case_pair()
        {
            // Over all 254 release islands the worst simultaneously-holdable pair is
            // Crimson Paradise (88) + The Land that Man Forgot (82) = 170, measured
            // from the catalogue AABBs at the 800 m unload radius. The default is a
            // ceiling with room for the world to grow, not a target.
            Assert.True(IslandResourceCheckoutPolicy.DefaultPerPeerResources >= 170 * 2,
                "the per-peer ceiling must clear the measured worst case with headroom");
        }

        [Fact]
        public void An_island_under_a_standing_peer_is_held_whole()
        {
            HashSet<IslandId> held = new();
            IReadOnlyList<IslandId> admitted = IslandResourceCheckoutPolicy.Admit(
                new[] { new IslandInterestCandidate(A, 0.0, 88) },
                held, 600.0, 800.0, 512);

            Assert.Equal(new[] { A }, admitted);
        }

        [Fact]
        public void A_held_island_survives_out_to_the_unload_radius_and_no_further()
        {
            HashSet<IslandId> held = new() { A };

            Assert.Equal(new[] { A }, IslandResourceCheckoutPolicy.Admit(
                new[] { new IslandInterestCandidate(A, 700.0 * 700.0, 4) },
                held, 600.0, 800.0, 512));
            Assert.Empty(IslandResourceCheckoutPolicy.Admit(
                new[] { new IslandInterestCandidate(A, 801.0 * 801.0, 4) },
                held, 600.0, 800.0, 512));
        }

        [Fact]
        public void An_unheld_island_beyond_the_load_radius_is_not_admitted()
        {
            Assert.Empty(IslandResourceCheckoutPolicy.Admit(
                new[] { new IslandInterestCandidate(A, 601.0 * 601.0, 4) },
                new HashSet<IslandId>(), 600.0, 800.0, 512));
        }

        /// <summary>
        /// The loudest possible version of the original bug: a newly approached
        /// island evicting the one under the player's feet.
        /// </summary>
        [Fact]
        public void A_newcomer_never_evicts_the_island_the_peer_is_standing_on()
        {
            HashSet<IslandId> held = new() { A };
            IReadOnlyList<IslandId> admitted = IslandResourceCheckoutPolicy.Admit(
                new[]
                {
                    new IslandInterestCandidate(B, 10.0, 6),   // nearer, but new
                    new IslandInterestCandidate(A, 500.0 * 500.0, 6),
                },
                held, 600.0, 800.0, perPeerBudget: 6);

            Assert.Equal(new[] { A }, admitted);
        }

        [Fact]
        public void An_island_that_does_not_fit_is_skipped_whole_and_a_smaller_one_still_fits()
        {
            IReadOnlyList<IslandId> admitted = IslandResourceCheckoutPolicy.Admit(
                new[]
                {
                    new IslandInterestCandidate(A, 1.0, 100),
                    new IslandInterestCandidate(B, 2.0, 9),
                },
                new HashSet<IslandId>(), 600.0, 800.0, perPeerBudget: 10);

            Assert.Equal(new[] { B }, admitted);
        }

        [Fact]
        public void Admission_is_order_independent_and_ties_break_on_island_id()
        {
            IslandInterestCandidate[] candidates =
            {
                new(C, 4.0, 1),
                new(A, 4.0, 1),
                new(B, 4.0, 1),
            };

            Assert.Equal(new[] { A, B, C }, IslandResourceCheckoutPolicy.Admit(
                candidates, new HashSet<IslandId>(), 600.0, 800.0, 512));
            Assert.Equal(new[] { A, B, C }, IslandResourceCheckoutPolicy.Admit(
                candidates.Reverse(), new HashSet<IslandId>(), 600.0, 800.0, 512));
        }

        [Fact]
        public void Desire_marks_only_admitted_islands_and_keeps_every_offered_entity()
        {
            IslandResource[] resources =
            {
                new(1, FixedPointPosition.FromMetres(0, 0, 0), A),
                new(2, FixedPointPosition.FromMetres(900, 0, 0), A),
                new(3, FixedPointPosition.FromMetres(5000, 0, 0), B),
            };

            IReadOnlyList<(long Id, FixedPointPosition Position, bool Desired)> desired =
                IslandResourceCheckoutPolicy.Desire(resources, new HashSet<IslandId> { A });

            Assert.Equal(3, desired.Count);
            Assert.Equal(new[] { 1L, 2L }, desired.Where(x => x.Desired).Select(x => x.Id));
            Assert.Equal(new[] { 3L }, desired.Where(x => !x.Desired).Select(x => x.Id));
        }

        /// <summary>
        /// An island bigger than the budget is admitted to NOBODY, which looks
        /// exactly like the bug this policy fixes. It has to be loud at boot.
        /// </summary>
        [Fact]
        public void An_island_larger_than_the_budget_is_reported_by_name()
        {
            string? warning = IslandResourceCheckoutPolicy.BudgetWarning(
                new[] { (A, 40), (B, 600) }, perPeerBudget: 512);

            Assert.NotNull(warning);
            Assert.Contains("island-b (600)", warning);
            Assert.DoesNotContain("island-a", warning);
            Assert.Contains(IslandResourceCheckoutPolicy.PerPeerBudgetEnvVar, warning);
        }

        [Fact]
        public void A_world_that_fits_reports_nothing()
        {
            Assert.Null(IslandResourceCheckoutPolicy.BudgetWarning(
                new[] { (A, 102), (B, 88) }, perPeerBudget: 512));
            Assert.Null(IslandResourceCheckoutPolicy.BudgetWarning(
                new[] { (A, 102) }, perPeerBudget: 0));
        }
    }
}

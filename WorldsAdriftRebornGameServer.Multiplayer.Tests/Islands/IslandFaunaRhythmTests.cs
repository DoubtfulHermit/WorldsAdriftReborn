using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// The rhythm decides how many animals EXIST from one moment to the next,
    /// so its failure modes are wire failure modes: a kink in the expression
    /// curve is a burst of AddEntity, a fraction outside [trough, 1] is an
    /// island flickering empty, and any nondeterminism is two peers being told
    /// two different worlds. Each of those is what gets tested, plus the two
    /// properties that make it an ECOLOGY rather than a dimmer switch: the five
    /// phases genuinely occur, and the predator genuinely lags its prey.
    /// </summary>
    public sealed class IslandFaunaRhythmTests
    {
        private const int Seed = 1;
        private static readonly IslandId Island = new IslandId("beautiful-wildlands");

        [Fact]
        public void The_rhythm_is_deterministic()
        {
            foreach (double t in new[] { 0.0, 61.5, 3600.0, 86_400.0, 2_592_000.0 })
            {
                Assert.Equal(
                    IslandFaunaRhythm.At(Seed, Island, t),
                    IslandFaunaRhythm.At(Seed, Island, t));
                Assert.Equal(
                    IslandFaunaRhythm.PreyExpressionAt(Seed, Island, t),
                    IslandFaunaRhythm.PreyExpressionAt(Seed, Island, t));
            }
        }

        [Fact]
        public void Phases_advance_in_order_with_fractions_inside_one_phase()
        {
            // Seeded from the FIRST observation, not from Dormant: an island no
            // longer starts its walk at the beginning of the cycle (each has its
            // own start offset - see the desync regression test below).
            FaunaPopulationPhase previous = IslandFaunaRhythm.At(Seed, Island, 0.0).Phase;
            int cycles = 0;
            for (double t = 0.0; t < 7200.0; t += 5.0)
            {
                (FaunaPopulationPhase phase, double fraction, int cycle) =
                    IslandFaunaRhythm.At(Seed, Island, t);
                Assert.InRange(fraction, 0.0, 1.0);
                if (phase != previous)
                {
                    // Either the next phase in the enum, or the wrap back to
                    // Dormant when a new cycle begins.
                    FaunaPopulationPhase expected = previous == FaunaPopulationPhase.Recovery
                        ? FaunaPopulationPhase.Dormant
                        : (FaunaPopulationPhase)((int)previous + 1);
                    Assert.Equal(expected, phase);
                    previous = phase;
                }
                cycles = cycle;
            }
            Assert.True(cycles >= 2, "two hours should span several ~24-minute cycles");
        }

        [Fact]
        public void Phase_durations_stay_inside_the_documented_swing()
        {
            for (int cycle = 0; cycle < 20; cycle++)
            {
                for (int phase = 0; phase < IslandFaunaRhythm.BasePhaseSeconds.Count; phase++)
                {
                    double duration = IslandFaunaRhythm.PhaseDuration(Seed, Island, cycle, phase);
                    double baseSeconds = IslandFaunaRhythm.BasePhaseSeconds[phase];
                    Assert.InRange(duration, baseSeconds * 0.7, baseSeconds * 1.3);
                }
            }
        }

        [Fact]
        public void Expression_is_continuous_bounded_and_visits_the_whole_range()
        {
            // The kink test: at half-second samples the steepest legal slope is
            // the Collapse ramp over its shortest duration - about 0.011 per
            // second - so any step over 0.01 is a discontinuity, and on the wire
            // a discontinuity is a burst of arrivals.
            double previous = IslandFaunaRhythm.PreyExpressionAt(Seed, Island, 0.0);
            double lowest = previous, highest = previous;
            for (double t = 0.5; t < 7200.0; t += 0.5)
            {
                double expression = IslandFaunaRhythm.PreyExpressionAt(Seed, Island, t);
                Assert.InRange(expression, IslandFaunaRhythm.TroughLevel, 1.0);
                Assert.True(Math.Abs(expression - previous) <= 0.01,
                    "expression stepped by " + Math.Abs(expression - previous) + " at t=" + t);
                lowest = Math.Min(lowest, expression);
                highest = Math.Max(highest, expression);
                previous = expression;
            }
            Assert.Equal(IslandFaunaRhythm.TroughLevel, lowest, 3);
            Assert.Equal(1.0, highest, 3);
        }

        [Fact]
        public void The_predator_is_exactly_its_preys_past()
        {
            double lag = IslandFaunaRhythm.PredatorLagSeconds(Seed, Island);
            Assert.InRange(lag, 120.0, 360.0);
            foreach (double t in new[] { 500.0, 1234.5, 7200.0 })
            {
                Assert.Equal(
                    IslandFaunaRhythm.PreyExpressionAt(Seed, Island, t - lag),
                    IslandFaunaRhythm.PredatorExpressionAt(Seed, Island, t));
            }
        }

        [Fact]
        public void Islands_swing_out_of_step_with_each_other()
        {
            // Synchronised islands would read as a world on one dimmer. Two real
            // islands must disagree about their phase somewhere in one cycle.
            IslandId other = new IslandId("roxborough-isle");
            bool differed = false;
            for (double t = 0.0; t < 2400.0 && !differed; t += 30.0)
            {
                differed = IslandFaunaRhythm.At(Seed, Island, t).Phase
                    != IslandFaunaRhythm.At(Seed, other, t).Phase;
            }
            Assert.True(differed, "two islands never disagreed about their phase");
        }

        /// <summary>
        /// THE REGRESSION TEST FOR THE LIVE BUG OF 2026-08-18, and the one that
        /// would have caught it before a player did.
        ///
        /// The walk's phase LENGTHS were per-island but its STARTING POINT was
        /// not, so every island began in Dormant at t=0 and the whole world sat
        /// at its emptiest together for the first minutes of every boot -
        /// "2 rays and 2 jellyfish on all islands". Pairwise disagreement (the
        /// test above) does not catch that: it passes as soon as the durations
        /// have pulled two islands apart, which takes about ten minutes.
        ///
        /// So this asserts the property that actually matters - AT EVERY
        /// SAMPLED INSTANT, INCLUDING BOOT, THE WORLD IS SPREAD ACROSS THE
        /// STATE MACHINE - and it samples t=0 first, because that is the moment
        /// the old code was uniform and the moment a player arrives after a
        /// restart.
        /// </summary>
        [Fact]
        public void The_whole_world_is_never_in_one_phase_together_least_of_all_at_boot()
        {
            IReadOnlyList<ReleaseIslandRecord> world = ReleaseWorldCatalog.All
                .Where(record => record.Survey.Tier == 1).ToList();
            Assert.True(world.Count >= 40, "the tier-1 world should be dozens of islands");

            foreach (double t in new[] { 0.0, 1.0, 60.0, 300.0, 600.0, 1200.0, 3600.0, 86_400.0 })
            {
                foreach (FaunaSpecies species in Enum.GetValues<FaunaSpecies>())
                {
                    Dictionary<FaunaPopulationPhase, int> spread =
                        new Dictionary<FaunaPopulationPhase, int>();
                    foreach (ReleaseIslandRecord island in world)
                    {
                        FaunaPopulationPhase phase = IslandFaunaRhythm
                            .PhaseFor(Seed, island.Definition.Id, species, t).Phase;
                        spread.TryGetValue(phase, out int count);
                        spread[phase] = count + 1;
                    }

                    // All five phases present, and no single phase holding more
                    // than 60% of the world: either failure is the world moving
                    // as one body, which is what the player saw. 60% rather than
                    // 50% because Bloom is deliberately the dominant state
                    // (about 39% of a cycle by duration), so an honest world
                    // clusters there - what must never recur is the 100% the
                    // bug produced.
                    Assert.True(spread.Count == 5,
                        species + " at t=" + t + ": the world occupied only "
                        + spread.Count + " of the five phases ("
                        + string.Join(", ", spread.Select(p => p.Key + ":" + p.Value)) + ")");
                    Assert.True(spread.Values.Max() <= world.Count * 0.6,
                        species + " at t=" + t + ": "
                        + spread.OrderByDescending(p => p.Value).First()
                        + " holds more than 60% of the world");
                }
            }
        }

        [Fact]
        public void An_islands_start_offset_is_its_own_and_covers_the_cycle()
        {
            // The offsets must SPREAD, not merely differ: offsets bunched into
            // one corner of the cycle would resynchronise the world.
            List<double> offsets = ReleaseWorldCatalog.All
                .Where(record => record.Survey.Tier == 1)
                .Select(record =>
                    IslandFaunaRhythm.StartOffsetSeconds(Seed, record.Definition.Id))
                .ToList();

            foreach (double offset in offsets)
            {
                Assert.InRange(offset, 0.0, IslandFaunaRhythm.NominalCycleSeconds);
            }
            // Every fifth of the cycle is occupied by somebody.
            for (int fifth = 0; fifth < 5; fifth++)
            {
                double low = IslandFaunaRhythm.NominalCycleSeconds * fifth / 5.0;
                double high = IslandFaunaRhythm.NominalCycleSeconds * (fifth + 1) / 5.0;
                Assert.True(offsets.Any(o => o >= low && o < high),
                    "no island starts in the " + fifth + "th fifth of the cycle");
            }
        }

        /// <summary>
        /// The world must not be systematically emptier than the flat
        /// pre-ecology population it replaced - the second half of the live
        /// regression. Capacity is a CEILING the rhythm expresses a fraction of,
        /// so the density scale has to carry the average island back over the
        /// old flat count. Asserted against the REAL catalogue at sampled
        /// instants, in the same units a player experiences: creatures per
        /// populated island.
        /// </summary>
        [Fact]
        public void The_average_populated_island_is_at_least_as_inhabited_as_the_flat_world_was()
        {
            const int FlatPreEcologyPopulation = 10; // 4 mantas + 6 jellies, every tier-1 island
            IReadOnlyList<ReleaseIslandRecord> world = ReleaseWorldCatalog.All
                .Where(record => record.Survey.Tier == 1).ToList();

            List<double> perIslandAtEachInstant = new List<double>();
            foreach (double t in new[] { 0.0, 60.0, 600.0, 1200.0, 3600.0, 7200.0, 86_400.0 })
            {
                int live = 0, populated = 0;
                foreach (ReleaseIslandRecord island in world)
                {
                    IslandId id = island.Definition.Id;
                    (int capM, int capJ) = IslandFaunaCapacity.ClampedToPeerBudget(
                        IslandFaunaCapacity.CapacityFor(
                            FaunaSpecies.MantaRay, island.Survey.Tier, island.Envelope, id),
                        IslandFaunaCapacity.CapacityFor(
                            FaunaSpecies.JellyFish, island.Survey.Tier, island.Envelope, id),
                        IslandFaunaInterestPolicy.DefaultPerPeerCreatures);
                    if (capM + capJ == 0) continue;
                    populated++;
                    live += IslandFaunaRhythm.ExpressedCount(capM,
                                IslandFaunaRhythm.ExpressionAt(Seed, id, FaunaSpecies.MantaRay, t))
                          + IslandFaunaRhythm.ExpressedCount(capJ,
                                IslandFaunaRhythm.ExpressionAt(Seed, id, FaunaSpecies.JellyFish, t));
                }

                double perIsland = (double)live / populated;
                perIslandAtEachInstant.Add(perIsland);

                // NO INSTANT MAY COLLAPSE. The bug put the world at 4.0 per
                // island; a breathing world may dip somewhat below the old flat
                // count at a given moment - that is the rhythm doing its job -
                // but never to a fraction of it.
                Assert.True(perIsland >= FlatPreEcologyPopulation * 0.8,
                    "at t=" + t + " the average populated island carried only "
                    + perIsland.ToString("0.0") + " creatures against the flat world's "
                    + FlatPreEcologyPopulation);
            }

            // AND THE WORLD IS NOT SYSTEMATICALLY EMPTIER THAN THE ONE IT
            // REPLACED: across the sampled instants the average island carries
            // at least the flat population. This is the assertion the density
            // scale exists to satisfy.
            double mean = perIslandAtEachInstant.Average();
            Assert.True(mean >= FlatPreEcologyPopulation,
                "the average populated island carries " + mean.ToString("0.0")
                + " creatures over time, against the flat world's " + FlatPreEcologyPopulation);
        }

        [Fact]
        public void Negative_time_is_the_start_of_the_world_not_an_exception()
        {
            // The predator lag asks about t < 0 during the first minutes of a
            // boot. The answer must be DEFINED and identical to t=0 - which is
            // the island's own start offset into the cycle, not necessarily
            // Dormant, since every island now begins somewhere different.
            Assert.Equal(
                IslandFaunaRhythm.At(Seed, Island, 0.0),
                IslandFaunaRhythm.At(Seed, Island, -500.0));
            Assert.InRange(IslandFaunaRhythm.At(Seed, Island, -500.0).PhaseFraction, 0.0, 1.0);
        }

        [Theory]
        [InlineData(0, 1.0, 0)]    // an empty island stays empty at any fraction
        [InlineData(1, 0.15, 1)]   // capacity one floors at one, not two
        [InlineData(4, 0.05, 2)]   // the two-animal floor: never a lone animal
        [InlineData(4, 1.0, 4)]    // full bloom is full capacity
        [InlineData(12, 0.6, 7)]   // plain rounding in between
        [InlineData(12, 5.0, 12)]  // a hostile fraction cannot exceed capacity
        // THE PROPORTIONAL FLOOR (the live-regression fix): a big island's worst
        // day is still a group. 12 x TroughLevel = 4, so a starved fraction
        // floors at four rather than at the flat two that read as broken.
        [InlineData(12, 0.05, 4)]
        [InlineData(20, 0.0, 6)]
        public void Expressed_counts_floor_proportionally_and_cap_at_capacity(
            int capacity, double fraction, int expected) =>
            Assert.Equal(expected, IslandFaunaRhythm.ExpressedCount(capacity, fraction));

        [Fact]
        public void The_phase_report_shows_the_predators_lagged_truth()
        {
            double lag = IslandFaunaRhythm.PredatorLagSeconds(Seed, Island);
            double t = 1000.0;
            Assert.Equal(
                IslandFaunaRhythm.At(Seed, Island, t - lag).Phase,
                IslandFaunaRhythm.PhaseFor(Seed, Island, FaunaSpecies.MantaRay, t).Phase);
            Assert.Equal(
                IslandFaunaRhythm.At(Seed, Island, t).Phase,
                IslandFaunaRhythm.PhaseFor(Seed, Island, FaunaSpecies.JellyFish, t).Phase);
        }
    }
}

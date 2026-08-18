using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// THE ADULT-SAFETY SUITE, and it is deliberately the first thing in this
    /// feature that exists.
    ///
    /// Component 1166 <c>AgeState</c> is not opt-in per creature. The shipped
    /// <c>MantaRay_unityclient</c> prefab carries <c>AgeVisualizer</c>, whose
    /// only <c>[Require]</c> is an <c>AgeStateReader</c>, and it is inert today
    /// solely because nobody answers 1166. The moment a serializer branch exists,
    /// that visualiser activates on EVERY manta it is served to and assigns
    /// <c>localScale = Vector3.one * Lerp(0.25, 1.0, secondsOld/secondsTillFullyGrown)</c>
    /// unconditionally. There is no "leave it alone" value, so an adult must be
    /// sent an explicit fully-grown age or THE WHOLE WORLD'S MANTAS SHRINK TO A
    /// QUARTER AT ONCE.
    ///
    /// Every test below therefore asserts on <see cref="FaunaAgeState.RenderedScale"/>
    /// - the client's own arithmetic, restated where a test can reach it - rather
    /// than on the seconds we happened to send. What matters is not that the
    /// numbers look right; it is that the animal draws at full size.
    /// </summary>
    public sealed class IslandFaunaAgeTests
    {
        private const int Seed = 1;
        private static readonly IslandId Island = new IslandId("beautiful-wildlands");

        // ---- HAZARD 0: the adult case, asserted before any calf exists --------

        [Fact]
        public void An_adult_renders_at_exactly_full_size()
        {
            Assert.Equal(1.0, IslandFaunaAge.Adult.FullyGrownRatio);
            Assert.Equal(IslandFaunaAge.RecoveredFullyGrownScale,
                IslandFaunaAge.Adult.RenderedScale);
        }

        [Fact]
        public void The_adult_state_is_explicitly_at_or_past_maturity_on_the_wire()
        {
            // Not "large enough" - the client divides, so the relationship is what
            // has to hold, and it has to hold in the numbers we actually send.
            Assert.True(
                IslandFaunaAge.Adult.SecondsOld >= IslandFaunaAge.Adult.SecondsTillFullyGrown,
                "an adult must be sent secondsOld >= secondsTillFullyGrown or it renders small");
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-1.0)]
        [InlineData(-1e18)]
        [InlineData(1e18)]
        [InlineData(0.0 / 1.0 - 0.0)]
        public void Every_degenerate_age_renders_full_size_rather_than_wrong(double age)
        {
            FaunaAgeState state = IslandFaunaAge.For(age);
            Assert.False(double.IsNaN(state.RenderedScale),
                "a NaN localScale is the one failure that does not name itself");
            // Zero is a legitimate newborn, so it is the one input here that may
            // be small; everything else must be an adult.
            if (age != 0.0)
            {
                Assert.Equal(IslandFaunaAge.RecoveredFullyGrownScale, state.RenderedScale);
            }
        }

        [Fact]
        public void The_growth_denominator_is_never_zero()
        {
            // n/0 with n > 0 is +Inf, which clamps to adult and is merely wrong;
            // 0/0 is NaN, which is not. Neither may be reachable.
            Assert.True(IslandFaunaAge.Adult.SecondsTillFullyGrown > 0);
            Assert.True(IslandFaunaAge.SecondsTillFullyGrown > 0);
            foreach (double age in new[] { 0.0, 1.0, 500.0, 1e9, double.NaN, -5.0 })
            {
                Assert.True(IslandFaunaAge.For(age).SecondsTillFullyGrown > 0);
            }
        }

        [Fact]
        public void The_inert_denominators_are_non_zero_too()
        {
            // RayAging divides by secondsTillNaturalDeath and MovementController
            // divides by maxMass. Neither runs on anything we serve today, and
            // both would produce a NaN if it ever did.
            Assert.True(IslandFaunaAge.Adult.SecondsTillNaturalDeath > 0);
            Assert.True(IslandFaunaAge.Adult.MaxMassKilograms > 0f);
            Assert.True(IslandFaunaAge.Adult.MinMassKilograms > 0f);
            Assert.True(IslandFaunaAge.Adult.MinMassKilograms
                < IslandFaunaAge.Adult.MaxMassKilograms);
        }

        [Fact]
        public void Every_manta_that_is_not_a_calf_renders_full_size_on_every_release_island()
        {
            // THE WORLD-WIDE CLAIM, swept rather than reasoned about, and against
            // the REAL catalogue rather than a fixture. This is the test that has
            // to hold for the feature to be safe to switch on at all: answering
            // 1166 activates AgeVisualizer on every manta in the world, so if the
            // policy ever returned anything but an adult for an ordinary animal,
            // every school on every island would be a quarter of its size.
            int slotsChecked = 0;
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                int capacity = IslandFaunaCapacity.CapacityFor(
                    FaunaSpecies.MantaRay, island.Survey.Tier, island.Envelope,
                    island.Definition.Id);
                for (int rank = 0; rank < capacity; rank++)
                {
                    foreach (double t in new[] { 0.0, 137.0, 900.0, 1531.0, 4000.0, 86_400.0 })
                    {
                        FaunaAgeState state = IslandFaunaAge.StateFor(
                            Seed, island.Definition.Id, FaunaSpecies.MantaRay,
                            capacity, rank, isCalfSlot: false, t);
                        Assert.Equal(IslandFaunaAge.RecoveredFullyGrownScale, state.RenderedScale);
                        slotsChecked++;
                    }
                }
            }
            Assert.True(slotsChecked > 1000,
                "the sweep only reached " + slotsChecked + " manta slots");
        }

        [Fact]
        public void A_jelly_is_always_an_adult_because_it_has_no_scale_path_at_all()
        {
            // No AgeVisualizer, no ScalableObjectVisualiser, no size field
            // anywhere in the basic-creature stack: 1166 on a jelly has no
            // consumer. The service never serves it one; if that guard were ever
            // removed, this makes sure the value it would get is harmless.
            for (double t = 0.0; t < 4000.0; t += 211.0)
            {
                Assert.Equal(IslandFaunaAge.RecoveredFullyGrownScale,
                    IslandFaunaAge.StateFor(Seed, Island, FaunaSpecies.JellyFish,
                        12, 9, isCalfSlot: true, t).RenderedScale);
            }
        }

        [Fact]
        public void Every_manta_slot_inside_the_population_floor_is_an_adult()
        {
            // The resident core never leaves, so it has no birth instant, so the
            // policy must answer "adult" for it - at every capacity the world
            // actually produces and across a whole cycle.
            for (int capacity = 1; capacity <= 24; capacity++)
            {
                int floor = IslandFaunaAge.FloorOf(capacity);
                for (int rank = 0; rank < floor; rank++)
                {
                    for (double t = 0.0; t < 3200.0; t += 37.0)
                    {
                        Assert.Null(IslandFaunaAge.AgeSeconds(
                            Seed, Island, FaunaSpecies.MantaRay, capacity, rank, t));
                    }
                }
            }
        }

        [Fact]
        public void The_floor_is_the_rhythms_own_floor_and_not_a_second_derivation()
        {
            // Proposal A's first risk: a birth-time inversion that assumes a flat
            // floor of two invents births for animals that never left. Pin our
            // reading of the floor to ExpressedCount's own behaviour rather than
            // to the formula, so a Phase 3 retune cannot silently split them.
            for (int capacity = 1; capacity <= 40; capacity++)
            {
                int floor = IslandFaunaAge.FloorOf(capacity);
                Assert.Equal(floor, IslandFaunaRhythm.ExpressedCount(capacity, 0.0));
                Assert.Equal(floor, IslandFaunaRhythm.ExpressedCount(capacity, -5.0));
            }
        }

        // ---- the inverse itself ----------------------------------------------

        [Fact]
        public void The_smoothstep_inverse_is_exact()
        {
            for (int i = 0; i <= 10_000; i++)
            {
                double f = i / 10_000.0;
                double y = f * f * (3.0 - (2.0 * f));
                Assert.True(Math.Abs(IslandFaunaAge.InverseSmoothStep(y) - f) < 1e-9,
                    "S^-1(S(" + f + ")) was " + IslandFaunaAge.InverseSmoothStep(y));
            }
        }

        [Fact]
        public void The_smoothstep_inverse_is_total()
        {
            foreach (double y in new[] { double.NaN, -1.0, 0.0, 1.0, 2.0, 1e300 })
            {
                double f = IslandFaunaAge.InverseSmoothStep(y);
                Assert.False(double.IsNaN(f));
                Assert.InRange(f, 0.0, 1.0);
            }
        }

        // ---- proposal A: a birth instant is the instant expression crossed ----

        [Fact]
        public void A_recovered_birth_instant_is_the_exact_crossing_of_the_expression_ramp()
        {
            // The claim is arithmetic, so assert it arithmetically: at the instant
            // returned, the species' expressed fraction is exactly the fraction at
            // which round(capacity * fraction) tips to this member's index + 1.
            int checkedCases = 0;
            for (int capacity = 4; capacity <= 20; capacity += 2)
            {
                for (int rank = IslandFaunaAge.FloorOf(capacity); rank < capacity; rank++)
                {
                    for (double t = 0.0; t < 6000.0; t += 53.0)
                    {
                        double? born = IslandFaunaAge.BirthElapsedSeconds(
                            Seed, Island, FaunaSpecies.MantaRay, capacity, rank, t);
                        if (born == null) continue;
                        checkedCases++;

                        double expression = IslandFaunaRhythm.ExpressionAt(
                            Seed, Island, FaunaSpecies.MantaRay, born.Value);
                        Assert.True(Math.Abs(expression - ((rank + 0.5) / capacity)) < 1e-9,
                            "member " + rank + " of " + capacity + " at t=" + t
                            + " was born at " + born.Value + " where expression is " + expression);

                        // And it is in the past, which is what makes it an age.
                        Assert.True(born.Value <= t + 1e-9);
                    }
                }
            }
            Assert.True(checkedCases > 200,
                "the sweep found only " + checkedCases + " expressed members to check");
        }

        [Fact]
        public void A_birth_is_the_instant_the_animal_joined_the_expressed_prefix()
        {
            // The same claim stated the way the checkout layer sees it: a second
            // before, the prefix was too short to contain this member; a second
            // after, it was not. Cases whose crossing lands within a second of a
            // phase edge are skipped - the smoothstep's slope is zero there, so a
            // one-second window genuinely may not move an integer, and asserting
            // it would be asserting on the ramp's shape rather than on the birth.
            int checkedCases = 0;
            for (int capacity = 6; capacity <= 18; capacity += 3)
            {
                for (int rank = IslandFaunaAge.FloorOf(capacity); rank < capacity; rank++)
                {
                    for (double t = 100.0; t < 5000.0; t += 71.0)
                    {
                        double? born = IslandFaunaAge.BirthElapsedSeconds(
                            Seed, Island, FaunaSpecies.MantaRay, capacity, rank, t);
                        if (born == null || born.Value < 2.0) continue;

                        int before = Expressed(capacity, born.Value - 1.0);
                        int after = Expressed(capacity, born.Value + 1.0);
                        if (before == after) continue;   // a crossing at a flat edge
                        checkedCases++;

                        Assert.True(before <= rank,
                            "a second before its birth the prefix already held member " + rank);
                        Assert.True(after >= rank + 1,
                            "a second after its birth the prefix still lacked member " + rank);
                    }
                }
            }
            Assert.True(checkedCases > 100,
                "the sweep found only " + checkedCases + " crossings to bracket");
        }

        [Fact]
        public void A_birth_instant_applies_the_islands_start_offset()
        {
            // Proposal A's second risk. Two islands differ ONLY by their hashed
            // start offset, so if the inversion ignored it every age in the world
            // would be wrong by up to a full cycle - 1530 s, longer than a calf's
            // whole visible life.
            IslandId other = new IslandId("some-other-rock");
            Assert.NotEqual(
                IslandFaunaRhythm.StartOffsetSeconds(Seed, Island),
                IslandFaunaRhythm.StartOffsetSeconds(Seed, other));

            int differing = 0, compared = 0;
            for (double t = 0.0; t < 4000.0; t += 29.0)
            {
                double? a = IslandFaunaAge.AgeSeconds(
                    Seed, Island, FaunaSpecies.MantaRay, 12, 9, t);
                double? b = IslandFaunaAge.AgeSeconds(
                    Seed, other, FaunaSpecies.MantaRay, 12, 9, t);
                if (a == null || b == null) continue;
                compared++;
                if (Math.Abs(a.Value - b.Value) > 1.0) differing++;
            }
            Assert.True(compared > 20, "too few instants where both islands expressed member 9");
            Assert.True(differing > compared / 2,
                "two islands with different start offsets returned the same ages");
        }

        [Fact]
        public void An_age_is_deterministic_and_never_negative()
        {
            for (double t = 0.0; t < 5000.0; t += 13.0)
            {
                double? first = IslandFaunaAge.AgeSeconds(
                    Seed, Island, FaunaSpecies.MantaRay, 10, 8, t);
                double? again = IslandFaunaAge.AgeSeconds(
                    Seed, Island, FaunaSpecies.MantaRay, 10, 8, t);
                Assert.Equal(first, again);
                if (first != null)
                {
                    Assert.False(double.IsNaN(first.Value));
                    Assert.True(first.Value >= 0.0);
                }
            }
        }

        [Fact]
        public void An_animal_that_is_not_expressed_has_no_age()
        {
            int absent = 0;
            for (double t = 0.0; t < 5000.0; t += 11.0)
            {
                // The very last slot of a big island is expressed only near a
                // bloom, and must answer null the rest of the time.
                if (Expressed(16, t) < 16)
                {
                    Assert.Null(IslandFaunaAge.AgeSeconds(
                        Seed, Island, FaunaSpecies.MantaRay, 16, 15, t));
                    absent++;
                }
            }
            Assert.True(absent > 50, "the sweep never found the last slot withdrawn");
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-1, 0)]
        [InlineData(4, -1)]
        [InlineData(4, 4)]
        [InlineData(4, 99)]
        public void A_nonsensical_slot_has_no_age_rather_than_an_exception(int capacity, int rank)
        {
            Assert.Null(IslandFaunaAge.BirthElapsedSeconds(
                Seed, Island, FaunaSpecies.MantaRay, capacity, rank, 1234.0));
        }

        [Fact]
        public void A_nonsensical_clock_has_no_age_rather_than_an_exception()
        {
            foreach (double t in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.Null(IslandFaunaAge.BirthElapsedSeconds(
                    Seed, Island, FaunaSpecies.MantaRay, 12, 9, t));
            }
            // A clock before the predator lag clamps rather than throwing, and the
            // age it yields is never negative.
            double? early = IslandFaunaAge.AgeSeconds(
                Seed, Island, FaunaSpecies.MantaRay, 12, 9, -50.0);
            Assert.True(early == null || early.Value >= 0.0);
        }

        [Fact]
        public void The_manta_runs_its_own_lagged_clock()
        {
            // The predator's expression is the prey's, a hashed lag behind, so an
            // age computed on the wrong clock is wrong by up to 360 s. The two
            // species must not agree on when the same slot was born.
            int differing = 0, compared = 0;
            for (double t = 500.0; t < 5000.0; t += 23.0)
            {
                double? manta = IslandFaunaAge.BirthElapsedSeconds(
                    Seed, Island, FaunaSpecies.MantaRay, 12, 9, t);
                double? jelly = IslandFaunaAge.BirthElapsedSeconds(
                    Seed, Island, FaunaSpecies.JellyFish, 12, 9, t);
                if (manta == null || jelly == null) continue;
                compared++;
                if (Math.Abs(manta.Value - jelly.Value) > 1.0) differing++;
            }
            Assert.True(compared > 20);
            Assert.True(differing > compared / 2,
                "the manta's birth times ignore the predator lag");
        }

        // ---- proposal B: the calf is actually small ----------------------------

        [Fact]
        public void A_calf_slot_renders_smaller_than_an_adult_the_moment_it_appears()
        {
            // The claim the whole feature is for. Take a calf slot on a
            // twelve-animal island, walk a couple of cycles, and assert that
            // whenever it has a birth instant it draws below full size - and that
            // it starts at the RECOVERED newborn scale rather than at something
            // we chose.
            const int Capacity = 12;
            int rank = Capacity - 1;
            int juvenile = 0;
            double smallest = 1.0;
            for (double t = 2000.0; t < 8000.0; t += 7.0)
            {
                double? age = IslandFaunaAge.AgeSeconds(
                    Seed, Island, FaunaSpecies.MantaRay, Capacity, rank, t);
                if (age == null) continue;
                FaunaAgeState state = IslandFaunaAge.StateFor(
                    Seed, Island, FaunaSpecies.MantaRay, Capacity, rank, isCalfSlot: true, t);
                Assert.True(state.RenderedScale < IslandFaunaAge.RecoveredFullyGrownScale,
                    "a calf with an age of " + age.Value + " s rendered full size");
                Assert.True(state.RenderedScale >= IslandFaunaAge.RecoveredBirthScale);
                smallest = Math.Min(smallest, state.RenderedScale);
                juvenile++;
            }
            Assert.True(juvenile > 100, "only " + juvenile + " instants had a juvenile");
            Assert.True(smallest < IslandFaunaAge.RecoveredBirthScale + 0.02,
                "the youngest calf seen rendered at " + smallest
                + "; a newborn should be at the prefab's own 0.25");
        }

        [Fact]
        public void A_calf_stays_visibly_a_calf_for_the_whole_window_it_is_expressed()
        {
            // Maturation is tied to the rhythm's nominal cycle precisely so this
            // holds: a calf slot is expressed for roughly half a cycle, so it
            // should still be well under adult size when its slot withdraws. If
            // it reached full size while still expressed, a player would watch a
            // manta finish growing for no reason.
            const int Capacity = 12;
            int rank = Capacity - 1;
            double oldest = 0.0;
            for (double t = 2000.0; t < 20_000.0; t += 3.0)
            {
                double? age = IslandFaunaAge.AgeSeconds(
                    Seed, Island, FaunaSpecies.MantaRay, Capacity, rank, t);
                if (age != null) oldest = Math.Max(oldest, age.Value);
            }
            Assert.True(oldest > 60.0, "the calf slot was never expressed for long");
            Assert.True(oldest < IslandFaunaAge.SecondsTillFullyGrown,
                "a calf reached full size (" + oldest + " s) while still expressed");
        }

        [Fact]
        public void A_calf_grows_rather_than_holding_one_size()
        {
            // Not a claim about live updates - the age is seeded once per checkout
            // and no update is ever pushed - but about the POLICY: a player who
            // leaves and comes back must find the calf bigger, because the age is
            // a function of the clock rather than of the slot.
            const int Capacity = 12;
            int rank = Capacity - 1;
            double? first = null, last = null;
            for (double t = 2000.0; t < 8000.0; t += 1.0)
            {
                double? age = IslandFaunaAge.AgeSeconds(
                    Seed, Island, FaunaSpecies.MantaRay, Capacity, rank, t);
                if (age == null) { first = null; continue; }
                if (first == null) { first = age; last = age; continue; }
                // Inside one expression window the age only ever climbs.
                Assert.True(age.Value >= last!.Value - 1e-9,
                    "a calf got younger between two adjacent instants");
                last = age;
            }
            Assert.NotNull(last);
        }

        [Fact]
        public void The_scale_a_calf_renders_at_is_the_clients_own_arithmetic()
        {
            // Spot-check the endpoints and the midpoint against
            // Lerp(0.25, 1.0, ratio) computed by hand, so a refactor of
            // RenderedScale cannot quietly change what the client will draw.
            Assert.Equal(0.25, IslandFaunaAge.For(0.0).RenderedScale, 12);
            Assert.Equal(0.625,
                IslandFaunaAge.For(IslandFaunaAge.SecondsTillFullyGrown / 2.0).RenderedScale, 2);
            Assert.Equal(1.0,
                IslandFaunaAge.For(IslandFaunaAge.SecondsTillFullyGrown).RenderedScale, 12);
        }

        [Fact]
        public void Almost_every_expressed_calf_slot_in_the_tier1_world_is_actually_a_juvenile()
        {
            // THE WORLD-WIDE COUNTERPART of the adult sweep: if calf slots mostly
            // fell back to "adult" the feature would exist on paper and nowhere
            // else. Measured over the tier-1 rollout across several cycles, past
            // the boot window, more than nine in ten expressed calf slots render
            // below full size; the remainder are slots whose most recent crossing
            // predates the process, which the policy declines to call a birth.
            int expressedSamples = 0, juvenile = 0;
            foreach (ReleaseIslandRecord island in ReleaseWorldRolloutPolicy.Select("tier1"))
            {
                (int capacity, _) = IslandFaunaCapacity.ClampedToPeerBudget(
                    IslandFaunaCapacity.CapacityFor(FaunaSpecies.MantaRay,
                        island.Survey.Tier, island.Envelope, island.Definition.Id),
                    IslandFaunaCapacity.CapacityFor(FaunaSpecies.JellyFish,
                        island.Survey.Tier, island.Envelope, island.Definition.Id),
                    IslandFaunaInterestPolicy.DefaultPerPeerCreatures);
                if (capacity < IslandFaunaFamily.MembersPerCalfSlot) continue;

                int rank = capacity - 1;    // always a calf slot of the last group
                for (double t = 3000.0; t < 12_000.0; t += 25.0)
                {
                    if (IslandFaunaRhythm.ExpressedCount(capacity,
                            IslandFaunaRhythm.ExpressionAt(Seed, island.Definition.Id,
                                FaunaSpecies.MantaRay, t)) <= rank)
                    {
                        continue;
                    }
                    expressedSamples++;
                    if (IslandFaunaAge.StateFor(Seed, island.Definition.Id,
                            FaunaSpecies.MantaRay, capacity, rank, isCalfSlot: true, t)
                        .RenderedScale < IslandFaunaAge.RecoveredFullyGrownScale)
                    {
                        juvenile++;
                    }
                }
            }
            Assert.True(expressedSamples > 1000,
                "only " + expressedSamples + " expressed calf-slot samples");
            Assert.True(juvenile * 10 > expressedSamples * 9,
                juvenile + " of " + expressedSamples + " expressed calf slots were juvenile");
        }

        private static int Expressed(int capacity, double t) =>
            IslandFaunaRhythm.ExpressedCount(capacity,
                IslandFaunaRhythm.ExpressionAt(Seed, Island, FaunaSpecies.MantaRay, t));
    }
}

using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// The ecological field is the first fauna maths whose OUTPUT no human has
    /// yet watched - it is staged unwired - so the tests hold the four
    /// properties that make it safe to wire later, against the REAL catalogue
    /// rather than fixtures:
    ///
    /// DETERMINISM - the field is f(seed, island, species, index, t) and nothing
    /// else, so a restarted server replays it. CLEARANCE - no group centre can
    /// ever come nearer the island's lateral centre than the species' recovered
    /// floor, because this server has no terrain query and "inside the rock" is
    /// unrecoverable at runtime. BOUNDED REACH AND SPEED - the epicycle must not
    /// stand wildlife much farther out than the old geometry did, nor move it
    /// fast enough for the 4 Hz pose stream to visibly step. FIELD HONESTY - a
    /// group really does orbit within two sigma of its bloom's maximum, so
    /// "orbits maxima in the ecology" is a checked claim, not a metaphor.
    /// </summary>
    public sealed class IslandFaunaEcologyTests
    {
        private const int Seed = IslandFaunaEcology.DefaultWorldSeed;

        // --- Determinism.

        [Fact]
        public void The_same_seed_produces_the_same_blooms_every_time()
        {
            ReleaseIslandRecord island = Median();
            foreach (FaunaSpecies species in Enum.GetValues<FaunaSpecies>())
            {
                FaunaBloom[] first = IslandFaunaEcology.BloomsFor(
                    Seed, island.Definition.Id, species, island.Envelope);
                FaunaBloom[] second = IslandFaunaEcology.BloomsFor(
                    Seed, island.Definition.Id, species, island.Envelope);
                Assert.Equal(first.Length, second.Length);
                for (int i = 0; i < first.Length; i++)
                {
                    Assert.Equal(first[i], second[i]);
                }
            }
        }

        [Fact]
        public void Different_islands_and_different_seeds_produce_different_fields()
        {
            ReleaseIslandRecord[] islands = ReleaseWorldCatalog.All
                .Where(record => record.Survey.Tier == 1)
                .OrderBy(record => record.Definition.Id).Take(2).ToArray();

            FaunaBloom a = IslandFaunaEcology.BloomsFor(
                Seed, islands[0].Definition.Id, FaunaSpecies.MantaRay, islands[0].Envelope)[0];
            FaunaBloom b = IslandFaunaEcology.BloomsFor(
                Seed, islands[1].Definition.Id, FaunaSpecies.MantaRay, islands[1].Envelope)[0];
            Assert.NotEqual(a.BaseAngleRadians, b.BaseAngleRadians);

            FaunaBloom reseeded = IslandFaunaEcology.BloomsFor(
                Seed + 1, islands[0].Definition.Id, FaunaSpecies.MantaRay, islands[0].Envelope)[0];
            Assert.NotEqual(a.BaseAngleRadians, reseeded.BaseAngleRadians);
        }

        [Fact]
        public void Seeded_uniforms_are_deterministic_and_in_range()
        {
            IslandId id = new IslandId("beautiful-wildlands");
            for (int channel = 1; channel <= 8; channel++)
            {
                double u = IslandFaunaEcology.Unit(Seed, id, FaunaSpecies.MantaRay, 0, channel);
                Assert.InRange(u, 0.0, 1.0 - double.Epsilon);
                Assert.Equal(u, IslandFaunaEcology.Unit(Seed, id, FaunaSpecies.MantaRay, 0, channel));
            }
            Assert.NotEqual(
                IslandFaunaEcology.Unit(Seed, id, FaunaSpecies.MantaRay, 0, 1),
                IslandFaunaEcology.Unit(Seed, id, FaunaSpecies.MantaRay, 0, 2));
        }

        // --- Clearance: the safety property, held by construction, proven by sweep.

        [Fact]
        public void No_group_can_ever_come_nearer_the_island_than_the_recovered_floor()
        {
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                foreach (FaunaSpecies species in Enum.GetValues<FaunaSpecies>())
                {
                    double floor = IslandFaunaEcology.ClearanceFloorMetres(
                        species, island.Envelope);
                    FaunaBloom[] blooms = IslandFaunaEcology.BloomsFor(
                        Seed, island.Definition.Id, species, island.Envelope);
                    foreach (FaunaBloom bloom in blooms)
                    {
                        for (int group = 0; group < 3; group++)
                        {
                            Assert.True(
                                IslandFaunaEcology.MinLateralReach(bloom, species, group)
                                    >= floor - 1e-9,
                                island.Definition.Id + " " + species
                                    + ": a group's closest approach dips inside the clearance floor");
                        }
                    }
                }
            }
        }

        [Fact]
        public void The_outermost_reach_stays_in_the_old_geometrys_proportions()
        {
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                double lateral = IslandFaunaMovement.LateralRadiusOf(island.Envelope);
                foreach (FaunaSpecies species in Enum.GetValues<FaunaSpecies>())
                {
                    double floor = IslandFaunaEcology.ClearanceFloorMetres(
                        species, island.Envelope);
                    foreach (FaunaBloom bloom in IslandFaunaEcology.BloomsFor(
                        Seed, island.Definition.Id, species, island.Envelope))
                    {
                        for (int group = 0; group < 3; group++)
                        {
                            // The documented bound: floor + 2*(drift + widest orbit),
                            // which the parameter fractions keep under floor + 0.8
                            // island radii. Wildlife may stand somewhat past the old
                            // fixed ring - that is the point - but never leave the
                            // island's neighbourhood.
                            Assert.True(
                                IslandFaunaEcology.MaxLateralReach(bloom, species, group)
                                    <= floor + (0.8 * lateral) + 2.0,
                                island.Definition.Id + " " + species
                                    + ": the epicycle reaches too far from the island");
                        }
                    }
                }
            }
        }

        // --- Speed: the 4 Hz pose stream must carry this without visible stepping.

        [Theory]
        [InlineData(FaunaSpecies.MantaRay, 12.0)]
        [InlineData(FaunaSpecies.JellyFish, 4.5)]
        public void Group_centres_never_exceed_the_species_speed_bound(
            FaunaSpecies species, double maxMetresPerSecond)
        {
            ReleaseIslandRecord island = Median();
            FaunaBloom[] blooms = IslandFaunaEcology.BloomsFor(
                Seed, island.Definition.Id, species, island.Envelope);

            const double Step = 0.25; // the real pose cadence
            for (int group = 0; group < 2; group++)
            {
                FaunaBloom bloom = blooms[IslandFaunaEcology.BloomIndexFor(group, blooms.Length)];
                (double px, double pz) = IslandFaunaEcology.GroupCentreAt(bloom, species, group, 0.0);
                for (double t = Step; t <= 3600.0; t += Step)
                {
                    (double x, double z) = IslandFaunaEcology.GroupCentreAt(bloom, species, group, t);
                    double speed = Math.Sqrt(((x - px) * (x - px)) + ((z - pz) * (z - pz))) / Step;
                    Assert.True(speed <= maxMetresPerSecond,
                        species + " group " + group + " moved at " + speed.ToString("0.0")
                        + " m/s at t=" + t);
                    (px, pz) = (x, z);
                }
            }
        }

        // --- Field honesty: a group orbits ITS maximum, and the field says so.

        [Fact]
        public void A_group_stays_within_two_sigma_of_its_blooms_maximum()
        {
            ReleaseIslandRecord island = Median();
            foreach (FaunaSpecies species in Enum.GetValues<FaunaSpecies>())
            {
                FaunaBloom[] blooms = IslandFaunaEcology.BloomsFor(
                    Seed, island.Definition.Id, species, island.Envelope);
                for (int group = 0; group < 3; group++)
                {
                    FaunaBloom bloom = blooms[IslandFaunaEcology.BloomIndexFor(group, blooms.Length)];
                    for (double t = 0.0; t <= 1800.0; t += 30.0)
                    {
                        (double bx, double bz) = IslandFaunaEcology.BloomCentreAt(bloom, t);
                        (double gx, double gz) = IslandFaunaEcology.GroupCentreAt(
                            bloom, species, group, t);
                        double distance = Math.Sqrt(
                            ((gx - bx) * (gx - bx)) + ((gz - bz) * (gz - bz)));
                        Assert.True(distance <= 2.0 * bloom.SigmaMetres,
                            species + " group " + group + " strayed " + distance.ToString("0")
                            + " m from a bloom of sigma " + bloom.SigmaMetres.ToString("0"));
                    }
                }
            }
        }

        [Fact]
        public void The_field_peaks_at_a_blooms_centre_and_its_gradient_vanishes_there()
        {
            ReleaseIslandRecord island = Median();
            FaunaBloom[] blooms = IslandFaunaEcology.BloomsFor(
                Seed, island.Definition.Id, FaunaSpecies.JellyFish, island.Envelope);
            FaunaBloom only = blooms[0];
            FaunaBloom[] single = { only };

            (double cx, double cz) = IslandFaunaEcology.BloomCentreAt(only, 100.0);
            double atPeak = IslandFaunaEcology.FieldAt(single, cx, cz, 100.0);
            double offPeak = IslandFaunaEcology.FieldAt(
                single, cx + (4.0 * only.SigmaMetres), cz, 100.0);
            Assert.True(atPeak > offPeak * 10.0,
                "the field is not peaked at its own bloom centre");

            (double gx, double gz) = IslandFaunaEcology.FieldGradientAt(single, cx, cz, 100.0);
            Assert.True(Math.Abs(gx) < 1e-9 && Math.Abs(gz) < 1e-9,
                "the gradient does not vanish at the maximum");
        }

        // --- Structure.

        [Fact]
        public void Bigger_islands_carry_more_blooms()
        {
            int[] counts = ReleaseWorldCatalog.All
                .Select(island => IslandFaunaEcology.BloomCountFor(island.Envelope))
                .Distinct().OrderBy(x => x).ToArray();
            Assert.Equal(new[] { 1, 2 }, counts);
        }

        [Theory]
        [InlineData(0, 2, 0)]
        [InlineData(1, 2, 1)]
        [InlineData(2, 2, 0)]
        [InlineData(5, 1, 0)]
        [InlineData(0, 0, 0)]
        public void Groups_round_robin_over_the_blooms(int group, int blooms, int expected) =>
            Assert.Equal(expected, IslandFaunaEcology.BloomIndexFor(group, blooms));

        [Fact]
        public void Two_groups_on_one_bloom_fly_different_circles()
        {
            ReleaseIslandRecord island = Median();
            FaunaBloom bloom = IslandFaunaEcology.BloomsFor(
                Seed, island.Definition.Id, FaunaSpecies.MantaRay, island.Envelope)[0];
            Assert.NotEqual(
                IslandFaunaEcology.GroupOrbitRadius(bloom, FaunaSpecies.MantaRay, 0),
                IslandFaunaEcology.GroupOrbitRadius(bloom, FaunaSpecies.MantaRay, 1));
        }

        private static ReleaseIslandRecord Median() =>
            ReleaseWorldCatalog.All
                .Where(record => record.Survey.Tier == 1)
                .OrderBy(record => IslandFaunaMovement.LateralRadiusOf(record.Envelope))
                .ElementAt(23);
    }
}

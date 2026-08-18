using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// The evaluator is the seam between the ecology maths and the live pose
    /// path, so what is tested here is the seam's own promises: one memoised
    /// bloom derivation shared by every consumer, the same restart-replay
    /// contract the classic movement keeps, motion the 4 Hz stream can carry,
    /// and rotations that are genuinely unit quaternions - the wire packs them
    /// into 32 bits and a denormalised one decodes as garbage.
    /// </summary>
    public sealed class FaunaEcologyEvaluatorTests
    {
        private static ReleaseIslandRecord Island() =>
            ReleaseWorldCatalog.All.Where(r => r.Survey.Tier == 1)
                .OrderBy(r => IslandFaunaMovement.LateralRadiusOf(r.Envelope))
                .ElementAt(23);

        private static FaunaCreature Creature(
            FaunaSpecies species, int school = 0, int member = 0) =>
            new FaunaCreature(IslandFaunaPolicy.FirstFaunaEntityId + member,
                species, Island().Definition.Id, member, school, member);

        [Fact]
        public void Blooms_are_memoised_and_identical_to_the_pure_derivation()
        {
            FaunaEcologyEvaluator evaluator =
                new FaunaEcologyEvaluator(IslandFaunaEcology.DefaultWorldSeed);
            ReleaseIslandRecord island = Island();

            FaunaBloom[] first = evaluator.BloomsFor(
                island.Definition.Id, FaunaSpecies.MantaRay, island.Envelope);
            FaunaBloom[] second = evaluator.BloomsFor(
                island.Definition.Id, FaunaSpecies.MantaRay, island.Envelope);
            Assert.Same(first, second);

            FaunaBloom[] pure = IslandFaunaEcology.BloomsFor(
                IslandFaunaEcology.DefaultWorldSeed, island.Definition.Id,
                FaunaSpecies.MantaRay, island.Envelope);
            Assert.Equal(pure.Length, first.Length);
            for (int i = 0; i < pure.Length; i++)
            {
                Assert.Equal(pure[i], first[i]);
            }
        }

        [Fact]
        public void Two_evaluators_on_one_seed_replay_identical_poses()
        {
            FaunaEcologyEvaluator a = new FaunaEcologyEvaluator(1);
            FaunaEcologyEvaluator b = new FaunaEcologyEvaluator(1);
            ReleaseIslandRecord island = Island();
            FaunaCreature creature = Creature(FaunaSpecies.MantaRay, 0, 2);

            foreach (double t in new[] { 0.0, 123.4, 86_400.0, 2_592_000.0 })
            {
                Assert.Equal(
                    a.LocalPoseAt(creature, island.Envelope, t),
                    b.LocalPoseAt(creature, island.Envelope, t));
            }
        }

        [Fact]
        public void A_different_seed_is_a_different_ecology()
        {
            ReleaseIslandRecord island = Island();
            FaunaCreature creature = Creature(FaunaSpecies.MantaRay);
            Assert.NotEqual(
                new FaunaEcologyEvaluator(1).LocalPoseAt(creature, island.Envelope, 100.0),
                new FaunaEcologyEvaluator(2).LocalPoseAt(creature, island.Envelope, 100.0));
        }

        [Theory]
        [InlineData(FaunaSpecies.MantaRay, 12.0)]
        [InlineData(FaunaSpecies.JellyFish, 4.0)]
        public void A_creatures_full_pose_stays_inside_the_species_speed_bound(
            FaunaSpecies species, double lateralBoundMetresPerSecond)
        {
            // The COMPLETE pose this time - group centre, vertical law and member
            // weave together - sampled at the real 250 ms cadence across an hour
            // that includes a dawn and a dusk. The lateral bound is a constant;
            // the vertical allowance is computed from the ISLAND, because the
            // jelly's dawn dive is the recovered altitude blend itself - the
            // shoal crosses three quarters of the island's height inside the 6%
            // phase ramp, so its peak speed is a property of the rock's size,
            // not a tunable of this feature (the classic path dives at exactly
            // the same rate).
            FaunaEcologyEvaluator evaluator =
                new FaunaEcologyEvaluator(IslandFaunaEcology.DefaultWorldSeed);
            ReleaseIslandRecord island = Island();
            FaunaCreature creature = Creature(species, 0, 1);

            double verticalAllowance = species == FaunaSpecies.JellyFish
                ? ((island.Envelope.MaxY - island.Envelope.MinY)
                        * IslandFaunaMovement.IslandWalkableHeightFraction)
                    / (IslandFaunaMovement.PhaseTransitionFraction
                        * IslandFaunaMovement.DayNightCycleSeconds)
                : 0.0; // the manta's band pace is already inside its lateral bound
            double maxMetresPerSecond = lateralBoundMetresPerSecond + verticalAllowance;

            const double Step = 0.25;
            (double px, double py, double pz) =
                evaluator.LocalPoseAt(creature, island.Envelope, 0.0);
            for (double t = Step; t <= 3600.0; t += Step)
            {
                (double x, double y, double z) =
                    evaluator.LocalPoseAt(creature, island.Envelope, t);
                double speed = Math.Sqrt(((x - px) * (x - px)) + ((y - py) * (y - py))
                    + ((z - pz) * (z - pz))) / Step;
                Assert.True(speed <= maxMetresPerSecond,
                    species + " moved at " + speed.ToString("0.0") + " m/s at t=" + t);
                Assert.False(double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z));
                (px, py, pz) = (x, y, z);
            }
        }

        [Fact]
        public void The_manta_keeps_its_recovered_vertical_band()
        {
            // Midpoint to top, never below - the recovery whose earlier
            // misreading put the wildlife under the island where nobody saw it.
            FaunaEcologyEvaluator evaluator =
                new FaunaEcologyEvaluator(IslandFaunaEcology.DefaultWorldSeed);
            ReleaseIslandRecord island = Island();
            FaunaCreature creature = Creature(FaunaSpecies.MantaRay);
            double midpoint = IslandFaunaMovement.CentreYOf(island.Envelope);
            double margin = IslandFaunaSchool.MantaSchoolVerticalRadiusMetres + 1e-9;

            for (double t = 0.0; t <= 3600.0; t += 7.0)
            {
                (double _, double y, double _2) =
                    evaluator.LocalPoseAt(creature, island.Envelope, t);
                Assert.True(y >= midpoint - margin,
                    "a manta sank below the island midpoint at t=" + t);
                Assert.True(y <= island.Envelope.MaxY + margin,
                    "a manta climbed above the island top at t=" + t);
            }
        }

        [Fact]
        public void The_jelly_keeps_its_recovered_daynight_altitude()
        {
            FaunaEcologyEvaluator evaluator =
                new FaunaEcologyEvaluator(IslandFaunaEcology.DefaultWorldSeed);
            ReleaseIslandRecord island = Island();
            FaunaCreature creature = Creature(FaunaSpecies.JellyFish);

            // Deep day (cycle fraction 0.5): the shoal hangs at the underside.
            double day = IslandFaunaMovement.DayNightCycleSeconds * 0.5;
            (double _, double dayY, double _2) =
                evaluator.LocalPoseAt(creature, island.Envelope, day);
            Assert.True(Math.Abs(dayY - island.Envelope.MinY)
                <= IslandFaunaSchool.JellyShoalVerticalRadiusMetres + 1e-9);

            // Deep night (fraction 0.0): risen to the walkable band.
            double nightY = island.Envelope.MinY
                + ((island.Envelope.MaxY - island.Envelope.MinY)
                    * IslandFaunaMovement.IslandWalkableHeightFraction);
            double night = IslandFaunaMovement.DayNightCycleSeconds * 2.0;
            (double _3, double atNight, double _4) =
                evaluator.LocalPoseAt(creature, island.Envelope, night);
            Assert.True(Math.Abs(atNight - nightY)
                <= IslandFaunaSchool.JellyShoalVerticalRadiusMetres + 1e-9);
        }

        [Fact]
        public void Rotations_are_unit_quaternions_for_both_species()
        {
            FaunaEcologyEvaluator evaluator =
                new FaunaEcologyEvaluator(IslandFaunaEcology.DefaultWorldSeed);
            ReleaseIslandRecord island = Island();

            foreach (FaunaSpecies species in Enum.GetValues<FaunaSpecies>())
            {
                FaunaCreature creature = Creature(species, 0, 1);
                for (double t = 0.0; t <= 1200.0; t += 61.0)
                {
                    FaunaRotation r = evaluator.RotationAt(creature, island.Envelope, t);
                    double norm = Math.Sqrt((r.W * r.W) + (r.X * r.X)
                        + (r.Y * r.Y) + (r.Z * r.Z));
                    // 1e-6, not 1e-9: LookRotation accumulates a few ulps of
                    // error through its cross products, and the wire's 32-bit
                    // packing renormalises far more coarsely than this anyway.
                    Assert.True(Math.Abs(norm - 1.0) < 1e-6,
                        species + " rotation norm " + norm + " at t=" + t);
                }
            }
        }

        [Fact]
        public void The_world_transform_is_the_local_pose_translated_by_the_island()
        {
            FaunaEcologyEvaluator evaluator =
                new FaunaEcologyEvaluator(IslandFaunaEcology.DefaultWorldSeed);
            ReleaseIslandRecord island = Island();
            FaunaCreature creature = Creature(FaunaSpecies.MantaRay);

            (double x, double y, double z) =
                evaluator.LocalPoseAt(creature, island.Envelope, 500.0);
            FaunaTransform world = evaluator.WorldTransformAt(
                creature, island.Definition, island.Envelope, 500.0);
            FixedPointPosition expected = island.Definition.LocalToGlobal(x, y, z);
            Assert.Equal(expected, world.Position);
        }
    }
}

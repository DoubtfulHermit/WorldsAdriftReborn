using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// The flattened movement model exists so a SECOND evaluator - the operator
    /// console's browser - can place a creature where this server has it. The one
    /// thing that would quietly break that is the model restating a number instead
    /// of reading it, so every test here compares a published field against the
    /// movement's own constant or accessor rather than against a literal.
    /// </summary>
    public class IslandFaunaMapModelTests
    {
        private static IslandTerrainEnvelope Envelope(
            double minX = -220, double minY = -180, double minZ = -140,
            double maxX = 180, double maxY = 60, double maxZ = 260) =>
            new IslandTerrainEnvelope(new IslandId("test-island"),
                minX, minY, minZ, maxX, maxY, maxZ);

        [Fact]
        public void Every_constant_is_the_movements_own_and_not_a_second_copy_of_it()
        {
            FaunaMapConstants c = IslandFaunaMapModel.Constants;

            Assert.Equal(IslandFaunaMovement.DayNightCycleSeconds, c.DayNightCycleSeconds);
            Assert.Equal(IslandFaunaMovement.DayBeginsAtCycleFraction, c.DayBeginsAtCycleFraction);
            Assert.Equal(IslandFaunaMovement.DayEndsAtCycleFraction, c.DayEndsAtCycleFraction);
            Assert.Equal(IslandFaunaMovement.PhaseTransitionFraction, c.PhaseTransitionFraction);
            Assert.Equal(IslandFaunaMovement.JellyDayRadiusRatio, c.JellyDayRadiusRatio);
            Assert.Equal(IslandFaunaMovement.JellyNightRadiusRatio, c.JellyNightRadiusRatio);
            Assert.Equal(IslandFaunaMovement.JellySecondsPerRevolution, c.JellySecondsPerRevolution);
            Assert.Equal(IslandFaunaMovement.IslandWalkableHeightFraction, c.IslandWalkableHeightFraction);
            Assert.Equal(IslandFaunaMovement.MantaVerticalSpanRatio, c.MantaVerticalSpanRatio);
            Assert.Equal(IslandFaunaMovement.MantaMetresPerSecond, c.MantaMetresPerSecond);
            Assert.Equal(IslandFaunaSchool.MantaSchoolRadiusMetres, c.MantaSchoolRadiusMetres);
            Assert.Equal(IslandFaunaSchool.MantaSchoolVerticalRadiusMetres, c.MantaSchoolVerticalRadiusMetres);
            Assert.Equal(IslandFaunaSchool.JellyShoalRadiusMetres, c.JellyShoalRadiusMetres);
            Assert.Equal(IslandFaunaSchool.JellyShoalVerticalRadiusMetres, c.JellyShoalVerticalRadiusMetres);
            Assert.Equal(IslandFaunaSchool.WeaveRadiansPerSecond, c.WeaveRadiansPerSecond);
            Assert.Equal(IslandFaunaSchool.GoldenAngleRadians, c.GoldenAngleRadians);
            Assert.Equal(IslandFaunaSchool.GoldenRatioFraction, c.GoldenRatioFraction);
            Assert.Equal(IslandFaunaPolicy.SchoolsPerIsland, c.SchoolsPerIsland);
        }

        [Fact]
        public void Every_island_scalar_is_the_movements_own_accessor_called()
        {
            IslandTerrainEnvelope envelope = Envelope();
            FaunaIslandMotion motion = IslandFaunaMapModel.MotionFor(envelope);

            Assert.Equal(IslandFaunaMovement.CentreXOf(envelope), motion.CentreX);
            Assert.Equal(IslandFaunaMovement.CentreYOf(envelope), motion.CentreY);
            Assert.Equal(IslandFaunaMovement.CentreZOf(envelope), motion.CentreZ);
            Assert.Equal(IslandFaunaMovement.HalfHeightOf(envelope), motion.HalfHeightMetres);
            Assert.Equal(IslandFaunaMovement.MantaOrbitRadiusOf(envelope), motion.MantaOrbitRadiusMetres);
            Assert.Equal(IslandFaunaMovement.MantaLapSecondsOf(envelope), motion.MantaLapSeconds);
            Assert.Equal(IslandFaunaMovement.LateralRadiusOf(envelope), motion.JellyLateralRadiusMetres);
            Assert.Equal(envelope.MinY, motion.MinY);
            Assert.Equal(envelope.MaxY, motion.MaxY);
        }

        /// <summary>
        /// The scalars are the whole point: a second evaluator that has them plus
        /// the constants can reproduce a school's centre without ever seeing an
        /// envelope. Reproduce it here, in the arithmetic the browser uses, and
        /// assert it lands on the server's own answer.
        /// </summary>
        [Fact]
        public void The_published_scalars_are_enough_to_reproduce_a_school_centre()
        {
            IslandTerrainEnvelope envelope = Envelope();
            FaunaIslandMotion m = IslandFaunaMapModel.MotionFor(envelope);
            FaunaCreature creature = new FaunaCreature(
                IslandFaunaPolicy.FirstFaunaEntityId, FaunaSpecies.MantaRay,
                envelope.IslandId, 0, 0, 0);

            foreach (double t in new[] { 0.0, 61.5, 1200.0, 4321.75 })
            {
                double lap = IslandFaunaSchool.Fraction(t / m.MantaLapSeconds);
                double theta = lap * 2.0 * Math.PI;
                double x = m.CentreX + (m.MantaOrbitRadiusMetres * Math.Sin(theta));
                double z = m.CentreZ + (m.MantaOrbitRadiusMetres * Math.Cos(theta));
                double y = m.CentreY + (m.HalfHeightMetres
                    * IslandFaunaMovement.MantaVerticalOffsetRatioAt(lap));

                (double ex, double ey, double ez) =
                    IslandFaunaMovement.MantaSchoolCentreAt(creature, envelope, t);
                Assert.Equal(ex, x, 9);
                Assert.Equal(ey, y, 9);
                Assert.Equal(ez, z, 9);
            }
        }

        [Fact]
        public void A_population_is_the_policys_own_counts()
        {
            for (int tier = 1; tier <= 4; tier++)
            {
                FaunaIslandPopulation population = IslandFaunaMapModel.PopulationFor(tier);
                Assert.Equal(IslandFaunaPolicy.MantaCountFor(tier), population.MantaRays);
                Assert.Equal(IslandFaunaPolicy.JellyFishCountFor(tier), population.JellyFish);
                Assert.Equal(IslandFaunaPolicy.SchoolsPerIsland, population.Schools);
                Assert.Equal(population.MantaRays + population.JellyFish, population.Total);
                Assert.Equal(
                    IslandFaunaPolicy.SchoolSizeFor(FaunaSpecies.MantaRay, tier),
                    population.MantaSchoolSize);
                Assert.Equal(
                    IslandFaunaPolicy.SchoolSizeFor(FaunaSpecies.JellyFish, tier),
                    population.JellyShoalSize);
            }
        }

        /// <summary>
        /// A tier outside 1..4 must degrade the population, never throw: the
        /// console asks for one per drawn island and a bad tier on one island
        /// cannot be allowed to take the whole map down.
        /// </summary>
        [Fact]
        public void A_tier_outside_the_range_clamps_rather_than_throwing()
        {
            Assert.Equal(IslandFaunaMapModel.PopulationFor(1), IslandFaunaMapModel.PopulationFor(-7));
            Assert.Equal(IslandFaunaMapModel.PopulationFor(4), IslandFaunaMapModel.PopulationFor(99));
        }

        /// <summary>
        /// A degenerate envelope - a zero-extent island, which the catalogue
        /// should never contain but a hand-written one could - must still produce
        /// a finite, positive geometry rather than a division by zero that reaches
        /// the browser as NaN and stops drawing the whole layer.
        /// </summary>
        [Fact]
        public void A_flat_envelope_still_yields_a_finite_positive_geometry()
        {
            FaunaIslandMotion motion = IslandFaunaMapModel.MotionFor(
                Envelope(minX: 0, minY: 0, minZ: 0, maxX: 0, maxY: 0, maxZ: 0));

            Assert.True(motion.MantaOrbitRadiusMetres > 0);
            Assert.True(motion.MantaLapSeconds > 0);
            Assert.True(motion.JellyLateralRadiusMetres > 0);
            Assert.True(motion.HalfHeightMetres > 0);
            Assert.False(double.IsNaN(motion.CentreX) || double.IsNaN(motion.CentreZ));
        }
    }
}

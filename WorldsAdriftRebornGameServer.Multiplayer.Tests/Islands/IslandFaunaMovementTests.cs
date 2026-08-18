using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// WHY THESE FACTS MATTER. The manta perimeter orbit and the jellyfish
    /// day/night drift ARE the feature: everything else - the population policy,
    /// the bounded registry, the interest scoping - exists only to decide which
    /// creatures get to move and how often their motion is published. If the
    /// geometry is wrong, a manta flies inside the rock or a kilometre off the
    /// perimeter and no amount of correct bookkeeping above it helps.
    ///
    /// Three properties are asserted rather than assumed. GEOMETRY RESCALES: the
    /// radii are ratios of the envelope's own extents, so the same facts are run
    /// against a tiny islet, a huge island and a strongly anisotropic one - the
    /// only "different viewport" this headless server has. PURITY: LocalPoseAt is
    /// a total function of its arguments with no Random, no DateTime and no
    /// accumulation, which is exactly what lets a restarted server replay the
    /// identical path and lets peers agree without syncing state. WIRING: the
    /// registry drives this maths through the FaunaPoseFunction delegate, and
    /// nothing else proves that the two halves actually fit together.
    /// </summary>
    public sealed class IslandFaunaMovementTests
    {
        private const double Tolerance = 1e-9;
        private const double LooseTolerance = 1e-6;

        // --- Manta perimeter orbit (RECOVERED, acs/PatrolVisualiser.cs)

        [Fact]
        public void Manta_holds_the_orbit_radius_outside_the_island_across_a_whole_revolution()
        {
            IslandTerrainEnvelope envelope = Normal();
            double lateral = IslandFaunaMovement.LateralRadiusOf(envelope);
            double orbit = IslandFaunaMovement.MantaOrbitRadiusOf(envelope);
            Assert.True(orbit > lateral, "the orbit must clear the island's lateral bounds");

            foreach (double seconds in Revolution())
            {
                Assert.Equal(orbit, LateralDistance(Manta(0), envelope, seconds), 6);
            }
        }

        [Fact]
        public void Manta_stays_outside_a_tiny_island() => AssertStaysOutside(Tiny());

        [Fact]
        public void Manta_stays_outside_a_huge_island() => AssertStaysOutside(Huge());

        [Fact]
        public void Manta_stays_outside_a_strongly_anisotropic_island() =>
            AssertStaysOutside(Anisotropic());

        [Fact]
        public void Manta_altitude_rises_and_falls_inside_the_island_half_height()
        {
            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                double centreY = (envelope.MinY + envelope.MaxY) / 2.0;
                double half = IslandFaunaMovement.HalfHeightOf(envelope);
                double lowest = double.MaxValue;
                double highest = double.MinValue;

                foreach (double seconds in Revolution())
                {
                    double y = IslandFaunaMovement.LocalPoseAt(Manta(0), envelope, seconds).Y;
                    Assert.True(Math.Abs(y - centreY) <= half + LooseTolerance,
                        "a manta must not climb outside the island's own half-height");
                    lowest = Math.Min(lowest, y);
                    highest = Math.Max(highest, y);
                }

                // A constant altitude would satisfy the bound above and be wrong:
                // the recovered path is a sinusoid, so it must genuinely move.
                Assert.True(highest - lowest > half,
                    "the vertical offset must both rise and fall, not sit flat");
            }
        }

        [Fact]
        public void Manta_advances_one_orbit_step_per_orbit_step_of_time()
        {
            IslandTerrainEnvelope envelope = Normal();
            double step = IslandFaunaMovement.MantaSecondsPerOrbitStep;

            for (int i = 0; i < 12; i++)
            {
                double first = HeadingDegrees(Manta(0), envelope, i * step);
                double second = HeadingDegrees(Manta(0), envelope, (i + 1) * step);
                double advanced = Normalise(second - first);
                Assert.True(Math.Abs(advanced - IslandFaunaMovement.MantaOrbitStepDegrees) < 1e-6,
                    "expected ~" + IslandFaunaMovement.MantaOrbitStepDegrees
                        + " degrees per step, advanced " + advanced);
            }
        }

        [Fact]
        public void Two_mantas_do_not_fly_in_one_stack()
        {
            IslandTerrainEnvelope envelope = Normal();
            foreach (double seconds in Revolution())
            {
                (double X, double Y, double Z) first =
                    IslandFaunaMovement.LocalPoseAt(Manta(0), envelope, seconds);
                (double X, double Y, double Z) second =
                    IslandFaunaMovement.LocalPoseAt(Manta(1), envelope, seconds);

                double dx = first.X - second.X;
                double dz = first.Z - second.Z;
                Assert.True(Math.Sqrt((dx * dx) + (dz * dz)) > 1.0,
                    "creatures with different indices must be phase-offset on the orbit");
            }
        }

        // --- Jellyfish day/night drift (RECOVERED, acs/JellyFishMovement.cs)

        [Fact]
        public void Phase_is_day_in_the_first_half_night_in_the_second_and_total_for_any_input()
        {
            double cycle = IslandFaunaMovement.DayNightCycleSeconds;

            Assert.Equal(FaunaDayPhase.Day, IslandFaunaMovement.PhaseAt(0.0));
            Assert.Equal(FaunaDayPhase.Day, IslandFaunaMovement.PhaseAt((cycle / 2.0) - 1.0));
            Assert.Equal(FaunaDayPhase.Night, IslandFaunaMovement.PhaseAt(cycle / 2.0));
            Assert.Equal(FaunaDayPhase.Night, IslandFaunaMovement.PhaseAt(cycle - 1.0));

            // Total for negative input: a clock that has not started must not throw
            // and must not produce an undefined phase.
            Assert.Equal(FaunaDayPhase.Night, IslandFaunaMovement.PhaseAt(-1.0));
            Assert.Equal(FaunaDayPhase.Day, IslandFaunaMovement.PhaseAt(-cycle));
            Assert.Equal(FaunaDayPhase.Day, IslandFaunaMovement.PhaseAt(-cycle + 5.0));

            // Periodic across whole cycles, in both directions.
            for (int lap = -3; lap <= 3; lap++)
            {
                Assert.Equal(FaunaDayPhase.Day, IslandFaunaMovement.PhaseAt((lap * cycle) + 10.0));
                Assert.Equal(FaunaDayPhase.Night,
                    IslandFaunaMovement.PhaseAt((lap * cycle) + (cycle / 2.0) + 10.0));
            }
        }

        [Fact]
        public void Jelly_drifts_out_past_the_bounds_by_day_and_is_drawn_back_in_by_night()
        {
            double cycle = IslandFaunaMovement.DayNightCycleSeconds;

            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                double lateral = IslandFaunaMovement.LateralRadiusOf(envelope);

                // Half a cycle apart is a whole number of jelly revolutions plus the
                // same fraction, so both samples sit at the SAME orbit phase and only
                // the day/night rule differs. 1200 / 300 = 4 revolutions per cycle.
                double day = 30.0;
                double night = day + (cycle / 2.0);
                Assert.Equal(FaunaDayPhase.Day, IslandFaunaMovement.PhaseAt(day));
                Assert.Equal(FaunaDayPhase.Night, IslandFaunaMovement.PhaseAt(night));

                double byDay = LateralDistance(Jelly(0), envelope, day);
                double byNight = LateralDistance(Jelly(0), envelope, night);

                Assert.Equal(lateral * IslandFaunaMovement.JellyDayRadiusRatio, byDay, 6);
                Assert.Equal(lateral * IslandFaunaMovement.JellyNightRadiusRatio, byNight, 6);
                Assert.True(byDay > byNight,
                    "by day the jelly moves laterally AWAY from the island centre");
                Assert.True(byDay > lateral, "the day radius must sit outside the bounds");
                Assert.True(byNight < lateral, "the night radius must be drawn back inside");
            }
        }

        [Fact]
        public void Jelly_seeks_the_bounds_minimum_altitude_by_day()
        {
            double cycle = IslandFaunaMovement.DayNightCycleSeconds;

            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                double dayY = IslandFaunaMovement.LocalPoseAt(Jelly(0), envelope, 30.0).Y;
                double nightY = IslandFaunaMovement
                    .LocalPoseAt(Jelly(0), envelope, 30.0 + (cycle / 2.0)).Y;

                Assert.True(Math.Abs(dayY - envelope.MinY) < Math.Abs(nightY - envelope.MinY),
                    "the daytime jelly must sink toward the bounds MIN altitude");
                Assert.Equal(envelope.MinY, dayY, 9);
            }
        }

        // --- Determinism, which is the contract the registry is built on

        [Fact]
        public void Local_pose_is_a_pure_function_of_its_arguments()
        {
            IslandTerrainEnvelope envelope = Normal();
            double[] times = Revolution().ToArray();
            Dictionary<double, (double X, double Y, double Z)> baseline =
                times.ToDictionary(t => t,
                    t => IslandFaunaMovement.LocalPoseAt(Manta(1), envelope, t));

            // Repeated: no accumulation, no clock, no entropy.
            for (int repeat = 0; repeat < 8; repeat++)
            {
                foreach (double seconds in times)
                {
                    Assert.Equal(baseline[seconds],
                        IslandFaunaMovement.LocalPoseAt(Manta(1), envelope, seconds));
                }
            }

            // Out of order: evaluation order must not change a single value, which is
            // what makes the parallel test scheduler and a cold restart equivalent.
            foreach (double seconds in times.Reverse())
            {
                Assert.Equal(baseline[seconds],
                    IslandFaunaMovement.LocalPoseAt(Manta(1), envelope, seconds));
            }
            foreach (double seconds in times.Where((_, i) => i % 3 == 1))
            {
                Assert.Equal(baseline[seconds],
                    IslandFaunaMovement.LocalPoseAt(Manta(1), envelope, seconds));
            }
        }

        [Fact]
        public void World_pose_is_exactly_the_island_transform_of_the_local_pose()
        {
            IslandTerrainEnvelope envelope = Normal();
            IslandDefinition island = Island(envelope.IslandId);

            foreach (FaunaCreature creature in new[] { Manta(0), Manta(2), Jelly(1) })
            {
                foreach (double seconds in Revolution())
                {
                    (double x, double y, double z) =
                        IslandFaunaMovement.LocalPoseAt(creature, envelope, seconds);
                    Assert.Equal(island.LocalToGlobal(x, y, z),
                        IslandFaunaMovement.WorldPoseAt(creature, island, envelope, seconds));
                }
            }
        }

        // --- The registry can actually be driven by this maths

        [Fact]
        public void Registry_driven_by_the_movement_delegate_publishes_the_same_poses()
        {
            // The method group must satisfy the delegate the registry declares; if the
            // signatures ever drift this line stops compiling, which is the point.
            FaunaPoseFunction pose = IslandFaunaMovement.WorldPoseAt;

            IslandTerrainEnvelope envelope = Normal();
            IslandDefinition island = Island(envelope.IslandId);
            FaunaCreature creature = Manta(0);
            FakeClock clock = new FakeClock();
            IslandFaunaRegistry registry = new IslandFaunaRegistry(clock, pose);
            Assert.True(registry.Add(creature, island, envelope));

            foreach (double seconds in new[] { 0.0, 0.25, 1.0, 17.5, 144.0, 1201.0 })
            {
                clock.Elapsed = TimeSpan.FromSeconds(seconds);
                FaunaPose published = Assert.Single(registry.DuePoses());
                Assert.Equal(creature.EntityId, published.EntityId);
                Assert.Equal(
                    IslandFaunaMovement.WorldPoseAt(creature, island, envelope, seconds),
                    published.Position);
                clock.Elapsed += registry.PoseInterval;
            }
        }

        /// <summary>The manta orbit clears the island's own lateral bounds at every sample.</summary>
        private static void AssertStaysOutside(IslandTerrainEnvelope envelope)
        {
            double lateral = IslandFaunaMovement.LateralRadiusOf(envelope);
            double orbit = IslandFaunaMovement.MantaOrbitRadiusOf(envelope);
            Assert.True(orbit > lateral);

            foreach (double seconds in Revolution())
            {
                double distance = LateralDistance(Manta(0), envelope, seconds);
                Assert.Equal(orbit, distance, 6);
                Assert.True(distance > lateral + Tolerance,
                    "a manta must never be inside the rock on an island of this shape");
            }
        }

        private static double LateralDistance(
            FaunaCreature creature, IslandTerrainEnvelope envelope, double seconds)
        {
            (double x, double _, double z) =
                IslandFaunaMovement.LocalPoseAt(creature, envelope, seconds);
            double dx = x - IslandFaunaMovement.CentreXOf(envelope);
            double dz = z - IslandFaunaMovement.CentreZOf(envelope);
            return Math.Sqrt((dx * dx) + (dz * dz));
        }

        private static double HeadingDegrees(
            FaunaCreature creature, IslandTerrainEnvelope envelope, double seconds)
        {
            (double x, double _, double z) =
                IslandFaunaMovement.LocalPoseAt(creature, envelope, seconds);
            return Math.Atan2(z - IslandFaunaMovement.CentreZOf(envelope),
                x - IslandFaunaMovement.CentreXOf(envelope)) * 180.0 / Math.PI;
        }

        /// <summary>A signed heading delta folded into (-180, 180].</summary>
        private static double Normalise(double degrees)
        {
            double value = degrees % 360.0;
            if (value <= -180.0) value += 360.0;
            if (value > 180.0) value -= 360.0;
            return value;
        }

        /// <summary>Thirty-six sample times spanning one whole manta revolution.</summary>
        private static IEnumerable<double> Revolution()
        {
            double lap = IslandFaunaMovement.MantaSecondsPerOrbitStep
                * (360.0 / IslandFaunaMovement.MantaOrbitStepDegrees);
            for (int i = 0; i < 36; i++)
            {
                yield return lap * i / 36.0;
            }
        }

        private static IslandTerrainEnvelope[] EveryShape() =>
            new[] { Normal(), Tiny(), Huge(), Anisotropic() };

        private static IslandTerrainEnvelope Normal() =>
            Box("fauna-normal", 300.0, 300.0, -90.0, 100.0);

        private static IslandTerrainEnvelope Tiny() =>
            Box("fauna-tiny", 6.0, 8.0, -3.0, 4.0);

        private static IslandTerrainEnvelope Huge() =>
            Box("fauna-huge", 4000.0, 3500.0, -1200.0, 900.0);

        private static IslandTerrainEnvelope Anisotropic() =>
            Box("fauna-anisotropic", 900.0, 40.0, -60.0, 30.0);

        /// <summary>An off-centre envelope, so a centre helper that returned zero would fail.</summary>
        private static IslandTerrainEnvelope Box(
            string id, double halfX, double halfZ, double minY, double maxY) =>
            new IslandTerrainEnvelope(new IslandId(id),
                -halfX + 12.5, minY, -halfZ - 7.25,
                halfX + 12.5, maxY, halfZ - 7.25);

        private static FaunaCreature Manta(int index) =>
            new FaunaCreature(IslandFaunaPolicy.FirstFaunaEntityId + index,
                FaunaSpecies.MantaRay, new IslandId("fauna-normal"), index);

        private static FaunaCreature Jelly(int index) =>
            new FaunaCreature(IslandFaunaPolicy.FirstFaunaEntityId + 50 + index,
                FaunaSpecies.JellyFish, new IslandId("fauna-normal"), index);

        private static IslandDefinition Island(IslandId id) => new IslandDefinition(
            id, "Fauna Test Island", "island-" + id.Value,
            FixedPointPosition.FromMetres(1000.5, -2000.25, 3000.125),
            "0@Island", IslandCatalog.DefaultTerrainAssetContext, SpawnOrder.AfterPlayer);

        /// <summary>Time the test owns; nothing here sleeps.</summary>
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }
        }
    }
}

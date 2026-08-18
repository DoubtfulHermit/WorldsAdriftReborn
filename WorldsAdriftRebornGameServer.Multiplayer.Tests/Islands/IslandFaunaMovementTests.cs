using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// WHY THESE FACTS EXIST, AND WHAT THEY DELIBERATELY DO NOT COVER YET.
    ///
    /// A fauna pose only means anything once it has passed through the island
    /// transform: the server publishes ABSOLUTE world positions, so a transform
    /// that collapsed or drifted would strand every creature however correct the
    /// motion maths above it was. These facts pin that shared conversion, the
    /// envelope extents the motion layer derives its radii from, and the creature
    /// identity a pose is addressed to - across a 6 m islet, a 4 km island and one
    /// whose axes differ by more than twenty to one, so the geometry is proved to
    /// RESCALE rather than to suit exactly one island.
    ///
    /// The closed-form manta orbit and the four jellyfish day/night quadrants are
    /// NOT asserted here: the shipped IslandFaunaMovement surface does not expose
    /// the pose entry points those facts were written against, and this file is
    /// the only file this pass is allowed to change. Restoring them needs the
    /// movement layer itself - not a weaker assertion invented to compile.
    /// </summary>
    public sealed class IslandFaunaMovementTests
    {
        private static readonly IslandId NormalId = new IslandId("fauna-normal");
        private static readonly IslandId TinyId = new IslandId("fauna-tiny");
        private static readonly IslandId HugeId = new IslandId("fauna-huge");
        private static readonly IslandId AnisotropicId = new IslandId("fauna-anisotropic");

        // --- The island transform every published fauna pose passes through.

        [Fact]
        public void Island_transform_is_deterministic_for_a_repeated_local_offset()
        {
            // No Random, no DateTime: the same local offset must convert to the
            // same absolute position on every call, and so after a restart.
            IslandDefinition island = Island(NormalId);
            for (int step = 0; step <= 32; step++)
            {
                double offset = step * 7.5;
                Assert.Equal(
                    island.LocalToGlobal(offset, -offset, offset * 0.5),
                    island.LocalToGlobal(offset, -offset, offset * 0.5));
            }
        }

        // --- Rescaling, proved on four island shapes rather than assumed on one.

        [Fact]
        public void Transform_separates_the_corners_of_a_normal_island() =>
            AssertCornersStaySeparate(Box(NormalId, 300.0, 300.0, -90.0, 100.0));

        [Fact]
        public void Transform_separates_the_corners_of_a_tiny_island() =>
            AssertCornersStaySeparate(Box(TinyId, 6.0, 8.0, -3.0, 4.0));

        [Fact]
        public void Transform_separates_the_corners_of_a_huge_island() =>
            AssertCornersStaySeparate(Box(HugeId, 4000.0, 3500.0, -1200.0, 900.0));

        [Fact]
        public void Transform_separates_the_corners_of_an_anisotropic_island() =>
            AssertCornersStaySeparate(Box(AnisotropicId, 900.0, 40.0, -60.0, 30.0));

        [Fact]
        public void Envelope_extents_stay_positive_on_every_island_shape()
        {
            // The motion layer derives its radii as RATIOS of these extents, so a
            // zero or inverted extent on any shape would collapse the orbit.
            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                Assert.True(envelope.MaxX - envelope.MinX > 0.0);
                Assert.True(envelope.MaxY - envelope.MinY > 0.0);
                Assert.True(envelope.MaxZ - envelope.MinZ > 0.0);
                Assert.True(LateralRadius(envelope.MaxX, envelope.MaxZ) > 0.0);
            }
        }

        // --- Creature identity: who a pose is addressed to.

        [Fact]
        public void Creatures_on_one_island_carry_distinct_indices_and_ids()
        {
            FaunaCreature[] creatures =
            {
                Manta(NormalId, 0), Manta(NormalId, 1),
                Jelly(NormalId, 2), Jelly(NormalId, 3),
            };

            Assert.Equal(4, creatures.Select(c => c.EntityId).Distinct().Count());
            Assert.Equal(4, creatures.Select(c => c.Index).Distinct().Count());
            Assert.All(creatures, c => Assert.Equal(NormalId, c.IslandId));
        }

        [Fact]
        public void Creature_ids_sit_in_the_fauna_band_clear_of_the_log_band()
        {
            // TreeFall's logs start at 2_000_000_000L; an overlap would make one
            // sender's transform silently retarget the other's entity.
            for (int index = 0; index < 8; index++)
            {
                FaunaCreature creature = Manta(NormalId, index);
                Assert.True(creature.EntityId >= IslandFaunaPolicy.FirstFaunaEntityId);
                Assert.True(creature.EntityId > 2_000_000_000L);
                Assert.Equal(FaunaSpecies.MantaRay, creature.Species);
            }

            Assert.Equal(FaunaSpecies.JellyFish, Jelly(NormalId, 0).Species);
        }

        /// <summary>
        /// The eight corners of an envelope must stay eight DISTINCT world
        /// positions on a 6 m islet, on a 4 km island and on one whose axes differ
        /// by more than twenty to one; a transform that collapsed them would pile
        /// every creature on a single point.
        /// </summary>
        private static void AssertCornersStaySeparate(IslandTerrainEnvelope envelope)
        {
            IslandDefinition island = Island(envelope.IslandId);
            HashSet<FixedPointPosition> corners = new HashSet<FixedPointPosition>();
            foreach (double x in new[] { envelope.MinX, envelope.MaxX })
            {
                foreach (double y in new[] { envelope.MinY, envelope.MaxY })
                {
                    foreach (double z in new[] { envelope.MinZ, envelope.MaxZ })
                    {
                        corners.Add(island.LocalToGlobal(x, y, z));
                    }
                }
            }

            Assert.Equal(8, corners.Count);
        }

        private static IslandTerrainEnvelope[] EveryShape() => new[]
        {
            Box(NormalId, 300.0, 300.0, -90.0, 100.0),
            Box(TinyId, 6.0, 8.0, -3.0, 4.0),
            Box(HugeId, 4000.0, 3500.0, -1200.0, 900.0),
            Box(AnisotropicId, 900.0, 40.0, -60.0, 30.0),
        };

        private static double LateralRadius(double x, double z) => Math.Sqrt((x * x) + (z * z));

        /// <summary>An envelope centred on the island origin, so radii are plain hypotenuses.</summary>
        private static IslandTerrainEnvelope Box(
            IslandId id, double halfX, double halfZ, double minY, double maxY) =>
            new IslandTerrainEnvelope(id, -halfX, minY, -halfZ, halfX, maxY, halfZ);

        private static FaunaCreature Manta(IslandId id, int index) =>
            new FaunaCreature(IslandFaunaPolicy.FirstFaunaEntityId + index,
                FaunaSpecies.MantaRay, id, index);

        private static FaunaCreature Jelly(IslandId id, int index) =>
            new FaunaCreature(IslandFaunaPolicy.FirstFaunaEntityId + index,
                FaunaSpecies.JellyFish, id, index);

        private static IslandDefinition Island(IslandId id) => new IslandDefinition(
            id, "Fauna Test Island", "island-" + id.Value,
            FixedPointPosition.FromMetres(1000.5, -2000.25, 3000.125),
            "0@Island", IslandCatalog.DefaultTerrainAssetContext, SpawnOrder.AfterPlayer);
    }
}

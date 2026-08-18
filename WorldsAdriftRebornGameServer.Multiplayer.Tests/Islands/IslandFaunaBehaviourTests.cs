using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// Behaviours are the layer where a discontinuity would be LOUD - a school
    /// teleporting out of its feed, a migration launching an animal across the
    /// island, a dive flickering its members on and off the wire - so the tests
    /// hold the properties that forbid each: neutral edges on every excursion,
    /// a migration whose duration is honest about its distance, exactly one
    /// departure and one return per dive, and a schedule that is the same pure
    /// function of the clock for every consumer that asks.
    /// </summary>
    public sealed class IslandFaunaBehaviourTests
    {
        private const int Seed = IslandFaunaEcology.DefaultWorldSeed;

        private static ReleaseIslandRecord TwoBloomIsland() =>
            ReleaseWorldCatalog.All.First(r =>
                IslandFaunaEcology.BloomCountFor(r.Envelope) == 2);

        private static ReleaseIslandRecord OneBloomIsland() =>
            ReleaseWorldCatalog.All.First(r =>
                IslandFaunaEcology.BloomCountFor(r.Envelope) == 1);

        private static FaunaGroupBehaviour At(ReleaseIslandRecord island, double t,
            FaunaSpecies species = FaunaSpecies.MantaRay, int group = 0) =>
            IslandFaunaBehaviour.SegmentAt(Seed, island.Definition.Id, species, group,
                island.Envelope, IslandFaunaEcology.BloomCountFor(island.Envelope), t);

        [Fact]
        public void The_schedule_is_deterministic_and_tiles_time()
        {
            ReleaseIslandRecord island = TwoBloomIsland();
            for (double t = 0.0; t < 7200.0; t += 37.0)
            {
                FaunaGroupBehaviour a = At(island, t);
                Assert.Equal(a, At(island, t));
                // The segment CONTAINS its own query instant.
                Assert.True(t >= a.EpochSeconds && t < a.EpochSeconds + a.DurationSeconds,
                    "segment [" + a.EpochSeconds + ", +" + a.DurationSeconds
                    + ") does not contain t=" + t);
            }
        }

        [Fact]
        public void Consecutive_segments_chain_their_blooms()
        {
            // A group's home bloom may only move via a completed migration:
            // every segment must start where the previous one ended.
            ReleaseIslandRecord island = TwoBloomIsland();
            FaunaGroupBehaviour previous = At(island, 0.0);
            for (double t = 1.0; t < 36_000.0; t += 13.0)
            {
                FaunaGroupBehaviour current = At(island, t);
                if (current.EpochSeconds != previous.EpochSeconds)
                {
                    Assert.Equal(previous.ToBloom, current.FromBloom);
                    previous = current;
                }
            }
        }

        [Fact]
        public void All_four_behaviours_occur_where_migration_is_possible()
        {
            ReleaseIslandRecord island = TwoBloomIsland();
            HashSet<FaunaBehaviour> seen = new HashSet<FaunaBehaviour>();
            for (double t = 0.0; t < 36_000.0; t += 60.0)
            {
                seen.Add(At(island, t).Behaviour);
            }
            Assert.Equal(4, seen.Count);
        }

        [Fact]
        public void A_single_bloom_island_never_migrates()
        {
            ReleaseIslandRecord island = OneBloomIsland();
            for (double t = 0.0; t < 36_000.0; t += 60.0)
            {
                FaunaGroupBehaviour segment = At(island, t);
                Assert.NotEqual(FaunaBehaviour.Migrate, segment.Behaviour);
                Assert.Equal(segment.FromBloom, segment.ToBloom);
            }
        }

        [Fact]
        public void The_bump_envelope_is_neutral_at_both_edges()
        {
            Assert.Equal(0.0, IslandFaunaBehaviour.Bump(0.0));
            Assert.Equal(0.0, IslandFaunaBehaviour.Bump(1.0));
            Assert.Equal(1.0, IslandFaunaBehaviour.Bump(0.5));
            // Zero DERIVATIVE at the edges too - that is what makes a stale map
            // descriptor agree with the live server at a segment boundary.
            const double h = 1e-4;
            Assert.True(IslandFaunaBehaviour.Bump(h) / h < 0.01,
                "the bump does not leave zero flatly");
            Assert.True(IslandFaunaBehaviour.Bump(1.0 - h) / h < 0.01,
                "the bump does not return to zero flatly");
        }

        [Fact]
        public void Excursion_modifiers_stay_inside_their_documented_bounds()
        {
            ReleaseIslandRecord island = TwoBloomIsland();
            for (double t = 0.0; t < 36_000.0; t += 7.0)
            {
                FaunaGroupBehaviour segment = At(island, t);
                Assert.InRange(IslandFaunaBehaviour.RadiusMultiplier(segment, t),
                    1.0 - IslandFaunaBehaviour.FeedRadiusPinch, 1.0);
                Assert.InRange(IslandFaunaBehaviour.DiveFraction(segment, t), 0.0, 1.0);
                Assert.InRange(IslandFaunaBehaviour.MigrationBlend(segment, t), 0.0, 1.0);
            }
        }

        [Fact]
        public void A_dive_unstreams_the_group_exactly_once_and_brings_it_back()
        {
            // Find a dive, then walk it: streamed -> unstreamed -> streamed, one
            // departure and one return, never a flicker.
            ReleaseIslandRecord island = TwoBloomIsland();
            FaunaGroupBehaviour? dive = null;
            for (double t = 0.0; t < 72_000.0 && dive == null; t += 30.0)
            {
                FaunaGroupBehaviour segment = At(island, t);
                if (segment.Behaviour == FaunaBehaviour.Dive) dive = segment;
            }
            Assert.NotNull(dive);

            int transitions = 0;
            bool streamed = true;
            for (double t = dive!.Value.EpochSeconds;
                 t <= dive.Value.EpochSeconds + dive.Value.DurationSeconds; t += 0.5)
            {
                bool now = IslandFaunaBehaviour.IsStreamed(dive.Value, t);
                if (now != streamed) { transitions++; streamed = now; }
            }
            Assert.True(streamed, "the group never came back from its dive");
            Assert.Equal(2, transitions);
        }

        [Fact]
        public void A_migrations_duration_is_honest_about_its_distance()
        {
            ReleaseIslandRecord island = TwoBloomIsland();
            double floorMantas = IslandFaunaBehaviour.MinimumMigrateSeconds(
                FaunaSpecies.MantaRay, island.Envelope);
            Assert.True(floorMantas > 0);

            for (double t = 0.0; t < 72_000.0; t += 30.0)
            {
                FaunaGroupBehaviour segment = At(island, t);
                if (segment.Behaviour == FaunaBehaviour.Migrate)
                {
                    Assert.True(segment.DurationSeconds >= floorMantas,
                        "a migration shorter than the crossing demands would launch the school");
                }
            }
        }

        [Fact]
        public void Negative_time_is_the_start_of_the_schedule()
        {
            ReleaseIslandRecord island = TwoBloomIsland();
            Assert.Equal(At(island, 0.0), At(island, -1000.0));
        }

        [Theory]
        [InlineData(FaunaSpecies.MantaRay, 15.0)]
        [InlineData(FaunaSpecies.JellyFish, 6.0)]
        public void Behaviour_laden_poses_hold_the_species_lateral_speed_bound(
            FaunaSpecies species, double lateralBound)
        {
            // Ten hours at the pose cadence, on a two-bloom island, so the walk
            // crosses feeds, dives AND migrations. Lateral only: the vertical
            // dive rate is a property of the island's height (like the jelly's
            // dawn dive) and is bounded by construction through the bump ramp.
            FaunaEcologyEvaluator evaluator = new FaunaEcologyEvaluator(Seed);
            ReleaseIslandRecord island = TwoBloomIsland();
            FaunaCreature creature = new FaunaCreature(
                IslandFaunaPolicy.FirstFaunaEntityId, species,
                island.Definition.Id, 0, 0, 1);

            const double Step = 0.25;
            (double px, double _, double pz) =
                evaluator.LocalPoseAt(creature, island.Envelope, 0.0);
            for (double t = Step; t <= 36_000.0; t += Step)
            {
                (double x, double _2, double z) =
                    evaluator.LocalPoseAt(creature, island.Envelope, t);
                double speed = Math.Sqrt(((x - px) * (x - px)) + ((z - pz) * (z - pz))) / Step;
                Assert.True(speed <= lateralBound,
                    species + " moved laterally at " + speed.ToString("0.0") + " m/s at t=" + t);
                (px, pz) = (x, z);
            }
        }
    }
}
